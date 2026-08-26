#if !GLOADER_SERVER
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using HarmonyLib;

internal static partial class RainwaveRadio
{
    // GTT's spoiler stream intentionally keeps ICY metadata populated even while
    // the station is running its music-guessing game.
    private const string GttStreamUrl = "https://icecast.gttradio.com/mp3_320k";
    private static int _providerPatchesInstalled;

    private static void EnsureProviderPatches()
    {
        if (Interlocked.Exchange(ref _providerPatchesInstalled, 1) != 0)
            return;

        var resolveStreamUrl = AccessTools.Method(
            typeof(RainwaveRadio),
            "ResolveStreamUrl",
            Type.EmptyTypes);
        var downloadText = AccessTools.Method(
            typeof(RainwaveRadio),
            "DownloadText",
            new[] { typeof(string), typeof(int) });

        if (resolveStreamUrl == null || downloadText == null)
            throw new MissingMethodException("VGMRadio could not find its stream helpers.");

        // Main.cs already owns this Harmony id, so its initialization cleanup also
        // removes these provider hooks if the radio fails to initialize.
        var harmony = new Harmony("gloader.mod.rainwaveradio");
        harmony.Patch(
            resolveStreamUrl,
            prefix: new HarmonyMethod(AccessTools.Method(
                typeof(RainwaveRadio),
                nameof(ResolveProviderStreamPrefix))));
        harmony.Patch(
            downloadText,
            prefix: new HarmonyMethod(AccessTools.Method(
                typeof(RainwaveRadio),
                nameof(DownloadProviderMetadataPrefix))));
    }

    private static bool ResolveProviderStreamPrefix(ref string __result)
    {
        if (_source != VgmSource.Gtt)
            return true;

        __result = GttStreamUrl;
        return false;
    }

    private static bool DownloadProviderMetadataPrefix(
        string url,
        int timeoutMilliseconds,
        ref string __result)
    {
        if (_source != VgmSource.Gtt ||
            string.IsNullOrEmpty(url) ||
            !url.StartsWith("https://rainwave.cc/api4/info", StringComparison.OrdinalIgnoreCase))
            return true;

        __result = DownloadGttMetadataAsRainwaveJson(timeoutMilliseconds);
        return false;
    }

    private static string DownloadGttMetadataAsRainwaveJson(int timeoutMilliseconds)
    {
        var request = (HttpWebRequest)WebRequest.Create(GttStreamUrl);
        request.Method = "GET";
        request.UserAgent = "gloader-vgm-radio/0.3";
        request.Accept = "*/*";
        request.Timeout = timeoutMilliseconds;
        request.ReadWriteTimeout = timeoutMilliseconds;
        request.KeepAlive = false;
        request.Headers["Icy-MetaData"] = "1";

        using (var response = (HttpWebResponse)request.GetResponse())
        {
            int metadataInterval;
            if (!int.TryParse(
                    response.GetResponseHeader("icy-metaint"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out metadataInterval) ||
                metadataInterval <= 0)
                throw new InvalidDataException("GTT stream did not provide an ICY metadata interval.");

            using (var stream = response.GetResponseStream())
            {
                if (stream == null)
                    throw new EndOfStreamException("GTT stream returned no response body.");

                // A new Icecast connection normally receives the title in its first
                // metadata block. Permit a few empty blocks because zero-length ICY
                // blocks are legal when metadata has not changed.
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    SkipExactly(stream, metadataInterval);
                    var lengthByte = stream.ReadByte();
                    if (lengthByte < 0)
                        throw new EndOfStreamException("GTT stream ended before its metadata block.");

                    var metadataBytes = lengthByte * 16;
                    if (metadataBytes == 0)
                        continue;

                    var buffer = new byte[metadataBytes];
                    ReadExactly(stream, buffer, 0, buffer.Length);
                    var metadata = Encoding.UTF8.GetString(buffer).TrimEnd('\0');
                    var title = ExtractIcyStreamTitle(metadata);
                    if (!string.IsNullOrWhiteSpace(title))
                        return BuildRainwaveCompatibleMetadata(title.Trim());
                }
            }
        }

        throw new InvalidDataException("GTT stream did not provide a current song title.");
    }

    private static void SkipExactly(Stream stream, int byteCount)
    {
        var buffer = new byte[Math.Min(8192, Math.Max(1, byteCount))];
        var remaining = byteCount;
        while (remaining > 0)
        {
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read <= 0)
                throw new EndOfStreamException("Radio stream ended while reading audio metadata.");
            remaining -= read;
        }
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            var read = stream.Read(buffer, offset, count);
            if (read <= 0)
                throw new EndOfStreamException("Radio stream ended while reading an ICY metadata block.");
            offset += read;
            count -= read;
        }
    }

    private static string ExtractIcyStreamTitle(string metadata)
    {
        const string marker = "StreamTitle='";
        var start = metadata.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += marker.Length;
        var end = metadata.IndexOf("';", start, StringComparison.Ordinal);
        if (end < 0)
            end = metadata.IndexOf('\'', start);
        if (end < 0)
            return null;

        return metadata.Substring(start, end - start);
    }

    private static string BuildRainwaveCompatibleMetadata(string title)
    {
        return "{\"sched_current\":{\"song_data\":{\"title\":\"" +
               EscapeJson(title) +
               "\",\"artists\":[]}}}";
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length + 16);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        builder.Append("\\u");
                        builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }
                    break;
            }
        }

        return builder.ToString();
    }
}
#endif
