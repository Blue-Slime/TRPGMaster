namespace MasterClient.Models;

public class UserProfile
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public SubscriptionLevel Subscription { get; set; }
    public DateTime LastLoginAt { get; set; }
}

public enum SubscriptionLevel
{
    Free = 0,
    VIP = 1,
    Premium = 2
}
