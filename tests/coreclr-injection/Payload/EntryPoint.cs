using System.Runtime.InteropServices;

namespace CoreClrInjectionPayload;

public static class EntryPoint
{
    public static int Initialize(string markerPath)
    {
        markerPath = Path.GetFullPath(markerPath);
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        File.WriteAllText(
            markerPath,
            $"Managed payload executed.{Environment.NewLine}PID={Environment.ProcessId}{Environment.NewLine}Framework={RuntimeInformation.FrameworkDescription}{Environment.NewLine}Architecture={RuntimeInformation.ProcessArchitecture}{Environment.NewLine}");
        return 23063;
    }
}
