using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace TerrariaCEHelper;

public static class EntryPoint
{
    private const int Success = 23063;
    private const int BadCommand = -20;
    private const int Failure = -21;
    private const string FishHarmonyId = "polskiftw.cheatengine.terraria.all-fish-are-crates";
    private const string SelfTestHarmonyId = "polskiftw.cheatengine.terraria.selftest";

    public static int Run(string command)
    {
        try
        {
            EnsureHarmonyLoaded();
            return command?.Trim().ToLowerInvariant() switch
            {
                "selftest" => SelfTest.Run() ? Success : Failure,
                "fish-on" => FishCrates.Enable() ? Success : Failure,
                "fish-off" => FishCrates.Disable() ? Success : Failure,
                "fish-status" => FishCrates.IsEnabled() ? Success : 0,
                _ => BadCommand
            };
        }
        catch (Exception ex)
        {
            WriteDiagnostic(ex);
            return Failure;
        }
    }

    private static void EnsureHarmonyLoaded()
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "0Harmony"))
            return;

        var assemblyDir = Path.GetDirectoryName(typeof(EntryPoint).Assembly.Location)
            ?? throw new InvalidOperationException("TerrariaCEHelper assembly directory is unavailable.");
        var harmonyPath = Path.Combine(assemblyDir, "0Harmony.dll");
        if (!File.Exists(harmonyPath))
            throw new FileNotFoundException("0Harmony.dll must be beside TerrariaCEHelper.dll.", harmonyPath);

        Assembly.LoadFrom(harmonyPath);
    }

    private static void WriteDiagnostic(Exception ex)
    {
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "TerrariaCEHelper-error.txt"), ex.ToString());
        }
        catch
        {
        }
    }

    private static class FishCrates
    {
        private static MethodInfo FindTarget()
        {
            var projectile = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a =>
                {
                    try { return a.GetType("Terraria.Projectile", throwOnError: false, ignoreCase: false); }
                    catch { return null; }
                })
                .FirstOrDefault(t => t is not null)
                ?? throw new MissingMemberException("Terraria.Projectile was not found in the current CoreCLR process.");

            var byRefBool = typeof(bool).MakeByRefType();
            var methods = projectile.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == "FishingCheck_RollDropLevels")
                .Where(m =>
                {
                    var p = m.GetParameters();
                    return p.Length > 0 && p[^1].ParameterType == byRefBool;
                })
                .ToArray();

            return methods.Length switch
            {
                1 => methods[0],
                0 => throw new MissingMethodException("Terraria.Projectile.FishingCheck_RollDropLevels with a final ref/out bool was not found."),
                _ => throw new AmbiguousMatchException("Multiple FishingCheck_RollDropLevels overloads end in ref/out bool.")
            };
        }

        public static bool Enable()
        {
            var target = FindTarget();
            if (HasOurPostfix(target))
                return true;

            var postfix = typeof(FishCrates).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(Postfix));

            new Harmony(FishHarmonyId).Patch(target, postfix: new HarmonyMethod(postfix));
            return HasOurPostfix(target);
        }

        public static bool Disable()
        {
            var target = FindTarget();
            new Harmony(FishHarmonyId).Unpatch(target, HarmonyPatchType.All, FishHarmonyId);
            return !HasOurPostfix(target);
        }

        public static bool IsEnabled()
        {
            var target = FindTarget();
            return HasOurPostfix(target);
        }

        private static bool HasOurPostfix(MethodBase target)
        {
            var info = Harmony.GetPatchInfo(target);
            return info?.Postfixes.Any(p => p.owner == FishHarmonyId) == true;
        }

        private static void Postfix(object[] __args)
        {
            if (__args.Length == 0 || __args[^1] is not bool)
                throw new InvalidOperationException("FishingCheck_RollDropLevels no longer ends in a bool argument.");

            __args[^1] = true;
        }
    }

    private static class SelfTest
    {
        public static bool Run()
        {
            var target = typeof(SelfTest).GetMethod(nameof(Target), BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(Target));
            var postfix = typeof(SelfTest).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(Postfix));
            var harmony = new Harmony(SelfTestHarmonyId);

            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                Target(123, out var value);
                return value;
            }
            finally
            {
                harmony.Unpatch(target, HarmonyPatchType.All, SelfTestHarmonyId);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Target(int ignored, out bool value)
        {
            _ = ignored;
            value = false;
        }

        private static void Postfix(object[] __args)
        {
            __args[^1] = true;
        }
    }
}
