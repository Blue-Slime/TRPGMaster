using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
        // P1: 全局配置路径（只读显示，由客户端主界面修改）
        if (this.FindControl<TextBlock>("GlobalAssetPathText") is { } globalPathText)
        {
            var globalPath = GlobalConfigStore.TryGetAssetLibraryPath();
            globalPathText.Text = string.IsNullOrWhiteSpace(globalPath)
                ? "未配置（请在客户端设置中配置）"
                : globalPath;
        }

        // P2: 模块默认路径（只读显示）
        if (this.FindControl<TextBlock>("DefaultAssetPathText") is { } defaultPathText)
        {
            var defaultPath = Path.Combine(AppContext.BaseDirectory, AssetLibraryFileSystemService.DefaultRootFolderName);
            defaultPathText.Text = defaultPath;
        }

        // 路径模式 RadioButton
        if (this.FindControl<RadioButton>("UseGlobalConfigRadio") is { } globalRadio &&
            this.FindControl<RadioButton>("UseModuleDefaultRadio") is { } defaultRadio &&
            this.FindControl<RadioButton>("UseCustomPathRadio") is { } customRadio)
        {
            globalRadio.IsChecked = s.MapModuleAssetPathMode == AssetPathMode.Global;
            defaultRadio.IsChecked = s.MapModuleAssetPathMode == AssetPathMode.ModuleDefault;
            customRadio.IsChecked = s.MapModuleAssetPathMode == AssetPathMode.Custom;
        }

        // P3: 模块自定义路径（可编辑）
        if (this.FindControl<TextBox>("CustomAssetPathBox") is { } customPathBox)
        {
            customPathBox.Text = s.MapModuleCustomAssetPath ?? "";
            customPathBox.IsEnabled = s.MapModuleAssetPathMode == AssetPathMode.Custom;
        }

        if (this.FindControl<Button>("BrowseAssetPathButton") is { } browseButton)
        {
            browseButton.IsEnabled = s.MapModuleAssetPathMode == AssetPathMode.Custom;
        }

        // 当前生效路径（显示实际使用的路径）
        if (this.FindControl<TextBlock>("CurrentEffectivePathText") is { } effectivePathText)
        {
            var effectivePath = AssetLibraryFileSystemService.ResolveEffectiveRootPath();
            effectivePathText.Text = effectivePath;
        }

        // 其他设置
        if (this.FindControl<TextBox>("PackageNameBox") is { } pkg) pkg.Text = s.PackageName;
        if (this.FindControl<NumericUpDown>("FeetPerCellBox") is { } feet) feet.Value = s.FeetPerCell;
        if (this.FindControl<NumericUpDown>("DefaultZoomBox") is { } zoom) zoom.Value = s.DefaultZoomPercent;
        if (this.FindControl<CheckBox>("ShowGridBox") is { } grid) grid.IsChecked = s.ShowGrid;
        if (this.FindControl<NumericUpDown>("AgentPortBox") is { } port) port.Value = s.AgentTcpPort;
    }

    private void OnModeRadioChanged(object? sender, RoutedEventArgs e)
    {
        // 切换路径模式时，启用/禁用自定义路径输入框
        var customRadio = this.FindControl<RadioButton>("UseCustomPathRadio");
        var isCustomMode = customRadio?.IsChecked == true;

        if (this.FindControl<TextBox>("CustomAssetPathBox") is { } customPathBox)
        {
            customPathBox.IsEnabled = isCustomMode;
        }

        if (this.FindControl<Button>("BrowseAssetPathButton") is { } browseButton)
        {
            browseButton.IsEnabled = isCustomMode;
        }
    }

    private async void OnBrowseAssetPathClick(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择素材库文件夹（模块自定义路径）",
            AllowMultiple = false
        });

        if (folder.Count > 0 && this.FindControl<TextBox>("CustomAssetPathBox") is { } pathBox)
        {
            pathBox.Text = folder[0].Path.LocalPath;
        }
    }

    private void OnOpenSettingsFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var settingsPath = GlobalSettingsStore.GetSettingsPath();
            var folderPath = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // 静默失败
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var customAssetPath = this.FindControl<TextBox>("CustomAssetPathBox")?.Text;

            // 确定路径模式
            var mode = AssetPathMode.Global;
            if (this.FindControl<RadioButton>("UseModuleDefaultRadio")?.IsChecked == true)
                mode = AssetPathMode.ModuleDefault;
            else if (this.FindControl<RadioButton>("UseCustomPathRadio")?.IsChecked == true)
                mode = AssetPathMode.Custom;

            var updated = new GlobalSettings
            {
                PackageName = this.FindControl<TextBox>("PackageNameBox")?.Text ?? _settings.PackageName,
                FeetPerCell = (int)(this.FindControl<NumericUpDown>("FeetPerCellBox")?.Value ?? _settings.FeetPerCell),
                DefaultZoomPercent = (int)(this.FindControl<NumericUpDown>("DefaultZoomBox")?.Value ?? _settings.DefaultZoomPercent),
                ShowGrid = this.FindControl<CheckBox>("ShowGridBox")?.IsChecked ?? _settings.ShowGrid,
                AgentTcpPort = (int)(this.FindControl<NumericUpDown>("AgentPortBox")?.Value ?? _settings.AgentTcpPort),
                AssetImportMode = "copy",
                MapModuleAssetPathMode = mode,
                MapModuleCustomAssetPath = string.IsNullOrWhiteSpace(customAssetPath) ? null : customAssetPath
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
