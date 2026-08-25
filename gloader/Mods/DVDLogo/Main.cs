#if !GLOADER_SERVER
using System;
using System.Diagnostics;
using System.IO;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

internal static class DvdLogoScreensaver
{
    private const float Speed = 190f;
    private const double MaxFrameSeconds = 0.05;
    private const int DefaultLogoWidth = 192;
    private const int MinimumLogoWidth = 32;
    private const int MaximumLogoWidth = 2048;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly Random Random = new Random();

    private static Texture2D _logo;
    private static Vector2 _position;
    private static Vector2 _velocity;
    private static Color _color = Color.White;
    private static float _hue;
    private static float _scale = 1f;
    private static int _logoWidth = DefaultLogoWidth;
    private static double _lastSeconds = -1.0;
    private static bool _initialized;
    private static bool _disabled;

    [HarmonyPatch(typeof(Main), "DrawInterface_33_MouseText")]
    private static class DrawPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            Draw();
        }
    }

    private static void Draw()
    {
        if (_disabled)
            return;

        try
        {
            var spriteBatch = Main.spriteBatch;
            if (spriteBatch == null)
                return;

            EnsureLogo(spriteBatch.GraphicsDevice);
            if (_logo == null)
                return;

            if (!_initialized)
                InitializeMotion();

            var now = Clock.Elapsed.TotalSeconds;
            if (_lastSeconds < 0.0)
                _lastSeconds = now;

            var elapsed = Math.Max(0.0, Math.Min(MaxFrameSeconds, now - _lastSeconds));
            _lastSeconds = now;

            Move((float)elapsed);

            spriteBatch.Draw(
                _logo,
                _position,
                null,
                _color,
                0f,
                Vector2.Zero,
                _scale,
                SpriteEffects.None,
                0f);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("DVDLogo disabled after an error: " + ex);
            _disabled = true;
        }
    }

    private static void EnsureLogo(GraphicsDevice graphicsDevice)
    {
        if (_logo != null)
            return;

        var modDirectory = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Mods",
            "DVDLogo");

        using (var stream = File.OpenRead(Path.Combine(modDirectory, "dvd-logo.png")))
            _logo = Texture2D.FromStream(graphicsDevice, stream);

        PremultiplyAlpha(_logo);
        LoadSettings(Path.Combine(modDirectory, "DVDLogo.ini"));
        _scale = _logo.Width > 0 ? _logoWidth / (float)_logo.Width : 1f;
    }

    private static void LoadSettings(string path)
    {
        _logoWidth = DefaultLogoWidth;

        try
        {
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

                int width;
                if (string.Equals(key, "Width", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(value, out width))
                {
                    _logoWidth = Math.Max(MinimumLogoWidth, Math.Min(MaximumLogoWidth, width));
                }
            }
        }
        catch
        {
            _logoWidth = DefaultLogoWidth;
        }
    }

    private static void InitializeMotion()
    {
        var maxX = Math.Max(0f, Main.screenWidth - RenderedWidth);
        var maxY = Math.Max(0f, Main.screenHeight - RenderedHeight);

        _position = new Vector2(
            maxX > 0f ? (float)(Random.NextDouble() * maxX) : 0f,
            maxY > 0f ? (float)(Random.NextDouble() * maxY) : 0f);

        var angle = (25.0 + Random.NextDouble() * 40.0) * Math.PI / 180.0;
        var xSign = Random.Next(2) == 0 ? -1f : 1f;
        var ySign = Random.Next(2) == 0 ? -1f : 1f;

        _velocity = new Vector2(
            (float)Math.Cos(angle) * Speed * xSign,
            (float)Math.Sin(angle) * Speed * ySign);

        _hue = (float)(Random.NextDouble() * 360.0);
        _color = HsvToColor(_hue, 0.9f, 1f);
        _initialized = true;
    }

    private static float RenderedWidth
    {
        get { return _logo == null ? 0f : _logo.Width * _scale; }
    }

    private static float RenderedHeight
    {
        get { return _logo == null ? 0f : _logo.Height * _scale; }
    }

    private static void PremultiplyAlpha(Texture2D texture)
    {
        var pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);

        for (var i = 0; i < pixels.Length; i++)
        {
            var pixel = pixels[i];
            if (pixel.A == 255)
                continue;

            if (pixel.A == 0)
            {
                pixels[i] = Color.Transparent;
                continue;
            }

            pixels[i] = new Color(
                (byte)(pixel.R * pixel.A / 255),
                (byte)(pixel.G * pixel.A / 255),
                (byte)(pixel.B * pixel.A / 255),
                pixel.A);
        }

        texture.SetData(pixels);
    }

    private static void Move(float elapsedSeconds)
    {
        if (elapsedSeconds <= 0f)
            return;

        _position += _velocity * elapsedSeconds;

        var maxX = Math.Max(0f, Main.screenWidth - RenderedWidth);
        var maxY = Math.Max(0f, Main.screenHeight - RenderedHeight);
        var bounced = false;

        if (maxX <= 0f)
        {
            _position.X = 0f;
        }
        else if (_position.X < 0f)
        {
            _position.X = -_position.X;
            _velocity.X = Math.Abs(_velocity.X);
            bounced = true;
        }
        else if (_position.X > maxX)
        {
            _position.X = maxX - (_position.X - maxX);
            _velocity.X = -Math.Abs(_velocity.X);
            bounced = true;
        }

        if (maxY <= 0f)
        {
            _position.Y = 0f;
        }
        else if (_position.Y < 0f)
        {
            _position.Y = -_position.Y;
            _velocity.Y = Math.Abs(_velocity.Y);
            bounced = true;
        }
        else if (_position.Y > maxY)
        {
            _position.Y = maxY - (_position.Y - maxY);
            _velocity.Y = -Math.Abs(_velocity.Y);
            bounced = true;
        }

        if (bounced)
            ChangeColor();
    }

    private static void ChangeColor()
    {
        _hue = (_hue + 70f + (float)(Random.NextDouble() * 220.0)) % 360f;
        _color = HsvToColor(_hue, 0.9f, 1f);
    }

    private static Color HsvToColor(float hue, float saturation, float value)
    {
        hue %= 360f;
        if (hue < 0f)
            hue += 360f;

        var chroma = value * saturation;
        var sector = hue / 60f;
        var x = chroma * (1f - Math.Abs(sector % 2f - 1f));

        float r;
        float g;
        float b;

        if (sector < 1f)
        {
            r = chroma; g = x; b = 0f;
        }
        else if (sector < 2f)
        {
            r = x; g = chroma; b = 0f;
        }
        else if (sector < 3f)
        {
            r = 0f; g = chroma; b = x;
        }
        else if (sector < 4f)
        {
            r = 0f; g = x; b = chroma;
        }
        else if (sector < 5f)
        {
            r = x; g = 0f; b = chroma;
        }
        else
        {
            r = chroma; g = 0f; b = x;
        }

        var m = value - chroma;
        return new Color(
            (byte)((r + m) * 255f),
            (byte)((g + m) * 255f),
            (byte)((b + m) * 255f),
            255);
    }
}
#endif
