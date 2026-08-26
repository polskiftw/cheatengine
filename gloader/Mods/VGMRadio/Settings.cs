#if !GLOADER_SERVER
using System;
using System.IO;

internal enum VgmSource
{
    Rainwave,
    Gtt
}

internal static partial class RainwaveRadio
{
    private static VgmSource _source = VgmSource.Rainwave;

    private static void LoadSettings()
    {
        _source = VgmSource.Rainwave;
        _stationId = DefaultStationId;
        _stationMount = DefaultStationMount;
        _showNowPlaying = true;

        // These provider hooks must exist before Initialize starts either worker thread.
        EnsureProviderPatches();

        try
        {
            var path = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Mods",
                "VGMRadio",
                "VGMRadio.ini");

            if (!File.Exists(path))
                return;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 ||
                    line.StartsWith("#", StringComparison.Ordinal) ||
                    line.StartsWith(";", StringComparison.Ordinal))
                    continue;

                var equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                var key = line.Substring(0, equals).Trim();
                var value = line.Substring(equals + 1).Trim();

                if (string.Equals(key, "Source", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "Provider", StringComparison.OrdinalIgnoreCase))
                {
                    ApplySource(value);
                }
                else if (string.Equals(key, "Station", StringComparison.OrdinalIgnoreCase))
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
            _source = VgmSource.Rainwave;
            _stationId = DefaultStationId;
            _stationMount = DefaultStationMount;
            _showNowPlaying = true;
        }
    }

    private static void ApplySource(string value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .ToLowerInvariant();

        switch (normalized)
        {
            case "gtt":
            case "gamethattune":
            case "gamethattuneradio":
                _source = VgmSource.Gtt;
                return;

            case "rainwave":
            default:
                _source = VgmSource.Rainwave;
                return;
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

            case "all":
            default:
                _stationId = DefaultStationId;
                _stationMount = DefaultStationMount;
                return;
        }
    }
}
#endif
