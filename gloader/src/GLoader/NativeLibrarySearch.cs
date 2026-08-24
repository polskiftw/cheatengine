using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GLoader
{
    internal static class NativeLibrarySearch
    {
        public static void UseDirectory(string directory)
        {
            if (!SetDllDirectory(directory))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetDllDirectory failed.");
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);
    }
}
