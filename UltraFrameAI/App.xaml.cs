using System.Diagnostics;
using Application = System.Windows.Application;

namespace UltraFrameAI;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) => LogStartupException("DispatcherUnhandledException", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogStartupException("UnhandledException", ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogStartupException("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    private static void LogStartupException(string source, Exception exception)
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UltraFrameAI");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(
                Path.Combine(logDir, "startup.log"),
                $"{DateTime.Now:O} [{source}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
