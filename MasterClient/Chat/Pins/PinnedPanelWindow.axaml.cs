using Avalonia.Controls;
using Avalonia.Input;

namespace MasterClient.Chat.Pins;

public partial class PinnedPanelWindow : Window
{
    public PinnedPanelWindow()
    {
        InitializeComponent();
    }

    // 双击置顶条目 → 请求主聊天窗跳转定位到该消息
    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not PinnedPanelViewModel vm) return;
        if (sender is Control { DataContext: PinnedItem item })
            vm.OpenCommand.Execute(item);
    }
}
