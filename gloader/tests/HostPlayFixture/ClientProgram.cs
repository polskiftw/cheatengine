using System;
using System.Diagnostics;
using System.IO;

namespace FixtureClient
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            var gameDirectory = Environment.CurrentDirectory;
            var server = Path.Combine(gameDirectory, "TerrariaServer.exe");
            if (!File.Exists(server))
            {
                Console.Error.WriteLine("Fixture server missing: " + server);
                return 91;
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "TerrariaServer.exe",
                    WorkingDirectory = gameDirectory,
                    Arguments = "--fixture-arg \"hello world\"",
                    UseShellExecute = false
                }
            };

            Console.WriteLine("[fixture client] launching TerrariaServer.exe through Process.Start()...");
            if (!process.Start())
            {
                Console.Error.WriteLine("Process.Start returned false.");
                return 92;
            }

            process.WaitForExit();
            Console.WriteLine("[fixture client] child exit code: " + process.ExitCode);
            return process.ExitCode;
        }
    }
}
