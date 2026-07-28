using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MasterClient.Chat.Messages;

public partial class MessageItemView : UserControl
{
    public MessageItemView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
