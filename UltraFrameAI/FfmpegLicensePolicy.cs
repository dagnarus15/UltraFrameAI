using System.Text.Json;

namespace UltraFrameAI;

internal sealed record FfmpegLicensePolicy(
    string License,
    bool DynamicLinking,
    bool AllowNativeEncoder)
{
    public static readonly FfmpegLicensePolicy StrictLlgplDynamic = new("LGPL", true, true);

    public static bool TryLoad(string directory, out FfmpegLicensePolicy? policy)
    {
        foreach (var candidate in GetPolicyCandidates(directory))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(candidate);
                policy = JsonSerializer.Deserialize<FfmpegLicensePolicy>(json, JsonOptions);
                return policy is not null;
            }
            catch
            {
                break;
            }
        }

        policy = null;
        return false;
    }

    public static bool IsStrictLlgplDynamic(FfmpegLicensePolicy? policy)
    {
        if (policy is null)
        {
            return false;
        }

        return string.Equals(policy.License, StrictLlgplDynamic.License, StringComparison.OrdinalIgnoreCase)
            && policy.DynamicLinking
            && policy.AllowNativeEncoder;
    }

    private static IEnumerable<string> GetPolicyCandidates(string directory)
    {
        yield return Path.Combine(directory, "UltraFrameAI.ffmpeg.policy.json");
        yield return Path.Combine(directory, "ffmpeg.policy.json");
        yield return Path.Combine(directory, "ffmpeg-license.json");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
