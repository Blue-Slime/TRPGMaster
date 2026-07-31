using Avalonia;
using Avalonia.Win32;

namespace MasterClient;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                // 原生 WGL 优先，ANGLE EGL 备用，最后 Software
                RenderingMode =
                [
                    Win32RenderingMode.Wgl,
                    Win32RenderingMode.AngleEgl,
                    Win32RenderingMode.Software,
                ]
            })
            .WithInterFont()
            .LogToTrace();
}
