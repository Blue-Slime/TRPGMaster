using Avalonia.Controls;

namespace MasterClient.Chat.Panels;

public partial class ChannelDialog : Window
{
    private bool _closed;

    public ChannelDialog()
    {
        InitializeComponent();
    }

    public void CloseWith(bool result)
    {
        if (_closed) return;
        _closed = true;
        Close(result);
    }
}
