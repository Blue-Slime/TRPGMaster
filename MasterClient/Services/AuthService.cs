using CommunityToolkit.Mvvm.ComponentModel;
using MasterClient.Models;

namespace MasterClient.Services;

public partial class AuthService : ObservableObject
{
    private readonly SettingsService _settingsService;

    [ObservableProperty]
    private LoginState _loginState = LoginState.NotLoggedIn;

    [ObservableProperty]
    private UserProfile? _currentUser;

    [ObservableProperty]
    private string? _currentToken;

    public AuthService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool IsLoggedIn => LoginState == LoginState.LoggedIn;
    public bool IsGuestMode => LoginState == LoginState.GuestMode;

    public async Task<bool> LoginAsync(string account, string password, bool rememberPassword)
    {
        try
        {
            LoginState = LoginState.LoggingIn;

            // TODO: 实际调用 LoginServer API
            await Task.Delay(1000); // 模拟网络请求

            // 模拟登录成功
            CurrentUser = new UserProfile
            {
                UserId = Guid.NewGuid(),
                Username = account.Contains("@") ? account.Split('@')[0] : account,
                Email = account.Contains("@") ? account : $"{account}@example.com",
                Subscription = SubscriptionLevel.Free,
                LastLoginAt = DateTime.Now
            };

            CurrentToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            LoginState = LoginState.LoggedIn;

            // 保存设置
            if (rememberPassword)
            {
                _settingsService.Settings.SavedAccount = account;
                _settingsService.Settings.RememberPassword = true;
                _settingsService.Settings.EncryptedPassword = password; // TODO: 加密保存密码
                _settingsService.Save();
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AuthService] 登录失败: {ex.Message}");
            LoginState = LoginState.NetworkError;
            return false;
        }
    }

    public void EnterGuestMode()
    {
        CurrentUser = new UserProfile
        {
            UserId = Guid.Empty,
            Username = "游客",
            Subscription = SubscriptionLevel.Free,
            LastLoginAt = DateTime.Now
        };
        CurrentToken = null;
        LoginState = LoginState.GuestMode;
    }

    public void Logout()
    {
        CurrentUser = null;
        CurrentToken = null;
        LoginState = LoginState.NotLoggedIn;
    }

    public Task<bool> TryAutoLoginAsync()
    {
        var settings = _settingsService.Settings;
        if (!settings.AutoLogin || string.IsNullOrEmpty(settings.SavedAccount))
        {
            return Task.FromResult(false);
        }

        // TODO: 使用保存的 token 尝试自动登录
        return Task.FromResult(false);
    }
}
