#if GLOADER_SERVER
public static class Mod
{
    public static void Load()
    {
        // Client-only radio. Host & Play / dedicated server targets intentionally do nothing.
    }
}
#else
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using NAudio.Wave;

public static class Mod
{
    public static void Load()
    {
        RainwaveRadio.Initialize();
    }
}

internal static class RainwaveRadio
{
    private const string HarmonyId = "gloader.mod.rainwaveradio";
    private const int StationId = 5; // Rainwave All
    private const string TuneInUrl = "https://rainwave.cc/tune_in/5.mp3.m3u";
    private const string FallbackStreamUrl = "https://gamestream.rainwave.cc/all.mp3";
    private const string InfoUrl = "https://rainwave.cc/api4/info?sid=5";

    private const int TargetSampleRate = 44100;
    private const int TargetChannels = 2;
    private const int TargetBits = 16;
    private const int BufferMilliseconds = 125;
    private const int DesiredPendingBuffers = 5;
    private const int MaxQueuedBuffers = 12;

    private const float PauseDuckLevel = 0.22f;
    private const float DuckDownSeconds = 0.35f;
    private const float DuckUpSeconds = 0.50f;
    private const double RadioHealthySeconds = 8.0;
    private const double WorkerRestartSeconds = 12.0;
    private const double OverlaySeconds = 6.0;
    private const double OverlayFadeInSeconds = 0.20;
    private const double OverlayFadeOutSeconds = 0.80;

    private static readonly object AudioQueueLock = new object();
    private static readonly Queue<byte[]> AudioQueue = new Queue<byte[]>();
    private static readonly object OverlayLock = new object();
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    private static Type _mainType;
    private static FieldInfo _musicVolumeField;
    private static FieldInfo _gamePausedField;

    private static object _dynamicSound;
    private static PropertyInfo _soundVolumeProperty;
    private static PropertyInfo _pendingBufferCountProperty;
    private static PropertyInfo _soundStateProperty;
    private static MethodInfo _submitBufferMethod;
    private static MethodInfo _playMethod;
    private static MethodInfo _stopMethod;
    private static MethodInfo _disposeMethod;

    private static int _audioGeneration;
    private static long _lastAudioUtcTicks;
    private static long _lastWorkerStartUtcTicks;
    private static int _hasEverReceivedAudio;
    private static int _initialized;

    private static double _lastTickSeconds;
    private static float _duck = 1f;

    private static string _nowPlaying = string.Empty;
    private static double _overlayStartSeconds;
    private static double _overlayEndSeconds;
    private static bool _overlayAvailable = true;

    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        _mainType = AccessTools.TypeByName("Terraria.Main");
        if (_mainType == null)
            throw new TypeLoadException("Terraria.Main was not found.");

        _musicVolumeField = AccessTools.Field(_mainType, "musicVolume");
        _gamePausedField = AccessTools.Field(_mainType, "gamePaused");
        if (_musicVolumeField == null)
            throw new MissingFieldException("Terraria.Main.musicVolume was not found.");

        var updateAudio = AccessTools.Method(_mainType, "UpdateAudio", Type.EmptyTypes);
        if (updateAudio == null)
            throw new MissingMethodException("Terraria.Main.UpdateAudio() was not found.");

        var harmony = new Harmony(HarmonyId);
        try
        {
            harmony.Patch(
                updateAudio,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(RainwaveRadio), nameof(UpdateAudioPrefix))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(RainwaveRadio), nameof(UpdateAudioPostfix))));
        }
        catch
        {
            harmony.UnpatchAll(HarmonyId);
            throw;
        }

        TryInstallOverlayPatch(harmony);
        StartMetadataWorker();
        StartAudioWorker();
    }

    private static void TryInstallOverlayPatch(Harmony harmony)
    {
        try
        {
            var drawMouseText = AccessTools.Method(_mainType, "DrawInterface_33_MouseText", Type.EmptyTypes);
            if (drawMouseText == null)
            {
                _overlayAvailable = false;
                return;
            }

            harmony.Patch(
                drawMouseText,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(RainwaveRadio), nameof(DrawOverlayPrefix))));
        }
        catch
        {
            // The radio is more important than the cosmetic now-playing overlay.
            _overlayAvailable = false;
        }
    }

    private static void UpdateAudioPrefix(out float __state)
    {
        __state = ReadMusicVolume();
        if (RadioIsHealthy())
            WriteMusicVolume(0f);
    }

    private static void UpdateAudioPostfix(float __state)
    {
        WriteMusicVolume(__state);
        Tick(__state);
    }

    private static void DrawOverlayPrefix()
    {
        if (!_overlayAvailable)
            return;

        try
        {
            DrawNowPlaying();
        }
        catch
        {
            // If a future Terraria update changes UI internals, disable only the overlay.
            _overlayAvailable = false;
        }
    }

    private static float ReadMusicVolume()
    {
        try
        {
            return Math.Max(0f, Math.Min(1f, Convert.ToSingle(_musicVolumeField.GetValue(null), CultureInfo.InvariantCulture)));
        }
        catch
        {
            return 1f;
        }
    }

    private static void WriteMusicVolume(float value)
    {
        try
        {
            _musicVolumeField.SetValue(null, Math.Max(0f, Math.Min(1f, value)));
        }
        catch
        {
        }
    }

    private static bool IsPaused()
    {
        if (_gamePausedField == null)
            return false;

        try
        {
            return Convert.ToBoolean(_gamePausedField.GetValue(null), CultureInfo.InvariantCulture);
        }
        catch
        {
            return false;
        }
    }

    private static void Tick(float musicSlider)
    {
        var now = Clock.Elapsed.TotalSeconds;
        var dt = _lastTickSeconds <= 0.0 ? 1.0 / 60.0 : Math.Max(0.0, Math.Min(0.25, now - _lastTickSeconds));
        _lastTickSeconds = now;

        var targetDuck = IsPaused() ? PauseDuckLevel : 1f;
        var transitionSeconds = targetDuck < _duck ? DuckDownSeconds : DuckUpSeconds;
        var step = transitionSeconds <= 0f ? 1f : (float)(dt / transitionSeconds);
        _duck = MoveTowards(_duck, targetDuck, step);

        EnsureAudioWorkerHealthy();

        if (!RadioIsHealthy())
        {
            SetDynamicSoundVolume(0f);
            return;
        }

        EnsureDynamicSound();
        if (_dynamicSound == null)
            return;

        SetDynamicSoundVolume(Math.Max(0f, Math.Min(1f, musicSlider * _duck)));
        FeedDynamicSound();
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (Math.Abs(target - current) <= maxDelta)
            return target;
        return current + Math.Sign(target - current) * maxDelta;
    }

    private static bool RadioIsHealthy()
    {
        if (Volatile.Read(ref _hasEverReceivedAudio) == 0)
            return false;

        var last = Interlocked.Read(ref _lastAudioUtcTicks);
        if (last <= 0)
            return false;

        return DateTime.UtcNow.Ticks - last <= TimeSpan.FromSeconds(RadioHealthySeconds).Ticks;
    }

    private static void EnsureAudioWorkerHealthy()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var lastAudio = Interlocked.Read(ref _lastAudioUtcTicks);
        var lastStart = Interlocked.Read(ref _lastWorkerStartUtcTicks);

        var stale = Volatile.Read(ref _hasEverReceivedAudio) == 0
            ? nowTicks - lastStart > TimeSpan.FromSeconds(WorkerRestartSeconds).Ticks
            : nowTicks - lastAudio > TimeSpan.FromSeconds(WorkerRestartSeconds).Ticks;

        if (stale && nowTicks - lastStart > TimeSpan.FromSeconds(WorkerRestartSeconds).Ticks)
            StartAudioWorker();
    }

    private static void StartAudioWorker()
    {
        var generation = Interlocked.Increment(ref _audioGeneration);
        Interlocked.Exchange(ref _lastWorkerStartUtcTicks, DateTime.UtcNow.Ticks);

        var thread = new Thread(() => AudioWorker(generation))
        {
            IsBackground = true,
            Name = "gloader Rainwave audio"
        };
        thread.Start();
    }

    private static void AudioWorker(int generation)
    {
        while (generation == Volatile.Read(ref _audioGeneration))
        {
            try
            {
                var streamUrl = ResolveStreamUrl();
                using (var reader = new MediaFoundationReader(streamUrl))
                using (var resampler = new MediaFoundationResampler(reader, new WaveFormat(TargetSampleRate, TargetBits, TargetChannels)))
                {
                    resampler.ResamplerQuality = 60;
                    var bytesPerSecond = TargetSampleRate * TargetChannels * (TargetBits / 8);
                    var bufferBytes = Math.Max(4096, bytesPerSecond * BufferMilliseconds / 1000);
                    bufferBytes -= bufferBytes % (TargetChannels * (TargetBits / 8));
                    var scratch = new byte[bufferBytes];

                    while (generation == Volatile.Read(ref _audioGeneration))
                    {
                        if (QueuedBufferCount() >= MaxQueuedBuffers)
                        {
                            Thread.Sleep(15);
                            continue;
                        }

                        var read = resampler.Read(scratch, 0, scratch.Length);
                        if (read <= 0)
                            throw new EndOfStreamException("Rainwave stream ended.");

                        var chunk = new byte[read];
                        Buffer.BlockCopy(scratch, 0, chunk, 0, read);
                        EnqueueAudio(chunk);
                        Interlocked.Exchange(ref _lastAudioUtcTicks, DateTime.UtcNow.Ticks);
                        Volatile.Write(ref _hasEverReceivedAudio, 1);
                    }
                }
            }
            catch
            {
                if (generation != Volatile.Read(ref _audioGeneration))
                    return;
                Thread.Sleep(1800);
            }
        }
    }

    private static string ResolveStreamUrl()
    {
        try
        {
            var playlist = DownloadText(TuneInUrl, 5000);
            foreach (var rawLine in playlist.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                Uri uri;
                if (Uri.TryCreate(line, UriKind.Absolute, out uri) &&
                    (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                    return uri.AbsoluteUri;
            }
        }
        catch
        {
        }

        return FallbackStreamUrl;
    }

    private static void StartMetadataWorker()
    {
        var thread = new Thread(MetadataWorker)
        {
            IsBackground = true,
            Name = "gloader Rainwave metadata"
        };
        thread.Start();
    }

    private static void MetadataWorker()
    {
        while (true)
        {
            try
            {
                var json = DownloadText(InfoUrl, 5000);
                string display;
                if (TryParseNowPlaying(json, out display) && !string.IsNullOrWhiteSpace(display))
                    SetNowPlaying(display);
            }
            catch
            {
            }

            Thread.Sleep(1500);
        }
    }

    private static string DownloadText(string url, int timeoutMilliseconds)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "GET";
        request.UserAgent = "gloader-rainwave-radio/0.1";
        request.Accept = "*/*";
        request.Timeout = timeoutMilliseconds;
        request.ReadWriteTimeout = timeoutMilliseconds;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

        using (var response = (HttpWebResponse)request.GetResponse())
        using (var stream = response.GetResponseStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            return reader.ReadToEnd();
    }

    private static bool TryParseNowPlaying(string json, out string display)
    {
        display = null;
        string current;
        string songData;
        string title;

        if (!TryGetObject(json, "sched_current", out current) ||
            !TryGetObject(current, "song_data", out songData) ||
            !TryGetString(songData, "title", out title) ||
            string.IsNullOrWhiteSpace(title))
            return false;

        string artistsArray;
        var artists = new List<string>();
        if (TryGetArray(songData, "artists", out artistsArray))
        {
            foreach (var artistObject in EnumerateObjects(artistsArray))
            {
                string artistName;
                if (TryGetString(artistObject, "name", out artistName) && !string.IsNullOrWhiteSpace(artistName))
                    artists.Add(artistName.Trim());
            }
        }

        var artistText = artists.Count == 0 ? string.Empty : string.Join(", ", artists.Distinct(StringComparer.OrdinalIgnoreCase));
        display = artistText.Length == 0
            ? "Now playing: " + title.Trim()
            : "Now playing: " + artistText + " - " + title.Trim();
        return true;
    }

    private static bool TryGetObject(string json, string key, out string value)
    {
        return TryGetComposite(json, key, '{', '}', out value);
    }

    private static bool TryGetArray(string json, string key, out string value)
    {
        return TryGetComposite(json, key, '[', ']', out value);
    }

    private static bool TryGetComposite(string json, string key, char open, char close, out string value)
    {
        value = null;
        int valueStart;
        if (!TryFindTopLevelValue(json, key, out valueStart) || valueStart >= json.Length || json[valueStart] != open)
            return false;

        var end = FindMatching(json, valueStart, open, close);
        if (end < 0)
            return false;

        value = json.Substring(valueStart, end - valueStart + 1);
        return true;
    }

    private static bool TryGetString(string json, string key, out string value)
    {
        value = null;
        int valueStart;
        if (!TryFindTopLevelValue(json, key, out valueStart) || valueStart >= json.Length || json[valueStart] != '"')
            return false;

        int end;
        if (!TryReadJsonString(json, valueStart, out value, out end))
            return false;
        return true;
    }

    private static bool TryFindTopLevelValue(string json, string key, out int valueStart)
    {
        valueStart = -1;
        if (string.IsNullOrEmpty(json))
            return false;

        var depth = 0;
        var i = 0;
        while (i < json.Length)
        {
            var c = json[i];
            if (c == '"')
            {
                string token;
                int end;
                if (!TryReadJsonString(json, i, out token, out end))
                    return false;

                if (depth == 1 && string.Equals(token, key, StringComparison.Ordinal))
                {
                    var p = SkipWhitespace(json, end + 1);
                    if (p < json.Length && json[p] == ':')
                    {
                        p = SkipWhitespace(json, p + 1);
                        valueStart = p;
                        return p < json.Length;
                    }
                }

                i = end + 1;
                continue;
            }

            if (c == '{' || c == '[')
                depth++;
            else if (c == '}' || c == ']')
                depth--;
            i++;
        }

        return false;
    }

    private static int FindMatching(string json, int start, char open, char close)
    {
        var depth = 0;
        for (var i = start; i < json.Length; i++)
        {
            if (json[i] == '"')
            {
                string ignored;
                int end;
                if (!TryReadJsonString(json, i, out ignored, out end))
                    return -1;
                i = end;
                continue;
            }

            if (json[i] == open)
                depth++;
            else if (json[i] == close && --depth == 0)
                return i;
        }
        return -1;
    }

    private static IEnumerable<string> EnumerateObjects(string jsonArray)
    {
        for (var i = 0; i < jsonArray.Length; i++)
        {
            if (jsonArray[i] == '"')
            {
                string ignored;
                int end;
                if (!TryReadJsonString(jsonArray, i, out ignored, out end))
                    yield break;
                i = end;
                continue;
            }

            if (jsonArray[i] != '{')
                continue;

            var endObject = FindMatching(jsonArray, i, '{', '}');
            if (endObject < 0)
                yield break;

            yield return jsonArray.Substring(i, endObject - i + 1);
            i = endObject;
        }
    }

    private static bool TryReadJsonString(string json, int quoteIndex, out string value, out int endQuote)
    {
        value = null;
        endQuote = -1;
        if (quoteIndex < 0 || quoteIndex >= json.Length || json[quoteIndex] != '"')
            return false;

        var builder = new StringBuilder();
        for (var i = quoteIndex + 1; i < json.Length; i++)
        {
            var c = json[i];
            if (c == '"')
            {
                value = builder.ToString();
                endQuote = i;
                return true;
            }

            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            if (++i >= json.Length)
                return false;

            c = json[i];
            switch (c)
            {
                case '"': builder.Append('"'); break;
                case '\\': builder.Append('\\'); break;
                case '/': builder.Append('/'); break;
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                case 'u':
                    if (i + 4 >= json.Length)
                        return false;
                    int code;
                    if (!int.TryParse(json.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        return false;
                    builder.Append((char)code);
                    i += 4;
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return false;
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
        return index;
    }

    private static void SetNowPlaying(string display)
    {
        lock (OverlayLock)
        {
            if (string.Equals(_nowPlaying, display, StringComparison.Ordinal))
                return;

            _nowPlaying = display;
            _overlayStartSeconds = Clock.Elapsed.TotalSeconds;
            _overlayEndSeconds = _overlayStartSeconds + OverlaySeconds;
        }
    }

    private static void EnsureDynamicSound()
    {
        if (_dynamicSound != null)
            return;

        try
        {
            var dynamicType = AccessTools.TypeByName("Microsoft.Xna.Framework.Audio.DynamicSoundEffectInstance");
            var channelsType = AccessTools.TypeByName("Microsoft.Xna.Framework.Audio.AudioChannels");
            if (dynamicType == null || channelsType == null)
                return;

            var stereo = Enum.Parse(channelsType, "Stereo", true);
            _dynamicSound = Activator.CreateInstance(dynamicType, new object[] { TargetSampleRate, stereo });
            _soundVolumeProperty = dynamicType.GetProperty("Volume", BindingFlags.Instance | BindingFlags.Public);
            _pendingBufferCountProperty = dynamicType.GetProperty("PendingBufferCount", BindingFlags.Instance | BindingFlags.Public);
            _soundStateProperty = dynamicType.GetProperty("State", BindingFlags.Instance | BindingFlags.Public);
            _submitBufferMethod = dynamicType.GetMethod("SubmitBuffer", new[] { typeof(byte[]) });
            _playMethod = dynamicType.GetMethod("Play", Type.EmptyTypes);
            _stopMethod = dynamicType.GetMethod("Stop", Type.EmptyTypes);
            _disposeMethod = dynamicType.GetMethod("Dispose", Type.EmptyTypes);

            if (_soundVolumeProperty == null || _pendingBufferCountProperty == null || _submitBufferMethod == null || _playMethod == null)
                DisposeDynamicSound();
        }
        catch
        {
            DisposeDynamicSound();
        }
    }

    private static void FeedDynamicSound()
    {
        try
        {
            var pending = Convert.ToInt32(_pendingBufferCountProperty.GetValue(_dynamicSound, null), CultureInfo.InvariantCulture);
            while (pending < DesiredPendingBuffers)
            {
                byte[] chunk;
                if (!TryDequeueAudio(out chunk))
                    break;

                _submitBufferMethod.Invoke(_dynamicSound, new object[] { chunk });
                pending++;
            }

            if (pending >= 2)
            {
                var state = _soundStateProperty == null ? null : _soundStateProperty.GetValue(_dynamicSound, null);
                if (state == null || !string.Equals(state.ToString(), "Playing", StringComparison.OrdinalIgnoreCase))
                    _playMethod.Invoke(_dynamicSound, null);
            }
        }
        catch
        {
            DisposeDynamicSound();
        }
    }

    private static void SetDynamicSoundVolume(float volume)
    {
        if (_dynamicSound == null || _soundVolumeProperty == null)
            return;

        try
        {
            _soundVolumeProperty.SetValue(_dynamicSound, volume, null);
        }
        catch
        {
        }
    }

    private static void DisposeDynamicSound()
    {
        var sound = _dynamicSound;
        _dynamicSound = null;
        if (sound == null)
            return;

        try { if (_stopMethod != null) _stopMethod.Invoke(sound, null); } catch { }
        try { if (_disposeMethod != null) _disposeMethod.Invoke(sound, null); } catch { }

        _soundVolumeProperty = null;
        _pendingBufferCountProperty = null;
        _soundStateProperty = null;
        _submitBufferMethod = null;
        _playMethod = null;
        _stopMethod = null;
        _disposeMethod = null;
    }

    private static void EnqueueAudio(byte[] chunk)
    {
        lock (AudioQueueLock)
        {
            if (AudioQueue.Count < MaxQueuedBuffers)
                AudioQueue.Enqueue(chunk);
        }
    }

    private static bool TryDequeueAudio(out byte[] chunk)
    {
        lock (AudioQueueLock)
        {
            if (AudioQueue.Count == 0)
            {
                chunk = null;
                return false;
            }

            chunk = AudioQueue.Dequeue();
            return true;
        }
    }

    private static int QueuedBufferCount()
    {
        lock (AudioQueueLock)
            return AudioQueue.Count;
    }

    private static void DrawNowPlaying()
    {
        string text;
        double start;
        double end;
        lock (OverlayLock)
        {
            text = _nowPlaying;
            start = _overlayStartSeconds;
            end = _overlayEndSeconds;
        }

        if (string.IsNullOrWhiteSpace(text))
            return;

        var now = Clock.Elapsed.TotalSeconds;
        if (now < start || now >= end)
            return;

        var alpha = 1.0;
        if (now - start < OverlayFadeInSeconds)
            alpha = Math.Max(0.0, Math.Min(1.0, (now - start) / OverlayFadeInSeconds));
        else if (end - now < OverlayFadeOutSeconds)
            alpha = Math.Max(0.0, Math.Min(1.0, (end - now) / OverlayFadeOutSeconds));

        var spriteBatchField = AccessTools.Field(_mainType, "spriteBatch");
        var screenHeightField = AccessTools.Field(_mainType, "screenHeight");
        var mouseTextColorField = AccessTools.Field(_mainType, "mouseTextColor");
        var spriteBatch = spriteBatchField == null ? null : spriteBatchField.GetValue(null);
        if (spriteBatch == null)
            return;

        var fontAssetsType = AccessTools.TypeByName("Terraria.GameContent.FontAssets");
        var chatManagerType = AccessTools.TypeByName("Terraria.UI.Chat.ChatManager");
        var vector2Type = AccessTools.TypeByName("Microsoft.Xna.Framework.Vector2");
        var colorType = AccessTools.TypeByName("Microsoft.Xna.Framework.Color");
        if (fontAssetsType == null || chatManagerType == null || vector2Type == null || colorType == null)
            return;

        var mouseTextMember = (MemberInfo)fontAssetsType.GetField("MouseText", BindingFlags.Static | BindingFlags.Public) ??
                              fontAssetsType.GetProperty("MouseText", BindingFlags.Static | BindingFlags.Public);
        object fontAsset = null;
        if (mouseTextMember is FieldInfo)
            fontAsset = ((FieldInfo)mouseTextMember).GetValue(null);
        else if (mouseTextMember is PropertyInfo)
            fontAsset = ((PropertyInfo)mouseTextMember).GetValue(null, null);
        if (fontAsset == null)
            return;

        var valueProperty = fontAsset.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
        var font = valueProperty == null ? null : valueProperty.GetValue(fontAsset, null);
        if (font == null)
            return;

        var screenHeight = screenHeightField == null ? 720 : Convert.ToInt32(screenHeightField.GetValue(null), CultureInfo.InvariantCulture);
        var y = Math.Max(20f, screenHeight - 92f);
        var position = Activator.CreateInstance(vector2Type, new object[] { 20f, y });
        var zero = GetStaticMember(vector2Type, "Zero");
        var one = GetStaticMember(vector2Type, "One");

        var baseBrightness = 255;
        if (mouseTextColorField != null)
        {
            try { baseBrightness = Convert.ToInt32(mouseTextColorField.GetValue(null), CultureInfo.InvariantCulture); }
            catch { }
        }
        baseBrightness = Math.Max(0, Math.Min(255, baseBrightness));
        var a = Math.Max(0, Math.Min(255, (int)Math.Round(255.0 * alpha)));
        var color = CreateColor(colorType, baseBrightness, baseBrightness, baseBrightness, a);
        if (color == null || zero == null || one == null)
            return;

        var draw = chatManagerType.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .FirstOrDefault(method => method.Name == "DrawColorCodedStringWithShadow" && method.GetParameters().Length == 10);
        if (draw == null)
            return;

        draw.Invoke(null, new[]
        {
            spriteBatch,
            font,
            text,
            position,
            color,
            (object)0f,
            zero,
            one,
            (object)(-1f),
            (object)2f
        });
    }

    private static object GetStaticMember(Type type, string name)
    {
        var field = type.GetField(name, BindingFlags.Static | BindingFlags.Public);
        if (field != null)
            return field.GetValue(null);
        var property = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public);
        return property == null ? null : property.GetValue(null, null);
    }

    private static object CreateColor(Type colorType, int r, int g, int b, int a)
    {
        var ints = colorType.GetConstructor(new[] { typeof(int), typeof(int), typeof(int), typeof(int) });
        if (ints != null)
            return ints.Invoke(new object[] { r, g, b, a });

        var bytes = colorType.GetConstructor(new[] { typeof(byte), typeof(byte), typeof(byte), typeof(byte) });
        if (bytes != null)
            return bytes.Invoke(new object[] { (byte)r, (byte)g, (byte)b, (byte)a });

        return GetStaticMember(colorType, "White");
    }
}
#endif
