namespace UltraFrameAI;

internal static class DonationSupportInfo
{
    public static IReadOnlyList<DonationSupportEntry> GetEntries()
    {
        return new[]
        {
            new DonationSupportEntry(
                "Boosty",
                "https://boosty.to/your-page",
                "Replace with your public support page."),
            new DonationSupportEntry(
                "DonationAlerts",
                "https://www.donationalerts.com/r/your-name",
                "Replace with your DonationAlerts page."),
            new DonationSupportEntry(
                "USDT TRC20",
                "TXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX",
                "Replace with your wallet address.")
        };
    }
}
