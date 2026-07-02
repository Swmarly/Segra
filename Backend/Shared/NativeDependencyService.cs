using System.Runtime.InteropServices;

namespace Segra.Backend.Shared
{
    public static class NativeDependencyService
    {
        private static readonly string[] VisualCppRuntimeDlls =
        [
            "vcruntime140.dll",
            "vcruntime140_1.dll",
            "msvcp140.dll",
        ];

        public static string[] GetMissingVisualCppRuntimeDlls()
        {
            return VisualCppRuntimeDlls
                .Where(dll => !CanLoadLibrary(dll))
                .ToArray();
        }

        public static bool HasVisualCppRuntime() => GetMissingVisualCppRuntimeDlls().Length == 0;

        private static bool CanLoadLibrary(string dllName)
        {
            IntPtr handle = LoadLibrary(dllName);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            FreeLibrary(handle);
            return true;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);
    }
}
