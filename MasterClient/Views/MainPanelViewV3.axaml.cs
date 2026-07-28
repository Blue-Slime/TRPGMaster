using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using MasterClient.ViewModels;

namespace MasterClient.Views;

public partial class MainPanelViewV3 : UserControl
{
    private Border? _indicator0;
    private Border? _indicator1;
    private Border? _indicator2;
    private Border? _indicator3;
    private Border? _bannerBackground;

    public MainPanelViewV3()
    {
        InitializeComponent();

        // 获取指示器控件引用
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _indicator0 = this.FindControl<Border>("Indicator0");
        _indicator1 = this.FindControl<Border>("Indicator1");
        _indicator2 = this.FindControl<Border>("Indicator2");
        _indicator3 = this.FindControl<Border>("Indicator3");
        _bannerBackground = this.FindControl<Border>("BannerBackground");

        // 订阅 ViewModel 的属性变化
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            // 初始化指示器状态
            UpdateIndicators(vm.CurrentBannerIndex);
            UpdateBannerBackground(vm.CurrentBanner);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is MainWindowViewModel vm)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentBannerIndex))
            {
                UpdateIndicators(vm.CurrentBannerIndex);
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.CurrentBanner))
            {
                UpdateBannerBackground(vm.CurrentBanner);
            }
        }
    }

    private void UpdateIndicators(int currentIndex)
    {
        var indicators = new[] { _indicator0, _indicator1, _indicator2, _indicator3 };

        for (int i = 0; i < indicators.Length; i++)
        {
            if (indicators[i] != null)
            {
                indicators[i]!.Opacity = (i == currentIndex) ? 1.0 : 0.4;
            }
        }
    }

    private void UpdateBannerBackground(Models.BannerInfo? banner)
    {
        if (_bannerBackground == null || banner == null) return;

        try
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Avalonia.RelativePoint(0, 0.5, Avalonia.RelativeUnit.Relative),
                EndPoint = new Avalonia.RelativePoint(1, 0.5, Avalonia.RelativeUnit.Relative)
            };

            gradient.GradientStops.Add(new GradientStop(Color.Parse(banner.Gradient.StartColor), 0));
            gradient.GradientStops.Add(new GradientStop(Color.Parse(banner.Gradient.MiddleColor), 0.5));
            gradient.GradientStops.Add(new GradientStop(Color.Parse(banner.Gradient.EndColor), 1));

            _bannerBackground.Background = gradient;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPanelViewV3] 更新Banner背景失败: {ex.Message}");
        }
    }

    private void ShowDirectConnect_Click(object? sender, RoutedEventArgs e)
    {
        var panel = this.FindControl<Border>("DirectConnectPanel");
        if (panel != null)
        {
            panel.IsVisible = !panel.IsVisible;
        }
    }

    // 指示器点击处理
    private void Indicator0_Click(object? sender, PointerPressedEventArgs e)
    {
        GoToBannerIndex(0);
    }

    private void Indicator1_Click(object? sender, PointerPressedEventArgs e)
    {
        GoToBannerIndex(1);
    }

    private void Indicator2_Click(object? sender, PointerPressedEventArgs e)
    {
        GoToBannerIndex(2);
    }

    private void Indicator3_Click(object? sender, PointerPressedEventArgs e)
    {
        GoToBannerIndex(3);
    }

    private void GoToBannerIndex(int index)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.GoToBannerCommand.Execute(index);
        }
    }
}
