using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Views;

public partial class InitiativeTrackerPanel : UserControl
{
    public InitiativeTrackerPanel()
    {
        InitializeComponent();
    }

    private void Entry_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is InitiativeEntryViewModel entry)
        {
            if (DataContext is InitiativeTrackerViewModel tracker)
            {
                tracker.SelectEntryCommand.Execute(entry);
            }
        }
    }

    private void RemoveEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is InitiativeEntryViewModel entry)
        {
            if (DataContext is InitiativeTrackerViewModel tracker)
            {
                tracker.RemoveEntry(entry);
            }
        }
    }
}
