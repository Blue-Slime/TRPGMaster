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
                var vm = new MainWindowViewModel();
                System.Console.WriteLine($"[MapEditor] Loaded: PackageName={vm.PackageName}, Roots={vm.HierarchyRoots.Count}, FirstRoot={vm.HierarchyRoots.FirstOrDefault()?.Name}");

                desktop.MainWindow = new MapEditorWindow
                {
                    DataContext = vm,
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
}