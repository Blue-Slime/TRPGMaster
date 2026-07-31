using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MasterClient.Chat.Panels;

/// <summary>
/// 频道 新建/编辑 对话框 VM（复用一个）。
/// Confirmed 事件回传 (channelName, categoryName)。
/// </summary>
public partial class ChannelDialogViewModel : ObservableObject
{
    public bool IsEditMode { get; }
    public string DialogTitle => IsEditMode ? "频道配置" : "新建频道";
    public string ConfirmText => IsEditMode ? "保存" : "创建";

    /// <summary>编辑模式时携带原频道ID</summary>
    public string? ChannelId { get; }

    [ObservableProperty] private string _channelName = string.Empty;
    [ObservableProperty] private string _categoryName = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;

    /// <summary>确认：回传 (channelName, categoryName)</summary>
    public event Action<string, string?>? Confirmed;
    public event Action? Cancelled;

    /// <summary>新建模式</summary>
    public ChannelDialogViewModel(string? categoryName = null)
    {
        IsEditMode = false;
        _categoryName = categoryName ?? string.Empty;
    }

    /// <summary>编辑模式</summary>
    public ChannelDialogViewModel(string channelId, string channelName, string? categoryName = null)
    {
        IsEditMode = true;
        ChannelId = channelId;
        ChannelName = channelName;
        _categoryName = categoryName ?? string.Empty;
    }

    [RelayCommand]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(ChannelName))
        {
            ErrorMessage = "频道名不能为空";
            return;
        }
        ErrorMessage = string.Empty;
        var cat = string.IsNullOrWhiteSpace(CategoryName) ? null : CategoryName.Trim();
        Confirmed?.Invoke(ChannelName.Trim(), cat);
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}
