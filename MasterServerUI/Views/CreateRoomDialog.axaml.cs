using Avalonia.Controls;
using Avalonia.Interactivity;
using MasterServerUI.ViewModels;

namespace MasterServerUI.Views;

public partial class CreateRoomDialog : Window
{
    private bool _closed;

    public CreateRoomDialog()
    {
        InitializeComponent();
    }

    public void SetResultAndClose(bool result)
    {
        if (_closed) return;
        _closed = true;
        Close(result);
    }
}
