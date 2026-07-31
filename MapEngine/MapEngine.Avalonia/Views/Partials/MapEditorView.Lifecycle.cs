using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MapEngine.Avalonia.Controls;
using MapEngine.Avalonia.Graphics;
using MapEngine.Avalonia.Services;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Views;

/// <summary>生命周期：DataContext 绑定、设置对话框、导航、VM 属性响应。</summary>
public partial class MapEditorView
{
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null) return;

        if (e.KeyModifiers == KeyModifiers.Control)
        {
            switch (e.Key)
            {
                case Key.Z:
                    _viewModel.CommandBus.Undo();
                    e.Handled = true;
                    break;
                case Key.Y:
                    _viewModel.CommandBus.Redo();
                    e.Handled = true;
                    break;

                // 复制 / 粘贴 / 创建副本（作用于当前选中对象）
                case Key.C when _viewModel.SelectedHierarchyItem is { } copySrc:
                    _viewModel.CopyHierarchyItem(copySrc);
                    e.Handled = true;
                    break;
                case Key.V:
                    _viewModel.PasteHierarchyToRoot();
                    e.Handled = true;
                    break;
                case Key.D when _viewModel.SelectedHierarchyItem is { } dupSrc:
                    _viewModel.DuplicateHierarchyItem(dupSrc);
                    e.Handled = true;
                    break;
            }
        }
        else if (e.KeyModifiers == KeyModifiers.None)
        {
            switch (e.Key)
            {
                // 锚点多边形：Enter 闭合提交，Esc 取消
                case Key.Enter when HasPendingPolygon:
                    CommitPendingPolygon();
                    e.Handled = true;
                    break;
                case Key.Escape when HasPendingPolygon:
                    CancelToolOverlay();
                    e.Handled = true;
                    break;

                // Esc 无待定多边形时：取消当前拖拽并回到选择工具
                case Key.Escape:
                    CancelToolOverlay();
                    _viewModel.SelectToolCommand.Execute("select");
                    e.Handled = true;
                    break;

                case Key.Delete:
                case Key.Back:
                    if (_viewModel.SelectedHierarchyItem is not null)
                    {
                        _viewModel.DeleteHierarchyItem(_viewModel.SelectedHierarchyItem);
                        e.Handled = true;
                    }
                    break;

                // ── 工具快捷键（对齐 Owlbear Rodeo 2）────────────────
                // 在文本输入控件里打字时不抢键。
                case Key.V when !IsTextInputFocused(): SwitchTool("select");  e.Handled = true; break;
                case Key.H when !IsTextInputFocused(): SwitchTool("pan");     e.Handled = true; break;
                case Key.D when !IsTextInputFocused(): SwitchTool("draw");    e.Handled = true; break;
                case Key.T when !IsTextInputFocused(): SwitchTool("text");    e.Handled = true; break;
                case Key.S when !IsTextInputFocused(): SwitchTool("shape");   e.Handled = true; break;
                case Key.M when !IsTextInputFocused(): SwitchTool("measure"); e.Handled = true; break;
                case Key.L when !IsTextInputFocused(): SwitchTool("laser");   e.Handled = true; break;
                case Key.F when !IsTextInputFocused(): SwitchTool("fog");     e.Handled = true; break;
                case Key.A when !IsTextInputFocused(): SwitchTool("attach");  e.Handled = true; break;
            }
        }
    }

    /// <summary>
    /// 切换工具（快捷键入口）。切换前取消进行中的工具绘制，避免残留 overlay 预览。
    /// laser 是 toggle 工具，SelectTool 内部会处理"再按一次关闭"。
    /// </summary>
    private void SwitchTool(string key)
    {
        if (_viewModel is null) return;
        CancelToolOverlay();
        _viewModel.SelectToolCommand.Execute(key);
    }

    /// <summary>
    /// 焦点是否在文本输入控件上。字母快捷键必须让位给正常打字
    /// （Inspector 的 hex 颜色框、Token 名称框等）。
    /// </summary>
    private bool IsTextInputFocused()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused is TextBox or AutoCompleteBox or NumericUpDown;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.NavigationRequested -= OnNavigationRequested;
            _viewModel.SettingsRequested -= OnSettingsRequested;
            _viewModel.ViewResetRequested -= OnViewResetRequested;
            _viewModel.SaveSceneRequested -= OnSaveSceneRequested;
            _viewModel.LoadSceneRequested -= OnLoadSceneRequested;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _mapDragFinalizeTimer.Stop();

        if (_mapSilkCanvas is not null)
            _mapSilkCanvas.RuntimeInfoAvailable -= OnRuntimeInfoAvailable;

        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.NavigationRequested -= OnNavigationRequested;
            _viewModel.SettingsRequested -= OnSettingsRequested;
            _viewModel.ViewResetRequested -= OnViewResetRequested;
            _viewModel.SaveSceneRequested -= OnSaveSceneRequested;
            _viewModel.LoadSceneRequested -= OnLoadSceneRequested;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.NavigationRequested += OnNavigationRequested;
            _viewModel.SettingsRequested += OnSettingsRequested;
            _viewModel.ViewResetRequested += OnViewResetRequested;
            _viewModel.SaveSceneRequested += OnSaveSceneRequested;
            _viewModel.LoadSceneRequested += OnLoadSceneRequested;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _lastZoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;

            // 初始化抽屉 CSS class
            SyncDrawerClass(LeftDrawer,   _viewModel.IsLeftDrawerOpen);
            SyncDrawerClass(RightDrawer,  _viewModel.IsRightDrawerOpen);
            SyncDrawerClass(BottomDrawer, _viewModel.IsBottomDrawerOpen);

            if (_mapSilkCanvas?.RuntimeInfo is GraphicsRuntimeInfo runtimeInfo)
                _viewModel.SetGraphicsRuntimeInfo(runtimeInfo);

            if (_tokenUIOverlay is not null)
            {
                _tokenUIManager = new TokenUIManager(
                    _tokenUIOverlay,
                    _viewModel,
                    () => (_cameraContentCenter, _viewModel?.ZoomScale ?? 1.0));
                Dispatcher.UIThread.Post(() => _tokenUIManager?.Sync(), DispatcherPriority.Loaded);
            }

            if (_mapTextOverlay is not null)
            {
                _mapTextManager = new MapTextManager(
                    _mapTextOverlay,
                    _viewModel,
                    () => (_cameraContentCenter, _viewModel?.ZoomScale ?? 1.0));
                Dispatcher.UIThread.Post(() => _mapTextManager?.Sync(), DispatcherPriority.Loaded);
            }

            Dispatcher.UIThread.Post(EnsureMapViewportInitialized, DispatcherPriority.Loaded);
        }
    }

    private void OnOpened(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(EnsureMapViewportInitialized, DispatcherPriority.Loaded);

    private void OnRuntimeInfoAvailable(object? sender, GraphicsRuntimeInfo runtimeInfo)
    {
        Dispatcher.UIThread.Post(() => _viewModel?.SetGraphicsRuntimeInfo(runtimeInfo));
    }

    private async void OnSettingsRequested(object? sender, EventArgs e)
    {
        try
        {
            var dialog = new SettingsDialog();
            var result = await dialog.ShowDialog<GlobalSettings?>(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
            if (result != null && _viewModel != null)
            {
                _viewModel.ApplyGlobalSettings(result);
            }
        }
        catch { }
    }

    private void OnNavigationRequested(object? sender, SelectionNavigationRequestEventArgs e)
    {
        switch (e.Target)
        {
            case SelectionNavigationTarget.Map when e.Selection is HierarchyItemViewModel mapItem:
                NavigateToMap(mapItem);
                break;
            case SelectionNavigationTarget.AssetLibrary when e.Selection is AssetItemViewModel assetItem:
                NavigateToAsset(assetItem);
                break;
        }
    }

    private void NavigateToMap(HierarchyItemViewModel item)
    {
        if (_viewModel is null) return;

        var targetX = MapViewportConstants.WorldOriginContent + item.X;
        var targetY = MapViewportConstants.WorldOriginContent - item.Y;
        SetCameraCenter(new Point(targetX, targetY), _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale);
    }

    private void NavigateToAsset(AssetItemViewModel item)
    {
        if (_assetItemsListBox is null) return;
        Dispatcher.UIThread.Post(() => _assetItemsListBox.ScrollIntoView(item));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null) return;

        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.IsLeftDrawerOpen):
                SyncDrawerClass(LeftDrawer, _viewModel.IsLeftDrawerOpen);
                SyncPushClasses();
                break;
            case nameof(MainWindowViewModel.IsRightDrawerOpen):
                SyncDrawerClass(RightDrawer, _viewModel.IsRightDrawerOpen);
                SyncPushClasses();
                break;
            case nameof(MainWindowViewModel.IsBottomDrawerOpen):
                SyncDrawerClass(BottomDrawer, _viewModel.IsBottomDrawerOpen);
                SyncPushClasses();
                break;
            case nameof(MainWindowViewModel.ZoomScale):
                var newZoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
                if (GetViewportSize().Width <= 0 || GetViewportSize().Height <= 0)
                {
                    _lastZoomScale = newZoomScale;
                    return;
                }

                if (_mapViewportInitialized && !_suppressZoomViewportPreservation)
                    PreserveViewportCenterOnZoom(_lastZoomScale, newZoomScale);

                _lastZoomScale = newZoomScale;
                SyncMapViewport();
                _tokenUIManager?.SyncFromViewModel(_cameraContentCenter, newZoomScale);
                _mapTextManager?.SyncFromViewModel(_cameraContentCenter, newZoomScale);
                break;
        }
    }

    private void OnViewResetRequested(object? sender, EventArgs e)
    {
        // ZoomScale 已由 VM 设置好；只需把相机移回世界原点
        CenterMapOnOrigin();
    }

    /// <summary>根据布尔开关给抽屉 Border 加/移除 "open" class，触发 XAML Transition 动画。</summary>
    private static void SyncDrawerClass(Border? drawer, bool isOpen)
    {
        if (drawer is null) return;
        if (isOpen) drawer.Classes.Add("open");
        else        drawer.Classes.Remove("open");
    }

    /// <summary>
    /// 当抽屉状态变化时，同步 push/compressed class 到各浮动胶囊，避免遮挡。
    /// 规则：
    ///   LeftDrawer  打开 → ToolbarCapsule 向右 260px（pushed-right）
    ///   RightDrawer 打开 → PanelToggleCapsule/ViewportCapsule 向左 320px（pushed-right）
    ///   BottomDrawer 打开 → LeftDrawer/RightDrawer/ToolbarCapsule/PanelToggleCapsule 底部压缩（compressed-bottom）
    ///   BottomDrawer 打开 → StatusBarCapsule/ViewportCapsule 向上（pushed-up）
    ///   ActionCapsule 固定不动，所有抽屉为它让位（通过 Margin 留出空间）
    /// </summary>
    private void SyncPushClasses()
    {
        if (_viewModel is null) return;

        var leftDrawer         = this.FindControl<Border>("LeftDrawer");
        var rightDrawer        = this.FindControl<Border>("RightDrawer");
        var toolbarCapsule     = this.FindControl<Border>("ToolbarCapsule");
        var panelToggleCapsule = this.FindControl<Border>("PanelToggleCapsule");
        var statusBar          = this.FindControl<Border>("StatusBarCapsule");
        var viewportCapsule    = this.FindControl<Border>("ViewportCapsule");

        bool leftOpen   = _viewModel.IsLeftDrawerOpen;
        bool rightOpen  = _viewModel.IsRightDrawerOpen;
        bool bottomOpen = _viewModel.IsBottomDrawerOpen;

        // 左上工具胶囊：LeftDrawer 打开时向右推，BottomDrawer 打开时底部压缩
        SyncClass(toolbarCapsule, "pushed-right",       leftOpen);
        SyncClass(toolbarCapsule, "compressed-bottom",  bottomOpen);

        // 右上面板切换胶囊：RightDrawer 打开时向左推，BottomDrawer 打开时底部压缩
        SyncClass(panelToggleCapsule, "pushed-right",      rightOpen);
        SyncClass(panelToggleCapsule, "compressed-bottom", bottomOpen);

        // 左右抽屉：BottomDrawer 打开时底部压缩
        SyncClass(leftDrawer,  "compressed-bottom", bottomOpen);
        SyncClass(rightDrawer, "compressed-bottom", bottomOpen);

        // 状态栏：随 LeftDrawer 右移 + BottomDrawer 上移（保持原有行为）
        SyncClass(statusBar, "pushed-left", leftOpen);
        SyncClass(statusBar, "pushed-up",   bottomOpen);

        // 右下视口胶囊：随 RightDrawer 左移 + BottomDrawer 上移
        SyncClass(viewportCapsule, "pushed-right", rightOpen);
        SyncClass(viewportCapsule, "pushed-up",    bottomOpen);
    }

    private static void SyncClass(Control? control, string className, bool active)
    {
        if (control is null) return;
        if (active) control.Classes.Add(className);
        else        control.Classes.Remove(className);
    }

    private async void OnSaveSceneRequested(object? sender, SaveSceneRequestEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存场景",
                DefaultExtension = "scene",
                SuggestedFileName = "新场景.scene",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("场景文件") { Patterns = new[] { "*.scene" } }
                }
            });

            if (file is null) return;

            var json = JsonSerializer.Serialize(e.Document, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            await File.WriteAllTextAsync(file.Path.LocalPath, json);
            e.SavedPath = file.Path.LocalPath;
        }
        catch (Exception ex)
        {
            if (_viewModel is not null)
                _viewModel.StatusMessage = $"保存失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 加载按钮的 Click 事件处理（通过 XAML x:Name 绑定）。
    /// async void 在 UI 事件处理器中合法；文件对话框完成后直接调用 ViewModel 同步方法，
    /// 避免 async void 事件 + 同步事件参数回传的竞态问题。
    /// </summary>
    public async void LoadScene_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "加载场景",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("场景文件") { Patterns = new[] { "*.scene" } }
                }
            });

            if (files.Count == 0) return;

            var json = await File.ReadAllTextAsync(files[0].Path.LocalPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var doc = JsonSerializer.Deserialize<ScenePersistDocument>(json, options);
            if (doc is null)
            {
                _viewModel.StatusMessage = "加载失败：文件格式无法识别";
                return;
            }

            _viewModel.LoadSceneFromDocument(doc, files[0].Path.LocalPath);
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"加载失败：{ex.Message}";
        }
    }

    // 保留旧事件处理器签名以免编译报错（已不再被订阅，实际不会触发）
    private void OnLoadSceneRequested(object? sender, LoadSceneRequestEventArgs e) { }
}
