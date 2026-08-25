using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GLoader
{
    internal static class ModDiscovery
    {
        public static IReadOnlyList<ModSource> Discover(string modsDirectory)
        {
            Directory.CreateDirectory(modsDirectory);

            foreach (var file in Directory
                .EnumerateFiles(modsDirectory, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                Log.Warn(
                    "Ignoring loose file in Mods root: " + Path.GetFileName(file) +
                    ". Each mod must live in its own immediate subfolder.");
            }

            var mods = new List<ModSource>();

            foreach (var directory in Directory
                .EnumerateDirectories(modsDirectory, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (directory.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sources = Directory
                    .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                    .Where(path => !IsDisabled(path))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (sources.Length == 0)
                {
                    continue;
                }

                var displayName = Path.GetFileName(directory);
                mods.Add(new ModSource(
                    MakeId(displayName),
                    displayName,
                    sources));
            }

            return mods;
        }

        private static bool IsDisabled(string path)
        {
            return path.EndsWith(".disabled.cs", StringComparison.OrdinalIgnoreCase);
        }

        private static string MakeId(string value)
        {
            var builder = new StringBuilder(value.Length);

            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
                else
                {
                    builder.Append('.');
                }
            }

            var id = builder.ToString().Trim('.');
            return string.IsNullOrWhiteSpace(id) ? "mod" : id;
        }
    }
}
