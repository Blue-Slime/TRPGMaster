using System;
using Avalonia;
using Avalonia.Controls;
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
        _tokenUIManager?.SyncFromViewModel(_cameraContentCenter, zoomScale);
        _mapTextManager?.SyncFromViewModel(_cameraContentCenter, zoomScale);
    }

    private void PreserveViewportCenterOnZoom(double oldZoomScale, double newZoomScale)
    {
        if (oldZoomScale <= 0 || newZoomScale <= 0) return;
        _cameraContentCenter = ClampCameraCenter(_cameraContentCenter, newZoomScale);
        ApplyCameraTransform(newZoomScale);
    }

    private MapRenderScene? BuildRenderScene()
        => _viewModel is null
            ? null
            : MapSceneBuilder.Build(_viewModel, GetViewportSize(), _cameraContentCenter);

    private void MapViewportHost_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            var zs = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
            _cameraContentCenter = ClampCameraCenter(_cameraContentCenter, zs);
            ApplyCameraTransform(zs);
        }
        Dispatcher.UIThread.Post(EnsureMapViewportInitialized, DispatcherPriority.Background);
    }

    private static double ContentToWorldX(double contentX)
        => contentX - MapViewportConstants.WorldOriginContent;

    private static double ContentToWorldY(double contentY)
        => MapViewportConstants.WorldOriginContent - contentY;
}
