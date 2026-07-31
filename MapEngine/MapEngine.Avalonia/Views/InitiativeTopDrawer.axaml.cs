using Avalonia.Controls;
using Avalonia.Input;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Views;

public partial class InitiativeTopDrawer : UserControl
{
    public InitiativeTopDrawer()
    {
        InitializeComponent();
    }

    private void Entry_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: InitiativeEntryViewModel entry }
            && DataContext is InitiativeTrackerViewModel viewModel)
        {
            viewModel.SelectEntryCommand.Execute(entry);
            e.Handled = true;
        }
    }
}
