using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        AppThemeManager.InitializeMutableResources();

        if (e.Args.Any(arg => arg.Equals("--benchmark-source", StringComparison.OrdinalIgnoreCase) || arg.Equals("--benchmark", StringComparison.OrdinalIgnoreCase)))
        {
            EnsureConsoleAttached();
            using var benchmarkCts = new CancellationTokenSource();
            ConsoleCancelEventHandler? cancelHandler = null;
            try
            {
                cancelHandler = (_, cancelArgs) =>
                {
                    cancelArgs.Cancel = true;
                    if (!benchmarkCts.IsCancellationRequested)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Ctrl+C received. Finishing cleanup...");
                        benchmarkCts.Cancel();
                    }
                };
                Console.CancelKeyPress += cancelHandler;
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                var exitCode = await BenchmarkRunner.RunAsync(e.Args, benchmarkCts.Token).ConfigureAwait(true);
                if (benchmarkCts.IsCancellationRequested)
                {
                    Console.WriteLine("Benchmark cancelled by user. Background processes stopped.");
                }
                Shutdown(exitCode);
                return;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Benchmark cancelled by user. Background processes stopped.");
                Shutdown(130);
                return;
            }
            catch (Exception ex)
            {
                LogStartupException("BenchmarkStartup", ex);
                Shutdown(1);
                return;
            }
            finally
            {
                if (cancelHandler is not null)
                {
                    Console.CancelKeyPress -= cancelHandler;
                }
            }
        }

        var splash = new SplashWindow();
        splash.Show();

        try
        {
            var window = new MainWindow();
            await window.InitializeStartupAsync().ConfigureAwait(true);

            MainWindow = window;
            window.Show();
            splash.Close();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            await window.EnsureFfmpegAvailableAsync().ConfigureAwait(true);
            await window.ShowStartupBenchmarkPromptIfNeededAsync().ConfigureAwait(true);
        }
        catch
        {
            splash.Close();
            throw;
        }
    }

    private static void LogStartupException(string source, Exception exception)
    {
        try
        {
            var logDir = ResolveRepoRoot();
            Directory.CreateDirectory(logDir);
            File.AppendAllText(
                Path.Combine(logDir, "startup.log"),
                $"{DateTime.Now:O} [{source}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static void EnsureConsoleAttached()
    {
        const uint AttachParentProcess = 0xFFFFFFFF;

        if (!AttachConsole(AttachParentProcess))
        {
            AllocConsole();
        }

        try
        {
            SetConsoleOutputCP(65001);
            SetConsoleCP(65001);

            var stdout = Console.OpenStandardOutput();
            var stderr = Console.OpenStandardError();
            Console.SetOut(new StreamWriter(stdout, Encoding.UTF8) { AutoFlush = true });
            Console.SetError(new StreamWriter(stderr, Encoding.UTF8) { AutoFlush = true });
        }
        catch
        {
        }
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleOutputCP(uint wCodePageID);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCP(uint wCodePageID);
}
