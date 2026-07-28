using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MasterServerUI.ViewModels;

public partial class CreateRoomDialogViewModel : ViewModelBase
{
    /// <summary>编辑模式时非空，创建模式时空</summary>
    public bool IsEditMode { get; }
    public string DialogTitle => IsEditMode ? "编辑房间" : "创建新房间";
    public string ConfirmText => IsEditMode ? "保存修改" : "创建房间";

    [ObservableProperty] private string _roomId = string.Empty;
    [ObservableProperty] private bool _isRoomIdReadOnly;   // 编辑时只读
    [ObservableProperty] private string _roomName = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _gameSystem = "COC 7版";
    [ObservableProperty] private int _maxPlayers = 6;
    [ObservableProperty] private bool _isPublic = true;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public string[] GameSystems { get; } =
    {
        "COC 7版", "COC 6版", "DND 5E", "PF 2E",
        "Fate Core", "Shadowrun", "自定义"
    };

    // 参数：name, maxPlayers, description, gameSystem, isPublic, password, roomId(编辑时非空)
    public event Action<string, int, string?, string?, bool, string?, string?>? Confirmed;
    public event Action? Cancelled;

    /// <summary>创建模式</summary>
    public CreateRoomDialogViewModel()
    {
        IsEditMode = false;
        IsRoomIdReadOnly = false;
        RoomId = $"room_{DateTime.Now:yyyyMMddHHmm}";
    }

    /// <summary>编辑模式：传入现有房间信息预填</summary>
    public CreateRoomDialogViewModel(MasterIM.Models.Room existing)
    {
        IsEditMode = true;
        IsRoomIdReadOnly = true;    // 编辑时 RoomId 不可改
        RoomId = existing.RoomId;
        RoomName = existing.RoomName;
        Description = existing.Description ?? "";
        IsPublic = existing.IsPublic;
        Password = existing.Password ?? "";
    }

    [RelayCommand]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(RoomName))
        {
            ErrorMessage = "房间名称不能为空";
            return;
        }
        if (!IsEditMode && string.IsNullOrWhiteSpace(RoomId))
        {
            ErrorMessage = "房间ID不能为空";
            return;
        }
        ErrorMessage = string.Empty;
        Confirmed?.Invoke(
            RoomName.Trim(),
            MaxPlayers,
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            GameSystem,
            IsPublic,
            string.IsNullOrWhiteSpace(Password) ? null : Password,
            RoomId.Trim()
        );
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

    public void SetError(string msg) => ErrorMessage = msg;
}
