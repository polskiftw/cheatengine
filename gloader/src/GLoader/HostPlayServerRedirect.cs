using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace GLoader
{
    internal static class HostPlayServerRedirect
    {
        private const string HarmonyId = "gloader.core.hostplay-server";
        private static string _loaderPath;
        private static string _modsDirectory;
        private static bool _installed;

        public static void Install(string loaderPath, string modsDirectory)
        {
            if (_installed)
            {
                return;
            }

            _loaderPath = Path.GetFullPath(loaderPath);
            _modsDirectory = Path.GetFullPath(modsDirectory);

            var startMethod = typeof(Process).GetMethod(
                "Start",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            if (startMethod == null)
            {
                throw new MissingMethodException(typeof(Process).FullName, "Start()");
            }

            var prefix = typeof(HostPlayServerRedirect).GetMethod(
                nameof(ProcessStartPrefix),
                BindingFlags.NonPublic | BindingFlags.Static);

            new Harmony(HarmonyId).Patch(startMethod, prefix: new HarmonyMethod(prefix));
            _installed = true;

            Log.Info("Host & Play server redirect enabled.");
        }

        private static void ProcessStartPrefix(Process __instance)
        {
            if (__instance == null || __instance.StartInfo == null)
            {
                return;
            }

            var startInfo = __instance.StartInfo;
            var requestedFile = TrimQuotes(startInfo.FileName);
            if (string.IsNullOrWhiteSpace(requestedFile) ||
                !string.Equals(Path.GetFileName(requestedFile), "TerrariaServer.exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var workingDirectory = string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
                ? Environment.CurrentDirectory
                : startInfo.WorkingDirectory;

            var serverPath = Path.IsPathRooted(requestedFile)
                ? Path.GetFullPath(requestedFile)
                : Path.GetFullPath(Path.Combine(workingDirectory, requestedFile));

            if (!File.Exists(serverPath))
            {
                Log.Warn("Host & Play requested TerrariaServer.exe, but the resolved path does not exist: " + serverPath);
                return;
            }

            var originalArguments = startInfo.Arguments ?? string.Empty;
            startInfo.FileName = _loaderPath;
            startInfo.Arguments =
                "--server --target " + Quote(serverPath) +
                " --mods " + Quote(_modsDirectory) +
                " --" +
                (string.IsNullOrWhiteSpace(originalArguments) ? string.Empty : " " + originalArguments);

            Log.Info("Routing Host & Play server through gloader: " + serverPath);
        }

        private static string TrimQuotes(string value)
        {
            return value?.Trim().Trim('"');
        }

        private static string Quote(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            var builder = new StringBuilder();
            builder.Append('"');
            var backslashes = 0;

            foreach (var character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                    continue;
                }

                builder.Append('\\', backslashes);
                backslashes = 0;
                builder.Append(character);
            }

            // Backslashes immediately before the closing quote must be doubled.
            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }
    }
}
