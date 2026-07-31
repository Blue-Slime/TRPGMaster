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

            foreach (var selHandle in scene.SelectionHandles)
            {
                AppendRect(solidVertices, selHandle, scene);
            }

            // ── 矢量形状：填充趟（三角形批次）───────────────────────────────────
            foreach (var shape in scene.VectorShapes)
            {
                if (shape.IsFilled)
                    AppendVectorShapeFill(solidVertices, shape, scene);
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

            // ── 矢量形状：描边趟（每个形状单独提交，LINE_LOOP/LINE_STRIP）─────
            DrawVectorShapeStrokes(scene.VectorShapes, scene);

            DrawSprites(scene.Sprites, scene);

            // ── Token 状态徽章（已移至 TokenUIManager Avalonia UI 层）───────
            // GL 层不再渲染徽章色块，改由 UI overlay 层渲染 emoji 文本
            // if (scene.ConditionBadges.Count > 0)
            //     DrawConditionBadges(scene);

            // ── 光源光晕（加法混合，在迷雾之前）────────────────────────────────
            if (scene.LightSources.Count > 0)
                DrawLights(scene);

            // ── 战争迷雾 ──────────────────────────────────────────────────────
            if (scene.FogEnabled)
                DrawFog(scene);

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

        // ─────────────────────────────────────────────────────────────────────
        // 矢量形状渲染（填充 + 描边两趟）
        // ─────────────────────────────────────────────────────────────────────

        private static void AppendVectorShapeFill(List<float> vertices, MapVectorShape shape, MapRenderScene scene)
        {
            switch (shape.Type)
            {
                case VectorShapeType.Rect:
                    AppendRectFill(vertices, shape, scene);
                    break;
                case VectorShapeType.Ellipse:
                    AppendEllipseFill(vertices, shape, scene);
                    break;
                case VectorShapeType.Polygon:
                case VectorShapeType.Freehand:
                    AppendPolygonFill(vertices, shape, scene);
                    break;
                case VectorShapeType.Cone:
                case VectorShapeType.Wedge:
                    AppendConeFill(vertices, shape, scene);
                    break;
                // Line 不填充
            }
        }

        private static void AppendRectFill(List<float> vertices, MapVectorShape shape, MapRenderScene scene)
        {
            var cx = ContentToNdcX(shape.CenterX, scene);
            var cy = ContentToNdcY(shape.CenterY, scene);
            var halfW = (float)((shape.Width / 2.0) * scene.Zoom / scene.ViewportWidth * 2.0);
            var halfH = (float)((shape.Height / 2.0) * scene.Zoom / scene.ViewportHeight * 2.0);

            var x1 = cx - halfW; var y1 = cy + halfH;
            var x2 = cx + halfW; var y2 = cy + halfH;
            var x3 = cx + halfW; var y3 = cy - halfH;
            var x4 = cx - halfW; var y4 = cy - halfH;

            AppendSolidVertex(vertices, x1, y1, shape.FillColor);
            AppendSolidVertex(vertices, x2, y2, shape.FillColor);
            AppendSolidVertex(vertices, x3, y3, shape.FillColor);
            AppendSolidVertex(vertices, x1, y1, shape.FillColor);
            AppendSolidVertex(vertices, x3, y3, shape.FillColor);
            AppendSolidVertex(vertices, x4, y4, shape.FillColor);
        }

        private static void AppendEllipseFill(List<float> vertices, MapVectorShape shape, MapRenderScene scene)
        {
            const int segments = 32;
            var cx = ContentToNdcX(shape.CenterX, scene);
            var cy = ContentToNdcY(shape.CenterY, scene);
            var radiusX = (float)((shape.Width / 2.0) * scene.Zoom / scene.ViewportWidth * 2.0);
            var radiusY = (float)((shape.Height / 2.0) * scene.Zoom / scene.ViewportHeight * 2.0);

            for (int i = 0; i < segments; i++)
            {
                var angle1 = (float)(2 * Math.PI * i / segments);
                var angle2 = (float)(2 * Math.PI * (i + 1) / segments);
                var x1 = cx + (float)(Math.Cos(angle1) * radiusX);
                var y1 = cy - (float)(Math.Sin(angle1) * radiusY);
                var x2 = cx + (float)(Math.Cos(angle2) * radiusX);
                var y2 = cy - (float)(Math.Sin(angle2) * radiusY);

                AppendSolidVertex(vertices, cx, cy, shape.FillColor);
                AppendSolidVertex(vertices, x1, y1, shape.FillColor);
                AppendSolidVertex(vertices, x2, y2, shape.FillColor);
            }
        }

        private static void AppendPolygonFill(List<float> vertices, MapVectorShape shape, MapRenderScene scene)
        {
            if (shape.Points.Count < 3) return;
            var cx = ContentToNdcX(shape.CenterX, scene);
            var cy = ContentToNdcY(shape.CenterY, scene);

            for (int i = 1; i < shape.Points.Count - 1; i++)
            {
                var p0 = shape.Points[0];
                var p1 = shape.Points[i];
                var p2 = shape.Points[i + 1];
                var x0 = ContentToNdcX(p0.X, scene);
                var y0 = ContentToNdcY(p0.Y, scene);
                var x1 = ContentToNdcX(p1.X, scene);
                var y1 = ContentToNdcY(p1.Y, scene);
                var x2 = ContentToNdcX(p2.X, scene);
                var y2 = ContentToNdcY(p2.Y, scene);

                AppendSolidVertex(vertices, x0, y0, shape.FillColor);
                AppendSolidVertex(vertices, x1, y1, shape.FillColor);
                AppendSolidVertex(vertices, x2, y2, shape.FillColor);
            }
        }

        private static void AppendConeFill(List<float> vertices, MapVectorShape shape, MapRenderScene scene)
        {
            const int segments = 32;
            var cx = ContentToNdcX(shape.CenterX, scene);
            var cy = ContentToNdcY(shape.CenterY, scene);
            var radius = (float)(shape.Radius * scene.Zoom / scene.ViewportWidth * 2.0);
            var dirRad = shape.Direction * Math.PI / 180.0;
            var halfAngleRad = shape.HalfAngle * Math.PI / 180.0;

            var startAngle = dirRad - halfAngleRad;
            var endAngle = dirRad + halfAngleRad;
            var angleRange = endAngle - startAngle;

            for (int i = 0; i < segments; i++)
            {
                var angle1 = startAngle + (angleRange * i / segments);
                var angle2 = startAngle + (angleRange * (i + 1) / segments);
                var x1 = cx + (float)(Math.Cos(angle1) * radius);
                var y1 = cy - (float)(Math.Sin(angle1) * radius);
                var x2 = cx + (float)(Math.Cos(angle2) * radius);
                var y2 = cy - (float)(Math.Sin(angle2) * radius);

                AppendSolidVertex(vertices, cx, cy, shape.FillColor);
                AppendSolidVertex(vertices, x1, y1, shape.FillColor);
                AppendSolidVertex(vertices, x2, y2, shape.FillColor);
            }
        }

        private unsafe void DrawVectorShapeStrokes(IReadOnlyList<MapVectorShape> shapes, MapRenderScene scene)
        {
            if (_gl is null) return;

            _gl.UseProgram(_solidShaderProgram);
            _gl.BindVertexArray(_solidVao);

            foreach (var shape in shapes)
            {
                var lineVertices = new List<float>(256);
                BuildStrokeVertices(lineVertices, shape, scene);
                if (lineVertices.Count < 2 * 6) continue;

                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _solidVbo);
                fixed (float* data = lineVertices.ToArray())
                {
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(lineVertices.Count * sizeof(float)), data, BufferUsageARB.DynamicDraw);
                }

                _gl.LineWidth(shape.StrokeWidth);
                var primitive = (shape.Type == VectorShapeType.Line || shape.Type == VectorShapeType.Freehand)
                    ? PrimitiveType.LineStrip
                    : PrimitiveType.LineLoop;

                _gl.DrawArrays(primitive, 0, (uint)(lineVertices.Count / 6));
            }

            _gl.LineWidth(1f);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            _gl.BindVertexArray(0);
            _gl.UseProgram(0);
        }

        private static void BuildStrokeVertices(List<float> vertices, MapVectorShape shape, MapRenderScene scene)
        {
            switch (shape.Type)
            {
                case VectorShapeType.Line:
                    {
                        var x1 = ContentToNdcX(shape.CenterX, scene);
                        var y1 = ContentToNdcY(shape.CenterY, scene);
                        var x2 = ContentToNdcX(shape.X2, scene);
                        var y2 = ContentToNdcY(shape.Y2, scene);
                        AppendSolidVertex(vertices, x1, y1, shape.StrokeColor);
                        AppendSolidVertex(vertices, x2, y2, shape.StrokeColor);
                    }
                    break;

                case VectorShapeType.Rect:
                    {
                        var cx = ContentToNdcX(shape.CenterX, scene);
                        var cy = ContentToNdcY(shape.CenterY, scene);
                        var halfW = (float)((shape.Width / 2.0) * scene.Zoom / scene.ViewportWidth * 2.0);
                        var halfH = (float)((shape.Height / 2.0) * scene.Zoom / scene.ViewportHeight * 2.0);
                        AppendSolidVertex(vertices, cx - halfW, cy + halfH, shape.StrokeColor);
                        AppendSolidVertex(vertices, cx + halfW, cy + halfH, shape.StrokeColor);
                        AppendSolidVertex(vertices, cx + halfW, cy - halfH, shape.StrokeColor);
                        AppendSolidVertex(vertices, cx - halfW, cy - halfH, shape.StrokeColor);
                    }
                    break;

                case VectorShapeType.Ellipse:
                    {
                        const int segments = 32;
                        var cx = ContentToNdcX(shape.CenterX, scene);
                        var cy = ContentToNdcY(shape.CenterY, scene);
                        var radiusX = (float)((shape.Width / 2.0) * scene.Zoom / scene.ViewportWidth * 2.0);
                        var radiusY = (float)((shape.Height / 2.0) * scene.Zoom / scene.ViewportHeight * 2.0);
                        for (int i = 0; i <= segments; i++)
                        {
                            var angle = (float)(2 * Math.PI * i / segments);
                            var x = cx + (float)(Math.Cos(angle) * radiusX);
                            var y = cy - (float)(Math.Sin(angle) * radiusY);
                            AppendSolidVertex(vertices, x, y, shape.StrokeColor);
                        }
                    }
                    break;

                case VectorShapeType.Polygon:
                case VectorShapeType.Freehand:
                    foreach (var pt in shape.Points)
                    {
                        var x = ContentToNdcX(pt.X, scene);
                        var y = ContentToNdcY(pt.Y, scene);
                        AppendSolidVertex(vertices, x, y, shape.StrokeColor);
                    }
                    break;

                case VectorShapeType.Cone:
                case VectorShapeType.Wedge:
                    {
                        const int segments = 32;
                        var cx = ContentToNdcX(shape.CenterX, scene);
                        var cy = ContentToNdcY(shape.CenterY, scene);
                        var radius = (float)(shape.Radius * scene.Zoom / scene.ViewportWidth * 2.0);
                        var dirRad = shape.Direction * Math.PI / 180.0;
                        var halfAngleRad = shape.HalfAngle * Math.PI / 180.0;
                        var startAngle = dirRad - halfAngleRad;
                        var endAngle = dirRad + halfAngleRad;

                        // 从圆心开始
                        AppendSolidVertex(vertices, cx, cy, shape.StrokeColor);
                        // 左边线
                        var x1 = cx + (float)(Math.Cos(startAngle) * radius);
                        var y1 = cy - (float)(Math.Sin(startAngle) * radius);
                        AppendSolidVertex(vertices, x1, y1, shape.StrokeColor);
                        // 圆弧
                        var angleRange = endAngle - startAngle;
                        for (int i = 1; i < segments; i++)
                        {
                            var angle = startAngle + (angleRange * i / segments);
                            var x = cx + (float)(Math.Cos(angle) * radius);
                            var y = cy - (float)(Math.Sin(angle) * radius);
                            AppendSolidVertex(vertices, x, y, shape.StrokeColor);
                        }
                        // 右边线
                        var x2 = cx + (float)(Math.Cos(endAngle) * radius);
                        var y2 = cy - (float)(Math.Sin(endAngle) * radius);
                        AppendSolidVertex(vertices, x2, y2, shape.StrokeColor);
                        // 回到圆心闭合
                        AppendSolidVertex(vertices, cx, cy, shape.StrokeColor);
                    }
                    break;
            }
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

        // ── 战争迷雾 ─────────────────────────────────────────────────────────
        // 实现：
        //   Pass 1 — 全屏半透明黑色层（正常 alpha blend）
        //   Pass 2 — 已揭示区域用 alpha=0 颜色覆盖，混合函数设为 (ZERO, ZERO) 完全擦除
        // 这样不需要 stencil buffer，兼容所有 GL 版本。
        private unsafe void DrawFog(MapRenderScene scene)
        {
            if (_gl is null) return;

            var solidVerts = new List<float>(24);

            // ── Pass 1：全屏暗层 ────────────────────────────────────────────
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // 覆盖 NDC [-1,1] 的全屏两个三角形
            var fogColor = new MapRenderColor(0f, 0f, 0f, 0.82f);
            // 左下, 右下, 右上, 左下, 右上, 左上
            solidVerts.Add(-1f); solidVerts.Add(-1f); solidVerts.Add(fogColor.R); solidVerts.Add(fogColor.G); solidVerts.Add(fogColor.B); solidVerts.Add(fogColor.A);
            solidVerts.Add( 1f); solidVerts.Add(-1f); solidVerts.Add(fogColor.R); solidVerts.Add(fogColor.G); solidVerts.Add(fogColor.B); solidVerts.Add(fogColor.A);
            solidVerts.Add( 1f); solidVerts.Add( 1f); solidVerts.Add(fogColor.R); solidVerts.Add(fogColor.G); solidVerts.Add(fogColor.B); solidVerts.Add(fogColor.A);
            solidVerts.Add(-1f); solidVerts.Add(-1f); solidVerts.Add(fogColor.R); solidVerts.Add(fogColor.G); solidVerts.Add(fogColor.B); solidVerts.Add(fogColor.A);
            solidVerts.Add( 1f); solidVerts.Add( 1f); solidVerts.Add(fogColor.R); solidVerts.Add(fogColor.G); solidVerts.Add(fogColor.B); solidVerts.Add(fogColor.A);
            solidVerts.Add(-1f); solidVerts.Add( 1f); solidVerts.Add(fogColor.R); solidVerts.Add(fogColor.G); solidVerts.Add(fogColor.B); solidVerts.Add(fogColor.A);

            _gl.UseProgram(_solidShaderProgram);
            _gl.BindVertexArray(_solidVao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _solidVbo);
            fixed (float* d = solidVerts.ToArray())
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(solidVerts.Count * sizeof(float)), d, BufferUsageARB.DynamicDraw);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(solidVerts.Count / 6));

            // ── Pass 2：揭示区域抠孔（写入 alpha=0 完全透明，src=ZERO dst=ZERO）
            _gl.BlendFunc(BlendingFactor.Zero, BlendingFactor.Zero);
            // ColorMask 只写 alpha 通道（保留 RGB 不变，只把 alpha 清零）
            _gl.ColorMask(false, false, false, true);

            solidVerts.Clear();
            var clearColor = new MapRenderColor(0f, 0f, 0f, 0f);
            foreach (var poly in scene.FogRevealedPolygons)
            {
                if (poly.Count < 3) continue;
                // 三角扇：第 0 点为中心，其余依次三角化
                var c = poly[0];
                var cx = (float)ContentToNdcX(c.X, scene);
                var cy = (float)ContentToNdcY(c.Y, scene);
                for (int i = 1; i < poly.Count - 1; i++)
                {
                    var (x1, y1) = poly[i];
                    var (x2, y2) = poly[i + 1];
                    var nx1 = (float)ContentToNdcX(x1, scene);
                    var ny1 = (float)ContentToNdcY(y1, scene);
                    var nx2 = (float)ContentToNdcX(x2, scene);
                    var ny2 = (float)ContentToNdcY(y2, scene);
                    solidVerts.Add(cx);  solidVerts.Add(cy);  solidVerts.Add(clearColor.R); solidVerts.Add(clearColor.G); solidVerts.Add(clearColor.B); solidVerts.Add(clearColor.A);
                    solidVerts.Add(nx1); solidVerts.Add(ny1); solidVerts.Add(clearColor.R); solidVerts.Add(clearColor.G); solidVerts.Add(clearColor.B); solidVerts.Add(clearColor.A);
                    solidVerts.Add(nx2); solidVerts.Add(ny2); solidVerts.Add(clearColor.R); solidVerts.Add(clearColor.G); solidVerts.Add(clearColor.B); solidVerts.Add(clearColor.A);
                }
            }

            if (solidVerts.Count > 0)
            {
                fixed (float* d = solidVerts.ToArray())
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(solidVerts.Count * sizeof(float)), d, BufferUsageARB.DynamicDraw);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(solidVerts.Count / 6));
            }

            // 恢复渲染状态
            _gl.ColorMask(true, true, true, true);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            _gl.BindVertexArray(0);
            _gl.UseProgram(0);
        }

        // ── 光源光晕：每个光源绘制两圈三角扇（亮圈 + 暗圈），加法混合 ──────────
        // 渲染策略：
        //   亮圈：全不透明度，颜色 = LightComponent.Color
        //   暗圈：半透明（alpha * 0.35），边缘 alpha 递减至 0（用顶点色径向衰减近似）
        // 混合函数：GL_ONE, GL_ONE（加法）→ 多光源自然叠加，不会过曝
        private unsafe void DrawLights(MapRenderScene scene)
        {
            if (_gl is null || scene.LightSources.Count == 0) return;

            const int Segments = 32;   // 每个圆的扇形分段数（32 段已足够平滑）

            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);   // 加法混合

            _gl.UseProgram(_solidShaderProgram);
            _gl.BindVertexArray(_solidVao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _solidVbo);

            var verts = new List<float>(Segments * 6 * 3 * 2);

            foreach (var light in scene.LightSources)
            {
                var cx = (float)ContentToNdcX(light.CenterX, scene);
                var cy = (float)ContentToNdcY(light.CenterY, scene);

                double halfAngle = light.IsCone ? light.ConeHalfAngle : 180.0;
                double dirRad    = light.IsCone ? (light.ConeDirection * Math.PI / 180.0) : 0.0;

                // ── 暗圈（DimRadius，alpha 低）────────────────────────────────
                if (light.DimRadius > 0)
                {
                    float dimAlpha = light.Color.A * 0.28f;
                    DrawLightCircle(verts, cx, cy, light.CenterX, light.CenterY,
                        light.DimRadius, halfAngle, dirRad, Segments, scene,
                        new MapRenderColor(light.Color.R, light.Color.G, light.Color.B, dimAlpha));
                }

                // ── 亮圈（BrightRadius，高不透明度）──────────────────────────
                if (light.BrightRadius > 0)
                {
                    DrawLightCircle(verts, cx, cy, light.CenterX, light.CenterY,
                        light.BrightRadius, halfAngle, dirRad, Segments, scene,
                        light.Color);
                }

                if (verts.Count == 0) continue;

                fixed (float* d = verts.ToArray())
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Count * sizeof(float)), d, BufferUsageARB.DynamicDraw);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(verts.Count / 6));
                verts.Clear();
            }

            // 恢复正常混合
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            _gl.BindVertexArray(0);
            _gl.UseProgram(0);
        }

        /// <summary>
        /// 向 verts 追加一个光源圆形（三角扇）的顶点数据。
        /// 中心点 alpha = color.A，边缘点 alpha = 0（实现径向衰减）。
        /// </summary>
        private static void DrawLightCircle(
            List<float> verts,
            float ndcCx, float ndcCy,
            double worldCx, double worldCy,
            double worldRadius,
            double halfAngleDeg, double directionRad,
            int segments,
            MapRenderScene scene,
            MapRenderColor color)
        {
            double halfAngleRad = halfAngleDeg * Math.PI / 180.0;
            double step = (halfAngleRad * 2.0) / segments;
            double startAngle = directionRad - halfAngleRad;

            for (int i = 0; i < segments; i++)
            {
                double a1 = startAngle + step * i;
                double a2 = startAngle + step * (i + 1);

                var ex1 = (float)ContentToNdcX(worldCx + Math.Cos(a1) * worldRadius, scene);
                var ey1 = (float)ContentToNdcY(worldCy + Math.Sin(a1) * worldRadius, scene);
                var ex2 = (float)ContentToNdcX(worldCx + Math.Cos(a2) * worldRadius, scene);
                var ey2 = (float)ContentToNdcY(worldCy + Math.Sin(a2) * worldRadius, scene);

                // 中心点（全亮）
                verts.Add(ndcCx); verts.Add(ndcCy);
                verts.Add(color.R); verts.Add(color.G); verts.Add(color.B); verts.Add(color.A);
                // 边缘点 1（alpha 衰减至 0）
                verts.Add(ex1); verts.Add(ey1);
                verts.Add(color.R); verts.Add(color.G); verts.Add(color.B); verts.Add(0f);
                // 边缘点 2（alpha 衰减至 0）
                verts.Add(ex2); verts.Add(ey2);
                verts.Add(color.R); verts.Add(color.G); verts.Add(color.B); verts.Add(0f);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 【已废弃】Token 状态徽章绘制 - 已移至 TokenUIManager（Avalonia UI 层）
        // ─────────────────────────────────────────────────────────────────────────────
        // GL 层不再渲染徽章色块，改由 UI overlay 层渲染 emoji 文本。
        // 此方法保留供参考，实际不再调用。

        [Obsolete("ConditionBadges 已移至 TokenUIManager，此方法不再使用")]
        private unsafe void DrawConditionBadges(MapRenderScene scene)
        {
            if (_gl is null || scene.ConditionBadges.Count == 0) return;

            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            var verts = new List<float>(scene.ConditionBadges.Count * 36); // 每个徽章 6 顶点 × 6 分量

            foreach (var badge in scene.ConditionBadges)
            {
                // 圆角矩形（暂时用普通矩形绘制）
                var centerX = badge.X + badge.Size / 2.0;
                var centerY = badge.Y + badge.Size / 2.0;
                var halfW = badge.Size / 2.0;
                var halfH = badge.Size / 2.0;

                // 四个角点（无旋转，rotation = 0）
                var ndc1x = (float)ContentToNdcX(centerX - halfW, scene);
                var ndc1y = (float)ContentToNdcY(centerY - halfH, scene);
                var ndc2x = (float)ContentToNdcX(centerX + halfW, scene);
                var ndc2y = (float)ContentToNdcY(centerY - halfH, scene);
                var ndc3x = (float)ContentToNdcX(centerX + halfW, scene);
                var ndc3y = (float)ContentToNdcY(centerY + halfH, scene);
                var ndc4x = (float)ContentToNdcX(centerX - halfW, scene);
                var ndc4y = (float)ContentToNdcY(centerY + halfH, scene);

                var r = badge.BackgroundColor.R;
                var g = badge.BackgroundColor.G;
                var b = badge.BackgroundColor.B;
                var a = badge.BackgroundColor.A;

                // 三角形 1: 左上, 右上, 右下
                verts.Add(ndc1x); verts.Add(ndc1y); verts.Add(r); verts.Add(g); verts.Add(b); verts.Add(a);
                verts.Add(ndc2x); verts.Add(ndc2y); verts.Add(r); verts.Add(g); verts.Add(b); verts.Add(a);
                verts.Add(ndc3x); verts.Add(ndc3y); verts.Add(r); verts.Add(g); verts.Add(b); verts.Add(a);

                // 三角形 2: 左上, 右下, 左下
                verts.Add(ndc1x); verts.Add(ndc1y); verts.Add(r); verts.Add(g); verts.Add(b); verts.Add(a);
                verts.Add(ndc3x); verts.Add(ndc3y); verts.Add(r); verts.Add(g); verts.Add(b); verts.Add(a);
                verts.Add(ndc4x); verts.Add(ndc4y); verts.Add(r); verts.Add(g); verts.Add(b); verts.Add(a);

                // TODO: 绘制 Icon emoji（需要文本渲染系统）
                // TODO: StackCount > 1 时右上角绘制 ×N 标记
            }

            if (verts.Count == 0) return;

            _gl.UseProgram(_solidShaderProgram);
            _gl.BindVertexArray(_solidVao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _solidVbo);

            fixed (float* ptr = verts.ToArray())
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer,
                    (nuint)(verts.Count * sizeof(float)),
                    ptr,
                    BufferUsageARB.StreamDraw);
            }

            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(verts.Count / 6));
            _gl.BindVertexArray(0);
        }
    }
}
