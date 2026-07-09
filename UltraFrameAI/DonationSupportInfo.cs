using UltraFrameAI.Resources;

namespace UltraFrameAI;

internal static class DonationSupportInfo
{
    public static IReadOnlyList<DonationSupportEntry> GetEntries()
    {
        return new[]
        {
            new DonationSupportEntry(
                "dagnaruscode.eu",
                LocalizedStrings.Get("DonationEntryAuthorSiteSummary"),
                LocalizedStrings.Get("DonationEntryAuthorSiteDetails"),
                "https://dagnaruscode.eu",
                LocalizedStrings.Get("DonationEntryAuthorSiteAction"))
        };
    }
}
