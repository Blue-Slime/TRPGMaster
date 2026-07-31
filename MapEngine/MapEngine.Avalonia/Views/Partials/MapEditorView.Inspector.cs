using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MapEngine.Avalonia.Commands;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Views;

/// <summary>属性检视器：编辑前后快照对比生成 Undo 命令、视野锥参数、组件增删。</summary>
public partial class MapEditorView
{
    private readonly Dictionary<TextBox, (string Property, object? OldValue)> _inspectorEditSnapshots = new();

    private void InspectorTextBox_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb || _viewModel?.SelectedHierarchyItem is null) return;
        if (tb.Tag is not string prop || string.IsNullOrEmpty(prop)) return;
        var item = _viewModel.SelectedHierarchyItem;
        _inspectorEditSnapshots[tb] = (prop, ReadProperty(item, prop));
    }

    private void InspectorTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb || _viewModel is null) return;
        if (!_inspectorEditSnapshots.TryGetValue(tb, out var snap)) return;
        _inspectorEditSnapshots.Remove(tb);

        var item = _viewModel.SelectedHierarchyItem;
        if (item is null) return;

        var newValue = ReadProperty(item, snap.Property);
        if (Equals(newValue, snap.OldValue)) return;

        WriteProperty(item, snap.Property, snap.OldValue);
        _viewModel.CommandBus.Execute(new VmSetPropertyCommand(_viewModel, item.Id, snap.Property, newValue));
    }

    private static object? ReadProperty(HierarchyItemViewModel item, string prop) => prop switch
    {
        "X" => item.X, "Y" => item.Y, "Z" => item.Z,
        "Rotation" => item.Rotation,
        "ScaleX" => item.ScaleX, "ScaleY" => item.ScaleY,
        "Opacity" => item.Opacity,
        "VisionRadius" => item.VisionRadius,
        "Orientation" => item.Orientation,
        _ => null
    };

    private static void WriteProperty(HierarchyItemViewModel item, string prop, object? value)
    {
        if (value is null) return;
        var d = Convert.ToDouble(value);
        switch (prop)
        {
            case "X": item.X = d; break;
            case "Y": item.Y = d; break;
            case "Z": item.Z = d; break;
            case "Rotation": item.Rotation = d; break;
            case "ScaleX": item.ScaleX = d; break;
            case "ScaleY": item.ScaleY = d; break;
            case "Opacity": item.Opacity = d; break;
            case "VisionRadius": item.VisionRadius = d; break;
            case "Orientation": item.Orientation = d; break;
        }
    }

    private void AddVisionCone_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null) return;
        _viewModel.AddVisionCone(_viewModel.SelectedHierarchyItem);
    }

    private void RemoveVisionCone_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null) return;
        if (sender is Button { CommandParameter: VisionConeViewModel cone })
        {
            _viewModel.RemoveVisionCone(_viewModel.SelectedHierarchyItem, cone);
        }
    }

    private async void VisionConeItem_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null) return;
        if ((sender as Control)?.DataContext is not VisionConeViewModel cone) return;

        var dialog = new Window
        {
            Title = "编辑视野锥",
            Width = 400,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
        };

        var nameBox = new TextBox { Text = cone.Name };
        var offsetBox = new TextBox { Text = cone.CenterOffset.ToString() };
        var rangeBox = new TextBox { Text = cone.Range.ToString() };
        var fovBox = new TextBox { Text = cone.FieldOfView.ToString() };
        var enabledCheck = new CheckBox { Content = "启用", IsChecked = cone.IsEnabled, Foreground = Brushes.White };

        var okButton = new Button { Content = "确定", Width = 80 };
        var cancelButton = new Button { Content = "取消", Width = 80, Margin = new Thickness(8, 0, 0, 0) };

        okButton.Click += (_, _) =>
        {
            if (double.TryParse(offsetBox.Text, out var offset) &&
                double.TryParse(rangeBox.Text, out var range) &&
                double.TryParse(fovBox.Text, out var fov))
            {
                cone.Name = nameBox.Text ?? "视野锥";
                cone.CenterOffset = Math.Clamp(offset, -180, 180);
                cone.Range = Math.Max(0, range);
                cone.FieldOfView = Math.Clamp(fov, 0, 360);
                cone.IsEnabled = enabledCheck.IsChecked ?? true;
                dialog.Close(true);
            }
        };
        cancelButton.Click += (_, _) => dialog.Close(false);

        var labelStyle = new Action<TextBlock>(t => { t.Foreground = Brushes.LightGray; t.FontSize = 11; });
        TextBlock Label(string text) { var t = new TextBlock { Text = text }; labelStyle(t); return t; }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 8,
            Children =
            {
                Label("名称"), nameBox,
                Label("中心偏转（-180 到 +180°）"), offsetBox,
                Label("视距（格子数）"), rangeBox,
                Label("视场角度（0-360°）"), fovBox,
                enabledCheck,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 12, 0, 0),
                    Children = { okButton, cancelButton }
                }
            }
        };

        await dialog.ShowDialog(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
    }

    private void AddComponent_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null) return;

        var item = _viewModel.SelectedHierarchyItem;

        // 显示组件选择菜单
        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem
                {
                    Header = "视野组件",
                    Icon = MenuIcon(IconPaths.Eye),
                    IsEnabled = !item.HasVisionComponent,
                    Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
                    {
                        _viewModel.AddVisionComponent(item);
                    })
                },
                new MenuItem
                {
                    Header = "墙壁组件",
                    Icon = MenuIcon(IconPaths.Wall),
                    IsEnabled = !item.HasWallComponent,
                    Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
                    {
                        _viewModel.AddWallComponent(item);
                    })
                },
                new MenuItem
                {
                    Header = "Token 组件",
                    Icon = MenuIcon(IconPaths.Token),
                    IsEnabled = !item.HasComponent<MapEngine.Core.Components.TokenComponent>(),
                    Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
                    {
                        _viewModel.AddTokenComponent(item);
                    })
                },
                new MenuItem
                {
                    Header = "光源组件（未实现）",
                    Icon = MenuIcon("M12,2A7,7 0 0,0 5,9C5,11.38 6.19,13.47 8,14.74V17A1,1 0 0,0 9,18H15A1,1 0 0,0 16,17V14.74C17.81,13.47 19,11.38 19,9A7,7 0 0,0 12,2M9,21A1,1 0 0,0 10,22H14A1,1 0 0,0 15,21V20H9V21Z"),
                    IsEnabled = false
                },
                new MenuItem
                {
                    Header = "音频组件（未实现）",
                    Icon = MenuIcon("M14,3.23V5.29C16.89,6.15 19,8.83 19,12C19,15.17 16.89,17.84 14,18.7V20.77C18,19.86 21,16.28 21,12C21,7.72 18,4.14 14,3.23M16.5,12C16.5,10.23 15.5,8.71 14,7.97V16C15.5,15.29 16.5,13.76 16.5,12M3,9V15H7L12,20V4L7,9H3Z"),
                    IsEnabled = false
                }
            }
        };

        menu.Open(sender as Button);
    }

    private async void SelectWallTexture_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择墙壁纹理",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("图片文件")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" }
                }
            }
        });

        if (files.Count > 0)
        {
            var path = files[0].Path.LocalPath;
            _viewModel.SelectedHierarchyItem.WallTexturePath = path;
        }
    }

    /// <summary>Tag 输入框 KeyDown：按 Enter 或 Tab 触发 AddTag。</summary>
    private void TagTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Tab)) return;
        if (_viewModel is null) return;
        _viewModel.AddTagCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>Token 肖像裁剪：打开文件选择器，将路径写入 PortraitPath。</summary>
    private async void PickTokenPortrait_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Token 肖像",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("图片文件")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" }
                }
            }
        });

        if (files.Count > 0)
        {
            _viewModel.SelectedHierarchyItem.PortraitPath = files[0].Path.LocalPath;
        }
    }
}
