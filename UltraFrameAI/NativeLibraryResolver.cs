using System.Reflection;
using System.Runtime.InteropServices;

namespace UltraFrameAI;

internal static class NativeLibraryResolver
{
    private static int _installed;

    public static void EnsureInstalled()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, ResolveLibrary);
    }

    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        foreach (var candidate in GetLibraryCandidates(libraryName))
        {
            if (File.Exists(candidate))
            {
                return NativeLibrary.Load(candidate);
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> GetLibraryCandidates(string libraryName)
    {
        var fileName = OperatingSystem.IsWindows() ? $"{libraryName}.dll" : libraryName;
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, fileName);
        yield return Path.Combine(Directory.GetCurrentDirectory(), fileName);

        var current = new DirectoryInfo(baseDir);
        while (current is not null)
        {
            yield return Path.Combine(current.FullName, "UltraFrameAI.Native", "AntiFlicker", "build", "Release", fileName);
            yield return Path.Combine(current.FullName, "UltraFrameAI.Native", "Encoder", "build", "Release", fileName);
            yield return Path.Combine(current.FullName, "UltraFrameAI", "bin", "Release", "net8.0-windows", "win-x64", fileName);
            yield return Path.Combine(current.FullName, "dist", "UltraFrameAI", fileName);
            current = current.Parent;
        }
    }
}
