namespace UltraFrameAI;

internal static class SupportedMediaExtensions
{
    public static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3g2",
        ".3gp",
        ".264",
        ".265",
        ".amv",
        ".asf",
        ".avi",
        ".avc",
        ".bik",
        ".bk2",
        ".divx",
        ".dv",
        ".dvr-ms",
        ".evo",
        ".f4a",
        ".f4b",
        ".f4p",
        ".f4v",
        ".flv",
        ".gif",
        ".h264",
        ".h265",
        ".hevc",
        ".ivf",
        ".m1v",
        ".m2p",
        ".m2t",
        ".m2ts",
        ".m2v",
        ".m4b",
        ".m4p",
        ".m4v",
        ".mjpg",
        ".mjpeg",
        ".mk3d",
        ".mka",
        ".mkv",
        ".mod",
        ".mov",
        ".mp4",
        ".mpe",
        ".mpeg",
        ".mpg",
        ".mpv",
        ".mts",
        ".mxf",
        ".nut",
        ".nuv",
        ".ogm",
        ".ogv",
        ".ps",
        ".qt",
        ".rm",
        ".rmvb",
        ".roq",
        ".smk",
        ".tod",
        ".tp",
        ".trp",
        ".ts",
        ".vid",
        ".vob",
        ".webm",
        ".wmv",
        ".wtv",
        ".y4m",
        ".yuv"
    };

    public static readonly HashSet<string> Image = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp",
        ".jpeg",
        ".jpg",
        ".jfif",
        ".pgm",
        ".png",
        ".ppm",
        ".tga",
        ".tif",
        ".tiff",
        ".webp"
    };

    public static bool IsVideoFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return !string.IsNullOrWhiteSpace(extension) && Video.Contains(extension);
    }

    public static bool IsImageFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return !string.IsNullOrWhiteSpace(extension) && Image.Contains(extension);
    }
}
