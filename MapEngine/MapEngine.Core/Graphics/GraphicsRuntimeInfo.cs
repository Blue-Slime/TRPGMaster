using System;

namespace MapEngine.Avalonia.Graphics;

public sealed class GraphicsRuntimeInfo
{
    public required string Version { get; init; }

    public required string Vendor { get; init; }

    public required string Renderer { get; init; }

    public required string ShadingLanguageVersion { get; init; }

    public required string ShaderProfileName { get; init; }

    public bool IsNativeDesktopOpenGl
        => !Version.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase)
           && string.Equals(ShaderProfileName, "GLSL330", StringComparison.OrdinalIgnoreCase);

    public string BackendSummary
        => IsNativeDesktopOpenGl
            ? $"原生 OpenGL | {Renderer}"
            : $"兼容后端 | {Renderer}";

    public string RuntimeHint
        => IsNativeDesktopOpenGl
            ? $"已命中原生桌面 OpenGL，Shader={ShaderProfileName}。"
            : $"未命中原生桌面 OpenGL，当前自动回退到 {ShaderProfileName}。";
}
