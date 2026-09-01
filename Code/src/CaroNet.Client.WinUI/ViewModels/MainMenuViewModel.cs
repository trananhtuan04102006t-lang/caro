using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CaroNet.Client.WinUI.Models;
using CaroNet.Client.WinUI.Services;
using CaroNet.Client.WinUI.Validation;
using System.Linq;
using Windows.Storage;

namespace CaroNet.Client.WinUI.ViewModels;

public sealed class MainMenuViewModel : INotifyPropertyChanged
{
    private readonly IGameClientService _gameClient;

    private string _connectionStatus = "Chưa kết nối";
    private string _authStatus = "Chưa đăng nhập";
    private string _playerName = string.Empty;
    private string _roomId = string.Empty;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _displayName = string.Empty;
    private bool _isConnected;
    private bool _isAuthenticated;
    private bool _isActionBusy;
    private string _actionStatus = string.Empty;
    private string _rankingStatus = "Mở Ranking để tải Top 10.";

    public MainMenuViewModel(IGameClientService gameClient)
    {
        _gameClient = gameClient;

        // Khôi phục phiên đăng nhập khi quay lại menu trong cùng cửa sổ app.
        if (_gameClient.CurrentAuth is AuthSession currentAuth)
        {
            ApplyAuthSession(currentAuth);
            AuthStatus = "Đã đăng nhập.";
            _isConnected = true;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ================= BEST RECORD =================

    public ObservableCollection<BestRecordItem> BestRecords { get; }
        = new();

    public async Task LoadBestRecordsAsync()
    {
        BestRecords.Clear();
        RefreshBestRecordsState("Đang tải Ranking...");

        try
        {
            if (!await EnsureConnectedAsync())
            {
                RefreshBestRecordsState("Chưa kết nối được server.");
                return;
            }

            IReadOnlyList<PlayerRecordSummary> records =
                await _gameClient.GetTopRecordsAsync(CancellationToken.None);

            var ranking = records
                .Where(record => !string.IsNullOrWhiteSpace(record.PlayerName))
                .OrderByDescending(record => record.WinRate)
                .ThenByDescending(record => record.Wins)
                .ThenBy(record => record.PlayerName)
                .Take(10)
                .ToList();

            int rank = 1;

            foreach (var record in ranking)
            {
                BestRecords.Add(new BestRecordItem
                {
                    Rank = rank++,
                    PlayerName = record.PlayerName,
                    Wins = record.Wins,
                    Losses = record.Losses,
                    Draws = record.Draws
                });
            }

            RefreshBestRecordsState(BestRecords.Count == 0
                ? "Chưa có người chơi nào trong Ranking."
                : $"Đã tải {BestRecords.Count} người chơi.");
        }
        catch (Exception ex)
        {
            RefreshBestRecordsState($"Không thể tải Ranking: {ex.Message}");
        }
    }

    

    public string PlayerName
    {
        get => _playerName;
        set => SetProperty(ref _playerName, value);
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set
        {
            if (SetProperty(ref _isAuthenticated, value))
            {
                OnPropertyChanged(nameof(GreetingText));
                OnPropertyChanged(nameof(CanUseGameActions));
            }
        }
    }

    public string GreetingText => IsAuthenticated
        ? $"Xin chào, {PlayerName}"
        : "Vui lòng đăng nhập để chơi";

    public bool IsActionBusy
    {
        get => _isActionBusy;
        private set
        {
            if (SetProperty(ref _isActionBusy, value))
            {
                OnPropertyChanged(nameof(CanUseGameActions));
            }
        }
    }

    public bool CanUseGameActions => IsAuthenticated && !IsActionBusy;

    public string ActionStatus
    {
        get => _actionStatus;
        private set => SetProperty(ref _actionStatus, value);
    }

    public string RankingStatus
    {
        get => _rankingStatus;
        private set => SetProperty(ref _rankingStatus, value);
    }

    public bool HasBestRecords => BestRecords.Count > 0;

    
    private string _serverHost = "127.0.0.1";
    public string ServerHost
    {
        get => _serverHost;
        set => SetProperty(ref _serverHost, value);
    }

    // Khai báo cho ServerPort
    private int _serverPort = 5000;
    public int ServerPort
    {
        get => _serverPort;
        set => SetProperty(ref _serverPort, value);
    }
    public string RoomId
    {
        get => _roomId;
        set => SetProperty(ref _roomId, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public string AuthStatus
    {
        get => _authStatus;
        private set => SetProperty(ref _authStatus, value);
    }

    public async Task<bool> ConnectAsync()
    {
        // Chỉ lưu địa chỉ server, không lưu tài khoản để test nhiều client.
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;

            localSettings.Values["ServerHost"] = ServerHost;
            localSettings.Values["ServerPort"] = ServerPort;
        }
        catch (InvalidOperationException)
        {
            try
            {
                string appDataFolder =
                    System.IO.Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "CaroNet");

                System.IO.Directory.CreateDirectory(appDataFolder);

                string filePath =
                    System.IO.Path.Combine(
                        appDataFolder,
                        "settings.txt");

                System.IO.File.WriteAllLines(
                    filePath,
                    new[]
                    {
                    ServerHost,
                    ServerPort.ToString()
                    });
            }
            catch
            {
            }
        }

        try
        {
            await _gameClient.ConnectAsync(
                new ConnectionRequest(
                    string.IsNullOrWhiteSpace(PlayerName) ? "Player" : PlayerName,
                    ServerHost,
                    ServerPort),
                CancellationToken.None);

            ConnectionStatus =
                $"Đã connect tới server {ServerHost}:{ServerPort}";

            _isConnected = true;

            return true;
        }
        catch (Exception ex)
        {
            ConnectionStatus = ex.Message;
            return false;
        }
    }

    public async Task<bool> RegisterAsync()
    {
        string? error = ValidateRegisterFields();
        if (error is not null)
        {
            AuthStatus = error;
            return false;
        }

        try
        {
            IsActionBusy = true;
            ActionStatus = "Đang tạo tài khoản...";

            if (!await EnsureConnectedAsync())
            {
                return false;
            }

            AuthSession session = await _gameClient.RegisterAsync(
                Username,
                Password,
                DisplayName,
                CancellationToken.None);

            ApplyAuthSession(session);
            AuthStatus = "Đăng ký và đăng nhập thành công.";
            return true;
        }
        catch (Exception ex)
        {
            AuthStatus = ex.Message;
            return false;
        }
        finally
        {
            IsActionBusy = false;
            ActionStatus = string.Empty;
        }
    }

    public async Task<bool> LoginAsync()
    {
        string? error = ValidateLoginFields();
        if (error is not null)
        {
            AuthStatus = error;
            return false;
        }

        try
        {
            IsActionBusy = true;
            ActionStatus = "Đang đăng nhập...";

            if (!await EnsureConnectedAsync())
            {
                return false;
            }

            AuthSession session = await _gameClient.LoginAsync(
                Username,
                Password,
                CancellationToken.None);

            ApplyAuthSession(session);
            AuthStatus = "Đăng nhập thành công.";
            return true;
        }
        catch (Exception ex)
        {
            AuthStatus = ex.Message;
            return false;
        }
        finally
        {
            IsActionBusy = false;
            ActionStatus = string.Empty;
        }
    }

    private async Task<bool> EnsureConnectedAsync()
    {
        if (_isConnected)
        {
            return true;
        }

        return await ConnectAsync();
    }

    public async Task<bool> CreateRoomAsync()
    {
        if (!IsAuthenticated)
        {
            ConnectionStatus = "Bạn cần đăng nhập trước khi tạo phòng.";
            return false;
        }

        try
        {
            IsActionBusy = true;
            ActionStatus = "Đang tạo phòng...";

            if (!await EnsureConnectedAsync())
            {
                return false;
            }

            GameViewState state =
                await _gameClient.CreateRoomAsync(
                    CancellationToken.None);

            ConnectionStatus = state.ConnectionStatus;

            return !string.IsNullOrWhiteSpace(state.RoomId);
        }
        catch (Exception ex)
        {
            ConnectionStatus = ex.Message;
            return false;
        }
        finally
        {
            IsActionBusy = false;
            ActionStatus = string.Empty;
        }
    }

    public async Task<bool> JoinRoomAsync()
    {
        string? roomIdError =
            RoomIdValidator.Validate(RoomId);

        if (!IsAuthenticated)
        {
            ConnectionStatus = "Bạn cần đăng nhập trước khi vào phòng.";
            return false;
        }

        if (roomIdError != null)
        {
            ConnectionStatus = roomIdError;
            return false;
        }

        try
        {
            IsActionBusy = true;
            ActionStatus = "Đang vào phòng...";

            if (!await EnsureConnectedAsync())
            {
                return false;
            }

            GameViewState state =
                await _gameClient.JoinRoomAsync(
                    RoomId,
                    CancellationToken.None);

            ConnectionStatus = state.ConnectionStatus;

            return !string.IsNullOrWhiteSpace(state.RoomId);
        }
        catch (Exception ex)
        {
            ConnectionStatus = ex.Message;
            return false;
        }
        finally
        {
            IsActionBusy = false;
            ActionStatus = string.Empty;
        }
    }

    public async Task<bool> QuickMatchAsync()
    {
        if (!IsAuthenticated)
        {
            ConnectionStatus = "Bạn cần đăng nhập trước khi chơi nhanh.";
            return false;
        }

        try
        {
            IsActionBusy = true;
            ActionStatus = "Đang tìm đối thủ...";

            if (!await EnsureConnectedAsync())
            {
                return false;
            }

            GameViewState state = await _gameClient.QuickMatchAsync(
                CancellationToken.None);

            ConnectionStatus = state.ConnectionStatus;

            return !string.IsNullOrWhiteSpace(state.RoomId);
        }
        catch (Exception ex)
        {
            ConnectionStatus = ex.Message;
            return false;
        }
        finally
        {
            IsActionBusy = false;
            ActionStatus = string.Empty;
        }
    }

    private void ApplyAuthSession(AuthSession session)
    {
        Username = session.Username;
        DisplayName = session.DisplayName;
        PlayerName = session.DisplayName;
        Password = string.Empty;
        IsAuthenticated = true;
        ConnectionStatus = $"Đã đăng nhập: {session.DisplayName}";
        OnPropertyChanged(nameof(GreetingText));
    }

    private void RefreshBestRecordsState(string status)
    {
        RankingStatus = status;
        OnPropertyChanged(nameof(HasBestRecords));
    }

    private string? ValidateRegisterFields()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            return "Vui lòng nhập tên hiển thị.";
        }

        return ValidateLoginFields(Username, Password);
    }

    private string? ValidateLoginFields(string? username = null, string? password = null)
    {
        string checkedUsername = username ?? Username;
        string checkedPassword = password ?? Password;

        if (string.IsNullOrWhiteSpace(checkedUsername))
        {
            return "Vui lòng nhập tên đăng nhập.";
        }

        if (string.IsNullOrWhiteSpace(checkedPassword))
        {
            return "Vui lòng nhập mật khẩu.";
        }

        return null;
    }

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;

        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
