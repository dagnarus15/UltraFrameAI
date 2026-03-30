using System.Runtime.InteropServices;
using System.Reflection;

namespace UltraFrameAI;

public sealed class AntiFlickerProcessor : IDisposable
{
    private const string LibraryName = "UltraFrameAI.AntiFlicker.Native";

    static AntiFlickerProcessor()
    {
        NativeLibrary.SetDllImportResolver(typeof(AntiFlickerProcessor).Assembly, ResolveLibrary);
    }

    private readonly nint _handle;
    private readonly int _frameBytes;
    private bool _disposed;

    private AntiFlickerProcessor(nint handle, int frameBytes)
    {
        _handle = handle;
        _frameBytes = frameBytes;
    }

    public static AntiFlickerProcessor? TryCreate(int width, int height, int channels, string contentMode, double strength)
    {
        try
        {
            if (strength <= 0)
            {
                return null;
            }

            var mode = MapContentMode(contentMode);
            var strengthScale = ApplyStrengthCurve(mode, strength);
            var profile = GetProfile(mode);
            var config = new NativeConfig
            {
                Width = width,
                Height = height,
                Channels = channels,
                DownscaleFactor = profile.DownscaleFactor,
                BlockSize = profile.BlockSize,
                SearchRadius = profile.SearchRadius,
                ContentMode = mode,
                BlendStrength = profile.BlendStrength,
                MaxBlend = profile.MaxBlend,
                EdgeGuard = profile.EdgeGuard,
                StrengthScale = strengthScale
            };

            var handle = af_create(ref config);
            if (handle == 0)
            {
                return null;
            }

            var frameBytes = checked(width * height * channels);
            return new AntiFlickerProcessor(handle, frameBytes);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    public bool Process(byte[] input, byte[] output)
    {
        if (_disposed || input is null || output is null || input.Length < _frameBytes || output.Length < _frameBytes)
        {
            return false;
        }

        try
        {
            return af_process(_handle, input, output, _frameBytes) == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Reset()
    {
        if (_disposed || _handle == 0)
        {
            return;
        }

        af_reset(_handle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != 0)
        {
            af_destroy(_handle);
        }

        GC.SuppressFinalize(this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeConfig
    {
        public int Width;
        public int Height;
        public int Channels;
        public int DownscaleFactor;
        public int BlockSize;
        public int SearchRadius;
        public int ContentMode;
        public float BlendStrength;
        public float MaxBlend;
        public float EdgeGuard;
        public float StrengthScale;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint af_create(ref NativeConfig config);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void af_destroy(nint handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void af_reset(nint handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int af_process(nint handle, byte[] input_bgr, byte[] output_bgr, int byte_count);

    private static int MapContentMode(string? contentMode) => contentMode?.Trim().ToLowerInvariant() switch
    {
        "video" => 0,
        "faces" => 2,
        "animeultra" => 3,
        "ultra" => 3,
        _ => 1
    };

    private static NativeProfile GetProfile(int mode) => mode switch
    {
        0 => new NativeProfile(4, 32, 2, 0.22f, 0.32f, 0.78f),
        2 => new NativeProfile(4, 24, 1, 0.14f, 0.22f, 1.05f),
        3 => new NativeProfile(4, 32, 2, 0.36f, 0.55f, 0.92f),
        _ => new NativeProfile(4, 32, 2, 0.30f, 0.45f, 0.90f)
    };

    private static float ApplyStrengthCurve(int mode, double strength)
    {
        var normalized = (float)Math.Clamp(strength / 100.0, 0.0, 1.0);
        var gamma = mode switch
        {
            0 => 1.00f,
            2 => 1.32f,
            3 => 0.74f,
            _ => 0.88f
        };

        return (float)Math.Pow(normalized, gamma);
    }

    private readonly record struct NativeProfile(int DownscaleFactor, int BlockSize, int SearchRadius, float BlendStrength, float MaxBlend, float EdgeGuard);

    private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.OrdinalIgnoreCase))
        {
            return nint.Zero;
        }

        foreach (var candidate in GetLibraryCandidates())
        {
            if (File.Exists(candidate))
            {
                return NativeLibrary.Load(candidate);
            }
        }

        return nint.Zero;
    }

    private static IEnumerable<string> GetLibraryCandidates()
    {
        var fileName = LibraryName + ".dll";
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, fileName);
        yield return Path.Combine(Directory.GetCurrentDirectory(), fileName);

        var current = new DirectoryInfo(baseDir);
        while (current is not null)
        {
            yield return Path.Combine(current.FullName, "UltraFrameAI.Native", "AntiFlicker", "build", "Release", fileName);
            yield return Path.Combine(current.FullName, "UltraFrameAI", "bin", "Release", "net8.0-windows", "win-x64", fileName);
            yield return Path.Combine(current.FullName, "dist", "UltraFrameAI", fileName);
            current = current.Parent;
        }
    }
}
