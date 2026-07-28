using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using MasterClient.Services;
using MasterClient.ViewModels;

namespace MasterClient.Views;

public partial class MainWindow : Window
{
    private TrayIconService? _trayIconService;

    public MainWindow()
    {
        InitializeComponent();

        // 订阅 ViewModel 事件
        DataContextChanged += OnDataContextChanged;

        // 窗口关闭时清理托盘图标
        Closed += OnWindowClosed;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.MinimizeToTrayRequested += OnMinimizeToTrayRequested;
            vm.RestoreFromTrayRequested += OnRestoreFromTrayRequested;
        }

        // 初始化托盘服务
        InitializeTrayIcon();
    }

    private void InitializeTrayIcon()
    {
        _trayIconService = App.Services?.GetService<TrayIconService>();
        if (_trayIconService != null)
        {
            _trayIconService.Initialize();

            // 订阅托盘事件
            _trayIconService.RestoreRequested += OnTrayRestoreRequested;
            _trayIconService.SettingsRequested += OnTraySettingsRequested;
            _trayIconService.ExitRequested += OnTrayExitRequested;
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void MinimizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void OnMinimizeToTrayRequested()
    {
        // 最小化到托盘
        Hide();

        // 显示托盘图标
        _trayIconService?.Show();
        _trayIconService?.SetToolTip("跑团大师启动器 - 运行中");
    }

    private void OnRestoreFromTrayRequested()
    {
        RestoreWindow();
    }

    private void OnTrayRestoreRequested()
    {
        RestoreWindow();
    }

    private void OnTraySettingsRequested()
    {
        // 先恢复窗口，再导航到设置页面
        RestoreWindow();

        if (DataContext is MainWindowViewModel vm)
        {
            vm.ShowSettingsCommand.Execute(null);
        }
    }

    private void OnTrayExitRequested()
    {
        // 完全退出应用
        _trayIconService?.Dispose();
        Close();
    }

    private void RestoreWindow()
    {
        // 隐藏托盘图标
        _trayIconService?.Hide();

        // 恢复窗口
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // 清理托盘图标
        if (_trayIconService != null)
        {
            _trayIconService.RestoreRequested -= OnTrayRestoreRequested;
            _trayIconService.SettingsRequested -= OnTraySettingsRequested;
            _trayIconService.ExitRequested -= OnTrayExitRequested;
            _trayIconService.Dispose();
        }
    }
}
