using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Views;

public partial class InitiativeTopDrawer : UserControl
{
    // 卡片宽度 88 + Spacing 12 = 每格步长 100（与 XAML 里的 Width/Spacing 保持一致）
    private const double CardStep = 100.0;

    private InitiativeTrackerViewModel? _subscribedVm;

    public InitiativeTopDrawer()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        // 取消旧订阅
        if (_subscribedVm is not null)
            _subscribedVm.ScrollToCurrentRequest -= OnScrollToCurrentRequest;

        _subscribedVm = DataContext as InitiativeTrackerViewModel;

        if (_subscribedVm is not null)
            _subscribedVm.ScrollToCurrentRequest += OnScrollToCurrentRequest;
    }

    /// <summary>
    /// 收到 ViewModel 的滚动请求：将第 <paramref name="index"/> 张卡片的中心移到 ScrollViewer 可视区域中心。
    /// </summary>
    private void OnScrollToCurrentRequest(int index)
    {
        var scroll = this.FindControl<ScrollViewer>("CardScrollViewer");
        if (scroll is null) return;

        // 在 Avalonia 里 Offset 操作需要在布局完成后执行
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // 当前卡片左边缘 x = index * CardStep
            // 希望卡片中心 = 可视区中心
            double cardCenter = index * CardStep + CardStep / 2.0;
            double viewportHalf = scroll.Viewport.Width / 2.0;
            double targetOffset = cardCenter - viewportHalf;

            // 夹在合法范围内
            double maxOffset = scroll.Extent.Width - scroll.Viewport.Width;
            targetOffset = Math.Max(0, Math.Min(targetOffset, Math.Max(0, maxOffset)));

            scroll.Offset = new Vector(targetOffset, 0);
        }, global::Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void Entry_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: InitiativeEntryViewModel entry }
            && DataContext is InitiativeTrackerViewModel viewModel)
        {
            viewModel.SelectEntryCommand.Execute(entry);
            e.Handled = true;
        }
    }
}
