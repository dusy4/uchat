using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Runtime.InteropServices;

namespace uchat
{
    public partial class App : Application
    {
        public static Window? m_window;

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var cmdArgs = Environment.GetCommandLineArgs();

            if (cmdArgs.Length < 3)
            {
                Console.WriteLine("USAGE: uchat.exe <ip> <port>");
                Console.WriteLine("Example: uchat.exe 25.1.2.3 1337");
                Environment.Exit(1);
                return;
            }

            string targetIp = cmdArgs[1];
            if (!int.TryParse(cmdArgs[2], out int targetPort) || targetPort < 1 || targetPort > 65535)
            {
                Console.WriteLine("Error: Invalid port number. Port must be between 1 and 65535.");
                Console.WriteLine("USAGE: uchat.exe <ip> <port>");
                Environment.Exit(1);
                return;
            }

            m_window = new Window();
            m_window.Title = $"Clover Chat  - {targetIp}:{targetPort}";

            try
            {
                m_window.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            }
            catch { }

            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(m_window);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

                if (appWindow != null)
                {
                    appWindow.Resize(new Windows.Graphics.SizeInt32(1280, 720));

                    if (appWindow.TitleBar != null)
                    {
                        appWindow.TitleBar.ExtendsContentIntoTitleBar = true;

                        var black = Windows.UI.Color.FromArgb(255, 0, 0, 0);
                        var white = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                        var gray = Windows.UI.Color.FromArgb(255, 150, 150, 150);
                        var darkGray = Windows.UI.Color.FromArgb(255, 30, 30, 30);

                        appWindow.TitleBar.BackgroundColor = black;
                        appWindow.TitleBar.ForegroundColor = white;
                        appWindow.TitleBar.InactiveBackgroundColor = black;
                        appWindow.TitleBar.InactiveForegroundColor = gray;

                        appWindow.TitleBar.ButtonBackgroundColor = black;
                        appWindow.TitleBar.ButtonForegroundColor = white;
                        appWindow.TitleBar.ButtonHoverBackgroundColor = darkGray;
                        appWindow.TitleBar.ButtonHoverForegroundColor = white;
                        appWindow.TitleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 50, 50, 50);
                        appWindow.TitleBar.ButtonPressedForegroundColor = white;
                        appWindow.TitleBar.ButtonInactiveBackgroundColor = black;
                        appWindow.TitleBar.ButtonInactiveForegroundColor = gray;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting window options: {ex.Message}");
            }

            Frame rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;
            m_window.Content = rootFrame;

            if (rootFrame.Content == null)
            {
                rootFrame.Navigate(typeof(MainPage), (targetIp, targetPort));
            }
            m_window.Activate();
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}