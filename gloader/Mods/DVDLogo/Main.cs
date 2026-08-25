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
    private const float HorizontalSpeed = 205f;
    private const float VerticalSpeed = 153f;
    private const double MaxFrameSeconds = 0.05;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly Color[] Colors =
    {
        new Color(255, 70, 70),
        new Color(255, 170, 45),
        new Color(255, 235, 55),
        new Color(80, 235, 105),
        new Color(55, 220, 235),
        new Color(80, 130, 255),
        new Color(185, 90, 255),
        new Color(255, 80, 195)
    };

    private static Texture2D _logo;
    private static Vector2 _position = new Vector2(64f, 64f);
    private static Vector2 _velocity = new Vector2(HorizontalSpeed, VerticalSpeed);
    private static double _lastSeconds = -1.0;
    private static int _colorIndex;
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

            var now = Clock.Elapsed.TotalSeconds;
            if (_lastSeconds < 0.0)
            {
                _lastSeconds = now;
                ClampIntoScreen();
            }

            var elapsed = Math.Max(0.0, Math.Min(MaxFrameSeconds, now - _lastSeconds));
            _lastSeconds = now;

            Move((float)elapsed);

            spriteBatch.Draw(
                _logo,
                _position,
                null,
                Colors[_colorIndex],
                0f,
                Vector2.Zero,
                1f,
                SpriteEffects.None,
                0f);
        }
        catch
        {
            // A cosmetic overlay should never be able to crash Terraria.
            _disabled = true;
        }
    }

    private static void EnsureLogo(GraphicsDevice graphicsDevice)
    {
        if (_logo != null)
            return;

        var path = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Mods",
            "DVDLogo",
            "dvd-logo.png");

        using (var stream = File.OpenRead(path))
            _logo = Texture2D.FromStream(graphicsDevice, stream);

        PremultiplyAlpha(_logo);
        ClampIntoScreen();
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

        var maxX = Math.Max(0f, Main.screenWidth - _logo.Width);
        var maxY = Math.Max(0f, Main.screenHeight - _logo.Height);
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
            _colorIndex = (_colorIndex + 1) % Colors.Length;
    }

    private static void ClampIntoScreen()
    {
        if (_logo == null)
            return;

        var maxX = Math.Max(0f, Main.screenWidth - _logo.Width);
        var maxY = Math.Max(0f, Main.screenHeight - _logo.Height);
        _position.X = Math.Max(0f, Math.Min(maxX, _position.X));
        _position.Y = Math.Max(0f, Math.Min(maxY, _position.Y));
    }
}
#endif
