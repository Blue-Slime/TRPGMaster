using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterServerUI.Models;

namespace MasterServerUI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings _settings;

    [ObservableProperty] private int _port;
    [ObservableProperty] private string _bindAddress = "localhost";
    [ObservableProperty] private int _maxConnections;
    [ObservableProperty] private bool _autoRefresh;
    [ObservableProperty] private int _refreshIntervalSeconds;
    [ObservableProperty] private bool _autoStartServer;
    [ObservableProperty] private int _windowWidth;
    [ObservableProperty] private int _windowHeight;

    public SettingsViewModel(AppSettings settings)
    {
        _settings = settings;
        Port = settings.Network.Port;
        BindAddress = settings.Network.BindAddress;
        MaxConnections = settings.Network.MaxConnections;
        AutoRefresh = settings.UI.AutoRefresh;
        RefreshIntervalSeconds = settings.UI.RefreshIntervalSeconds;
        AutoStartServer = settings.UI.AutoStartServer;
        WindowWidth = settings.UI.WindowWidth;
        WindowHeight = settings.UI.WindowHeight;
    }

    [RelayCommand]
    private void Save()
    {
        _settings.Network.Port = Port;
        _settings.Network.BindAddress = BindAddress;
        _settings.Network.MaxConnections = MaxConnections;
        _settings.UI.AutoRefresh = AutoRefresh;
        _settings.UI.RefreshIntervalSeconds = RefreshIntervalSeconds;
        _settings.UI.AutoStartServer = AutoStartServer;
        _settings.UI.WindowWidth = WindowWidth;
        _settings.UI.WindowHeight = WindowHeight;
        _settings.Save();
    }
}
