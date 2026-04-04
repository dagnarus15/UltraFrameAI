using System.Reflection;

namespace UltraFrameAI;

internal static class HelpCenterInfo
{
    public static IReadOnlyList<HelpVersionEntry> GetVersions()
    {
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev";
        return new[]
        {
            new HelpVersionEntry("UltraFrame AI", assemblyVersion),
            new HelpVersionEntry("UltraFrameAI-Realesrgan-Pipe", "bundled local fork"),
            new HelpVersionEntry("FFmpeg.AutoGen", "8.0.0"),
            new HelpVersionEntry("System.Management", "9.0.4"),
            new HelpVersionEntry("AntiFlicker.Native", "bundled local module")
        };
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
            new HelpLinkEntry("UltraFrameAI.Native", "https://github.com/your-name/UltraFrameAI/tree/main/UltraFrameAI.Native", "Native anti-flicker module repository path placeholder."),
            new HelpLinkEntry("FFmpeg.AutoGen", "https://github.com/Ruslan-B/FFmpeg.AutoGen", "Managed FFmpeg binding used by the app."),
            new HelpLinkEntry("FFmpeg", "https://ffmpeg.org/", "Media processing toolkit used by the pipeline.")
        };
    }
}
