using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace GLoader
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var loaderDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            var options = LoaderOptions.Parse(args);

            if (options.ShowHelp)
            {
                LoaderOptions.PrintHelp();
                return 0;
            }

            try
            {
                var targetPath = TargetLocator.Find(loaderDirectory, options);
                var gameDirectory = Path.GetDirectoryName(targetPath);
                var modsDirectory = string.IsNullOrWhiteSpace(options.ModsPath)
                    ? Path.Combine(loaderDirectory, "Mods")
                    : Path.GetFullPath(options.ModsPath);

                Log.Initialize(Path.Combine(loaderDirectory, "logs"));
                Log.Info("gloader 0.1.0-alpha");
                Log.Info("Target: " + targetPath);
                Log.Info("Target version: " + GetFileVersion(targetPath));
                Log.Info("Mods: " + modsDirectory);

                Directory.SetCurrentDirectory(gameDirectory);
                NativeLibrarySearch.UseDirectory(gameDirectory);

                using (var resolver = new ManagedAssemblyResolver(gameDirectory, loaderDirectory))
                {
                    var gameAssembly = GameBootstrap.Load(targetPath);

                    if (!options.DisableMods)
                    {
                        ModRuntime.LoadAll(modsDirectory, gameAssembly, gameDirectory, loaderDirectory);
                    }
                    else
                    {
                        Log.Info("Mods disabled by --no-mods.");
                    }

                    Log.Info("Starting Terraria.");
                    return GameBootstrap.InvokeEntryPoint(gameAssembly, options.GameArguments.ToArray());
                }
            }
            catch (Exception ex)
            {
                try
                {
                    Log.Error(ex.ToString());
                }
                catch
                {
                    // Logging must never hide the original startup error.
                }

                Console.Error.WriteLine();
                Console.Error.WriteLine("gloader failed:");
                Console.Error.WriteLine(ex);
                Console.Error.WriteLine();
                Console.Error.WriteLine("See gloader\\logs\\gloader.log for details.");
                return 1;
            }
            finally
            {
                Log.Dispose();
            }
        }

        private static string GetFileVersion(string path)
        {
            try
            {
                return FileVersionInfo.GetVersionInfo(path).FileVersion ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
