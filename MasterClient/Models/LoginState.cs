namespace MasterClient.Models;

public enum LoginState
{
    NotLoggedIn,      // 未登录
    LoggingIn,        // 登录中
    LoggedIn,         // 已登录
    GuestMode,        // 游客模式
    TokenExpired,     // Token过期
    NetworkError      // 网络错误
}
