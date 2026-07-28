using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MapEngine.Avalonia.Services;

namespace MapEngine.Avalonia.Views;

public partial class SettingsDialog : Window
{
    private GlobalSettings _settings;

    public SettingsDialog()
    {
        InitializeComponent();
        _settings = GlobalSettingsStore.Load();
        LoadIntoControls(_settings);
    }

    private void LoadIntoControls(GlobalSettings s)
    {
        if (this.FindControl<TextBox>("PackageNameBox") is { } pkg) pkg.Text = s.PackageName;
        if (this.FindControl<NumericUpDown>("FeetPerCellBox") is { } feet) feet.Value = s.FeetPerCell;
        if (this.FindControl<NumericUpDown>("DefaultZoomBox") is { } zoom) zoom.Value = s.DefaultZoomPercent;
        if (this.FindControl<CheckBox>("ShowGridBox") is { } grid) grid.IsChecked = s.ShowGrid;
        if (this.FindControl<NumericUpDown>("AgentPortBox") is { } port) port.Value = s.AgentTcpPort;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var updated = new GlobalSettings
            {
                PackageName = this.FindControl<TextBox>("PackageNameBox")?.Text ?? _settings.PackageName,
                FeetPerCell = (int)(this.FindControl<NumericUpDown>("FeetPerCellBox")?.Value ?? _settings.FeetPerCell),
                DefaultZoomPercent = (int)(this.FindControl<NumericUpDown>("DefaultZoomBox")?.Value ?? _settings.DefaultZoomPercent),
                ShowGrid = this.FindControl<CheckBox>("ShowGridBox")?.IsChecked ?? _settings.ShowGrid,
                AgentTcpPort = (int)(this.FindControl<NumericUpDown>("AgentPortBox")?.Value ?? _settings.AgentTcpPort),
                AssetImportMode = "copy"
            };
            GlobalSettingsStore.Save(updated);
            Close(updated);
        }
        catch
        {
            Close(null);
        }
    }
}
