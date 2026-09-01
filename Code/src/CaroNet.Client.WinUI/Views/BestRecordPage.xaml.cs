using CaroNet.Client.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CaroNet.Client.WinUI.Views;

public sealed partial class BestRecordPage : Page
{
    public BestRecordPage()
    {
        this.InitializeComponent();
    }

    // Tải Ranking khi mở trang để luôn lấy dữ liệu mới từ server.
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is MainMenuViewModel mainMenuViewModel)
        {
            DataContext = mainMenuViewModel;
            await mainMenuViewModel.LoadBestRecordsAsync();
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }
}
