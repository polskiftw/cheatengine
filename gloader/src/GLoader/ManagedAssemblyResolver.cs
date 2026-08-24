using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GLoader
{
    internal sealed class ManagedAssemblyResolver : IDisposable
    {
        private readonly string[] _directories;

        public ManagedAssemblyResolver(params string[] directories)
        {
            _directories = directories
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
        }

        private Assembly Resolve(object sender, ResolveEventArgs args)
        {
            var requested = new AssemblyName(args.Name);

            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly =>
                {
                    try
                    {
                        return string.Equals(
                            assembly.GetName().Name,
                            requested.Name,
                            StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });

            if (loaded != null)
            {
                return loaded;
            }

            foreach (var directory in _directories)
            {
                foreach (var extension in new[] { ".dll", ".exe" })
                {
                    var candidate = Path.Combine(directory, requested.Name + extension);
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }

                    try
                    {
                        return Assembly.LoadFrom(candidate);
                    }
                    catch (BadImageFormatException)
                    {
                        // Native library; not a managed assembly candidate.
                    }
                    catch (FileLoadException)
                    {
                        // Continue searching other locations.
                    }
                }
            }

            return null;
        }
    }
}
