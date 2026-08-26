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
        VGMRadio.Initialize();
    }
}

internal static partial class VGMRadio
{
    private const string HarmonyId = "gloader.mod.vgmradio";

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
                prefix: new HarmonyMethod(AccessTools.Method(typeof(VGMRadio), nameof(UpdateAudioPrefix))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(VGMRadio), nameof(UpdateAudioPostfix))));

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
            Name = "gloader VGMRadio audio"
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
                            throw new EndOfStreamException("Radio stream ended.");

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

    private static string DownloadText(string url, int timeoutMilliseconds)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "GET";
        request.UserAgent = "gloader-vgm-radio/0.4";
        request.Accept = "*/*";
        request.Timeout = timeoutMilliseconds;
        request.ReadWriteTimeout = timeoutMilliseconds;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

        using (var response = (HttpWebResponse)request.GetResponse())
        using (var stream = response.GetResponseStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            return reader.ReadToEnd();
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
}
#endif
