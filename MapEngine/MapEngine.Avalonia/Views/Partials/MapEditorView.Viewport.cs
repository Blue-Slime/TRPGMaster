using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MapEngine.Avalonia.Graphics;
using MapEngine.Avalonia.ViewModels;
using MapEngine.Render;

namespace MapEngine.Avalonia.Views;

/// <summary>地图视口：相机中心、缩放、平移、坐标同步。</summary>
public partial class MapEditorView
{
    private void EnsureMapViewportInitialized()
    {
        if (_viewModel is null) return;

        var viewportSize = GetViewportSize();
        if (viewportSize.Width <= 0 || viewportSize.Height <= 0) return;

        if (!_mapViewportInitialized)
            CenterMapOnOrigin();
        else
            SyncMapViewport();
    }

    private void CenterMapOnOrigin()
    {
        if (_viewModel is null) return;

        _cameraContentCenter = ClampCameraCenter(
            new Point(MapViewportConstants.WorldOriginContent, MapViewportConstants.WorldOriginContent),
            _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale);
        _mapViewportInitialized = true;
        _lastZoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        ApplyCameraTransform(_lastZoomScale);
        SyncMapViewport();
    }

    private void SyncMapViewport()
    {
        if (_viewModel is null) return;

        var zoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        var viewportSize = GetViewportSize();
        if (viewportSize.Width <= 0 || viewportSize.Height <= 0) return;

        var viewportWidthInContent  = viewportSize.Width  / zoomScale;
        var viewportHeightInContent = viewportSize.Height / zoomScale;
        _viewModel.UpdateMapViewport(
            _cameraContentCenter.X - (viewportWidthInContent  / 2.0),
            _cameraContentCenter.Y - (viewportHeightInContent / 2.0),
            viewportWidthInContent,
            viewportHeightInContent);
        _mapSilkCanvas?.RequestFrame();
    }

    private Size GetViewportSize()
        => _mapViewportSurface?.Bounds.Size
           ?? this.FindControl<Grid>("MapViewportHost")?.Bounds.Size
           ?? default;

    private Point ClampCameraCenter(Point proposedCenter, double zoomScale)
    {
        var viewportSize = GetViewportSize();
        var scale = Math.Max(zoomScale, 0.0001);
        var halfW = viewportSize.Width  / scale / 2.0;
        var halfH = viewportSize.Height / scale / 2.0;
        var minCX = halfW;
        var maxCX = MapViewportConstants.ContentSize - halfW;
        var minCY = halfH;
        var maxCY = MapViewportConstants.ContentSize - halfH;

        if (minCX > maxCX) minCX = maxCX = MapViewportConstants.WorldOriginContent;
        if (minCY > maxCY) minCY = maxCY = MapViewportConstants.WorldOriginContent;

        return new Point(
            Math.Clamp(proposedCenter.X, minCX, maxCX),
            Math.Clamp(proposedCenter.Y, minCY, maxCY));
    }

    private void SetCameraCenter(Point targetCenter, double zoomScale)
    {
        _cameraContentCenter = ClampCameraCenter(targetCenter, zoomScale);
        ApplyCameraTransform(zoomScale);
        SyncMapViewport();
    }

    private void ApplyCameraTransform(double zoomScale)
    {
        _ = zoomScale;
        _mapSilkCanvas?.RequestFrame();
        _mapTextManager?.SyncFromViewModel(_cameraContentCenter, zoomScale);
        UpdateOverlayTransform(); // 同步更新 overlay Canvas 的变换矩阵
    }

    private void PreserveViewportCenterOnZoom(double oldZoomScale, double newZoomScale)
    {
        if (oldZoomScale <= 0 || newZoomScale <= 0) return;
        _cameraContentCenter = ClampCameraCenter(_cameraContentCenter, newZoomScale);
        ApplyCameraTransform(newZoomScale);
    }

    private MapRenderScene? BuildRenderScene()
    {
        if (_viewModel is null) return null;

        // 获取当前扮演角色的楼层信息
        var activeChar = _viewModel.ActiveCharacter;
        int playerFloor = activeChar?.BackingObject.Floor ?? 0;
        string? playerBuildingId = activeChar?.BackingObject.BuildingId;
        int focusFloor = _viewModel.FocusFloor;

        return MapSceneBuilder.Build(
            _viewModel,
            GetViewportSize(),
            _cameraContentCenter,
            focusFloor,
            playerFloor,
            playerBuildingId);
    }

    private void MapViewportHost_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            var zs = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
            _cameraContentCenter = ClampCameraCenter(_cameraContentCenter, zs);
            ApplyCameraTransform(zs); // 已包含 UpdateOverlayTransform() 调用
        }
        Dispatcher.UIThread.Post(EnsureMapViewportInitialized, DispatcherPriority.Background);

        // 强制刷新工具预览层，确保使用最新视口尺寸
        Dispatcher.UIThread.Post(() =>
        {
            UpdateOverlayTransform();
            _toolOverlayCanvas?.InvalidateVisual();
        }, DispatcherPriority.Render);
    }

    private static double ContentToWorldX(double contentX)
        => contentX - MapViewportConstants.WorldOriginContent;

    private static double ContentToWorldY(double contentY)
        => MapViewportConstants.WorldOriginContent - contentY;

    // ─────────────────────────────────────────────────────────────────────
    // 坐标系统重构：统一使用世界坐标（Content单位）+ 视口变换矩阵
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 获取世界坐标 → 屏幕像素的变换矩阵。
    /// 用于 _toolOverlayCanvas.RenderTransform，让预览层可以直接用世界坐标绘制。
    /// </summary>
    private Matrix GetWorldToScreenMatrix()
    {
        var viewportSize = GetViewportSize();
        var zoom = _viewModel?.ZoomScale ?? 1.0;
        if (zoom <= 0) zoom = 1.0;

        // 1. 缩放：世界单位（Content） → 屏幕像素
        // 2. 平移：相机中心对齐到视口中心
        var translateX = viewportSize.Width / 2.0 - _cameraContentCenter.X * zoom;
        var translateY = viewportSize.Height / 2.0 - _cameraContentCenter.Y * zoom;

        return Matrix.CreateScale(zoom, zoom) * Matrix.CreateTranslation(translateX, translateY);
    }

    /// <summary>
    /// 屏幕像素坐标 → 世界坐标（Content单位）。
    /// 用于输入层，将鼠标位置转换为世界坐标后再传递给工具方法。
    /// </summary>
    private Point ScreenToWorld(Point screenPos)
    {
        var viewportSize = GetViewportSize();
        var zoom = _viewModel?.ZoomScale ?? 1.0;
        if (zoom <= 0) zoom = 1.0;

        var contentX = _cameraContentCenter.X + (screenPos.X - viewportSize.Width / 2.0) / zoom;
        var contentY = _cameraContentCenter.Y + (screenPos.Y - viewportSize.Height / 2.0) / zoom;

        return new Point(contentX, contentY);
    }

    /// <summary>
    /// 更新 overlay Canvas 的 RenderTransform，使其跟随相机移动/缩放。
    /// 调用时机：相机移动、缩放、视口尺寸改变。
    /// </summary>
    private void UpdateOverlayTransform()
    {
        if (_toolOverlayCanvas is null) return;
        _toolOverlayCanvas.RenderTransform = new MatrixTransform(GetWorldToScreenMatrix());
    }
}
