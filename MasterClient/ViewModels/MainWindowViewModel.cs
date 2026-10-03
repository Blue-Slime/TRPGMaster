using System.Collections.ObjectModel;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterClient.Models;
using MasterClient.Services;
using MapEngine.Avalonia.Hosting;
using MapEngine.Core.Hosting;
using Avalonia.Input.Platform;

namespace MasterClient.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private readonly RoomDiscoveryService _roomDiscoveryService;
    private readonly GameLauncherService _gameLauncherService;
    private readonly SettingsService _settingsService;
    private readonly UpdateService _updateService;
    private readonly ModuleLauncherService _moduleLauncherService;
    private readonly MasterServerConnectionService _connectionService;

    // 地图窗口懒加载（门户入口用独立窗口，首次点击才创建，之后复用）
    private MapEngine.Avalonia.Views.MapEditorWindow? _mapWindow;
    private System.Timers.Timer? _bannerTimer;

    // 房间连接功能已拆分为独立组件 ConnectingViewModel（见 Connect/）
    public MasterClient.Connect.ConnectingViewModel ConnectingVm { get; } = new();

    #region Observable Properties

    [ObservableProperty]
    private bool _isLoginView = false;

    [ObservableProperty]
    private bool _isMainPanelView = true;

    // 正在连接房间的过渡界面（由 ConnectingVm.IsBusy 驱动，此处镜像用于视图显隐切换）
    [ObservableProperty]
    private bool _isConnectingView = false;

    // ===== 账号登录中状态（与房间连接彻底解耦，独立一套）=====
    [ObservableProperty]
    private bool _isLoginBusy = false;

    [ObservableProperty]
    private string _loginBusyStatus = "正在登录...";

    private const int MaxLoginRetries = 3;

    [ObservableProperty]
    private string _account = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _rememberPassword = true;

    [ObservableProperty]
    private bool _autoLogin = false;

    [ObservableProperty]
    private bool _isLoggingIn = false;

    [ObservableProperty]
    private string _loginErrorMessage = string.Empty;

    [ObservableProperty]
    private string _directConnectAddress = string.Empty;

    // 直连世界：房间ID / 用户ID（配合 DirectConnectAddress 作为服务器地址）
    // 目前先全量输入，未来换一次性加密邀请链接 + 房间记忆
    [ObservableProperty]
    private string _directRoomId = string.Empty;

    // 全局用户ID（在设置页手填，造访/记忆房间统一使用）
    [ObservableProperty]
    private string _userId = "player";

    [ObservableProperty]
    private string _directConnectError = string.Empty;

    [ObservableProperty]
    private bool _isScanning = false;

    [ObservableProperty]
    private RoomInfo? _selectedRoom;

    [ObservableProperty]
    private ObservableCollection<RoomInfo> _discoveredRooms = new();

    [ObservableProperty]
    private ObservableCollection<RecentRoom> _recentRooms = new();

    // 导航相关
    [ObservableProperty]
    private NavigationPage _currentPage = NavigationPage.Home;

    [ObservableProperty]
    private bool _isHomePage = true;

    [ObservableProperty]
    private bool _isConnectRoomPage = false;

    [ObservableProperty]
    private bool _isRecentRoomsPage = false;

    [ObservableProperty]
    private bool _isEditorsPage = false;

    [ObservableProperty]
    private bool _isStoragePage = false;

    [ObservableProperty]
    private bool _isSettingsPage = false;

    [ObservableProperty]
    private bool _isHelpPage = false;

    // 海报轮播
    [ObservableProperty]
    private ObservableCollection<BannerInfo> _banners = new();

    [ObservableProperty]
    private BannerInfo? _currentBanner;

    [ObservableProperty]
    private int _currentBannerIndex = 0;

    // ===== 设置页面 =====

    // 存储设置
    [ObservableProperty]
    private string _defaultRoomsPath = string.Empty;

    [ObservableProperty]
    private string _assetLibraryPath = string.Empty;

    // 启动设置
    [ObservableProperty]
    private bool _startWithWindows = false;

    [ObservableProperty]
    private bool _minimizeToTrayOnClose = true;

    [ObservableProperty]
    private bool _checkUpdateOnStart = true;

    // 显示设置
    [ObservableProperty]
    private string _selectedTheme = "Dark";

    [ObservableProperty]
    private int _fontSize = 14;

    [ObservableProperty]
    private bool _showTimestamp = true;

    [ObservableProperty]
    private bool _compactMode = false;

    // 输入状态指示器
    [ObservableProperty]
    private bool _typingIndicatorEnabled = true;

    [ObservableProperty]
    private int _typingSendCooldown = 4;

    [ObservableProperty]
    private int _typingDisplayDuration = 5;

    [ObservableProperty]
    private bool _typingClearModeImmediate = true;

    // 文件传输
    [ObservableProperty]
    private int _chunkSizeKB = 64;

    [ObservableProperty]
    private int _chunkTimeoutSeconds = 30;

    [ObservableProperty]
    private int _globalTimeoutSeconds = 600;

    [ObservableProperty]
    private int _maxRetryCount = 3;

    // 通知设置
    [ObservableProperty]
    private bool _enableSound = true;

    [ObservableProperty]
    private bool _enableDesktopNotification = true;

    [ObservableProperty]
    private bool _highlightMention = true;

    // 公告栏
    [ObservableProperty]
    private ObservableCollection<AnnouncementInfo> _announcements = new();

    // 公告详情弹窗
    [ObservableProperty]
    private bool _isAnnouncementDetailOpen = false;

    [ObservableProperty]
    private AnnouncementInfo? _selectedAnnouncement;

    // 更新检查
    [ObservableProperty]
    private bool _isCheckingUpdate = false;

    [ObservableProperty]
    private bool _hasUpdate = false;

    [ObservableProperty]
    private bool _isUpdateDialogOpen = false;

    [ObservableProperty]
    private string _latestVersion = string.Empty;

    [ObservableProperty]
    private string _updateNotes = string.Empty;

    // MasterServer 连接
    [ObservableProperty]
    private bool _isConnectedToServer = false;

    [ObservableProperty]
    private bool _isConnecting = false;

    [ObservableProperty]
    private string _serverAddress = "localhost:7890";

    [ObservableProperty]
    private string _connectionStatus = "未连接";

    [ObservableProperty]
    private string _connectionErrorMessage = string.Empty;

    // 本地服务器快捷入口（静默扫描）
    [ObservableProperty]
    private bool _isLocalServerAvailable = false;

    [ObservableProperty]
    private bool _isCheckingLocalServer = false;

    [ObservableProperty]
    private ObservableCollection<LocalRoomInfo> _localServerRooms = new();

    #endregion

    #region Computed Properties

    public UserProfile? CurrentUser => _authService.CurrentUser;
    public bool IsLoggedIn => _authService.IsLoggedIn;
    public bool IsGuestMode => _authService.IsGuestMode;
    /// <summary>
    /// 是否需要显示登录提示（未登录且不是游客模式）
    /// </summary>
    public bool NeedsLogin => !IsLoggedIn && !IsGuestMode;
    /// <summary>
    /// 是否显示欢迎消息（已登录或游客模式）
    /// </summary>
    public bool ShowWelcome => IsLoggedIn || IsGuestMode;
    public string UserDisplayName => CurrentUser?.Username ?? "游客";
    public string SubscriptionDisplay => CurrentUser?.Subscription switch
    {
        SubscriptionLevel.VIP => "VIP会员",
        SubscriptionLevel.Premium => "高级会员",
        _ => "免费用户"
    };

    #endregion

    public MainWindowViewModel(
        AuthService authService,
        RoomDiscoveryService roomDiscoveryService,
        GameLauncherService gameLauncherService,
        SettingsService settingsService,
        UpdateService updateService,
        ModuleLauncherService moduleLauncherService,
        MasterServerConnectionService connectionService)
    {
        _authService = authService;
        _roomDiscoveryService = roomDiscoveryService;
        _gameLauncherService = gameLauncherService;
        _settingsService = settingsService;
        _updateService = updateService;
        _moduleLauncherService = moduleLauncherService;
        _connectionService = connectionService;

        // 加载保存的设置
        LoadSettings();

        // 初始化海报
        InitializeBanners();

        // 初始化公告
        InitializeAnnouncements();

        // 订阅事件
        _roomDiscoveryService.RoomDiscovered += OnRoomDiscovered;
        _gameLauncherService.GameStarted += OnGameStarted;
        _gameLauncherService.GameExited += OnGameExited;

        // 订阅连接服务事件
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _connectionService.ConnectionError += OnConnectionError;

        // 房间连接组件事件：驱动"正在连接"界面显隐 + 成功记忆房间
        ConnectingVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectingVm.IsBusy))
            {
                if (ConnectingVm.IsBusy) { IsLoginView = false; IsMainPanelView = false; IsConnectingView = true; }
            }
        };
        ConnectingVm.Succeeded += (server, room, channel, user) =>
        {
            _settingsService.AddRecentRoom(new RecentRoom
            {
                RoomId = room, RoomName = room, ServerAddress = server,
                ChannelId = channel, UserId = user
            });
            RecentRooms = new ObservableCollection<RecentRoom>(_settingsService.Settings.RecentRooms);
        };
        ConnectingVm.Finished += () =>
        {
            // 仅在"成功进房"或"错误态下手动返回"时触发 → 回门户。
            // 连接失败不再触发此事件（改为停在连接界面显示错误 + 重试/返回）。
            SwitchToMainPanel();
        };

        // 初始化已知服务器列表（不再自动创建本地服务器记录）
        InitializeKnownServers();

        // 移除自动扫描：用户通过输入地址手动连接服务器
        // _ = ScanLocalServerSilentlyAsync();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Settings;

        // 存储设置
        DefaultRoomsPath = settings.DefaultRoomsPath;
        AssetLibraryPath = settings.AssetLibraryPath ?? string.Empty;

        // 全局用户ID
        if (!string.IsNullOrWhiteSpace(settings.UserId))
            UserId = settings.UserId;

        // 登录设置
        RememberPassword = settings.RememberPassword;
        AutoLogin = settings.AutoLogin;

        if (!string.IsNullOrEmpty(settings.SavedAccount))
        {
            Account = settings.SavedAccount;
        }

        // 加载保存的密码
        if (RememberPassword && !string.IsNullOrEmpty(settings.EncryptedPassword))
        {
            Password = settings.EncryptedPassword; // TODO: 解密密码
        }

        // 启动设置
        StartWithWindows = settings.StartWithWindows;
        MinimizeToTrayOnClose = settings.MinimizeToTray;
        CheckUpdateOnStart = settings.CheckUpdateOnStart;

        // 显示设置
        SelectedTheme = settings.Display.Theme;
        FontSize = settings.Display.FontSize;
        ShowTimestamp = settings.Display.ShowTimestamp;
        CompactMode = settings.Display.CompactMode;

        // 输入状态指示器
        TypingIndicatorEnabled = settings.TypingIndicator.Enabled;
        TypingSendCooldown = settings.TypingIndicator.SendCooldownSeconds;
        TypingDisplayDuration = settings.TypingIndicator.DisplayDurationSeconds;
        TypingClearModeImmediate = settings.TypingIndicator.ClearModeOnSend == "Immediate";

        // 文件传输
        ChunkSizeKB = settings.FileTransfer.ChunkSizeKB;
        ChunkTimeoutSeconds = settings.FileTransfer.ChunkTimeoutSeconds;
        GlobalTimeoutSeconds = settings.FileTransfer.GlobalTimeoutSeconds;
        MaxRetryCount = settings.FileTransfer.MaxRetryCount;

        // 通知设置
        EnableSound = settings.Notification.EnableSound;
        EnableDesktopNotification = settings.Notification.EnableDesktopNotification;
        HighlightMention = settings.Notification.HighlightMention;

        // 加载最近房间
        RecentRooms = new ObservableCollection<RecentRoom>(settings.RecentRooms);
    }

    #region Banner Carousel

    private void InitializeBanners()
    {
        Banners = new ObservableCollection<BannerInfo>
        {
            new BannerInfo
            {
                Id = 1,
                Tag = "🎉 新版本发布",
                Title = "跑团大师 1.0",
                Subtitle = "全新的桌游体验",
                Description = "与好友一起开启奇幻冒险之旅。\n支持多种规则系统，自由创建角色与世界。",
                Icon = "🎲",
                Type = BannerType.Update,
                ActionText = "立即开始",
                Gradient = new BannerGradient
                {
                    StartColor = "#4c1d95",
                    MiddleColor = "#7c3aed",
                    EndColor = "#a78bfa"
                }
            },
            new BannerInfo
            {
                Id = 2,
                Tag = "📢 功能介绍",
                Title = "角色卡编辑器",
                Subtitle = "创建你的专属角色",
                Description = "支持多种规则系统的角色卡模板。\n自定义属性、技能、背景故事。",
                Icon = "👤",
                Type = BannerType.Feature,
                ActionText = "打开编辑器",
                Gradient = new BannerGradient
                {
                    StartColor = "#065f46",
                    MiddleColor = "#059669",
                    EndColor = "#34d399"
                }
            },
            new BannerInfo
            {
                Id = 3,
                Tag = "🗺️ 地图系统",
                Title = "战斗地图编辑器",
                Subtitle = "打造独特战场",
                Description = "网格化地图编辑，支持雾战系统。\n丰富的地形和障碍物素材。",
                Icon = "🗺️",
                Type = BannerType.Feature,
                ActionText = "打开编辑器",
                Gradient = new BannerGradient
                {
                    StartColor = "#1e3a8a",
                    MiddleColor = "#3b82f6",
                    EndColor = "#93c5fd"
                }
            },
            new BannerInfo
            {
                Id = 4,
                Tag = "🎲 骰子系统",
                Title = "自定义骰子规则",
                Subtitle = "支持各种TRPG规则",
                Description = "COC、DND、PF等规则预设。\n自定义骰子表达式和检定规则。",
                Icon = "🎲",
                Type = BannerType.Feature,
                ActionText = "打开编辑器",
                Gradient = new BannerGradient
                {
                    StartColor = "#9d174d",
                    MiddleColor = "#ec4899",
                    EndColor = "#f9a8d4"
                }
            }
        };

        if (Banners.Count > 0)
        {
            CurrentBanner = Banners[0];
            CurrentBannerIndex = 0;
        }

        // 启动自动轮播
        StartBannerCarousel();
    }

    private void StartBannerCarousel()
    {
        _bannerTimer = new System.Timers.Timer(5000); // 5秒切换
        _bannerTimer.Elapsed += OnBannerTimerElapsed;
        _bannerTimer.AutoReset = true;
        _bannerTimer.Start();
    }

    private void OnBannerTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            NextBanner();
        });
    }

    [RelayCommand]
    private void NextBanner()
    {
        if (Banners.Count == 0) return;

        CurrentBannerIndex = (CurrentBannerIndex + 1) % Banners.Count;
        CurrentBanner = Banners[CurrentBannerIndex];
    }

    [RelayCommand]
    private void PreviousBanner()
    {
        if (Banners.Count == 0) return;

        CurrentBannerIndex = (CurrentBannerIndex - 1 + Banners.Count) % Banners.Count;
        CurrentBanner = Banners[CurrentBannerIndex];
    }

    [RelayCommand]
    private void GoToBanner(int index)
    {
        if (index >= 0 && index < Banners.Count)
        {
            CurrentBannerIndex = index;
            CurrentBanner = Banners[index];
        }
    }

    [RelayCommand]
    private void BannerAction()
    {
        if (CurrentBanner == null) return;

        switch (CurrentBanner.Id)
        {
            case 1: // 新版本 - 开始游戏
                NavigateTo(NavigationPage.ConnectRoom);
                break;
            case 2: // 角色卡编辑器
                OpenCharacterEditor();
                break;
            case 3: // 地图编辑器
                OpenMapEditor();
                break;
            case 4: // 骰子编辑器
                OpenDiceEditor();
                break;
        }
    }

    #endregion

    #region Announcements

    private void InitializeAnnouncements()
    {
        Announcements = new ObservableCollection<AnnouncementInfo>
        {
            new AnnouncementInfo
            {
                Id = 1,
                Tag = "更新",
                TagColor = "#2d5a4a",  // 深青绿色，与UI协调
                Title = "跑团大师 1.0 正式发布",
                Summary = "全新的桌游体验，支持多种规则系统，自由创建角色与世界。",
                Content = @"亲爱的冒险者们，

跑团大师 1.0 版本正式发布！这是一个全新的桌游平台，为您带来前所未有的 TRPG 体验。

主要功能：
• 支持多种规则系统（COC、DND、PF等）
• 自由创建角色与世界
• 实时多人在线游戏
• 内置骰子系统和规则计算
• 地图编辑器和战斗系统

感谢所有参与测试的玩家，是你们的反馈让这款产品变得更好！

祝游戏愉快！
跑团大师开发团队",
                PublishDate = DateTime.Now,
                Type = AnnouncementType.Update,
                IsImportant = true,
                Author = "官方"
            },
            new AnnouncementInfo
            {
                Id = 2,
                Tag = "公告",
                TagColor = "#2d4a6a",  // 深蓝色，与UI协调
                Title = "新用户注册指南",
                Summary = "欢迎来到跑团大师！查看快速入门指南，了解如何创建角色和加入游戏。",
                Content = @"欢迎来到跑团大师！

快速入门步骤：
1. 注册账号或以游客身份进入
2. 创建或加入一个世界（房间）
3. 在编辑器中创建您的角色卡
4. 与好友一起开始冒险！

常见问题：
Q: 如何创建角色？
A: 点击左侧导航栏的「编辑器」，选择「角色卡编辑器」。

Q: 如何加入朋友的房间？
A: 可以通过局域网扫描、直接连接或邀请码加入。

Q: 游戏支持哪些规则？
A: 目前支持 COC 7版、DND 5E、PF 2E 等主流规则。

如有更多问题，请访问帮助中心。",
                PublishDate = DateTime.Now.AddDays(-1),
                Type = AnnouncementType.Notice,
                Author = "官方"
            },
            new AnnouncementInfo
            {
                Id = 3,
                Tag = "活动",
                TagColor = "#5a4a2d",  // 深橙棕色，与UI协调
                Title = "首届线上跑团大赛报名开始",
                Summary = "参与活动即有机会获得VIP会员资格和精美周边奖品！",
                Content = @"🎉 首届线上跑团大赛正式开启！

活动时间：2025年1月1日 - 2025年1月31日

参与方式：
1. 组建 4-6 人小队
2. 在活动期间完成至少一个完整模组
3. 录制精彩片段并投稿

奖品设置：
🥇 冠军队伍：每人获得永久VIP + 限定周边礼包
🥈 亚军队伍：每人获得1年VIP + 定制骰子套装
🥉 季军队伍：每人获得6个月VIP + 游戏内称号

特别奖：
• 最佳KP奖：专属主持人徽章
• 最佳角色扮演奖：限定角色皮肤
• 人气奖：由观众投票决定

立即报名，开启你的冒险之旅！",
                PublishDate = DateTime.Now.AddDays(-2),
                Type = AnnouncementType.Event,
                Author = "活动组"
            }
        };
    }

    [RelayCommand]
    private void OpenAnnouncementDetail(AnnouncementInfo announcement)
    {
        if (announcement == null) return;

        SelectedAnnouncement = announcement;
        IsAnnouncementDetailOpen = true;
        System.Diagnostics.Debug.WriteLine($"[StartMaster] 打开公告详情: {announcement.Title}");
    }

    [RelayCommand]
    private void CloseAnnouncementDetail()
    {
        IsAnnouncementDetailOpen = false;
        System.Diagnostics.Debug.WriteLine("[StartMaster] 关闭公告详情");
    }

    #endregion

    #region Navigation

    private void NavigateTo(NavigationPage page)
    {
        CurrentPage = page;

        // 更新所有页面可见性
        IsHomePage = page == NavigationPage.Home;
        IsConnectRoomPage = page == NavigationPage.ConnectRoom;
        IsRecentRoomsPage = page == NavigationPage.RecentRooms;
        IsEditorsPage = page == NavigationPage.Editors;
        IsStoragePage = page == NavigationPage.Storage;
        IsSettingsPage = page == NavigationPage.Settings;
        IsHelpPage = page == NavigationPage.Help;

        System.Diagnostics.Debug.WriteLine($"[StartMaster] 导航到: {page}");
    }

    #endregion

    #region Login Commands

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Account) || string.IsNullOrWhiteSpace(Password))
        {
            LoginErrorMessage = "请输入账号和密码";
            return;
        }

        // 账号登录用自己独立的"登录中"状态（不再和房间连接共用）
        IsLoginBusy = true;
        LoginErrorMessage = string.Empty;

        for (int attempt = 1; attempt <= MaxLoginRetries; attempt++)
        {
            LoginBusyStatus = attempt == 1
                ? "正在登录..."
                : $"登录失败，第 {attempt} 次重试中...";

            try
            {
                var success = await _authService.LoginAsync(Account, Password, RememberPassword);
                if (success)
                {
                    IsLoginBusy = false;
                    SwitchToMainPanel();
                    return;
                }

                // 认证失败（账号/密码错误），不重试
                IsLoginBusy = false;
                LoginErrorMessage = "登录失败，请检查账号密码";
                return;
            }
            catch (Exception ex)
            {
                if (attempt < MaxLoginRetries)
                    await Task.Delay(3000);
                else
                {
                    IsLoginBusy = false;
                    LoginErrorMessage = $"登录失败（已重试 {MaxLoginRetries} 次）：{ex.Message}";
                }
            }
        }
    }

    [RelayCommand]
    private void EnterGuestMode()
    {
        _authService.EnterGuestMode();
        SwitchToMainPanel();
    }

    [RelayCommand]
    private void Logout()
    {
        _authService.Logout();
        OnPropertyChanged(nameof(CurrentUser));
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(IsGuestMode));
        OnPropertyChanged(nameof(NeedsLogin));
        OnPropertyChanged(nameof(ShowWelcome));
        OnPropertyChanged(nameof(UserDisplayName));
        OnPropertyChanged(nameof(SubscriptionDisplay));
    }

    [RelayCommand]
    private void ShowLogin()
    {
        SwitchToLoginView();
    }

    [RelayCommand]
    private void BackToMainPanel()
    {
        SwitchToMainPanel();
    }

    private void SwitchToMainPanel()
    {
        IsLoginView = false;
        IsConnectingView = false;
        IsMainPanelView = true;
        OnPropertyChanged(nameof(CurrentUser));
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(IsGuestMode));
        OnPropertyChanged(nameof(NeedsLogin));
        OnPropertyChanged(nameof(ShowWelcome));
        OnPropertyChanged(nameof(UserDisplayName));
        OnPropertyChanged(nameof(SubscriptionDisplay));
    }

    private void SwitchToLoginView()
    {
        IsMainPanelView = false;
        IsConnectingView = false;
        IsLoginView = true;
        Password = string.Empty;
    }

    #endregion

    #region Room Discovery Commands

    [RelayCommand]
    private async Task ScanLanRoomsAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        DiscoveredRooms.Clear();

        try
        {
            await _roomDiscoveryService.StartScanAsync();
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void StopScan()
    {
        _roomDiscoveryService.StopScan();
        IsScanning = false;
    }

    private void OnRoomDiscovered(RoomInfo room)
    {
        // 确保在 UI 线程更新
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // 检查是否已存在
            var existing = DiscoveredRooms.FirstOrDefault(r => r.ServerAddress == room.ServerAddress);
            if (existing == null)
            {
                DiscoveredRooms.Add(room);
            }
        });
    }

    // —— 房间连接：委托给独立的 ConnectingVm 组件 ——

    [RelayCommand]
    private async Task DirectConnect()
    {
        DirectConnectError = string.Empty;

        if (string.IsNullOrWhiteSpace(DirectConnectAddress) ||
            string.IsNullOrWhiteSpace(DirectRoomId))
        {
            DirectConnectError = "请填写服务器地址和房间ID";
            return;
        }
        if (string.IsNullOrWhiteSpace(UserId))
        {
            DirectConnectError = "请先在设置页填写用户ID";
            return;
        }

        await ConnectingVm.ConnectAsync(
            MasterClient.Connect.ConnectingViewModel.NormalizeServerUrl(DirectConnectAddress.Trim()),
            DirectRoomId.Trim(),
            "channel_lobby",
            UserId.Trim());
    }

    [RelayCommand]
    private async Task JoinRecentRoom(RecentRoom? recent)
    {
        if (recent == null) return;
        if (string.IsNullOrWhiteSpace(UserId))
        {
            DirectConnectError = "请先在设置页填写用户ID";
            NavigateTo(NavigationPage.ConnectRoom);
            return;
        }
        // 用全局用户ID进房（不再用记忆里存的旧ID）
        await ConnectingVm.ConnectAsync(
            MasterClient.Connect.ConnectingViewModel.NormalizeServerUrl(recent.ServerAddress),
            recent.RoomId,
            string.IsNullOrWhiteSpace(recent.ChannelId) ? "channel_lobby" : recent.ChannelId,
            UserId.Trim());
    }

    // 兼容旧的服务器分组/脱机入口（逐步废弃）：同样走 ConnectingVm
    private async Task JoinRoomAsync(RoomInfo room)
    {
        if (room == null) return;
        await ConnectingVm.ConnectAsync(
            MasterClient.Connect.ConnectingViewModel.NormalizeServerUrl(room.ServerAddress),
            room.Id.ToString(),
            "channel_lobby",
            string.IsNullOrWhiteSpace(UserDisplayName) ? "player" : UserDisplayName);
    }

    #endregion

    #region Feature Commands

    [RelayCommand]
    private void ShowOnlineRooms()
    {
        if (!IsLoggedIn)
        {
            return;
        }
        // TODO: 显示联网房间列表
    }

    [RelayCommand]
    private void ShowCloudRooms()
    {
        if (!IsLoggedIn || CurrentUser?.Subscription == SubscriptionLevel.Free)
        {
            return;
        }
        // TODO: 显示云房间列表
    }

    #endregion

    #region MasterServer Connection Commands

    [RelayCommand]
    private async Task ConnectToServerAsync()
    {
        if (IsConnecting || IsConnectedToServer) return;

        if (string.IsNullOrWhiteSpace(ServerAddress))
        {
            ConnectionErrorMessage = "请输入服务器地址";
            return;
        }

        IsConnecting = true;
        ConnectionStatus = "正在连接...";
        ConnectionErrorMessage = string.Empty;

        try
        {
            var success = await _connectionService.ConnectAsync(ServerAddress);
            if (!success)
            {
                ConnectionStatus = "连接失败";
                ConnectionErrorMessage = "无法连接到服务器";
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = "连接错误";
            ConnectionErrorMessage = ex.Message;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectFromServerAsync()
    {
        if (!IsConnectedToServer) return;

        ConnectionStatus = "正在断开...";
        await _connectionService.DisconnectAsync();
    }

    [RelayCommand]
    private async Task RefreshServerRoomsAsync()
    {
        if (!IsConnectedToServer) return;

        await _connectionService.RequestRoomListAsync();
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsConnectedToServer = e.IsConnected;

            if (e.IsConnected)
            {
                ConnectionStatus = $"已连接 (会话: {e.SessionId?[..8]}...)";
                ConnectionErrorMessage = string.Empty;
            }
            else
            {
                ConnectionStatus = string.IsNullOrEmpty(e.DisconnectReason)
                    ? "已断开"
                    : $"已断开: {e.DisconnectReason}";
            }
        });
    }

    private void OnConnectionError(object? sender, ConnectionErrorEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ConnectionErrorMessage = e.ErrorMessage;
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 连接错误: {e.ErrorMessage}");
        });
    }

    #endregion

    #region V2 Main Commands

    [RelayCommand]
    private void ShowHome()
    {
        NavigateTo(NavigationPage.Home);
    }

    [RelayCommand]
    private void ConnectRoom()
    {
        NavigateTo(NavigationPage.ConnectRoom);
        ConnectRoomRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowEditors()
    {
        NavigateTo(NavigationPage.Editors);
        ShowEditorsRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowStorage()
    {
        NavigateTo(NavigationPage.Storage);
        ShowStorageRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowRecentRooms()
    {
        NavigateTo(NavigationPage.RecentRooms);
        ShowRecentRoomsRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowHelp()
    {
        NavigateTo(NavigationPage.Help);
        ShowHelpRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowSettings()
    {
        NavigateTo(NavigationPage.Settings);
    }

    [RelayCommand]
    private void CreateOfflineRoom()
    {
        System.Diagnostics.Debug.WriteLine("[StartMaster] 创建脱机房间");
        var offlineRoom = new RoomInfo
        {
            Id = Guid.NewGuid(),
            Name = "脱机房间",
            GMName = UserDisplayName,
            ServerAddress = "localhost:0",
            Type = RoomType.Offline,
            PlayerCount = 1,
            MaxPlayers = 6,
            IsOnline = false
        };
        _ = JoinRoomAsync(offlineRoom);
    }

    [RelayCommand]
    private void RemoveRecentRoom(RecentRoom? recent)
    {
        if (recent == null) return;
        _settingsService.RemoveRecentRoom(recent);
        RecentRooms = new ObservableCollection<RecentRoom>(_settingsService.Settings.RecentRooms);
    }

    [RelayCommand]
    private void ClearRecentRooms()
    {
        _settingsService.ClearRecentRooms();
        RecentRooms.Clear();
    }

    public event Action? ConnectRoomRequested;
    public event Action? ShowEditorsRequested;
    public event Action? ShowStorageRequested;
    public event Action? ShowRecentRoomsRequested;
    public event Action? ShowHelpRequested;

    #endregion

    #region Editor Commands

    [RelayCommand]
    private void OpenCharacterEditor()
    {
        _moduleLauncherService.LaunchModule("CharacterEditor");
    }

    [RelayCommand]
    private void OpenCardFactory()
    {
        _moduleLauncherService.LaunchModule("CardFactory");
    }

    [RelayCommand]
    private void OpenMapEditor()
    {
        // 门户入口：以独立窗口打开地图编辑器（地图在聊天室内是嵌入覆盖层，这里是独立编辑）。
        // 懒加载：首次调用才构造窗口，之后直接激活。
        if (_mapWindow is null || !_mapWindow.IsVisible)
        {
            var vm = new MapEngine.Avalonia.ViewModels.MainWindowViewModel();
            _mapWindow = new MapEngine.Avalonia.Views.MapEditorWindow { DataContext = vm };

            var owner = GetMainWindow();
            if (owner != null)
                _mapWindow.Show(owner);
            else
                _mapWindow.Show();
        }
        else
        {
            _mapWindow.Activate();
        }
    }

    private static Avalonia.Controls.Window? GetMainWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var w in desktop.Windows)
                if (w.IsActive) return w;
            return desktop.MainWindow;
        }
        return null;
    }

    [RelayCommand]
    private void OpenDiceEditor()
    {
        _moduleLauncherService.LaunchModule("DiceEditor");
    }

    [RelayCommand]
    private void OpenRulebookEditor()
    {
        _moduleLauncherService.LaunchModule("RulebookEditor");
    }

    #endregion

    #region Storage Commands

    [RelayCommand]
    private void OpenCharacterStorage()
    {
        var path = GetStoragePath("Characters");
        OpenFolderInExplorer(path);
    }

    [RelayCommand]
    private void OpenMapStorage()
    {
        var path = GetStoragePath("Maps");
        OpenFolderInExplorer(path);
    }

    [RelayCommand]
    private void OpenRulebookStorage()
    {
        var path = GetStoragePath("Rulebooks");
        OpenFolderInExplorer(path);
    }

    [RelayCommand]
    private void OpenAssetStorage()
    {
        var path = GetStoragePath("Assets");
        OpenFolderInExplorer(path);
    }

    private string GetStoragePath(string folderName)
    {
        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var exeDir = System.IO.Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
        var storagePath = System.IO.Path.Combine(exeDir, "Storage", folderName);

        // 确保目录存在
        if (!System.IO.Directory.Exists(storagePath))
        {
            System.IO.Directory.CreateDirectory(storagePath);
        }

        return storagePath;
    }

    private void OpenFolderInExplorer(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 打开文件夹: {path}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 打开文件夹失败: {ex.Message}");
        }
    }

    #endregion

    #region Settings Commands

    [RelayCommand]
    private async Task BrowseDefaultRoomsPathAsync()
    {
        try
        {
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "选择默认房间存储路径",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                DefaultRoomsPath = folders[0].Path.LocalPath;
                System.Diagnostics.Debug.WriteLine($"[StartMaster] 已选择房间存储路径: {DefaultRoomsPath}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 选择文件夹失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task BrowseAssetLibraryPathAsync()
    {
        try
        {
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "选择全局素材库路径",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                AssetLibraryPath = folders[0].Path.LocalPath;
                System.Diagnostics.Debug.WriteLine($"[StartMaster] 已选择素材库路径: {AssetLibraryPath}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 选择文件夹失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var settings = _settingsService.Settings;

        // 存储设置
        settings.DefaultRoomsPath = DefaultRoomsPath;
        settings.AssetLibraryPath = string.IsNullOrWhiteSpace(AssetLibraryPath) ? null : AssetLibraryPath.Trim();

        // 全局用户ID
        settings.UserId = string.IsNullOrWhiteSpace(UserId) ? "player" : UserId.Trim();

        // 启动设置
        settings.RememberPassword = RememberPassword;
        settings.AutoLogin = AutoLogin;
        settings.StartWithWindows = StartWithWindows;
        settings.MinimizeToTray = MinimizeToTrayOnClose;
        settings.CheckUpdateOnStart = CheckUpdateOnStart;

        // 显示设置
        settings.Display.Theme = SelectedTheme;
        settings.Display.FontSize = FontSize;
        settings.Display.ShowTimestamp = ShowTimestamp;
        settings.Display.CompactMode = CompactMode;

        // 输入状态指示器
        settings.TypingIndicator.Enabled = TypingIndicatorEnabled;
        settings.TypingIndicator.SendCooldownSeconds = TypingSendCooldown;
        settings.TypingIndicator.DisplayDurationSeconds = TypingDisplayDuration;
        settings.TypingIndicator.ClearModeOnSend = TypingClearModeImmediate ? "Immediate" : "Timeout";

        // 文件传输
        settings.FileTransfer.ChunkSizeKB = ChunkSizeKB;
        settings.FileTransfer.ChunkTimeoutSeconds = ChunkTimeoutSeconds;
        settings.FileTransfer.GlobalTimeoutSeconds = GlobalTimeoutSeconds;
        settings.FileTransfer.MaxRetryCount = MaxRetryCount;

        // 通知设置
        settings.Notification.EnableSound = EnableSound;
        settings.Notification.EnableDesktopNotification = EnableDesktopNotification;
        settings.Notification.HighlightMention = HighlightMention;

        _settingsService.Save();
        System.Diagnostics.Debug.WriteLine("[StartMaster] 设置已保存");
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        // 存储设置
        DefaultRoomsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Rooms");
        AssetLibraryPath = string.Empty;

        // 启动设置
        RememberPassword = true;
        AutoLogin = false;
        StartWithWindows = false;
        MinimizeToTrayOnClose = true;
        CheckUpdateOnStart = true;

        // 显示设置
        SelectedTheme = "Dark";
        FontSize = 14;
        ShowTimestamp = true;
        CompactMode = false;

        // 输入状态指示器
        TypingIndicatorEnabled = true;
        TypingSendCooldown = 4;
        TypingDisplayDuration = 5;
        TypingClearModeImmediate = true;

        // 文件传输
        ChunkSizeKB = 64;
        ChunkTimeoutSeconds = 30;
        GlobalTimeoutSeconds = 600;
        MaxRetryCount = 3;

        // 通知设置
        EnableSound = true;
        EnableDesktopNotification = true;
        HighlightMention = true;

        System.Diagnostics.Debug.WriteLine("[StartMaster] 设置已重置为默认值");
    }

    [RelayCommand]
    private void OpenDefaultRoomsFolder()
    {
        if (string.IsNullOrEmpty(DefaultRoomsPath)) return;

        try
        {
            if (!System.IO.Directory.Exists(DefaultRoomsPath))
            {
                System.IO.Directory.CreateDirectory(DefaultRoomsPath);
            }
            OpenFolderInExplorer(DefaultRoomsPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 打开房间文件夹失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenAssetLibraryFolder()
    {
        if (string.IsNullOrEmpty(AssetLibraryPath)) return;

        try
        {
            if (!System.IO.Directory.Exists(AssetLibraryPath))
            {
                System.IO.Directory.CreateDirectory(AssetLibraryPath);
            }
            OpenFolderInExplorer(AssetLibraryPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 打开素材库文件夹失败: {ex.Message}");
        }
    }

    #endregion

    #region Update Commands

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        if (IsCheckingUpdate) return;

        IsCheckingUpdate = true;
        System.Diagnostics.Debug.WriteLine("[StartMaster] 开始检查更新...");

        try
        {
            // 使用模拟更新检查（开发阶段）
            // 正式发布时替换为: var updateInfo = await _updateService.CheckForUpdateAsync();
            var updateInfo = await _updateService.CheckForUpdateMockAsync(simulateHasUpdate: false);

            if (updateInfo != null)
            {
                HasUpdate = _updateService.HasUpdate;
                LatestVersion = _updateService.LatestVersion;
                UpdateNotes = _updateService.UpdateNotes;

                if (HasUpdate)
                {
                    // 显示更新对话框
                    IsUpdateDialogOpen = true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[StartMaster] 当前已是最新版本");
                }
            }
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private void CloseUpdateDialog()
    {
        IsUpdateDialogOpen = false;
    }

    [RelayCommand]
    private void OpenDownloadPage()
    {
        _updateService.OpenDownloadPage();
        IsUpdateDialogOpen = false;
    }

    [RelayCommand]
    private void OpenFeedbackPage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/trpgmaster/feedback/issues",
                UseShellExecute = true
            });
            System.Diagnostics.Debug.WriteLine("[StartMaster] 打开反馈页面");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 打开反馈页面失败: {ex.Message}");
        }
    }

    public string VersionDisplay => _updateService.GetVersionDisplayString();

    #endregion

    #region Game Events

    private void OnGameStarted()
    {
        // 触发最小化到托盘事件
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            MinimizeToTrayRequested?.Invoke();
        });
    }

    private void OnGameExited()
    {
        // 触发恢复窗口事件
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RestoreFromTrayRequested?.Invoke();
        });
    }

    public event Action? MinimizeToTrayRequested;
    public event Action? RestoreFromTrayRequested;

    #endregion

    #region Server Management (已知的世界)

    /// <summary>
    /// 本地存储的服务器列表
    /// </summary>
    public ObservableCollection<ServerGroupViewModel> LocalServers { get; } = new();

    /// <summary>
    /// 云端同步的服务器列表
    /// </summary>
    public ObservableCollection<ServerGroupViewModel> CloudServers { get; } = new();

    /// <summary>
    /// 已知服务器分组列表（用于"已知的世界"页面，合并本地和云端）
    /// </summary>
    public ObservableCollection<ServerGroupViewModel> KnownServers { get; } = new();

    /// <summary>
    /// 是否正在检测服务器
    /// </summary>
    [ObservableProperty]
    private bool _isCheckingServers = false;

    /// <summary>
    /// 是否有本地存储的服务器
    /// </summary>
    public bool HasLocalServers => LocalServers.Count > 0;

    /// <summary>
    /// 是否有云端同步的服务器
    /// </summary>
    public bool HasCloudServers => CloudServers.Count > 0;

    /// <summary>
    /// 初始化已知服务器列表
    /// </summary>
    private void InitializeKnownServers()
    {
        LocalServers.Clear();
        CloudServers.Clear();
        KnownServers.Clear();

        // 加载本地存储的服务器
        foreach (var serverRecord in _settingsService.Settings.KnownServers.Where(s => s.Source == StorageSource.Local))
        {
            var vm = new ServerGroupViewModel(serverRecord);
            LocalServers.Add(vm);
            KnownServers.Add(vm);
        }

        // 如果已登录，加载云端同步的服务器
        if (IsLoggedIn)
        {
            // TODO: 从账号服务获取云端服务器列表
            // 目前模拟云端数据也从本地配置读取（标记为Cloud的）
            foreach (var serverRecord in _settingsService.Settings.KnownServers.Where(s => s.Source == StorageSource.Cloud))
            {
                var vm = new ServerGroupViewModel(serverRecord);
                CloudServers.Add(vm);
                KnownServers.Add(vm);
            }
        }

        OnPropertyChanged(nameof(HasLocalServers));
        OnPropertyChanged(nameof(HasCloudServers));
    }

    /// <summary>
    /// 静默扫描本地服务器（启动时自动执行）
    /// 扫到了显示快捷入口，扫不到不显示
    /// </summary>
    private async Task ScanLocalServerSilentlyAsync()
    {
        // 稍等UI加载完成
        await Task.Delay(300);
        await CheckLocalServerAsync(timeoutSeconds: 2);
    }

    /// <summary>
    /// 手动刷新本地服务器状态（快速检测）
    /// </summary>
    [RelayCommand]
    private async Task RefreshLocalServerAsync()
    {
        // 手动刷新无延迟，超时更短
        await CheckLocalServerAsync(timeoutSeconds: 1.5);
    }

    /// <summary>
    /// 检测本地服务器
    /// </summary>
    /// <param name="timeoutSeconds">超时秒数（本地服务器响应应该很快）</param>
    private async Task CheckLocalServerAsync(double timeoutSeconds)
    {
        const string localAddress = "localhost:7890";

        if (IsCheckingLocalServer) return; // 防止重复检测

        IsCheckingLocalServer = true;
        System.Diagnostics.Debug.WriteLine($"[StartMaster] 检测本地服务器 (超时: {timeoutSeconds}s)...");

        try
        {
            // 本地服务器响应应该很快，使用较短超时
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var connected = await _connectionService.ConnectAsync(localAddress, cts.Token);

            if (connected)
            {
                System.Diagnostics.Debug.WriteLine("[StartMaster] 本地服务器在线");

                // 获取房间列表
                var rooms = await _connectionService.RequestRoomListAsync();

                // 更新UI
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LocalServerRooms.Clear();
                    foreach (var room in rooms)
                    {
                        LocalServerRooms.Add(new LocalRoomInfo
                        {
                            RoomId = room.Id,
                            RoomName = room.Name,
                            GmName = room.OwnerName ?? "",
                            PlayerCount = room.CurrentPlayers,
                            MaxPlayers = room.MaxPlayers,
                            ServerAddress = localAddress
                        });
                    }
                    IsLocalServerAvailable = true;
                });

                System.Diagnostics.Debug.WriteLine($"[StartMaster] 本地服务器发现 {rooms.Count} 个房间");

                // 断开连接（只是检测，不保持连接）
                await _connectionService.DisconnectAsync();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[StartMaster] 本地服务器不可用");
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLocalServerAvailable = false;
                    LocalServerRooms.Clear();
                });
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("[StartMaster] 本地服务器检测超时");
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLocalServerAvailable = false;
                LocalServerRooms.Clear();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 本地服务器检测错误: {ex.Message}");
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLocalServerAvailable = false;
                LocalServerRooms.Clear();
            });
        }
        finally
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsCheckingLocalServer = false;
            });
        }
    }

    /// <summary>
    /// 加入本地服务器房间
    /// </summary>
    [RelayCommand]
    private async Task JoinLocalRoomAsync(LocalRoomInfo? localRoom)
    {
        if (localRoom == null) return;

        var roomInfo = new RoomInfo
        {
            Id = localRoom.RoomId,
            Name = localRoom.RoomName,
            GMName = localRoom.GmName,
            ServerAddress = localRoom.ServerAddress,
            PlayerCount = localRoom.PlayerCount,
            MaxPlayers = localRoom.MaxPlayers,
            Type = RoomType.LAN,
            IsOnline = true
        };

        await JoinRoomAsync(roomInfo);
    }

    /// <summary>
    /// 添加服务器到已知列表
    /// 根据登录状态决定存储到云端还是本地
    /// </summary>
    [RelayCommand]
    private void AddServerToKnown(string? address = null)
    {
        var serverAddress = address ?? ServerAddress;
        if (string.IsNullOrWhiteSpace(serverAddress)) return;

        // 检查是否已存在（任何来源）
        var existing = _settingsService.Settings.KnownServers
            .FirstOrDefault(s => s.Address.Equals(serverAddress, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 服务器已存在: {serverAddress}");
            return;
        }

        // 根据登录状态决定存储来源
        var source = IsLoggedIn ? StorageSource.Cloud : StorageSource.Local;

        // 创建新服务器记录
        var serverRecord = new ServerRecord
        {
            Id = ServerRecord.GenerateId(serverAddress),
            Address = serverAddress,
            DisplayName = serverAddress,
            Type = DetermineServerType(serverAddress),
            Source = source,
            AddedAt = DateTime.Now
        };

        // 保存到设置
        _settingsService.Settings.KnownServers.Add(serverRecord);
        _settingsService.Save();

        // 添加到 UI
        var vm = new ServerGroupViewModel(serverRecord);
        KnownServers.Add(vm);

        if (source == StorageSource.Cloud)
        {
            CloudServers.Add(vm);
            OnPropertyChanged(nameof(HasCloudServers));
            // TODO: 同步到云端
        }
        else
        {
            LocalServers.Add(vm);
            OnPropertyChanged(nameof(HasLocalServers));
        }

        System.Diagnostics.Debug.WriteLine($"[StartMaster] 已添加服务器: {serverAddress} (存储: {source})");
    }

    /// <summary>
    /// 将服务器复制到云端（同步到账号）
    /// </summary>
    [RelayCommand]
    private void CopyServerToCloud(ServerGroupViewModel? server)
    {
        if (server == null || !IsLoggedIn) return;

        // 检查云端是否已存在
        var existingCloud = CloudServers.FirstOrDefault(s => s.Address.Equals(server.Address, StringComparison.OrdinalIgnoreCase));
        if (existingCloud != null)
        {
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 服务器已在云端: {server.Address}");
            return;
        }

        // 创建云端副本
        var cloudRecord = server.Record.Clone();
        cloudRecord.Source = StorageSource.Cloud;
        cloudRecord.Id = ServerRecord.GenerateId(server.Address) + "_cloud";

        // 保存
        _settingsService.Settings.KnownServers.Add(cloudRecord);
        _settingsService.Save();

        // 添加到 UI
        var cloudVm = new ServerGroupViewModel(cloudRecord);
        CloudServers.Add(cloudVm);
        KnownServers.Add(cloudVm);
        OnPropertyChanged(nameof(HasCloudServers));

        // TODO: 同步到云端服务

        System.Diagnostics.Debug.WriteLine($"[StartMaster] 已复制服务器到云端: {server.Address}");
    }

    /// <summary>
    /// 将服务器存储到本地
    /// </summary>
    [RelayCommand]
    private void CopyServerToLocal(ServerGroupViewModel? server)
    {
        if (server == null) return;

        // 检查本地是否已存在
        var existingLocal = LocalServers.FirstOrDefault(s => s.Address.Equals(server.Address, StringComparison.OrdinalIgnoreCase));
        if (existingLocal != null)
        {
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 服务器已在本地: {server.Address}");
            return;
        }

        // 创建本地副本
        var localRecord = server.Record.Clone();
        localRecord.Source = StorageSource.Local;
        localRecord.Id = ServerRecord.GenerateId(server.Address) + "_local";

        // 保存
        _settingsService.Settings.KnownServers.Add(localRecord);
        _settingsService.Save();

        // 添加到 UI
        var localVm = new ServerGroupViewModel(localRecord);
        LocalServers.Add(localVm);
        KnownServers.Add(localVm);
        OnPropertyChanged(nameof(HasLocalServers));

        System.Diagnostics.Debug.WriteLine($"[StartMaster] 已存储服务器到本地: {server.Address}");
    }

    /// <summary>
    /// 从已知列表移除服务器
    /// </summary>
    [RelayCommand]
    private void RemoveKnownServer(ServerGroupViewModel? server)
    {
        if (server == null) return;

        // 从设置中移除
        var record = _settingsService.Settings.KnownServers
            .FirstOrDefault(s => s.Id == server.Record.Id);
        if (record != null)
        {
            _settingsService.Settings.KnownServers.Remove(record);
            _settingsService.Save();
        }

        // 从 UI 移除
        KnownServers.Remove(server);

        if (server.Source == StorageSource.Cloud)
        {
            CloudServers.Remove(server);
            OnPropertyChanged(nameof(HasCloudServers));
            // TODO: 从云端删除
        }
        else
        {
            LocalServers.Remove(server);
            OnPropertyChanged(nameof(HasLocalServers));
        }

        System.Diagnostics.Debug.WriteLine($"[StartMaster] 已移除服务器: {server.Address}");
    }

    /// <summary>
    /// 检测单个服务器状态并获取房间列表
    /// </summary>
    [RelayCommand]
    private async Task CheckServerAsync(ServerGroupViewModel server)
    {
        if (server.IsChecking) return;

        server.IsChecking = true;
        server.UpdateStatus(ServerStatus.Checking);

        try
        {
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 开始检测服务器: {server.Address}");

            // 尝试连接
            var connected = await _connectionService.ConnectAsync(server.Address);

            if (connected)
            {
                server.UpdateStatus(ServerStatus.Online);

                // 获取房间列表
                var rooms = await _connectionService.RequestRoomListAsync();

                // 更新房间状态
                server.UpdateRooms(rooms);

                // 更新检测时间
                server.Record.LastCheckedAt = DateTime.Now;
                server.UpdateLastCheckedDisplay();

                System.Diagnostics.Debug.WriteLine($"[StartMaster] 服务器在线，发现 {rooms.Count} 个房间");

                // 断开连接（只是检测，不保持连接）
                await _connectionService.DisconnectAsync();
            }
            else
            {
                server.UpdateStatus(ServerStatus.Offline);
                System.Diagnostics.Debug.WriteLine($"[StartMaster] 服务器离线: {server.Address}");
            }

            // 保存更新后的记录
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            server.UpdateStatus(ServerStatus.Error, ex.Message);
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 检测服务器失败: {ex.Message}");
        }
        finally
        {
            server.IsChecking = false;
        }
    }

    /// <summary>
    /// 检测所有已知服务器
    /// </summary>
    [RelayCommand]
    private async Task CheckAllServersAsync()
    {
        if (IsCheckingServers) return;

        IsCheckingServers = true;
        System.Diagnostics.Debug.WriteLine($"[StartMaster] 开始检测所有服务器 ({KnownServers.Count} 个)");

        foreach (var server in KnownServers)
        {
            await CheckServerAsync(server);
        }

        IsCheckingServers = false;
        System.Diagnostics.Debug.WriteLine("[StartMaster] 所有服务器检测完成");
    }

    /// <summary>
    /// 加入已知房间
    /// </summary>
    [RelayCommand]
    private async Task JoinKnownRoomAsync(RoomItemViewModel? roomVm)
    {
        if (roomVm == null || !roomVm.CanJoin)
        {
            System.Diagnostics.Debug.WriteLine("[StartMaster] 无法加入房间：房间不可用");
            return;
        }

        // 创建 RoomInfo 用于启动游戏
        var roomInfo = new RoomInfo
        {
            Id = roomVm.RoomId,
            Name = roomVm.RoomName,
            GMName = roomVm.GmName,
            ServerAddress = roomVm.ServerAddress,
            PlayerCount = roomVm.CurrentPlayers,
            MaxPlayers = roomVm.MaxPlayers,
            Type = RoomType.Remote,
            IsOnline = true
        };

        // 更新最后加入时间
        roomVm.Record.LastJoinedAt = DateTime.Now;
        roomVm.UpdateLastJoinedDisplay();
        _settingsService.Save();

        await JoinRoomAsync(roomInfo);
    }

    /// <summary>
    /// 删除已知房间记录
    /// </summary>
    [RelayCommand]
    private void RemoveKnownRoom(RoomItemViewModel? roomVm)
    {
        if (roomVm == null) return;

        // 找到所属服务器
        var server = KnownServers.FirstOrDefault(s => s.Address == roomVm.ServerAddress);
        if (server != null)
        {
            // 从服务器记录中移除
            var record = server.Record.Rooms.FirstOrDefault(r => r.RoomId == roomVm.RoomId);
            if (record != null)
            {
                server.Record.Rooms.Remove(record);
            }

            // 从 UI 移除
            server.Rooms.Remove(roomVm);
            server.TotalRoomCount = server.Rooms.Count;

            _settingsService.Save();

            System.Diagnostics.Debug.WriteLine($"[StartMaster] 已移除房间记录: {roomVm.RoomName}");
        }
    }

    /// <summary>
    /// 记录房间访问（加入房间时调用）
    /// </summary>
    public void RecordRoomVisit(string serverAddress, ServerRoomInfo room)
    {
        // 查找或创建服务器记录
        var serverRecord = _settingsService.Settings.KnownServers
            .FirstOrDefault(s => s.Address.Equals(serverAddress, StringComparison.OrdinalIgnoreCase));

        if (serverRecord == null)
        {
            serverRecord = new ServerRecord
            {
                Id = ServerRecord.GenerateId(serverAddress),
                Address = serverAddress,
                DisplayName = serverAddress,
                Type = DetermineServerType(serverAddress),
                AddedAt = DateTime.Now
            };
            _settingsService.Settings.KnownServers.Add(serverRecord);
        }

        serverRecord.LastConnectedAt = DateTime.Now;

        // 查找或创建房间记录
        var roomRecord = serverRecord.Rooms.FirstOrDefault(r => r.RoomId == room.Id);
        if (roomRecord == null)
        {
            roomRecord = new RoomRecord
            {
                RoomId = room.Id,
                RoomName = room.Name,
                GMName = room.OwnerName ?? "",
                MaxPlayers = room.MaxPlayers
            };
            serverRecord.Rooms.Add(roomRecord);
        }

        roomRecord.RoomName = room.Name;
        roomRecord.GMName = room.OwnerName ?? "";
        roomRecord.MaxPlayers = room.MaxPlayers;
        roomRecord.LastJoinedAt = DateTime.Now;

        _settingsService.Save();

        // 刷新 UI
        InitializeKnownServers();
    }

    /// <summary>
    /// 确定服务器类型
    /// </summary>
    private static ServerType DetermineServerType(string address)
    {
        if (string.IsNullOrEmpty(address)) return ServerType.Remote;

        var host = address.Split(':')[0].ToLowerInvariant();

        if (host == "localhost" || host == "127.0.0.1")
            return ServerType.Local;

        if (host.StartsWith("192.168.") || host.StartsWith("10.") || host.StartsWith("172."))
            return ServerType.LAN;

        return ServerType.Remote;
    }

    #endregion

    #region Context Menu Commands (右键菜单)

    /// <summary>
    /// 复制服务器地址到剪贴板
    /// </summary>
    [RelayCommand]
    private async Task CopyServerAddressAsync(ServerGroupViewModel? server)
    {
        if (server == null) return;

        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.Clipboard != null)
            {
                await desktop.MainWindow.Clipboard.SetTextAsync(server.Address);
                System.Diagnostics.Debug.WriteLine($"[StartMaster] 已复制服务器地址: {server.Address}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 复制地址失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 切换服务器收藏状态
    /// </summary>
    [RelayCommand]
    private void ToggleServerFavorite(ServerGroupViewModel? server)
    {
        if (server == null) return;

        server.IsFavorite = !server.IsFavorite;
        server.Record.IsFavorite = server.IsFavorite;
        _settingsService.Save();

        System.Diagnostics.Debug.WriteLine($"[StartMaster] 服务器 {server.Address} 收藏状态: {server.IsFavorite}");
    }

    /// <summary>
    /// 重命名服务器
    /// </summary>
    [RelayCommand]
    private void RenameServer(ServerGroupViewModel? server)
    {
        if (server == null) return;

        // TODO: 弹出重命名对话框
        // 暂时使用简单的方式，后续可以实现对话框
        System.Diagnostics.Debug.WriteLine($"[StartMaster] 重命名服务器: {server.DisplayName}");
    }

    /// <summary>
    /// 复制房间ID到剪贴板
    /// </summary>
    [RelayCommand]
    private async Task CopyRoomIdAsync(RoomItemViewModel? room)
    {
        if (room == null) return;

        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.Clipboard != null)
            {
                await desktop.MainWindow.Clipboard.SetTextAsync(room.RoomId.ToString());
                System.Diagnostics.Debug.WriteLine($"[StartMaster] 已复制房间ID: {room.RoomId}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 复制房间ID失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 切换房间收藏状态
    /// </summary>
    [RelayCommand]
    private void ToggleRoomFavorite(RoomItemViewModel? room)
    {
        if (room == null) return;

        room.IsFavorite = !room.IsFavorite;
        room.Record.IsFavorite = room.IsFavorite;
        _settingsService.Save();

        System.Diagnostics.Debug.WriteLine($"[StartMaster] 房间 {room.RoomName} 收藏状态: {room.IsFavorite}");
    }

    /// <summary>
    /// 复制房间连接信息（服务器地址+房间ID）
    /// </summary>
    [RelayCommand]
    private async Task CopyRoomConnectionInfoAsync(RoomItemViewModel? room)
    {
        if (room == null) return;

        try
        {
            var connectionInfo = $"服务器: {room.ServerAddress}\n房间ID: {room.RoomId}\n房间名: {room.RoomName}";

            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.Clipboard != null)
            {
                await desktop.MainWindow.Clipboard.SetTextAsync(connectionInfo);
                System.Diagnostics.Debug.WriteLine($"[StartMaster] 已复制房间连接信息");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartMaster] 复制连接信息失败: {ex.Message}");
        }
    }

    #endregion
}
