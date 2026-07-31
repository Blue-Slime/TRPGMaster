using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Views;

/// <summary>共享工具方法：菜单参数提取、拖放效果、坐标换算、命名输入框、拖放收尾计时器。</summary>
public partial class MapEditorView
{
    private static T? GetMenuParameter<T>(object? sender) where T : class
        => (sender as MenuItem)?.CommandParameter as T;

    /// <summary>
    /// 构造 Owlbear 风格的菜单图标（SVG 路径 → <see cref="PathIcon"/>）。
    /// 代码里动态构建的 ContextMenu 无法走 XAML StaticResource，故在此集中处理。
    /// </summary>
    private static PathIcon MenuIcon(string pathData) => new()
    {
        Data = Geometry.Parse(pathData),
        Width = 14,
        Height = 14,
        Foreground = new SolidColorBrush(Color.Parse("#909296")),
    };

    private void UpdateDragEffects(DragEventArgs e)
    {
        e.DragEffects = !_dragPlacementFinalized && (e.DataTransfer.Contains(AssetDragFormat) || e.DataTransfer.Contains(DataFormat.Text))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private bool TryGetMapCanvasPosition(DragEventArgs e, out double contentX, out double contentY)
    {
        contentX = 0;
        contentY = 0;

        if (_mapViewportSurface is null || _viewModel is null)
        {
            return false;
        }

        var zoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        var position = e.GetPosition(_mapViewportSurface);
        var viewportSize = GetViewportSize();

        if (position.X < 0 || position.Y < 0 || position.X > viewportSize.Width || position.Y > viewportSize.Height)
        {
            return false;
        }

        contentX = _cameraContentCenter.X + ((position.X - (viewportSize.Width / 2.0)) / zoomScale);
        contentY = _cameraContentCenter.Y + ((position.Y - (viewportSize.Height / 2.0)) / zoomScale);

        return contentX >= 0
            && contentY >= 0
            && contentX <= MapViewportConstants.ContentSize
            && contentY <= MapViewportConstants.ContentSize;
    }


    private void FinalizeMapPreviewAtLastValidPosition()
    {
        if (_dragPlacementFinalized || _viewModel is null || !_viewModel.HasMapDragPreview)
        {
            return;
        }

        CancelPendingMapFinalize();
        _viewModel.CommitMapDragPreview();
        _dragPlacementFinalized = true;
    }

    private void ScheduleMapFinalize()
    {
        if (_dragPlacementFinalized || _viewModel is null || !_viewModel.HasMapDragPreview)
        {
            return;
        }

        _mapDragFinalizeTimer.Stop();
        _mapDragFinalizeTimer.Start();
    }

    private void CancelPendingMapFinalize()
        => _mapDragFinalizeTimer.Stop();

    private void OnMapDragFinalizeTimerTick(object? sender, EventArgs e)
    {
        _mapDragFinalizeTimer.Stop();
        FinalizeMapPreviewAtLastValidPosition();
    }

    private System.Threading.Tasks.Task<AssetItemViewModel?> TryGetDraggedAssetAsync(DragEventArgs e)
    {
        if (_viewModel is null)
        {
            return System.Threading.Tasks.Task.FromResult<AssetItemViewModel?>(null);
        }

        var rawPath = e.DataTransfer.TryGetValue(AssetDragFormat)
            ?? e.DataTransfer.TryGetText();

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return System.Threading.Tasks.Task.FromResult<AssetItemViewModel?>(null);
        }

        return System.Threading.Tasks.Task.FromResult(_viewModel.GetAssetItemByPath(rawPath));
    }


    private async System.Threading.Tasks.Task<string?> PromptForNameAsync(string title, string initialText)
    {
        var inputBox = new TextBox
        {
            Text = initialText,
            Width = 280
        };

        var okButton = new Button
        {
            Content = "确定",
            MinWidth = 72
        };

        var cancelButton = new Button
        {
            Content = "取消",
            MinWidth = 72
        };

        var dialog = new Window
        {
            Title = title,
            Width = 360,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "请输入名称" },
                    inputBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, okButton }
                    }
                }
            }
        };

        okButton.Click += (_, _) => dialog.Close(inputBox.Text?.Trim());
        cancelButton.Click += (_, _) => dialog.Close(null);
        inputBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                dialog.Close(inputBox.Text?.Trim());
            }
        };
        dialog.Opened += (_, _) =>
        {
            inputBox.Focus();
            inputBox.SelectAll();
        };

        return await dialog.ShowDialog<string?>(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
    }

}
