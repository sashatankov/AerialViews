using System;
using System.Windows;
using AerialViews.Screensaver;

namespace AerialViews
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Create and show the screensaver host
            var host = new ScreensaverHost();
            host.Show();
        }
    }
}
