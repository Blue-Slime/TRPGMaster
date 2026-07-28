using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MasterClient.Chat.Search;

public partial class MessageSearchWindow : Window
{
    public MessageSearchWindow()
    {
        InitializeComponent();
    }

    // 双击结果行 → 走 VM 的 OpenCommand（发 NavigateRequested），随后关窗回到聊天定位。
    private void OnResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: SearchResultItem item }
            && DataContext is MessageSearchViewModel vm)
        {
            vm.OpenCommand.Execute(item);
            Close();
        }
    }
}
