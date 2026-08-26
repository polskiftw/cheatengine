using System;
using System.Collections.Generic;
using System.IO;

namespace GLoader
{
    internal static class TargetLocator
    {
        public static string Find(string loaderDirectory, LoaderOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.TargetPath))
            {
                return Validate(options.TargetPath);
            }

            var environmentTarget = Environment.GetEnvironmentVariable("GLOADER_TERRARIA");
            if (!string.IsNullOrWhiteSpace(environmentTarget))
            {
                return Validate(environmentTarget);
            }

            var fileName = options.DedicatedServer ? "TerrariaServer.exe" : "Terraria.exe";
            var candidates = new List<string>
            {
                Path.Combine(loaderDirectory, fileName),
                Path.Combine(loaderDirectory, "..", fileName),
                Path.Combine(Environment.CurrentDirectory, fileName)
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            throw new FileNotFoundException(
                "Could not find " + fileName + ". Put gloader.exe beside " + fileName +
                " or launch with --target \"C:\\path\\to\\Terraria.exe\".");
        }

        private static string Validate(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Terraria target does not exist.", fullPath);
            }

            return fullPath;
        }
    }
}
