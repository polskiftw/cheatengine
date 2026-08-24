using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace GLoader
{
    internal static class ReferenceCollector
    {
        public static IReadOnlyList<MetadataReference> Collect(
            Assembly gameAssembly,
            string gameDirectory,
            string loaderDirectory)
        {
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                AddAssemblyLocation(paths, assembly, overwrite: false);
            }

            AddManagedFiles(paths, loaderDirectory, overwrite: false);
            AddManagedFiles(paths, gameDirectory, overwrite: false);

            // The exact Terraria assembly selected by the user always wins over a
            // same-named assembly that might already have been visible elsewhere.
            AddAssemblyLocation(paths, gameAssembly, overwrite: true);

            var references = new List<MetadataReference>();
            foreach (var path in paths.Values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
                catch (BadImageFormatException)
                {
                    // Ignore native binaries.
                }
                catch (IOException ex)
                {
                    Log.Warn("Could not use compiler reference " + path + ": " + ex.Message);
                }
            }

            return references;
        }

        private static void AddManagedFiles(
            IDictionary<string, string> paths,
            string directory,
            bool overwrite)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                AddManagedPath(paths, path, overwrite);
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
            {
                AddManagedPath(paths, path, overwrite);
            }
        }

        private static void AddAssemblyLocation(
            IDictionary<string, string> paths,
            Assembly assembly,
            bool overwrite)
        {
            try
            {
                if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
                {
                    return;
                }

                AddManagedPath(paths, assembly.Location, overwrite);
            }
            catch (NotSupportedException)
            {
                // Dynamic or byte-loaded assembly with no usable location.
            }
        }

        private static void AddManagedPath(
            IDictionary<string, string> paths,
            string path,
            bool overwrite)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                var name = AssemblyName.GetAssemblyName(fullPath).Name;

                if (overwrite || !paths.ContainsKey(name))
                {
                    paths[name] = fullPath;
                }
            }
            catch (BadImageFormatException)
            {
                // Native DLL/exe.
            }
            catch (FileLoadException)
            {
                // Not a usable managed reference.
            }
            catch (FileNotFoundException)
            {
                // File disappeared while scanning; ignore it.
            }
        }
    }
}
