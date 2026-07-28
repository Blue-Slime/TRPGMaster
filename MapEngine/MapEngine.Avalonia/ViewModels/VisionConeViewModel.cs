using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MapEngine.Avalonia.Services;

namespace MapEngine.Avalonia.ViewModels;

public sealed class VisionConeViewModel : ObservableObject
{
    private string _id;
    private string _name;
    private double _centerOffset;
    private double _range;
    private double _fieldOfView;
    private bool _isEnabled;

    public VisionConeViewModel(VisionConeDto dto)
    {
        _id = dto.Id;
        _name = dto.Name;
        _centerOffset = dto.CenterOffset;
        _range = dto.Range;
        _fieldOfView = dto.FieldOfView;
        _isEnabled = dto.IsEnabled;
    }

    public VisionConeViewModel()
    {
        _id = $"cone-{Guid.NewGuid():N}";
        _name = "视野锥";
        _centerOffset = 0;
        _range = 12;
        _fieldOfView = 90;
        _isEnabled = true;
    }

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public double CenterOffset
    {
        get => _centerOffset;
        set => SetProperty(ref _centerOffset, value);
    }

    public double Range
    {
        get => _range;
        set => SetProperty(ref _range, value);
    }

    public double FieldOfView
    {
        get => _fieldOfView;
        set => SetProperty(ref _fieldOfView, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public string DisplayText => $"{Name} (偏移:{CenterOffset:F0}° 视距:{Range:F1} FOV:{FieldOfView:F0}°)";

    public VisionConeDto ToDto() => new()
    {
        Id = Id,
        Name = Name,
        CenterOffset = CenterOffset,
        Range = Range,
        FieldOfView = FieldOfView,
        IsEnabled = IsEnabled
    };
}
