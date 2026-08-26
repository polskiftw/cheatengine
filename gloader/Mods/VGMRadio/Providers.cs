#if !GLOADER_SERVER
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

internal static partial class VGMRadio
{
    // GTT's spoiler stream keeps ICY song metadata populated even while the
    // station is running its music-guessing game.
    private const string GttStreamUrl = "https://icecast.gttradio.com/mp3_320k";

    private static readonly Regex RainwaveCurrentSongRegex = new Regex(
        @"""sched_current""\s*:\s*\{.*?""song_data""\s*:\s*\{.*?""title""\s*:\s*""((?:\\.|[^""\\])*)"".*?""artists""\s*:\s*\[(.*?)\]",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex RainwaveArtistRegex = new Regex(
        @"""name""\s*:\s*""((?:\\.|[^""\\])*)""",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static string ResolveStreamUrl()
    {
        return _source == VgmSource.Gtt
            ? GttStreamUrl
            : ResolveRainwaveStreamUrl();
    }

    private static string ResolveRainwaveStreamUrl()
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

    private static bool TryGetProviderNowPlaying(out string display)
    {
        return _source == VgmSource.Gtt
            ? TryGetGttNowPlaying(out display)
            : TryGetRainwaveNowPlaying(out display);
    }

    private static bool TryGetRainwaveNowPlaying(out string display)
    {
        display = null;

        var json = DownloadText("https://rainwave.cc/api4/info?sid=" + _stationId, 5000);
        if (string.IsNullOrWhiteSpace(json))
            return false;

        var current = RainwaveCurrentSongRegex.Match(json);
        if (!current.Success)
            return false;

        var title = UnescapeJsonString(current.Groups[1].Value).Trim();
        if (title.Length == 0)
            return false;

        var artists = RainwaveArtistRegex.Matches(current.Groups[2].Value)
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

    private static bool TryGetGttNowPlaying(out string display)
    {
        display = null;
        var title = DownloadGttStreamTitle(5000);
        if (string.IsNullOrWhiteSpace(title))
            return false;

        display = "Now playing: " + title.Trim();
        return true;
    }

    private static string DownloadGttStreamTitle(int timeoutMilliseconds)
    {
        var request = (HttpWebRequest)WebRequest.Create(GttStreamUrl);
        request.Method = "GET";
        request.UserAgent = "gloader-vgm-radio/0.4";
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
                        return title.Trim();
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
                        if (int.TryParse(
                            value.Substring(i + 1, 4),
                            NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture,
                            out code))
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
}
#endif
