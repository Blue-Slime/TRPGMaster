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

        var view = new MapEditorView();
        // DataContext 由外部（App.axaml.cs）注入，这里传递给内容控件
        DataContextChanged += (_, _) => view.DataContext = DataContext;
        Content = view;
    }
}
