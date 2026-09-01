using CaroNet.Client.WinUI.Views;
using Microsoft.UI.Xaml;
using System;
using System.IO;

namespace CaroNet.Client.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        InitializeComponent();
        UnhandledException += App_UnhandledException;
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            // Ghi log cục bộ để dễ tìm lỗi XAML khi app thoát đột ngột.
            string logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CaroNet");

            Directory.CreateDirectory(logDirectory);

            string logPath = Path.Combine(logDirectory, "client-crash.log");
            File.AppendAllText(
                logPath,
                $"[{DateTime.Now:O}] {e.Message}{Environment.NewLine}{e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Không ném tiếp lỗi ghi log để tránh vòng lặp crash.
        }
    }
}
