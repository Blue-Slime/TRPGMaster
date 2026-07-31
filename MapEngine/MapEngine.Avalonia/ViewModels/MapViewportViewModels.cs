using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Data;
using MapEngine.Core.Components;
using MapEngine.Avalonia.Commands;
using MapEngine.Avalonia.Graphics;
using MapEngine.Avalonia.Services;
using MapEngine.Core.Hosting;

namespace MapEngine.Avalonia.ViewModels;

public sealed class MapTileViewModel
{
    public MapTileViewModel(double canvasLeft, double canvasTop, double size, string background, string borderBrush, string label)
    {
        CanvasLeft = canvasLeft;
        CanvasTop = canvasTop;
        Size = size;
        Background = background;
        BorderBrush = borderBrush;
        Label = label;
    }

    public double CanvasLeft { get; }

    public double CanvasTop { get; }

    public double Size { get; }

    public string Background { get; }

    public string BorderBrush { get; }

    public string Label { get; }
}

public sealed class MapGuideLineViewModel
{
    public MapGuideLineViewModel(double canvasLeft, double canvasTop, double width, double height, string stroke)
    {
        CanvasLeft = canvasLeft;
        CanvasTop = canvasTop;
        Width = width;
        Height = height;
        Stroke = stroke;
    }

    public double CanvasLeft { get; }

    public double CanvasTop { get; }

    public double Width { get; }

    public double Height { get; }

    public string Stroke { get; }
}
