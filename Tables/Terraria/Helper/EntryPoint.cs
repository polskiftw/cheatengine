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
    private const string LuckyHarmonyId = "polskiftw.cheatengine.terraria.lucky-treasure-bags";
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
                "lucky-on" => LuckyTreasureBags.Enable() ? Success : Failure,
                "lucky-off" => LuckyTreasureBags.Disable() ? Success : Failure,
                "lucky-status" => LuckyTreasureBags.IsEnabled() ? Success : 0,
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

    private static Type FindTerrariaType(string fullName)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .OrderByDescending(a => string.Equals(a.GetName().Name, "TerrariaRelease", StringComparison.Ordinal))
            .Select(a =>
            {
                try { return a.GetType(fullName, throwOnError: false, ignoreCase: false); }
                catch { return null; }
            })
            .FirstOrDefault(t => t is not null)
            ?? throw new MissingMemberException($"{fullName} was not found in the current CoreCLR process.");
    }

    private static class FishCrates
    {
        private static MethodInfo FindTarget()
        {
            var projectile = FindTerrariaType("Terraria.Projectile");
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

    private static class LuckyTreasureBags
    {
        private sealed record Drop(int Item, int Stack = 1);

        private sealed class State
        {
            public required object Player { get; init; }
            public required int Bag { get; init; }
            public object? Source { get; set; }
            public HashSet<int> Seen { get; } = new();
            public bool Active { get; set; } = true;
            public bool InDevArmor { get; set; }
            public bool DevArmorSeen { get; set; }
        }

        private sealed record Targets(MethodInfo OpenBossBag, MethodInfo QuickSpawnItem, MethodInfo TryGettingDevArmor);

        [ThreadStatic]
        private static State? _state;

        // Independent chance rolls only. Random-choice groups and guaranteed drops are intentionally absent.
        // This map is regenerated from the clean Terraria 1.4.5.8 Player.OpenBossBag source.
        private static readonly IReadOnlyDictionary<int, Drop[]> ChanceDrops = new Dictionary<int, Drop[]>
        {
            [3318] = new[] { new Drop(2430), new Drop(2493), new Drop(1309) },
            [3319] = new[] { new Drop(2112), new Drop(1299) },
            [3320] = new[] { new Drop(994), new Drop(2111) },
            [3321] = new[] { new Drop(2104), new Drop(3060) },
            [3322] = new[] { new Drop(2108), new Drop(1132), new Drop(1170), new Drop(2502), new Drop(5483) },
            [3324] = new[] { new Drop(2105) },
            [3325] = new[] { new Drop(2113) },
            [3326] = new[] { new Drop(2106) },
            [3327] = new[] { new Drop(2107) },
            [3328] = new[] { new Drop(2109), new Drop(1182), new Drop(1305), new Drop(1157), new Drop(3021) },
            [3329] = new[] { new Drop(2110), new Drop(6158), new Drop(1294) },
            [3330] = new[] { new Drop(2588), new Drop(2609) },
            [3331] = new[] { new Drop(3372) },
            [3332] = new[] { new Drop(3373), new Drop(4469) },
            [3860] = new[] { new Drop(3863), new Drop(3883) },
            [4782] = new[] { new Drop(4784), new Drop(4823), new Drop(4715), new Drop(4778, 3), new Drop(5075) },
            [4957] = new[] { new Drop(4959), new Drop(4981), new Drop(4758), new Drop(4980) },
            [5111] = new[] { new Drop(5109), new Drop(5385), new Drop(5098), new Drop(5101), new Drop(5113) }
        };

        // These are the Hardmode boss bags whose vanilla branches call TryGettingDevArmor.
        // Queen Slime (4957) is intentionally excluded because vanilla does not roll a developer set there.
        private static readonly HashSet<int> DevArmorEligible = new()
        {
            3325, 3326, 3327, 3328, 3329, 3330, 3331, 3332, 3860, 4782
        };

        public static bool Enable()
        {
            var targets = FindTargets();
            if (HasOurPatch(targets.OpenBossBag))
                return true;

            var openPrefix = PatchMethod(nameof(OpenPrefix));
            var openPostfix = PatchMethod(nameof(OpenPostfix));
            var quickPrefix = PatchMethod(nameof(QuickSpawnPrefix));
            var devPrefix = PatchMethod(nameof(DevArmorPrefix));
            var devPostfix = PatchMethod(nameof(DevArmorPostfix));
            var harmony = new Harmony(LuckyHarmonyId);

            try
            {
                harmony.Patch(targets.OpenBossBag, prefix: new HarmonyMethod(openPrefix), postfix: new HarmonyMethod(openPostfix));
                harmony.Patch(targets.QuickSpawnItem, prefix: new HarmonyMethod(quickPrefix));
                harmony.Patch(targets.TryGettingDevArmor, prefix: new HarmonyMethod(devPrefix), postfix: new HarmonyMethod(devPostfix));
            }
            catch
            {
                UnpatchTargets(targets);
                throw;
            }

            return HasOurPatch(targets.OpenBossBag);
        }

        public static bool Disable()
        {
            var targets = FindTargets();
            UnpatchTargets(targets);
            _state = null;
            return !HasOurPatch(targets.OpenBossBag);
        }

        public static bool IsEnabled() => HasOurPatch(FindTargets().OpenBossBag);

        private static Targets FindTargets()
        {
            var player = FindTerrariaType("Terraria.Player");
            var entitySource = FindTerrariaType("Terraria.DataStructures.IEntitySource");
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var open = ExactMethod(player, "OpenBossBag", flags,
                p => p.Length == 1 && p[0].ParameterType == typeof(int));
            var quick = ExactMethod(player, "QuickSpawnItem", flags,
                p => p.Length == 3 &&
                     p[0].ParameterType == entitySource &&
                     p[1].ParameterType == typeof(int) &&
                     p[2].ParameterType == typeof(int));
            var dev = ExactMethod(player, "TryGettingDevArmor", flags,
                p => p.Length == 1 && p[0].ParameterType == entitySource);

            return new Targets(open, quick, dev);
        }

        private static MethodInfo ExactMethod(Type type, string name, BindingFlags flags, Func<ParameterInfo[], bool> predicate)
        {
            var matches = type.GetMethods(flags).Where(m => m.Name == name && predicate(m.GetParameters())).ToArray();
            return matches.Length switch
            {
                1 => matches[0],
                0 => throw new MissingMethodException($"Required Terraria method was not found: {type.FullName}.{name}"),
                _ => throw new AmbiguousMatchException($"Multiple matching Terraria methods were found: {type.FullName}.{name}")
            };
        }

        private static MethodInfo PatchMethod(string name) =>
            typeof(LuckyTreasureBags).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(name);

        private static bool HasOurPatch(MethodBase target)
        {
            var info = Harmony.GetPatchInfo(target);
            return info is not null &&
                   (info.Prefixes.Any(p => p.owner == LuckyHarmonyId) || info.Postfixes.Any(p => p.owner == LuckyHarmonyId));
        }

        private static void UnpatchTargets(Targets targets)
        {
            var harmony = new Harmony(LuckyHarmonyId);
            harmony.Unpatch(targets.OpenBossBag, HarmonyPatchType.All, LuckyHarmonyId);
            harmony.Unpatch(targets.QuickSpawnItem, HarmonyPatchType.All, LuckyHarmonyId);
            harmony.Unpatch(targets.TryGettingDevArmor, HarmonyPatchType.All, LuckyHarmonyId);
        }

        private static void OpenPrefix(object __instance, object[] __args)
        {
            if (__args.Length != 1 || __args[0] is not int bag)
                throw new InvalidOperationException("Player.OpenBossBag no longer has the expected int bag argument.");

            _state = new State { Player = __instance, Bag = bag };
        }

        private static void QuickSpawnPrefix(object[] __args)
        {
            var state = _state;
            if (state is null || !state.Active || __args.Length < 2)
                return;

            if (state.Source is null && __args[0] is not null)
                state.Source = __args[0];

            if (__args[1] is int item && item > 0)
                state.Seen.Add(item);

            if (state.InDevArmor)
                state.DevArmorSeen = true;
        }

        private static void DevArmorPrefix(object[] __args)
        {
            var state = _state;
            if (state is null || !state.Active)
                return;

            if (state.Source is null && __args.Length > 0 && __args[0] is not null)
                state.Source = __args[0];

            state.InDevArmor = true;
        }

        private static void DevArmorPostfix()
        {
            var state = _state;
            if (state is not null)
                state.InDevArmor = false;
        }

        private static void OpenPostfix(object __instance)
        {
            var state = _state;
            if (state is null || !state.Active || !ReferenceEquals(state.Player, __instance))
                return;

            try
            {
                Supplement(state);
            }
            finally
            {
                state.Active = false;
                state.InDevArmor = false;
                _state = null;
            }
        }

        private static void Supplement(State state)
        {
            var hasChanceDrops = ChanceDrops.TryGetValue(state.Bag, out var drops);
            var devEligible = DevArmorEligible.Contains(state.Bag);
            if (!hasChanceDrops && !devEligible)
                return;

            if (state.Source is null)
                throw new InvalidOperationException($"Lucky Treasure Bags did not observe an item source while opening bag {state.Bag}.");

            var targets = FindTargets();

            if (hasChanceDrops)
            {
                foreach (var drop in drops)
                {
                    if (state.Seen.Contains(drop.Item))
                        continue;

                    targets.QuickSpawnItem.Invoke(state.Player, new object?[] { state.Source, drop.Item, drop.Stack });
                }
            }

            if (!devEligible || state.DevArmorSeen)
                return;

            for (var attempt = 0; attempt < 512 && !state.DevArmorSeen; attempt++)
                targets.TryGettingDevArmor.Invoke(state.Player, new object?[] { state.Source });

            if (!state.DevArmorSeen)
                throw new InvalidOperationException($"Lucky Treasure Bags could not obtain a developer set for eligible bag {state.Bag} after 512 vanilla attempts.");
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
