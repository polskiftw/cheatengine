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
using System.Text.RegularExpressions;
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

    private const int DefaultStationId = 5;
    private const string DefaultStationMount = "all";

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

    private static readonly Regex CurrentSongRegex = new Regex(
        "\"sched_current\"\s*:\s*\{.*?\"song_data\"\s*:\s*\{.*?\"title\"\s*:\s*\"((?:\\.|[^\"\\])*)\".*?\"artists\"\s*:\s*\[(.*?)\]",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex ArtistRegex = new Regex(
        "\"name\"\s*:\s*\"((?:\\.|[^\"\\])*)\"",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static int _stationId = DefaultStationId;
    private static string _stationMount = DefaultStationMount;
    private static bool _showNowPlaying = true;

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

        LoadSettings();

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

            if (_showNowPlaying)
                TryInstallOverlayPatch(harmony);
            else
                _overlayAvailable = false;

            StartMetadataWorker();
            StartAudioWorker();
        }
        catch
        {
            harmony.UnpatchAll(HarmonyId);
            throw;
        }
    }

    private static void LoadSettings()
    {
        _stationId = DefaultStationId;
        _stationMount = DefaultStationMount;
        _showNowPlaying = true;

        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods", "RainwaveRadio.ini");
            if (!File.Exists(path))
                return;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
                    continue;

                var equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                var key = line.Substring(0, equals).Trim();
                var value = line.Substring(equals + 1).Trim();

                if (string.Equals(key, "Station", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyStation(value);
                }
                else if (string.Equals(key, "ShowNowPlaying", StringComparison.OrdinalIgnoreCase))
                {
                    bool enabled;
                    if (bool.TryParse(value, out enabled))
                        _showNowPlaying = enabled;
                }
            }
        }
        catch
        {
            _stationId = DefaultStationId;
            _stationMount = DefaultStationMount;
            _showNowPlaying = true;
        }
    }

    private static void ApplyStation(string value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .ToLowerInvariant();

        switch (normalized)
        {
            case "game":
            case "gamemusic":
                _stationId = 1;
                _stationMount = "game";
                return;
            case "ocremix":
            case "ocr":
                _stationId = 2;
                _stationMount = "ocremix";
                return;
            case "covers":
            case "cover":
                _stationId = 3;
                _stationMount = "covers";
                return;
            case "chiptunes":
            case "chiptune":
            case "chip":
                _stationId = 4;
                _stationMount = "chiptune";
                return;
            case "chill":
                _stationId = 6;
                _stationMount = "chill";
                return;
            default:
                _stationId = DefaultStationId;
                _stationMount = DefaultStationMount;
                return;
        }
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
            // UI changes should never disable the radio itself.
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
        if (!_overlayAvailable || !_showNowPlaying)
            return;

        try
        {
            DrawNowPlaying();
        }
        catch
        {
            _overlayAvailable = false;
        }
    }

    private static float ReadMusicVolume()
    {
        try
        {
            return Clamp01(Convert.ToSingle(_musicVolumeField.GetValue(null), CultureInfo.InvariantCulture));
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
            _musicVolumeField.SetValue(null, Clamp01(value));
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
        var dt = _lastTickSeconds <= 0.0
            ? 1.0 / 60.0
            : Math.Max(0.0, Math.Min(0.25, now - _lastTickSeconds));
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

        SetDynamicSoundVolume(Clamp01(musicSlider * _duck));
        FeedDynamicSound();
    }

    private static float Clamp01(float value)
    {
        return Math.Max(0f, Math.Min(1f, value));
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
        return last > 0 && DateTime.UtcNow.Ticks - last <= TimeSpan.FromSeconds(RadioHealthySeconds).Ticks;
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
                using (var resampler = new MediaFoundationResampler(
                    reader,
                    new WaveFormat(TargetSampleRate, TargetBits, TargetChannels)))
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
            var playlist = DownloadText(
                "https://rainwave.cc/tune_in/" + _stationId + ".mp3.m3u",
                5000);

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

        return "https://gamestream.rainwave.cc/" + _stationMount + ".mp3";
    }

    private static void StartMetadataWorker()
    {
        if (!_showNowPlaying)
            return;

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
                var json = DownloadText("https://rainwave.cc/api4/info?sid=" + _stationId, 5000);
                string display;
                if (TryParseNowPlaying(json, out display))
                    SetNowPlaying(display);
            }
            catch
            {
            }

            Thread.Sleep(1500);
        }
    }

    private static bool TryParseNowPlaying(string json, out string display)
    {
        display = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        var current = CurrentSongRegex.Match(json);
        if (!current.Success)
            return false;

        var title = UnescapeJsonString(current.Groups[1].Value).Trim();
        if (title.Length == 0)
            return false;

        var artists = ArtistRegex.Matches(current.Groups[2].Value)
            .Cast<Match>()
            .Select(match => UnescapeJsonString(match.Groups[1].Value).Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        display = artists.Length == 0
            ? "Now playing: " + title
            : "Now playing: " + string.Join(", ", artists) + " - " + title;
        return true;
    }

    private static string UnescapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0)
            return value ?? string.Empty;

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c != '\\' || i + 1 >= value.Length)
            {
                builder.Append(c);
                continue;
            }

            c = value[++i];
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
                    if (i + 4 < value.Length)
                    {
                        int code;
                        if (int.TryParse(value.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        {
                            builder.Append((char)code);
                            i += 4;
                            break;
                        }
                    }
                    builder.Append('u');
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }
        return builder.ToString();
    }

    private static string DownloadText(string url, int timeoutMilliseconds)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "GET";
        request.UserAgent = "gloader-rainwave-radio/0.2";
        request.Accept = "*/*";
        request.Timeout = timeoutMilliseconds;
        request.ReadWriteTimeout = timeoutMilliseconds;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

        using (var response = (HttpWebResponse)request.GetResponse())
        using (var stream = response.GetResponseStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            return reader.ReadToEnd();
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

            if (_soundVolumeProperty == null ||
                _pendingBufferCountProperty == null ||
                _submitBufferMethod == null ||
                _playMethod == null)
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
            var pending = Convert.ToInt32(
                _pendingBufferCountProperty.GetValue(_dynamicSound, null),
                CultureInfo.InvariantCulture);

            while (pending < DesiredPendingBuffers)
            {
                byte[] chunk;
                if (!TryDequeueAudio(out chunk))
                    break;

                _submitBufferMethod.Invoke(_dynamicSound, new object[] { chunk });
                pending++;
            }

            if (pending < 2)
                return;

            var state = _soundStateProperty == null
                ? null
                : _soundStateProperty.GetValue(_dynamicSound, null);

            if (state == null || !string.Equals(state.ToString(), "Playing", StringComparison.OrdinalIgnoreCase))
                _playMethod.Invoke(_dynamicSound, null);
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
            _soundVolumeProperty.SetValue(_dynamicSound, Clamp01(volume), null);
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

        var screenHeight = screenHeightField == null
            ? 720
            : Convert.ToInt32(screenHeightField.GetValue(null), CultureInfo.InvariantCulture);
        var position = Activator.CreateInstance(vector2Type, new object[] { 20f, Math.Max(20f, screenHeight - 92f) });
        var zero = GetStaticMember(vector2Type, "Zero");
        var one = GetStaticMember(vector2Type, "One");

        var brightness = 255;
        if (mouseTextColorField != null)
        {
            try { brightness = Convert.ToInt32(mouseTextColorField.GetValue(null), CultureInfo.InvariantCulture); }
            catch { }
        }
        brightness = Math.Max(0, Math.Min(255, brightness));
        var a = Math.Max(0, Math.Min(255, (int)Math.Round(255.0 * alpha)));
        var color = CreateColor(colorType, brightness, brightness, brightness, a);
        if (color == null || zero == null || one == null)
            return;

        var draw = chatManagerType.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .FirstOrDefault(method =>
                method.Name == "DrawColorCodedStringWithShadow" && method.GetParameters().Length == 10);
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
