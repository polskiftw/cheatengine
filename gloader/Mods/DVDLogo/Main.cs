#if !GLOADER_SERVER
using System;
using System.Diagnostics;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

internal static class DvdLogoScreensaver
{
    private const int LogoWidth = 64;
    private const int LogoHeight = 32;
    private const float HorizontalSpeed = 205f;
    private const float VerticalSpeed = 153f;
    private const double MaxFrameSeconds = 0.05;

    // 64x32 one-bit mask derived from the supplied DVD logo.
    // Rogue Chaos uses the same basic idea: a monochrome mask is tinted at draw time.
    private const string LogoBitsBase64 =
        "AAAAAAAAAAAAAfg4HPwAAAAD/jwd/wAAAAP/PD3/gAAAA48cOefAAAADh5x5w8AAAAeHnHHDwAAAB4ec8cPAAAAHh5zjw8AAAAcHH+PDgAAABw8fw4eAAAAHfg/D/wAAAA/8D4P/AAAAD/gPh/wAAAAHAAYDwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/AAAAAAAH////4AAAAP/AAAP/AAAHwAAAAAPgAA4AAAAAAHAAGAAAAAAAGAAQAAAVgAAIABwAAf+AADgAB4AB/4AB4AAB/gAAAH+AAAAf////+AAAAAAf//gAAAAAAAAAAAAAAAAAAAAAAAAA==";

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
    private static Vector2 _position = new Vector2(96f, 96f);
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
                new Vector2(LogoWidth / 2f, LogoHeight / 2f),
                1f,
                SpriteEffects.None,
                0f);
        }
        catch
        {
            // Cosmetic overlay only: a failure must never take Terraria down with it.
            _disabled = true;
        }
    }

    private static void EnsureLogo(GraphicsDevice graphicsDevice)
    {
        if (_logo != null)
            return;

        var packed = Convert.FromBase64String(LogoBitsBase64);
        if (packed.Length != (LogoWidth * LogoHeight) / 8)
            throw new InvalidOperationException("Embedded DVD logo mask has the wrong size.");

        var pixels = new Color[LogoWidth * LogoHeight];
        for (var i = 0; i < pixels.Length; i++)
        {
            var bit = (packed[i >> 3] >> (7 - (i & 7))) & 1;
            pixels[i] = bit == 0 ? Color.Transparent : Color.White;
        }

        _logo = new Texture2D(graphicsDevice, LogoWidth, LogoHeight);
        _logo.SetData(pixels);
        ClampIntoScreen();
    }

    private static void Move(float elapsedSeconds)
    {
        if (elapsedSeconds <= 0f)
            return;

        _position += _velocity * elapsedSeconds;

        var halfWidth = LogoWidth / 2f;
        var halfHeight = LogoHeight / 2f;
        var minX = halfWidth;
        var maxX = Math.Max(halfWidth, Main.screenWidth - halfWidth);
        var minY = halfHeight;
        var maxY = Math.Max(halfHeight, Main.screenHeight - halfHeight);
        var bounced = false;

        if (_position.X < minX)
        {
            _position.X = minX + (minX - _position.X);
            _velocity.X = Math.Abs(_velocity.X);
            bounced = true;
        }
        else if (_position.X > maxX)
        {
            _position.X = maxX - (_position.X - maxX);
            _velocity.X = -Math.Abs(_velocity.X);
            bounced = true;
        }

        if (_position.Y < minY)
        {
            _position.Y = minY + (minY - _position.Y);
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
        var halfWidth = LogoWidth / 2f;
        var halfHeight = LogoHeight / 2f;
        var minX = halfWidth;
        var maxX = Math.Max(halfWidth, Main.screenWidth - halfWidth);
        var minY = halfHeight;
        var maxY = Math.Max(halfHeight, Main.screenHeight - halfHeight);

        _position.X = Math.Max(minX, Math.Min(maxX, _position.X));
        _position.Y = Math.Max(minY, Math.Min(maxY, _position.Y));
    }
}
#endif
