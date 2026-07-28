using Avalonia.Controls;

namespace MasterClient.Services;

/// <summary>
/// 系统托盘图标服务
/// </summary>
public class TrayIconService : IDisposable
{
    private TrayIcon? _trayIcon;
    private NativeMenu? _trayMenu;
    private bool _isVisible;

    public bool IsVisible => _isVisible;

    public event Action? RestoreRequested;
    public event Action? ExitRequested;
    public event Action? SettingsRequested;

    public TrayIconService()
    {
    }

    /// <summary>
    /// 初始化托盘图标（需要在UI线程调用）
    /// </summary>
    public void Initialize()
    {
        if (_trayIcon != null) return;

        // 创建托盘菜单
        _trayMenu = new NativeMenu();

        var restoreItem = new NativeMenuItem("恢复窗口");
        restoreItem.Click += (s, e) => RestoreRequested?.Invoke();
        _trayMenu.Add(restoreItem);

        _trayMenu.Add(new NativeMenuItemSeparator());

        var settingsItem = new NativeMenuItem("设置");
        settingsItem.Click += (s, e) => SettingsRequested?.Invoke();
        _trayMenu.Add(settingsItem);

        _trayMenu.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += (s, e) => ExitRequested?.Invoke();
        _trayMenu.Add(exitItem);

        // 创建托盘图标
        _trayIcon = new TrayIcon
        {
            ToolTipText = "跑团大师启动器",
            Menu = _trayMenu,
            IsVisible = false
        };

        // 尝试加载图标
        try
        {
            var iconUri = new Uri("avares://MasterClient/Assets/avalonia-logo.ico");
            using var stream = Avalonia.Platform.AssetLoader.Open(iconUri);
            _trayIcon.Icon = new WindowIcon(stream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TrayIcon] 加载图标失败: {ex.Message}");
        }

        // 托盘图标点击事件
        _trayIcon.Clicked += OnTrayIconClicked;
    }

    /// <summary>
    /// 显示托盘图标
    /// </summary>
    public void Show()
    {
        if (_trayIcon == null)
        {
            Initialize();
        }

        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = true;
            _isVisible = true;
            System.Diagnostics.Debug.WriteLine("[TrayIcon] 托盘图标已显示");
        }
    }

    /// <summary>
    /// 隐藏托盘图标
    /// </summary>
    public void Hide()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _isVisible = false;
            System.Diagnostics.Debug.WriteLine("[TrayIcon] 托盘图标已隐藏");
        }
    }

    /// <summary>
    /// 更新托盘提示文字
    /// </summary>
    public void SetToolTip(string text)
    {
        if (_trayIcon != null)
        {
            _trayIcon.ToolTipText = text;
        }
    }

    /// <summary>
    /// 托盘图标点击事件处理
    /// </summary>
    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        // 单击托盘图标恢复窗口
        RestoreRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Clicked -= OnTrayIconClicked;
            _trayIcon.IsVisible = false;
            _trayIcon = null;
        }

        _trayMenu = null;
        _isVisible = false;
    }
}
