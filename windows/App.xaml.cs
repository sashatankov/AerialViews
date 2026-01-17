using System;
using System.Windows;
using AerialViews.Screensaver;
using NLog;

namespace AerialViews
{
    public partial class App : Application
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Setup unhandled exception handling
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;

            try
            {
                Logger.Info("Application starting with args: {0}", string.Join(" ", e.Args));
                
                // Don't auto-create window - let ScreensaverHost decide based on command-line args
                // ScreensaverHost will be created by XAML startup or manually
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to start application");
                MessageBox.Show($"Failed to start: {ex.Message}\n\nCheck logs at: %AppData%\\AerialViews\\logs", 
                    "Aerial Views Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            Logger.Fatal(ex, "Unhandled exception");
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error(e.Exception, "Unhandled dispatcher exception");
            e.Handled = true;
        }
    }
}
