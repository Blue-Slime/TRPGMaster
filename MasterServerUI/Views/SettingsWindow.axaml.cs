using Avalonia.Controls;
using Avalonia.Interactivity;
using MasterServerUI.ViewModels;

namespace MasterServerUI.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.SaveCommand.Execute(null);
        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);
}
