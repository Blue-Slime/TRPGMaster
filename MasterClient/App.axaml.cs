using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MasterClient.Services;
using MasterClient.ViewModels;
using MasterClient.Views;

namespace MasterClient;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 配置依赖注入
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            DisableAvaloniaDataAnnotationValidation();

            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Services
        services.AddSingleton<SettingsService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<RoomDiscoveryService>();
        services.AddSingleton<GameLauncherService>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<ModuleLauncherService>();
        services.AddSingleton<MasterServerConnectionService>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        // Avalonia 12: BindingPlugins is internal; DataAnnotations validation is disabled
        // by default when using CompiledBindings, so this is no longer needed.
    }
}
