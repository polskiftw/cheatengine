using System;
using System.Collections.Generic;
using System.IO;

namespace GLoader
{
    internal sealed class LoaderOptions
    {
        public string TargetPath { get; private set; }
        public string ModsPath { get; private set; }
        public bool DedicatedServer { get; private set; }
        public bool DisableMods { get; private set; }
        public bool ShowHelp { get; private set; }
        public List<string> GameArguments { get; } = new List<string>();

        public static LoaderOptions Parse(string[] args)
        {
            var result = new LoaderOptions();
            var passThrough = false;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                if (passThrough)
                {
                    result.GameArguments.Add(arg);
                    continue;
                }

                if (arg == "--")
                {
                    passThrough = true;
                    continue;
                }

                if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/?", StringComparison.OrdinalIgnoreCase))
                {
                    result.ShowHelp = true;
                    continue;
                }

                if (arg.Equals("--server", StringComparison.OrdinalIgnoreCase))
                {
                    result.DedicatedServer = true;
                    continue;
                }

                if (arg.Equals("--no-mods", StringComparison.OrdinalIgnoreCase))
                {
                    result.DisableMods = true;
                    continue;
                }

                if (arg.Equals("--target", StringComparison.OrdinalIgnoreCase))
                {
                    result.TargetPath = RequireValue(args, ref i, "--target");
                    continue;
                }

                if (arg.Equals("--mods", StringComparison.OrdinalIgnoreCase))
                {
                    result.ModsPath = RequireValue(args, ref i, "--mods");
                    continue;
                }

                if (result.TargetPath == null &&
                    arg.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(Unquote(arg)))
                {
                    result.TargetPath = Unquote(arg);
                    continue;
                }

                result.GameArguments.Add(arg);
            }

            return result;
        }

        public static void PrintHelp()
        {
            Console.WriteLine("gloader - raw C# source mod loader for vanilla Terraria");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  gloader.exe");
            Console.WriteLine("  gloader.exe --target \"C:\\...\\Terraria.exe\"");
            Console.WriteLine("  gloader.exe --server");
            Console.WriteLine("  gloader.exe --mods \"C:\\...\\gmods\"");
            Console.WriteLine("  gloader.exe --no-mods");
            Console.WriteLine("  gloader.exe -- <arguments passed to Terraria>");
            Console.WriteLine();
            Console.WriteLine("Default layout: put gloader.exe beside Terraria.exe and keep gmods beside them.");
            Console.WriteLine("If no target is given, gloader looks beside itself first, then one folder above");
            Console.WriteLine("itself, and in the current working directory.");
        }

        private static string RequireValue(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException(option + " requires a value.");
            }

            index++;
            return Unquote(args[index]);
        }

        private static string Unquote(string value)
        {
            if (value == null)
            {
                return null;
            }

            return value.Trim().Trim('"');
        }
    }
}
