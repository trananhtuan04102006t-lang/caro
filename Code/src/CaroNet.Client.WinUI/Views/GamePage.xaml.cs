using CaroNet.Client.WinUI.Services;
using CaroNet.Client.WinUI.ViewModels;
using CaroNet.Shared.Game;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace CaroNet.Client.WinUI.Views;

public sealed partial class GamePage : Page
{
    private const string MaterialSymbolsFont =
        "ms-appx:///Assets/Fonts/MaterialSymbolsRounded.ttf#Material Symbols Rounded";
    private const int TurnCountdownSeconds = 30;
    private static readonly SolidColorBrush BoardCellBackgroundBrush =
        new(Colors.Transparent);
    private static readonly SolidColorBrush BoardCellBorderBrush =
        new(ColorHelper.FromArgb(64, 138, 145, 154));
    private static readonly SolidColorBrush BoardCellHoverBackgroundBrush =
        new(ColorHelper.FromArgb(72, 78, 163, 255));
    private static readonly SolidColorBrush BoardCellHoverBorderBrush =
        new(ColorHelper.FromArgb(180, 78, 163, 255));

    private readonly GameViewModel _viewModel;
    private readonly DispatcherTimer _turnCountdownTimer;
    private bool _gameEndDialogShowing;
    private bool _drawOfferDialogShowing;
    private bool _rematchRequestDialogShowing;
    private bool _wasGameEnded;
    private int _turnSecondsRemaining = TurnCountdownSeconds;
    private int _copyRoomFeedbackVersion;
    private string _lastTurnCountdownKey = string.Empty;
    private BoardPosition? _lastAnimatedMove;

    public GamePage()
    {
        this.InitializeComponent();

        _viewModel = new GameViewModel(AppServices.GetGameClientForCurrentMode());
        this.DataContext = _viewModel;

        _viewModel.SetDispatcher(action => DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
            () => action()));

        _viewModel.ChatMessages.CollectionChanged += ChatMessages_CollectionChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.DrawOfferReceived += ViewModel_DrawOfferReceived;

        _turnCountdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _turnCountdownTimer.Tick += TurnCountdownTimer_Tick;

        Loaded += GamePage_Loaded;
        Unloaded += GamePage_Unloaded;
    }

    private void GamePage_Loaded(object sender, RoutedEventArgs e)
    {
        // Chờ control trong XAML sẵn sàng rồi mới dựng bàn cờ.
        EmptyStateTextBlock.Visibility = Visibility.Visible;

        BuildBoard();
        UpdateTurnUI();
    }

    private void GamePage_Unloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= GamePage_Loaded;
        Unloaded -= GamePage_Unloaded;
        _viewModel.ChatMessages.CollectionChanged -= ChatMessages_CollectionChanged;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.DrawOfferReceived -= ViewModel_DrawOfferReceived;
        _turnCountdownTimer.Stop();
        _turnCountdownTimer.Tick -= TurnCountdownTimer_Tick;
    }

    private async void ViewModel_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameViewModel.IsMyTurn) ||
            e.PropertyName == nameof(GameViewModel.IsOpponentTurn) ||
            e.PropertyName == nameof(GameViewModel.TurnMessage) ||
            e.PropertyName == nameof(GameViewModel.IsGameEnded) ||
            e.PropertyName == nameof(GameViewModel.ConnectionStatus) ||
            e.PropertyName == nameof(GameViewModel.CurrentTurnSymbol) ||
            e.PropertyName == nameof(GameViewModel.HasOpponent))
        {
            UpdateTurnUI();
        }

        if (e.PropertyName == nameof(GameViewModel.LastMovePosition))
        {
            AnimateLastMoveCell();
        }

        if (e.PropertyName == nameof(GameViewModel.HasPendingRematchRequest) &&
            _viewModel.HasPendingRematchRequest)
        {
            await ShowRematchRequestDialogAsync();
            return;
        }

        if (e.PropertyName == nameof(GameViewModel.ConnectionStatus) &&
            _viewModel.ConnectionStatus.StartsWith("Trận đấu mới", StringComparison.Ordinal))
        {
            _wasGameEnded = false;
            BuildBoard();
            UpdateTurnUI();
            return;
        }

        if (e.PropertyName == nameof(GameViewModel.ServerError) &&
            _viewModel.ServerError == "Đối thủ đã ngắt kết nối. Bạn thắng!")
        {
            await ShowOpponentDisconnectedDialogAsync();
            return;
        }

        if (e.PropertyName == nameof(GameViewModel.IsGameEnded) &&
            !_viewModel.IsGameEnded)
        {
            _wasGameEnded = false;
            return;
        }

        if (e.PropertyName == nameof(GameViewModel.IsGameEnded) &&
            _viewModel.IsGameEnded &&
            !_wasGameEnded &&
            !_gameEndDialogShowing)
        {
            _wasGameEnded = true;
            _gameEndDialogShowing = true;
            HighlightWinningCellsOnUI();
            await ShowGameEndedDialogAsync();
            _gameEndDialogShowing = false;
            if (_viewModel.HasPendingRematchRequest)
            {
                await ShowRematchRequestDialogAsync();
            }
        }
    }

    private void ChatMessages_CollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            EmptyStateTextBlock.Visibility = _viewModel.ChatMessages.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add &&
                ChatListView.Items.Count > 0)
            {
                var lastItem = ChatListView.Items[^1];
                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        ChatListView.ScrollIntoView(lastItem);
                    }
                    catch (Exception ex)
                    {
                        global::System.Diagnostics.Debug.WriteLine($"Auto-scroll failed: {ex.Message}");
                    }
                });
            }
        });
    }

    private async void SendChatButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SendChatAsync();
    }

    private async void ChatInputTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || !_viewModel.IsSendButtonEnabled)
        {
            return;
        }

        e.Handled = true;
        await _viewModel.SendChatAsync();
    }

    private async void CopyRoomIdButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.RoomId))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(_viewModel.RoomId);
        Clipboard.SetContent(package);

        int feedbackVersion = ++_copyRoomFeedbackVersion;
        CopyRoomIdIconTextBlock.Text = "check";
        ToolTipService.SetToolTip(CopyRoomIdButton, "Đã sao chép");

        await Task.Delay(1200);

        if (feedbackVersion == _copyRoomFeedbackVersion)
        {
            CopyRoomIdIconTextBlock.Text = "content_copy";
            ToolTipService.SetToolTip(CopyRoomIdButton, "Sao chép mã phòng");
        }
    }

    private async void DrawOfferButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SendDrawOfferAsync();
    }

    private async void ResignButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowResignConfirmationDialogAsync();
    }

    private async void RematchButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SendRematchRequestAsync();
    }

    private async void BackToMenuButton_Click(object sender, RoutedEventArgs e)
    {
        await LeaveMatchAndReturnToMenuAsync();
    }

    private async void ViewModel_DrawOfferReceived(object? sender, DrawOfferReceivedEventArgs e)
    {
        if (_drawOfferDialogShowing || _gameEndDialogShowing)
        {
            return;
        }

        _drawOfferDialogShowing = true;

        try
        {
            var dialog = new ContentDialog
            {
                Title = "Đối thủ xin hòa",
                Content = $"{e.SenderName} muốn kết thúc ván bằng kết quả hòa.",
                PrimaryButtonText = "Đồng ý hòa",
                SecondaryButtonText = "Từ chối",
                CloseButtonText = "Để sau",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await _viewModel.SendDrawResponseAsync(accepted: true);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await _viewModel.SendDrawResponseAsync(accepted: false);
            }
        }
        finally
        {
            _drawOfferDialogShowing = false;
        }
    }

    private void BuildBoard()
    {
        BoardGrid.RowDefinitions.Clear();
        BoardGrid.ColumnDefinitions.Clear();
        BoardGrid.Children.Clear();

        for (var index = 0; index < GameViewModel.BoardSize; index++)
        {
            BoardGrid.RowDefinitions.Add(new RowDefinition());
            BoardGrid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        foreach (var cell in _viewModel.BoardCells)
        {
            var cellContent = new Grid
            {
                DataContext = cell,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var lastMoveOverlay = new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(96, 216, 162, 58)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            lastMoveOverlay.SetBinding(UIElement.OpacityProperty, new Binding
            {
                Path = new PropertyPath(nameof(BoardCellViewModel.LastMoveHighlightOpacity)),
                Mode = BindingMode.OneWay,
            });

            var winningOverlay = new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(142, 62, 133, 88)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            winningOverlay.SetBinding(UIElement.OpacityProperty, new Binding
            {
                Path = new PropertyPath(nameof(BoardCellViewModel.WinningCellOverlayOpacity)),
                Mode = BindingMode.OneWay,
            });

            var markText = new TextBlock
            {
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            markText.SetBinding(TextBlock.TextProperty, new Binding
            {
                Path = new PropertyPath(nameof(BoardCellViewModel.Mark)),
                Mode = BindingMode.OneWay,
            });

            markText.SetBinding(TextBlock.ForegroundProperty, new Binding
            {
                Path = new PropertyPath(nameof(BoardCellViewModel.Mark)),
                Converter = (IValueConverter)Resources["BoardMarkForegroundConverter"],
                Mode = BindingMode.OneWay,
            });

            var lastMoveDot = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(3.5),
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 229, 72, 77)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 4, 4)
            };

            lastMoveDot.SetBinding(UIElement.OpacityProperty, new Binding
            {
                Path = new PropertyPath(nameof(BoardCellViewModel.LastMoveIndicatorOpacity)),
                Mode = BindingMode.OneWay,
            });

            cellContent.Children.Add(lastMoveOverlay);
            cellContent.Children.Add(winningOverlay);
            cellContent.Children.Add(markText);
            cellContent.Children.Add(lastMoveDot);

            var cellBorder = new Border
            {
                DataContext = cell,
                Child = cellContent,
                Background = BoardCellBackgroundBrush,
                BorderBrush = BoardCellBorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            cellBorder.PointerEntered += BoardCell_PointerEntered;
            cellBorder.PointerExited += BoardCell_PointerExited;
            cellBorder.Tapped += BoardCell_Tapped;

            Grid.SetRow(cellBorder, cell.Row);
            Grid.SetColumn(cellBorder, cell.Column);
            BoardGrid.Children.Add(cellBorder);
        }
    }

    private async void BoardCell_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!_viewModel.IsMyTurn || _viewModel.IsGameEnded)
        {
            return;
        }

        if (sender is Border { DataContext: BoardCellViewModel cell } &&
            string.IsNullOrWhiteSpace(cell.Mark))
        {
            await _viewModel.MakeMoveAsync(cell.Row, cell.Column);
        }
    }

    private void BoardCell_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border cellBorder && CanPreviewMove(cellBorder))
        {
            cellBorder.Background = BoardCellHoverBackgroundBrush;
            cellBorder.BorderBrush = BoardCellHoverBorderBrush;
        }
    }

    private void BoardCell_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border cellBorder)
        {
            ResetBoardCellChrome(cellBorder);
        }
    }

    private void UpdateTurnUI()
    {
        bool canPlay = _viewModel.IsMyTurn && !_viewModel.IsGameEnded;
        bool isEnded = _viewModel.IsGameEnded;

        if (TurnBanner != null)
        {
            if (isEnded)
            {
                TurnBanner.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 72, 55, 36));
                TurnBanner.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 176, 138, 74));
                TurnBannerTextBlock.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 241, 214));
            }
            else if (canPlay)
            {
                TurnBanner.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 33, 77, 54));
                TurnBanner.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 95, 168, 120));
                TurnBannerTextBlock.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 240, 255, 244));
            }
            else
            {
                TurnBanner.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 48, 52, 59));
                TurnBanner.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 98, 104, 115));
                TurnBannerTextBlock.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 242, 244, 248));
            }
        }

        var boardBorder = BoardGrid?.Parent as Border;
        if (boardBorder != null)
        {
            boardBorder.BorderBrush = isEnded
                ? new SolidColorBrush(ColorHelper.FromArgb(255, 176, 138, 74))
                : canPlay
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 95, 168, 120))
                    : new SolidColorBrush(ColorHelper.FromArgb(255, 98, 104, 115));
        }

        if (BoardGrid is null)
        {
            UpdateTurnCountdown();
            return;
        }

        foreach (var child in BoardGrid.Children)
        {
            if (child is not Border cellBorder)
            {
                continue;
            }

            bool isEmptyCell = cellBorder.DataContext is BoardCellViewModel cell &&
                string.IsNullOrWhiteSpace(cell.Mark);
            cellBorder.IsHitTestVisible = canPlay && isEmptyCell;
            cellBorder.Opacity = isEnded || canPlay ? 1.0 : 0.72;
            ResetBoardCellChrome(cellBorder);
        }

        UpdateTurnCountdown();
    }

    private void HighlightWinningCellsOnUI()
    {
        if (BoardGrid is null)
        {
            return;
        }

        foreach (var child in BoardGrid.Children)
        {
            if (child is not Border cellBorder)
            {
                continue;
            }

            if (cellBorder.DataContext is BoardCellViewModel { IsWinningCell: true })
            {
                AnimateElementOpacity(cellBorder, 0.72, 1.0, 260);
            }
        }
    }

    private void TurnCountdownTimer_Tick(object? sender, object e)
    {
        if (_turnSecondsRemaining > 0)
        {
            _turnSecondsRemaining--;
        }

        UpdateTimerText();

        if (_turnSecondsRemaining == 0)
        {
            _turnCountdownTimer.Stop();
        }
    }

    private void UpdateTurnCountdown()
    {
        bool isActive =
            !string.IsNullOrWhiteSpace(_viewModel.RoomId) &&
            _viewModel.HasOpponent &&
            !_viewModel.IsGameEnded &&
            (_viewModel.IsMyTurn || _viewModel.IsOpponentTurn);

        string countdownKey = isActive
            ? string.Join(
                ":",
                _viewModel.RoomId,
                _viewModel.CurrentTurnSymbol,
                _viewModel.LastMovePosition?.Row.ToString() ?? "-",
                _viewModel.LastMovePosition?.Column.ToString() ?? "-",
                _viewModel.ConnectionStatus)
            : "inactive";

        if (!isActive)
        {
            _turnCountdownTimer.Stop();
            _lastTurnCountdownKey = countdownKey;
            _turnSecondsRemaining = TurnCountdownSeconds;
            UpdateTimerText();
            return;
        }

        if (!string.Equals(_lastTurnCountdownKey, countdownKey, StringComparison.Ordinal))
        {
            _lastTurnCountdownKey = countdownKey;
            _turnSecondsRemaining = TurnCountdownSeconds;
        }

        if (!_turnCountdownTimer.IsEnabled)
        {
            _turnCountdownTimer.Start();
        }

        UpdateTimerText();
    }

    private void UpdateTimerText()
    {
        if (MyTimerTextBlock is null || OpponentTimerTextBlock is null)
        {
            return;
        }

        if (_viewModel.IsGameEnded)
        {
            ApplyTimerText(MyTimerTextBlock, "Kết thúc", isActive: false, isEnded: true);
            ApplyTimerText(OpponentTimerTextBlock, "Kết thúc", isActive: false, isEnded: true);
            return;
        }

        if (!_viewModel.HasOpponent || string.IsNullOrWhiteSpace(_viewModel.RoomId))
        {
            ApplyTimerText(MyTimerTextBlock, "--", isActive: false, isEnded: false);
            ApplyTimerText(OpponentTimerTextBlock, "--", isActive: false, isEnded: false);
            return;
        }

        string secondsText = $"{_turnSecondsRemaining}s";
        ApplyTimerText(MyTimerTextBlock, _viewModel.IsMyTurn ? secondsText : "--", _viewModel.IsMyTurn, isEnded: false);
        ApplyTimerText(OpponentTimerTextBlock, _viewModel.IsOpponentTurn ? secondsText : "--", _viewModel.IsOpponentTurn, isEnded: false);
    }

    private static void ApplyTimerText(TextBlock textBlock, string text, bool isActive, bool isEnded)
    {
        textBlock.Text = text;
        textBlock.Foreground = isEnded
            ? new SolidColorBrush(ColorHelper.FromArgb(255, 214, 199, 161))
            : isActive
                ? new SolidColorBrush(ColorHelper.FromArgb(255, 255, 214, 102))
                : new SolidColorBrush(ColorHelper.FromArgb(255, 170, 176, 186));
    }

    private void AnimateLastMoveCell()
    {
        if (_viewModel.LastMovePosition is not { } move)
        {
            _lastAnimatedMove = null;
            return;
        }

        if (_lastAnimatedMove == move)
        {
            return;
        }

        _lastAnimatedMove = move;

        if (FindBoardCell(move.Row, move.Column) is FrameworkElement cellElement)
        {
            AnimateElementOpacity(cellElement, 0.78, 1.0, 180);
        }
    }

    private FrameworkElement? FindBoardCell(int row, int column)
    {
        if (BoardGrid is null)
        {
            return null;
        }

        foreach (var child in BoardGrid.Children)
        {
            if (child is FrameworkElement cellElement &&
                Grid.GetRow(cellElement) == row &&
                Grid.GetColumn(cellElement) == column)
            {
                return cellElement;
            }
        }

        return null;
    }

    private bool CanPreviewMove(Border cellBorder)
    {
        return _viewModel.IsMyTurn &&
            !_viewModel.IsGameEnded &&
            cellBorder.DataContext is BoardCellViewModel cell &&
            string.IsNullOrWhiteSpace(cell.Mark);
    }

    private static void ResetBoardCellChrome(Border cellBorder)
    {
        cellBorder.Background = BoardCellBackgroundBrush;
        cellBorder.BorderBrush = BoardCellBorderBrush;
    }

    private static void AnimateElementOpacity(UIElement element, double from, double to, int milliseconds)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, nameof(UIElement.Opacity));

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private async Task ShowRematchRequestDialogAsync()
    {
        if (_rematchRequestDialogShowing ||
            _gameEndDialogShowing ||
            !_viewModel.HasPendingRematchRequest)
        {
            return;
        }

        _rematchRequestDialogShowing = true;

        try
        {
            var dialog = new ContentDialog
            {
                Title = "Đối thủ muốn chơi lại",
                Content = "Bạn có thể bắt đầu ván mới ngay hoặc tiếp tục xem lại bàn cờ hiện tại.",
                PrimaryButtonText = "Chơi lại",
                CloseButtonText = "Xem tiếp",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await _viewModel.SendRematchRequestAsync();
            }
        }
        finally
        {
            _rematchRequestDialogShowing = false;
        }
    }

    private async Task LeaveMatchAndReturnToMenuAsync()
    {
        if (string.IsNullOrWhiteSpace(_viewModel.RoomId))
        {
            Frame.Navigate(typeof(MainMenuPage));
            return;
        }

        if (_viewModel.IsGameEnded)
        {
            await _viewModel.LeaveRoomAsync();
            AppServices.EndAiGame();
            Frame.Navigate(typeof(MainMenuPage));
            return;
        }

        string title = _viewModel.HasOpponent
            ? "Rời ván đấu?"
            : "Rời phòng?";
        string content = _viewModel.HasOpponent && !_viewModel.IsGameEnded
            ? "Ván đang diễn ra. Nếu rời bây giờ, bạn sẽ đầu hàng và đối thủ được tính thắng."
            : _viewModel.HasOpponent
                ? "Bạn sẽ rời phòng hiện tại. Đối thủ sẽ được thông báo rằng bạn đã rời phòng."
                : "Bạn đang ở phòng một mình. Nếu rời bây giờ, phòng sẽ được xóa khỏi server.";

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = _viewModel.HasOpponent ? "Rời ván" : "Rời phòng",
            CloseButtonText = "Ở lại",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        await _viewModel.LeaveRoomAsync();
        AppServices.EndAiGame();
        Frame.Navigate(typeof(MainMenuPage));
    }

    private async Task ShowResignConfirmationDialogAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Đầu hàng ván này?",
            Content = "Nếu xác nhận, ván đấu sẽ kết thúc ngay và đối thủ được tính thắng.",
            PrimaryButtonText = "Đầu hàng",
            CloseButtonText = "Hủy",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await _viewModel.SendResignAsync();
        }
    }

    private async Task ShowOpponentDisconnectedDialogAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Kết thúc trận đấu",
            Content = "Đối thủ đã ngắt kết nối. Bạn thắng!",
            CloseButtonText = "Về menu",
            XamlRoot = this.XamlRoot
        };

        await dialog.ShowAsync();
        await _viewModel.LeaveRoomAsync();
        AppServices.EndAiGame();
        Frame.Navigate(typeof(MainMenuPage));
    }

    private async Task ShowGameEndedDialogAsync()
    {
        string titleText = "Hòa!";
        var iconColor = Colors.DarkGray;
        string statusIcon = "radio_button_unchecked";

        if (_viewModel.ServerError.Contains("Bạn thắng", StringComparison.OrdinalIgnoreCase))
        {
            titleText = "Bạn thắng!";
            statusIcon = "check_circle";
            iconColor = Colors.Green;
        }
        else if (_viewModel.ServerError.Contains("Bạn thua", StringComparison.OrdinalIgnoreCase) ||
            _viewModel.ServerError.Contains("Bạn đã đầu hàng", StringComparison.OrdinalIgnoreCase))
        {
            titleText = "Bạn thua!";
            statusIcon = "cancel";
            iconColor = Colors.Red;
        }
        else if (AppServices.GameClient is SocketGameClientService socketService &&
            socketService.WinningCells.Count > 0)
        {
            var firstWinCell = socketService.WinningCells.First();
            var matchingCell = _viewModel.BoardCells.FirstOrDefault(
                cell => cell.Row == firstWinCell.Row && cell.Column == firstWinCell.Col);

            if (matchingCell is not null && !string.IsNullOrEmpty(matchingCell.Mark))
            {
                if (matchingCell.Mark == _viewModel.PlayerSymbol)
                {
                    titleText = "Bạn thắng!";
                    statusIcon = "check_circle";
                    iconColor = Colors.Green;
                }
                else
                {
                    titleText = "Bạn thua!";
                    statusIcon = "cancel";
                    iconColor = Colors.Red;
                }
            }
        }

        var titleStackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12
        };

        titleStackPanel.Children.Add(new TextBlock
        {
            Text = statusIcon,
            FontFamily = new FontFamily(MaterialSymbolsFont),
            Foreground = new SolidColorBrush(iconColor),
            FontSize = 26,
            VerticalAlignment = VerticalAlignment.Center
        });

        titleStackPanel.Children.Add(new TextBlock
        {
            Text = titleText,
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        var dialog = new ContentDialog
        {
            Title = titleStackPanel,
            Content = string.IsNullOrEmpty(_viewModel.ServerError)
                ? "Ván đấu đã khép lại thành công."
                : _viewModel.ServerError,
            PrimaryButtonText = "Chơi lại",
            SecondaryButtonText = "Về menu",
            CloseButtonText = "Xem lại bàn cờ",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            await _viewModel.SendRematchRequestAsync();
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await _viewModel.LeaveRoomAsync();
            AppServices.EndAiGame();
            Frame.Navigate(typeof(MainMenuPage));
        }
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}

public sealed class ChatBubbleAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is GameViewModel.ChatMessageViewModel { IsSystemMessage: true })
        {
            return HorizontalAlignment.Center;
        }

        return value is GameViewModel.ChatMessageViewModel { IsOwnMessage: true }
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}

public sealed class ChatBubbleBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is GameViewModel.ChatMessageViewModel { IsSystemMessage: true })
        {
            return new SolidColorBrush(ColorHelper.FromArgb(255, 70, 81, 95));
        }

        if (value is GameViewModel.ChatMessageViewModel { IsOwnMessage: true })
        {
            return new SolidColorBrush(ColorHelper.FromArgb(255, 47, 95, 141));
        }

        return new SolidColorBrush(ColorHelper.FromArgb(255, 60, 65, 74));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}

public sealed class ChatBubbleForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return new SolidColorBrush(Colors.White);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}

public sealed class BoardMarkForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value switch
        {
            "X" => new SolidColorBrush(ColorHelper.FromArgb(255, 78, 163, 255)),
            "O" => new SolidColorBrush(ColorHelper.FromArgb(255, 255, 107, 107)),
            _ => new SolidColorBrush(ColorHelper.FromArgb(255, 242, 244, 248))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
