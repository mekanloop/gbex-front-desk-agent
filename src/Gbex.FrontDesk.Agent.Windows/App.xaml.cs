using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Gbex.FrontDesk.Agent.Windows;

public partial class App : Application
{
    public static string AgentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            WriteCrashLog(args.ExceptionObject as Exception);
        };
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog(args.Exception);
            MessageBox.Show(
                $"GBEX Front Desk Agent beklenmeyen hata verdi.\n\nDetay log dosyası:\n{LogPath}",
                "GBEX Front Desk Agent",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            args.Handled = true;
        };
    }

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GBEX",
        "FrontDeskAgent",
        "front-desk-agent.log"
    );

    private static void WriteCrashLog(Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(
                LogPath,
                $"{DateTimeOffset.Now:O} GBEX Front Desk Agent crash{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}"
            );
        }
        catch
        {
            // Last-resort logging must never trigger another crash.
        }
    }
}
