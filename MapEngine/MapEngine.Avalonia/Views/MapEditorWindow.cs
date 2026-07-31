using Avalonia.Controls;

namespace MapEngine.Avalonia.Views;

/// <summary>
/// 独立运行时的 Window 壳，内容是 MapEditorView (UserControl)。
/// 嵌入主程序时直接使用 MapEditorView，不需要此类。
/// </summary>
public partial class MapEditorWindow : Window
{
    public MapEditorWindow()
    {
        Title = "MapEngine - 战棋地图";
        Width = 1400;
        Height = 860;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // !! 关键修复：直接在这里创建 ViewModel，确保绑定生效
        var vm = new ViewModels.MainWindowViewModel();
        System.Diagnostics.Debug.WriteLine($"[MapEditorWindow] ViewModel created: {vm.PackageName}");

        var view = new MapEditorView
        {
            DataContext = vm  // 直接设置，不依赖 Window.DataContext 传递
        };
        Content = view;

        // Window 的 DataContext 也设置上，保持一致性
        DataContext = vm;
    }
}
