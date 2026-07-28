using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Silk.NET.OpenGL;
using MapEngine.Render;

namespace MapEngine
{
    public class MapRenderPipeline : IDisposable
    {
        private const uint SolidVertexStride = 6u * sizeof(float);
        private const uint SpriteVertexStride = 8u * sizeof(float);

        private GL? _gl;
        private uint _solidVao;
        private uint _solidVbo;
        private uint _solidShaderProgram;
        private uint _spriteVao;
        private uint _spriteVbo;
        private uint _spriteShaderProgram;
        private int _spriteSamplerLocation = -1;
        private string _shaderProfileName = "Unknown";
        private readonly Dictionary<string, TextureResource> _textureCache = new(StringComparer.OrdinalIgnoreCase);

        public string ShaderProfileName => _shaderProfileName;

        private sealed record ShaderProfile(
            string Name,
            string SolidVertexSource,
            string SolidFragmentSource,
            string SpriteVertexSource,
            string SpriteFragmentSource,
            bool BindAttributeLocations);

        private sealed record ShaderPrograms(uint SolidProgram, uint SpriteProgram);

        private sealed record TextureResource(uint Handle, int Width, int Height);

        private static readonly ShaderProfile[] ShaderProfiles =
        [
            new(
                "GLSL330",
                """
                #version 330 core
                layout (location = 0) in vec2 aPos;
                layout (location = 1) in vec4 aColor;
                out vec4 vColor;
                void main()
                {
                    gl_Position = vec4(aPos, 0.0, 1.0);
                    vColor = aColor;
                }
                """,
                """
                #version 330 core
                in vec4 vColor;
                out vec4 FragColor;
                void main()
                {
                    FragColor = vColor;
                }
                """,
                """
                #version 330 core
                layout (location = 0) in vec2 aPos;
                layout (location = 1) in vec4 aColor;
                layout (location = 2) in vec2 aUv;
                out vec4 vColor;
                out vec2 vUv;
                void main()
                {
                    gl_Position = vec4(aPos, 0.0, 1.0);
                    vColor = aColor;
                    vUv = aUv;
                }
                """,
                """
                #version 330 core
                in vec4 vColor;
                in vec2 vUv;
                uniform sampler2D uTexture;
                out vec4 FragColor;
                void main()
                {
                    FragColor = texture(uTexture, vUv) * vColor;
                }
                """,
                false),
            new(
                "GLES300",
                """
                #version 300 es
                precision mediump float;
                layout (location = 0) in vec2 aPos;
                layout (location = 1) in vec4 aColor;
                out vec4 vColor;
                void main()
                {
                    gl_Position = vec4(aPos, 0.0, 1.0);
                    vColor = aColor;
                }
                """,
                """
                #version 300 es
                precision mediump float;
                in vec4 vColor;
                out vec4 FragColor;
                void main()
                {
                    FragColor = vColor;
                }
                """,
                """
                #version 300 es
                precision mediump float;
                layout (location = 0) in vec2 aPos;
                layout (location = 1) in vec4 aColor;
                layout (location = 2) in vec2 aUv;
                out vec4 vColor;
                out vec2 vUv;
                void main()
                {
                    gl_Position = vec4(aPos, 0.0, 1.0);
                    vColor = aColor;
                    vUv = aUv;
                }
                """,
                """
                #version 300 es
                precision mediump float;
                in vec4 vColor;
                in vec2 vUv;
                uniform sampler2D uTexture;
                out vec4 FragColor;
                void main()
                {
                    FragColor = texture(uTexture, vUv) * vColor;
                }
                """,
                false),
            new(
                "GLES100",
                """
                #version 100
                attribute vec2 aPos;
                attribute vec4 aColor;
                varying vec4 vColor;
                void main()
                {
                    gl_Position = vec4(aPos, 0.0, 1.0);
                    vColor = aColor;
                }
                """,
                """
                #version 100
                precision mediump float;
                varying vec4 vColor;
                void main()
                {
                    gl_FragColor = vColor;
                }
                """,
                """
                #version 100
                attribute vec2 aPos;
                attribute vec4 aColor;
                attribute vec2 aUv;
                varying vec4 vColor;
                varying vec2 vUv;
                void main()
                {
                    gl_Position = vec4(aPos, 0.0, 1.0);
                    vColor = aColor;
                    vUv = aUv;
                }
                """,
                """
                #version 100
                precision mediump float;
                varying vec4 vColor;
                varying vec2 vUv;
                uniform sampler2D uTexture;
                void main()
                {
                    gl_FragColor = texture2D(uTexture, vUv) * vColor;
                }
                """,
                true)
        ];

        public unsafe void Initialize(GL gl)
        {
            _gl = gl;
            var programs = CreateShaderPrograms();
            _solidShaderProgram = programs.SolidProgram;
            _spriteShaderProgram = programs.SpriteProgram;
            _spriteSamplerLocation = _gl.GetUniformLocation(_spriteShaderProgram, "uTexture");

            _solidVao = _gl.GenVertexArray();
            _solidVbo = _gl.GenBuffer();
            _gl.BindVertexArray(_solidVao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _solidVbo);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)0, (void*)0, BufferUsageARB.DynamicDraw);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, SolidVertexStride, (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, SolidVertexStride, (void*)(2 * sizeof(float)));
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            _gl.BindVertexArray(0);

            _spriteVao = _gl.GenVertexArray();
            _spriteVbo = _gl.GenBuffer();
            _gl.BindVertexArray(_spriteVao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _spriteVbo);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)0, (void*)0, BufferUsageARB.DynamicDraw);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, SpriteVertexStride, (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, SpriteVertexStride, (void*)(2 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, SpriteVertexStride, (void*)(6 * sizeof(float)));
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            _gl.BindVertexArray(0);
        }

        private ShaderPrograms CreateShaderPrograms()
        {
            if (_gl is null)
            {
                throw new InvalidOperationException("OpenGL pipeline is not initialized.");
            }

            Exception? lastException = null;
            foreach (var profile in ShaderProfiles)
            {
                uint solidProgram = 0;
                uint spriteProgram = 0;

                try
                {
                    solidProgram = CreateProgram(profile.SolidVertexSource, profile.SolidFragmentSource, profile.BindAttributeLocations);
                    spriteProgram = CreateProgram(profile.SpriteVertexSource, profile.SpriteFragmentSource, profile.BindAttributeLocations);
                    _shaderProfileName = profile.Name;
                    return new ShaderPrograms(solidProgram, spriteProgram);
                }
                catch (Exception ex)
                {
                    lastException = new Exception($"Shader profile {profile.Name} failed: {ex.Message}", ex);
                    if (solidProgram != 0)
                    {
                        _gl.DeleteProgram(solidProgram);
                    }

                    if (spriteProgram != 0)
                    {
                        _gl.DeleteProgram(spriteProgram);
                    }
                }
            }

            throw new Exception("No supported shader profile compiled successfully.", lastException);
        }

        private uint CreateProgram(string vertexSource, string fragmentSource, bool bindAttributeLocations)
        {
            if (_gl is null)
            {
                throw new InvalidOperationException("OpenGL pipeline is not initialized.");
            }

            uint vertexShader = 0;
            uint fragmentShader = 0;
            uint program = 0;

            try
            {
                vertexShader = _gl.CreateShader(ShaderType.VertexShader);
                _gl.ShaderSource(vertexShader, vertexSource);
                _gl.CompileShader(vertexShader);
                CheckShaderCompileStatus(vertexShader);

                fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
                _gl.ShaderSource(fragmentShader, fragmentSource);
                _gl.CompileShader(fragmentShader);
                CheckShaderCompileStatus(fragmentShader);

                program = _gl.CreateProgram();
                _gl.AttachShader(program, vertexShader);
                _gl.AttachShader(program, fragmentShader);
                if (bindAttributeLocations)
                {
                    _gl.BindAttribLocation(program, 0, "aPos");
                    _gl.BindAttribLocation(program, 1, "aColor");
                    _gl.BindAttribLocation(program, 2, "aUv");
                }

                _gl.LinkProgram(program);
                CheckProgramLinkStatus(program);
                return program;
            }
            finally
            {
                if (vertexShader != 0)
                {
                    _gl.DeleteShader(vertexShader);
                }

                if (fragmentShader != 0)
                {
                    _gl.DeleteShader(fragmentShader);
                }
            }
        }

        public unsafe void Render(MapRenderScene? scene)
        {
            if (_gl == null)
            {
                return;
            }

            _gl.Disable(EnableCap.DepthTest);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.ClearColor(0.078f, 0.086f, 0.106f, 1.0f);
            _gl.Clear((uint)ClearBufferMask.ColorBufferBit | (uint)ClearBufferMask.DepthBufferBit);

            if (scene is null || scene.ViewportWidth <= 0 || scene.ViewportHeight <= 0)
            {
                return;
            }

            DrawSprites(scene.BackgroundSprites, scene);

            var solidVertices = new List<float>(16384);
            foreach (var tile in scene.Tiles)
            {
                AppendRect(solidVertices, tile, scene);
            }

            foreach (var gridRect in scene.GridRects)
            {
                AppendRect(solidVertices, gridRect, scene);
            }

            foreach (var obj in scene.Objects)
            {
                AppendRect(solidVertices, obj, scene);
            }

            foreach (var arc in scene.VisionCones)
            {
                AppendArc(solidVertices, arc, scene);
            }

            foreach (var fan in scene.VisionFans)
            {
                AppendFanPolygon(solidVertices, fan, scene);
            }

            foreach (var wallLine in scene.WallLines)
            {
                AppendRect(solidVertices, wallLine, scene);
            }

            foreach (var wallHandle in scene.WallHandles)
            {
                AppendRect(solidVertices, wallHandle, scene);
            }

            if (solidVertices.Count > 0)
            {
                _gl.UseProgram(_solidShaderProgram);
                _gl.BindVertexArray(_solidVao);
                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _solidVbo);

                fixed (float* data = solidVertices.ToArray())
                {
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(solidVertices.Count * sizeof(float)), data, BufferUsageARB.DynamicDraw);
                }

                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(solidVertices.Count / 6));
                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
                _gl.BindVertexArray(0);
                _gl.UseProgram(0);
            }

            DrawSprites(scene.Sprites, scene);

            var error = _gl.GetError();
            if (error != GLEnum.NoError)
            {
                Console.WriteLine($"OpenGL Error ({_shaderProfileName}): {error}");
            }
        }

        private unsafe void DrawSprites(IReadOnlyList<MapRenderSprite> sprites, MapRenderScene scene)
        {
            if (_gl is null || sprites.Count == 0)
            {
                return;
            }

            _gl.UseProgram(_spriteShaderProgram);
            _gl.ActiveTexture(TextureUnit.Texture0);
            if (_spriteSamplerLocation >= 0)
            {
                _gl.Uniform1(_spriteSamplerLocation, 0);
            }

            _gl.BindVertexArray(_spriteVao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _spriteVbo);

            var vertices = new List<float>(48);
            foreach (var sprite in sprites)
            {
                var texture = GetOrCreateTexture(sprite.TexturePath);
                if (texture is null)
                {
                    continue;
                }

                vertices.Clear();
                AppendSprite(vertices, sprite, scene);
                if (vertices.Count == 0)
                {
                    continue;
                }

                fixed (float* data = vertices.ToArray())
                {
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Count * sizeof(float)), data, BufferUsageARB.DynamicDraw);
                }

                _gl.BindTexture(TextureTarget.Texture2D, texture.Handle);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(vertices.Count / 8));
            }

            _gl.BindTexture(TextureTarget.Texture2D, 0);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            _gl.BindVertexArray(0);
            _gl.UseProgram(0);
        }

        private unsafe TextureResource? GetOrCreateTexture(string texturePath)
        {
            if (_gl is null || string.IsNullOrWhiteSpace(texturePath))
            {
                return null;
            }

            if (_textureCache.TryGetValue(texturePath, out var texture))
            {
                return texture;
            }

            if (!System.IO.File.Exists(texturePath))
            {
                return null;
            }

            using var image = Image.Load<Rgba32>(texturePath);
            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);

            var handle = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, handle);
            fixed (byte* pixelData = pixels)
            {
                _gl.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    (int)InternalFormat.Rgba,
                    (uint)image.Width,
                    (uint)image.Height,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    pixelData);
            }

            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            _gl.BindTexture(TextureTarget.Texture2D, 0);

            texture = new TextureResource(handle, image.Width, image.Height);
            _textureCache[texturePath] = texture;
            return texture;
        }

        private static void AppendRect(List<float> vertices, MapRenderRect rect, MapRenderScene scene)
        {
            if (Math.Abs(rect.Rotation) < 0.01)
            {
                var left = ContentToNdcX(rect.X, scene);
                var top = ContentToNdcY(rect.Y, scene);
                var right = ContentToNdcX(rect.X + rect.Width, scene);
                var bottom = ContentToNdcY(rect.Y + rect.Height, scene);
                if (IsOutsideViewport(left, top, right, bottom)) return;
                AppendSolidVertex(vertices, left, top, rect.FillColor);
                AppendSolidVertex(vertices, right, top, rect.FillColor);
                AppendSolidVertex(vertices, right, bottom, rect.FillColor);
                AppendSolidVertex(vertices, left, top, rect.FillColor);
                AppendSolidVertex(vertices, right, bottom, rect.FillColor);
                AppendSolidVertex(vertices, left, bottom, rect.FillColor);
            }
            else
            {
                var centerX = rect.X + rect.Width / 2.0;
                var centerY = rect.Y + rect.Height / 2.0;
                var halfW = rect.Width / 2.0;
                var halfH = rect.Height / 2.0;
                var angleRad = rect.Rotation * Math.PI / 180.0;
                var cos = Math.Cos(angleRad);
                var sin = Math.Sin(angleRad);
                // 标准2D旋转：x' = x*cos - y*sin, y' = x*sin + y*cos
                // 四个角点相对于中心：(-W,-H), (W,-H), (W,H), (-W,H)
                var x1 = -halfW * cos - (-halfH) * sin;  // 左上
                var y1 = -halfW * sin + (-halfH) * cos;
                var x2 =  halfW * cos - (-halfH) * sin;  // 右上
                var y2 =  halfW * sin + (-halfH) * cos;
                var x3 =  halfW * cos - halfH * sin;      // 右下
                var y3 =  halfW * sin + halfH * cos;
                var x4 = -halfW * cos - halfH * sin;      // 左下
                var y4 = -halfW * sin + halfH * cos;
                var ndc1x = ContentToNdcX(centerX + x1, scene);
                var ndc1y = ContentToNdcY(centerY + y1, scene);
                var ndc2x = ContentToNdcX(centerX + x2, scene);
                var ndc2y = ContentToNdcY(centerY + y2, scene);
                var ndc3x = ContentToNdcX(centerX + x3, scene);
                var ndc3y = ContentToNdcY(centerY + y3, scene);
                var ndc4x = ContentToNdcX(centerX + x4, scene);
                var ndc4y = ContentToNdcY(centerY + y4, scene);
                AppendSolidVertex(vertices, ndc1x, ndc1y, rect.FillColor);
                AppendSolidVertex(vertices, ndc2x, ndc2y, rect.FillColor);
                AppendSolidVertex(vertices, ndc3x, ndc3y, rect.FillColor);
                AppendSolidVertex(vertices, ndc1x, ndc1y, rect.FillColor);
                AppendSolidVertex(vertices, ndc3x, ndc3y, rect.FillColor);
                AppendSolidVertex(vertices, ndc4x, ndc4y, rect.FillColor);
            }
        }

        private static void AppendFanPolygon(List<float> vertices, MapRenderPolygon fan, MapRenderScene scene)
        {
            if (fan.EdgeVertices.Count < 2) return;
            // OutlineOnly 模式不在此填充（留给后续描边渲染）
            if (fan.Mode == PolygonFillMode.OutlineOnly) return;
            // StencilMask 模式当前先按 Fan 处理（待后续在 Render 入口处用 stencil 测试单独走管线）
            var centerNdcX = ContentToNdcX(fan.CenterX, scene);
            var centerNdcY = ContentToNdcY(fan.CenterY, scene);
            for (int i = 0; i < fan.EdgeVertices.Count - 1; i++)
            {
                var (x1, y1) = fan.EdgeVertices[i];
                var (x2, y2) = fan.EdgeVertices[i + 1];
                var n1x = ContentToNdcX(x1, scene);
                var n1y = ContentToNdcY(y1, scene);
                var n2x = ContentToNdcX(x2, scene);
                var n2y = ContentToNdcY(y2, scene);
                AppendSolidVertex(vertices, centerNdcX, centerNdcY, fan.FillColor);
                AppendSolidVertex(vertices, n1x, n1y, fan.FillColor);
                AppendSolidVertex(vertices, n2x, n2y, fan.FillColor);
            }
        }

        private static void AppendArc(List<float> vertices, MapRenderArc arc, MapRenderScene scene)
        {
            const int segments = 32;
            var centerX = ContentToNdcX(arc.CenterX, scene);
            var centerY = ContentToNdcY(arc.CenterY, scene);

            var startRad = arc.StartAngleDegrees * Math.PI / 180.0;
            var endRad = arc.EndAngleDegrees * Math.PI / 180.0;
            var angleRange = endRad - startRad;

            var radiusNdcX = (float)((arc.Radius * scene.Zoom) / scene.ViewportWidth * 2.0);
            var radiusNdcY = (float)((arc.Radius * scene.Zoom) / scene.ViewportHeight * 2.0);

            for (int i = 0; i < segments; i++)
            {
                var angle1 = startRad + (angleRange * i / segments);
                var angle2 = startRad + (angleRange * (i + 1) / segments);

                var x1 = centerX + (float)(Math.Cos(angle1) * radiusNdcX);
                var y1 = centerY - (float)(Math.Sin(angle1) * radiusNdcY);
                var x2 = centerX + (float)(Math.Cos(angle2) * radiusNdcX);
                var y2 = centerY - (float)(Math.Sin(angle2) * radiusNdcY);

                AppendSolidVertex(vertices, centerX, centerY, arc.FillColor);
                AppendSolidVertex(vertices, x1, y1, arc.FillColor);
                AppendSolidVertex(vertices, x2, y2, arc.FillColor);
            }
        }

        private static void AppendSprite(List<float> vertices, MapRenderSprite sprite, MapRenderScene scene)
        {
            if (Math.Abs(sprite.Rotation) < 0.01)
            {
                var left = ContentToNdcX(sprite.X, scene);
                var top = ContentToNdcY(sprite.Y, scene);
                var right = ContentToNdcX(sprite.X + sprite.Width, scene);
                var bottom = ContentToNdcY(sprite.Y + sprite.Height, scene);

                if (IsOutsideViewport(left, top, right, bottom))
                    return;

                AppendSpriteVertex(vertices, left, top, sprite.TintColor, 0f, 0f);
                AppendSpriteVertex(vertices, right, top, sprite.TintColor, 1f, 0f);
                AppendSpriteVertex(vertices, right, bottom, sprite.TintColor, 1f, 1f);
                AppendSpriteVertex(vertices, left, top, sprite.TintColor, 0f, 0f);
                AppendSpriteVertex(vertices, right, bottom, sprite.TintColor, 1f, 1f);
                AppendSpriteVertex(vertices, left, bottom, sprite.TintColor, 0f, 1f);
            }
            else
            {
                var centerX = sprite.X + sprite.Width / 2.0;
                var centerY = sprite.Y + sprite.Height / 2.0;
                var halfW = sprite.Width / 2.0;
                var halfH = sprite.Height / 2.0;
                var angleRad = sprite.Rotation * Math.PI / 180.0;
                var cos = Math.Cos(angleRad);
                var sin = Math.Sin(angleRad);
                var x1 = -halfW * cos - (-halfH) * sin;  // 左上
                var y1 = -halfW * sin + (-halfH) * cos;
                var x2 =  halfW * cos - (-halfH) * sin;  // 右上
                var y2 =  halfW * sin + (-halfH) * cos;
                var x3 =  halfW * cos - halfH * sin;      // 右下
                var y3 =  halfW * sin + halfH * cos;
                var x4 = -halfW * cos - halfH * sin;      // 左下
                var y4 = -halfW * sin + halfH * cos;

                var ndc1x = ContentToNdcX(centerX + x1, scene);
                var ndc1y = ContentToNdcY(centerY + y1, scene);
                var ndc2x = ContentToNdcX(centerX + x2, scene);
                var ndc2y = ContentToNdcY(centerY + y2, scene);
                var ndc3x = ContentToNdcX(centerX + x3, scene);
                var ndc3y = ContentToNdcY(centerY + y3, scene);
                var ndc4x = ContentToNdcX(centerX + x4, scene);
                var ndc4y = ContentToNdcY(centerY + y4, scene);

                AppendSpriteVertex(vertices, ndc1x, ndc1y, sprite.TintColor, 0f, 0f);
                AppendSpriteVertex(vertices, ndc2x, ndc2y, sprite.TintColor, 1f, 0f);
                AppendSpriteVertex(vertices, ndc3x, ndc3y, sprite.TintColor, 1f, 1f);
                AppendSpriteVertex(vertices, ndc1x, ndc1y, sprite.TintColor, 0f, 0f);
                AppendSpriteVertex(vertices, ndc3x, ndc3y, sprite.TintColor, 1f, 1f);
                AppendSpriteVertex(vertices, ndc4x, ndc4y, sprite.TintColor, 0f, 1f);
            }
        }

        private static bool IsOutsideViewport(float left, float top, float right, float bottom)
            => (right < -1f && left < -1f)
               || (right > 1f && left > 1f)
               || (top < -1f && bottom < -1f)
               || (top > 1f && bottom > 1f);

        private static void AppendSolidVertex(List<float> vertices, float x, float y, MapRenderColor color)
        {
            vertices.Add(x);
            vertices.Add(y);
            vertices.Add(color.R);
            vertices.Add(color.G);
            vertices.Add(color.B);
            vertices.Add(color.A);
        }

        private static void AppendSpriteVertex(List<float> vertices, float x, float y, MapRenderColor color, float u, float v)
        {
            vertices.Add(x);
            vertices.Add(y);
            vertices.Add(color.R);
            vertices.Add(color.G);
            vertices.Add(color.B);
            vertices.Add(color.A);
            vertices.Add(u);
            vertices.Add(v);
        }

        private static float ContentToNdcX(double contentX, MapRenderScene scene)
        {
            var screenX = ((contentX - scene.CameraCenterX) * scene.Zoom) + (scene.ViewportWidth / 2.0);
            return (float)((screenX / scene.ViewportWidth) * 2.0 - 1.0);
        }

        private static float ContentToNdcY(double contentY, MapRenderScene scene)
        {
            var screenY = ((contentY - scene.CameraCenterY) * scene.Zoom) + (scene.ViewportHeight / 2.0);
            return (float)(1.0 - ((screenY / scene.ViewportHeight) * 2.0));
        }

        private void CheckShaderCompileStatus(uint shader)
        {
            if (_gl == null)
            {
                throw new InvalidOperationException("OpenGL pipeline is not initialized.");
            }

            _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int success);
            if (success == 0)
            {
                string infoLog = _gl.GetShaderInfoLog(shader);
                throw new Exception($"Error compiling shader: {infoLog}");
            }
        }

        private void CheckProgramLinkStatus(uint program)
        {
            if (_gl == null)
            {
                throw new InvalidOperationException("OpenGL pipeline is not initialized.");
            }

            _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int success);
            if (success == 0)
            {
                string infoLog = _gl.GetProgramInfoLog(program);
                throw new Exception($"Error linking program: {infoLog}");
            }
        }

        public void Dispose()
        {
            if (_gl == null)
            {
                return;
            }

            foreach (var texture in _textureCache.Values)
            {
                _gl.DeleteTexture(texture.Handle);
            }

            _textureCache.Clear();
            _gl.DeleteVertexArray(_solidVao);
            _gl.DeleteBuffer(_solidVbo);
            _gl.DeleteProgram(_solidShaderProgram);
            _gl.DeleteVertexArray(_spriteVao);
            _gl.DeleteBuffer(_spriteVbo);
            _gl.DeleteProgram(_spriteShaderProgram);
            _gl = null;
        }
    }
}
