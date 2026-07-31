using Avalonia;
using Avalonia.Diagnostics;
using System;
using System.Linq;

namespace MapEngine.Shell;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // 命令行工具模式
        if (args.Length > 0 && args[0] == "import-assets")
        {
            var sourcePath = args.Length > 1 ? args[1] : null;
            var targetRoot = args.Length > 2 ? args[2] : null;
            ImportAssetsCommand.Execute(sourcePath, targetRoot);
            return;
        }

        // 默认 Avalonia UI 模式
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode =
                [
                    Win32RenderingMode.Wgl,
                    Win32RenderingMode.AngleEgl,
                    Win32RenderingMode.Software
                ],
                CompositionMode =
                [
                    Win32CompositionMode.RedirectionSurface
                ]
            })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
