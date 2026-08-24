using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace GLoader
{
    internal static class ModRuntime
    {
        public static void LoadAll(
            string modsDirectory,
            Assembly gameAssembly,
            string gameDirectory,
            string loaderDirectory,
            bool isServerTarget)
        {
            var mods = ModDiscovery.Discover(modsDirectory);
            Log.Info("Discovered " + mods.Count + " source mod(s).");

            if (mods.Count == 0)
            {
                return;
            }

            var references = ReferenceCollector.Collect(
                gameAssembly,
                gameDirectory,
                loaderDirectory);

            foreach (var mod in mods)
            {
                LoadOne(mod, references, isServerTarget);
            }
        }

        private static void LoadOne(
            ModSource mod,
            System.Collections.Generic.IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference> references,
            bool isServerTarget)
        {
            var harmonyId = "gloader.mod." + mod.Id;
            Harmony harmony = null;

            try
            {
                Log.Info("Compiling mod: " + mod.DisplayName);
                var assembly = ModCompiler.Compile(mod, references, isServerTarget);

                InvokeOptionalLoad(assembly);

                harmony = new Harmony(harmonyId);
                harmony.PatchAll(assembly);

                Log.Info("Loaded mod: " + mod.DisplayName);
            }
            catch (Exception ex)
            {
                try
                {
                    harmony?.UnpatchAll(harmonyId);
                }
                catch (Exception cleanupEx)
                {
                    Log.Warn("Patch cleanup failed for " + mod.DisplayName + ": " + cleanupEx.Message);
                }

                Log.Error("Mod failed: " + mod.DisplayName + Environment.NewLine + Unwrap(ex));
            }
        }

        private static void InvokeOptionalLoad(Assembly assembly)
        {
            var candidates = assembly
                .GetTypes()
                .Where(type => string.Equals(type.Name, "Mod", StringComparison.Ordinal))
                .Select(type => new
                {
                    Type = type,
                    Method = type.GetMethod(
                        "Load",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                        binder: null,
                        types: Type.EmptyTypes,
                        modifiers: null)
                })
                .Where(candidate => candidate.Method != null)
                .OrderBy(candidate => candidate.Type.FullName, StringComparer.Ordinal)
                .ToArray();

            if (candidates.Length == 0)
            {
                return;
            }

            if (candidates.Length > 1)
            {
                throw new AmbiguousMatchException(
                    "A source mod may contain at most one class named Mod with a static parameterless Load() method.");
            }

            candidates[0].Method.Invoke(null, null);
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation &&
                   invocation.InnerException != null)
            {
                exception = invocation.InnerException;
            }

            return exception;
        }
    }
}
