using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using MapEngine.Avalonia.ViewModels;
using MapEngine.Avalonia.Views;

namespace MapEngine.Shell;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // MapEditorWindow 内部会创建 ViewModel，不再在这里创建
            desktop.MainWindow = new MapEditorWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}