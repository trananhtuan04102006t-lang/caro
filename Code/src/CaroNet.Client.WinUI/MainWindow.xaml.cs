using Microsoft.UI.Xaml;

namespace CaroNet.Client.WinUI.Views;

/// <summary>
/// Main application window for the Caro player client.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        RootFrame.Navigate(typeof(MainMenuPage));
    }
}
