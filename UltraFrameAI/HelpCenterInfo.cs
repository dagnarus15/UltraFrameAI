using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace UltraFrameAI;

internal static class HelpCenterInfo
{
    private const string ModelsFallbackVersion = "20220424 (Real-ESRGAN-ncnn-vulkan v0.2.0)";

    public static string GetApplicationVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev";
    }

    public static IReadOnlyList<HelpVersionEntry> GetLibraryVersions()
    {
        return new[]
        {
            new HelpVersionEntry("UltraFrameAI-Realesrgan-Pipe", GetRealesrganForkVersion()),
            new HelpVersionEntry("FFmpeg", GetFfmpegVersion()),
            new HelpVersionEntry("System.Management", "9.0.4"),
            new HelpVersionEntry("RealESRGAN models", GetModelBundleVersion())
        };
    }

    private static string GetRealesrganForkVersion()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            return "unknown";
        }

        var forkPath = Path.Combine(repoRoot, "realesrgan-ncnn-vulkan-fork");
        if (!Directory.Exists(forkPath))
        {
            return "unknown";
        }

        return RunProcess("git", "-C \"" + forkPath + "\" describe --tags --always --dirty")
            ?? RunProcess("git", "-C \"" + forkPath + "\" rev-parse --short HEAD")
            ?? "unknown";
    }

    private static string GetModelBundleVersion()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            return ModelsFallbackVersion;
        }

        var metadataPath = Path.Combine(repoRoot, "realesrgan-ncnn-vulkan-20220424", "VERSION.json");
        if (!File.Exists(metadataPath))
        {
            return ModelsFallbackVersion;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<ModelBundleVersionInfo>(File.ReadAllText(metadataPath));
            if (metadata is null ||
                string.IsNullOrWhiteSpace(metadata.BundleVersion) ||
                string.IsNullOrWhiteSpace(metadata.UpstreamProject) ||
                string.IsNullOrWhiteSpace(metadata.UpstreamVersion))
            {
                return ModelsFallbackVersion;
            }

            return $"{metadata.BundleVersion} ({metadata.UpstreamProject} {metadata.UpstreamVersion})";
        }
        catch
        {
            return ModelsFallbackVersion;
        }
    }

    private static string GetFfmpegVersion()
    {
        var output = RunProcess("ffmpeg", "-version");
        if (string.IsNullOrWhiteSpace(output))
        {
            return "not found";
        }

        var firstLine = output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return "unknown";
        }

        var prefix = "ffmpeg version ";
        if (firstLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var version = firstLine[prefix.Length..].Trim();
            var firstSpace = version.IndexOf(' ');
            return firstSpace > 0 ? version[..firstSpace] : version;
        }

        return firstLine.Trim();
    }

    private static string? FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            if (Directory.Exists(Path.Combine(current.FullName, "UltraFrameAI")) &&
                Directory.Exists(Path.Combine(current.FullName, "realesrgan-ncnn-vulkan-20220424")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string? RunProcess(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)
                ? output.Trim()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class ModelBundleVersionInfo
    {
        public string BundleVersion { get; set; } = string.Empty;

        public string UpstreamProject { get; set; } = string.Empty;

        public string UpstreamVersion { get; set; } = string.Empty;
    }

    public static IReadOnlyList<HelpLinkEntry> GetContactLinks()
    {
        return new[]
        {
            new HelpLinkEntry("GitHub", "https://github.com/your-name/UltraFrameAI", "Main repository placeholder. Replace with your project URL."),
            new HelpLinkEntry("Reddit", "https://www.reddit.com/user/your-name", "Replace with your Reddit profile or community link."),
            new HelpLinkEntry("Telegram", "https://t.me/your_name", "Replace with your public Telegram contact or channel."),
            new HelpLinkEntry("Discord", "https://discord.gg/your-invite", "Optional community or support server placeholder.")
        };
    }

    public static IReadOnlyList<HelpLinkEntry> GetSourceLinks()
    {
        return new[]
        {
            new HelpLinkEntry("UltraFrame AI repository", "https://github.com/your-name/UltraFrameAI", "Replace with the main application repository."),
            new HelpLinkEntry("UltraFrameAI-Realesrgan-Pipe", "https://github.com/alexander-diener/UltraFrameAI-Realesrgan-Pipe", "Pipeline RealESRGAN fork used by the app."),
            new HelpLinkEntry("Real-ESRGAN", "https://github.com/xinntao/Real-ESRGAN", "Original Real-ESRGAN project."),
            new HelpLinkEntry("ncnn", "https://github.com/Tencent/ncnn", "Inference backend used by the RealESRGAN fork."),
            new HelpLinkEntry("realsr-ncnn-vulkan", "https://github.com/nihui/realsr-ncnn-vulkan", "Related upstream project referenced by the RealESRGAN fork."),
            new HelpLinkEntry("FFmpeg", "https://ffmpeg.org/", "Media processing toolkit used by the pipeline.")
        };
    }
}
