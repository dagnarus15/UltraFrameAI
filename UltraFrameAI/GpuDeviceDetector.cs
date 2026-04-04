using System.Diagnostics;
using System.Globalization;
using System.Management;

namespace UltraFrameAI;

internal sealed record DetectedGpuDevice(
    int DeviceId,
    string Name,
    long? MemoryMb,
    int? MaxGraphicsClockMhz,
    int? PowerLimitW,
    long SortScore);

internal static class GpuDeviceDetector
{
    public static IReadOnlyList<DetectedGpuDevice> DetectDevices()
    {
        var nvidiaDevices = TryDetectNvidiaDevices();
        if (nvidiaDevices.Count > 0)
        {
            return nvidiaDevices;
        }

        return TryDetectWmiDevices();
    }

    private static IReadOnlyList<DetectedGpuDevice> TryDetectNvidiaDevices()
    {
        try
        {
            var nvidiaSmiPath = FindNvidiaSmiPath();
            if (string.IsNullOrWhiteSpace(nvidiaSmiPath))
            {
                return Array.Empty<DetectedGpuDevice>();
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = nvidiaSmiPath,
                    Arguments = "--query-gpu=index,name,memory.total,clocks.max.graphics,power.limit --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return Array.Empty<DetectedGpuDevice>();
            }

            var devices = new List<DetectedGpuDevice>();
            foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = rawLine.Split(',');
                if (parts.Length < 5)
                {
                    continue;
                }

                if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var deviceId))
                {
                    continue;
                }

                var name = parts[1].Trim();
                var memoryMb = TryParseInt(parts[2]);
                var graphicsClock = TryParseInt(parts[3]);
                var powerLimit = TryParseInt(parts[4]);

                devices.Add(new DetectedGpuDevice(
                    deviceId,
                    name,
                    memoryMb,
                    graphicsClock,
                    powerLimit,
                    ComputeSortScore(memoryMb, graphicsClock, powerLimit)));
            }

            return devices
                .OrderByDescending(device => device.SortScore)
                .ThenBy(device => device.DeviceId)
                .ToArray();
        }
        catch
        {
            return Array.Empty<DetectedGpuDevice>();
        }
    }

    private static IReadOnlyList<DetectedGpuDevice> TryDetectWmiDevices()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name,AdapterRAM FROM Win32_VideoController");
            using var results = searcher.Get();
            var devices = new List<DetectedGpuDevice>();
            var nextDeviceId = 0;

            foreach (var result in results.Cast<ManagementObject>())
            {
                var name = Convert.ToString(result["Name"], CultureInfo.InvariantCulture)?.Trim();
                if (string.IsNullOrWhiteSpace(name) || IsSoftwareAdapter(name))
                {
                    continue;
                }

                var adapterRamBytes = TryConvertToInt64(result["AdapterRAM"]);
                var memoryMb = adapterRamBytes > 0 ? adapterRamBytes / (1024 * 1024) : null;

                devices.Add(new DetectedGpuDevice(
                    nextDeviceId++,
                    name,
                    memoryMb,
                    null,
                    null,
                    ComputeSortScore(memoryMb, null, null)));
            }

            return devices
                .OrderByDescending(device => device.SortScore)
                .ThenBy(device => device.DeviceId)
                .ToArray();
        }
        catch
        {
            return Array.Empty<DetectedGpuDevice>();
        }
    }

    private static bool IsSoftwareAdapter(string name)
    {
        return name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("RDP", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Citrix", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Miracast", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Parsec Virtual", StringComparison.OrdinalIgnoreCase);
    }

    private static long ComputeSortScore(long? memoryMb, int? graphicsClockMhz, int? powerLimitW)
    {
        return (memoryMb ?? 0L) * 1_000_000L +
               (graphicsClockMhz ?? 0) * 1_000L +
               (powerLimitW ?? 0);
    }

    private static int? TryParseInt(string raw)
    {
        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static long? TryConvertToInt64(object? value)
    {
        try
        {
            return value switch
            {
                null => null,
                int intValue => intValue,
                uint uintValue => uintValue,
                long longValue => longValue,
                ulong ulongValue when ulongValue <= long.MaxValue => (long)ulongValue,
                _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? FindNvidiaSmiPath()
    {
        var candidates = new[]
        {
            "nvidia-smi.exe",
            Path.Combine(AppContext.BaseDirectory, "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(folder.Trim(), "nvidia-smi.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return null;
    }
}
