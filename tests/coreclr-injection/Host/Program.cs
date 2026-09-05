using System.Runtime.InteropServices;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CoreClrInjectionHost <ready-file> <stop-file>");
    return 2;
}

var readyFile = Path.GetFullPath(args[0]);
var stopFile = Path.GetFullPath(args[1]);

Directory.CreateDirectory(Path.GetDirectoryName(readyFile)!);
File.WriteAllText(
    readyFile,
    $"PID={Environment.ProcessId}{Environment.NewLine}Framework={RuntimeInformation.FrameworkDescription}{Environment.NewLine}Architecture={RuntimeInformation.ProcessArchitecture}{Environment.NewLine}");

while (!File.Exists(stopFile))
    Thread.Sleep(100);

return 0;
