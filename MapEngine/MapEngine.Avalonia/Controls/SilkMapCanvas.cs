using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Controls;
using MapEngine.Avalonia.Graphics;
using Silk.NET.OpenGL;
using System;
using System.IO;
using MapEngine.Render;
using Avalonia.Threading;

namespace MapEngine.Avalonia.Controls
{
    public class SilkMapCanvas : OpenGlControlBase
    {
        private static readonly string DebugLogPath = Path.Combine(AppContext.BaseDirectory, "silk-debug.log");
        private GL? _gl;
        private MapRenderPipeline? _pipeline;
        private bool _hasLoggedRenderSkip;
        private bool _renderLoopStarted;

        public Func<MapRenderScene?>? SceneProvider { get; set; }
        public GraphicsRuntimeInfo? RuntimeInfo { get; private set; }
        public event EventHandler<GraphicsRuntimeInfo>? RuntimeInfoAvailable;

        /// <summary>最近一次渲染的场景（用于命中测试）。</summary>
        public MapRenderScene? LastRenderedScene { get; private set; }

        public void RequestFrame()
            => RequestNextFrameRendering();

        protected override void OnOpenGlInit(GlInterface gl)
        {
            base.OnOpenGlInit(gl);
            
            try
            {
                WriteDebugLog($"Init start. Bounds={Bounds.Width:0.##}x{Bounds.Height:0.##}");
                // 使用Avalonia的GlInterface提供的GetProcAddress来初始化Silk.NET的GL
                _gl = GL.GetApi(name => gl.GetProcAddress(name));
                
                // 确保 OpenGL 状态正确
                _gl.Enable(EnableCap.DepthTest);
                
                _pipeline = new MapRenderPipeline();
                _pipeline.Initialize(_gl);
                RuntimeInfo = new GraphicsRuntimeInfo
                {
                    Version = _gl.GetStringS(StringName.Version),
                    Vendor = _gl.GetStringS(StringName.Vendor),
                    Renderer = _gl.GetStringS(StringName.Renderer),
                    ShadingLanguageVersion = _gl.GetStringS(StringName.ShadingLanguageVersion),
                    ShaderProfileName = _pipeline.ShaderProfileName
                };

                WriteDebugLog(
                    $"GL info: version={RuntimeInfo.Version}, " +
                    $"vendor={RuntimeInfo.Vendor}, " +
                    $"renderer={RuntimeInfo.Renderer}, " +
                    $"shading-language={RuntimeInfo.ShadingLanguageVersion}, " +
                    $"shader-profile={RuntimeInfo.ShaderProfileName}");
                RuntimeInfoAvailable?.Invoke(this, RuntimeInfo);
                
                WriteDebugLog("OpenGL initialized successfully.");

                if (!_renderLoopStarted)
                {
                    _renderLoopStarted = true;
                    // 保持持续重绘，支撑相机移动和 Silk 主渲染层刷新。
                    DispatcherTimer.Run(() =>
                    {
                        RequestNextFrameRendering();
                        return true;
                    }, TimeSpan.FromMilliseconds(16));
                }
            }
            catch (Exception ex)
            {
                WriteDebugLog($"OpenGL init error: {ex}");
            }
        }

        protected override void OnOpenGlRender(GlInterface gl, int fb)
        {
            try
            {
                var pipeline = _pipeline;
                var glApi = _gl;

                if (pipeline != null && glApi != null && VisualRoot != null)
                {
                    // 设置视口大小，处理高DPI缩放
                    var topLevel = TopLevel.GetTopLevel(this);
                    var renderScaling = topLevel?.RenderScaling ?? 1.0;
                    var width = (uint)(Bounds.Width * renderScaling);
                    var height = (uint)(Bounds.Height * renderScaling);
                    
                    // 确保我们绑定了 Avalonia 提供的 Framebuffer
                    glApi.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
                    
                    glApi.Viewport(0, 0, width, height);
                    var scene = SceneProvider?.Invoke();
                    LastRenderedScene = scene;
                    pipeline.Render(scene);
                }
                else if (!_hasLoggedRenderSkip)
                {
                    _hasLoggedRenderSkip = true;
                    WriteDebugLog($"Render skipped. Pipeline={(pipeline is not null)}, GL={(glApi is not null)}, VisualRoot={(VisualRoot is not null)}");
                }
            }
            catch (Exception ex)
            {
                WriteDebugLog($"OpenGL render error: {ex}");
            }
        }

        protected override void OnOpenGlDeinit(GlInterface gl)
        {
            _pipeline?.Dispose();
            _gl?.Dispose();
            _pipeline = null;
            _gl = null;
            RuntimeInfo = null;
            LastRenderedScene = null;
            _hasLoggedRenderSkip = false;
            WriteDebugLog("OpenGL deinit.");
            base.OnOpenGlDeinit(gl);
        }

        private static void WriteDebugLog(string message)
        {
            try
            {
                File.AppendAllText(DebugLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Ignore logging failures to avoid affecting rendering.
            }
        }
    }
}
