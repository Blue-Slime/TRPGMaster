using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MasterServerUI.Models;
using MasterServerUI.ViewModels;
using MasterServerUI.Views;
using MasterIM.Server;

namespace MasterServerUI;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }
    public static AppSettings Settings { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 避免 Avalonia 与 CommunityToolkit 的重复校验
        DisableAvaloniaDataAnnotationValidation();

        // 加载配置
        Settings = AppSettings.Load();

        // 配置 DI 容器
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };

            desktop.MainWindow.Width = Settings.UI.WindowWidth;
            desktop.MainWindow.Height = Settings.UI.WindowHeight;

            desktop.MainWindow.Closing += (s, e) =>
            {
                if (desktop.MainWindow.WindowState == Avalonia.Controls.WindowState.Normal)
                {
                    Settings.UI.WindowWidth = (int)desktop.MainWindow.Width;
                    Settings.UI.WindowHeight = (int)desktop.MainWindow.Height;
                    Settings.Save();
                }
            };

            if (Settings.UI.AutoStartServer)
            {
                var viewModel = Services.GetRequiredService<MainWindowViewModel>();
                _ = viewModel.StartServerCommand.ExecuteAsync(null);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // 已加载的配置
        services.AddSingleton(Settings);

        // MasterServerInstance 单例（默认使用共享数据目录）
        services.AddSingleton(sp => new MasterServerInstance());

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        // Avalonia 12: BindingPlugins is internal; no longer needed with CompiledBindings.
    }
}
