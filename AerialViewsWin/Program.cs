using System.IO;
using System.Windows;
using AerialViews.Services;
using AerialViews.UI;

namespace AerialViews
{
    public static class Program
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AerialViews", "debug.log");

        private static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch { }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            Log($"=== START args=[{string.Join(",", args)}] ===");
            try
            {
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                    Log($"UnhandledException: {e.ExceptionObject}");

                Log("Creating App");
                var app = new App();

                Log("InitializeComponent");
                app.InitializeComponent();

                app.DispatcherUnhandledException += (s, e) =>
                {
                    Log($"DispatcherException: {e.Exception}");
                    e.Handled = false;
                };

                string mode = ParseMode(args);
                Log($"Mode: {mode}");

                switch (mode)
                {
                    case "s":
                        Log("Launching multi-monitor screensaver");
                        MultiMonitorManager.LaunchScreensaver(app);
                        Log("Screensaver exited");
                        break;

                    case "p":
                        Log("Preview mode - exiting");
                        return;

                    case "c":
                    default:
                        Log("Creating SettingsWindow");
                        var settingsWindow = new SettingsWindow();
                        Log("Running settings");
                        app.Run(settingsWindow);
                        Log("Settings exited");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"FATAL: {ex}");
                MessageBox.Show($"Error: {ex.Message}\n\nSee: {LogPath}", "AerialViews Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Log("=== END ===");
        }

        private static string ParseMode(string[] args)
        {
            if (args.Length == 0) return "c";

            string firstArg = args[0].ToLowerInvariant().TrimStart('/').TrimStart('-');

            if (firstArg.StartsWith("s"))
                return "s";
            if (firstArg.StartsWith("p"))
                return "p";
            if (firstArg.StartsWith("c"))
                return "c";

            return "c";
        }
    }
}
