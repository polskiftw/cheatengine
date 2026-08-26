#if !GLOADER_SERVER
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;

internal static partial class VGMRadio
{
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
                prefix: new HarmonyMethod(AccessTools.Method(typeof(VGMRadio), nameof(DrawOverlayPrefix))));
        }
        catch
        {
            // UI changes should never disable the radio itself.
            _overlayAvailable = false;
        }
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

    private static void StartMetadataWorker()
    {
        if (!_showNowPlaying)
            return;

        var thread = new Thread(MetadataWorker)
        {
            IsBackground = true,
            Name = "gloader VGMRadio metadata"
        };
        thread.Start();
    }

    private static void MetadataWorker()
    {
        while (true)
        {
            try
            {
                string display;
                if (TryGetProviderNowPlaying(out display))
                    SetNowPlaying(display);
            }
            catch
            {
            }

            Thread.Sleep(1500);
        }
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
        var position = Activator.CreateInstance(
            vector2Type,
            new object[] { 20f, Math.Max(20f, screenHeight - 92f) });
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
                method.Name == "DrawColorCodedStringWithShadow" &&
                method.GetParameters().Length == 10);
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
