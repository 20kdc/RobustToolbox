using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Shared;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Robust.Client.Graphics.Clyde
{
    internal partial class Clyde
    {
        /// <summary>
        ///     GL Context(s) provided by the windowing system (GLFW, SDL2...)
        /// </summary>
        private sealed class GLContextWindow : GLContextBase
        {
            private readonly Dictionary<WindowId, WindowData> _windowData = new();

            public override GLContextSpec[] SpecsToTry
            {
                get
                {
                    // Compat mode: only GLES2.
                    if (Clyde.Cfg.GetCVar(CVars.DisplayCompat))
                    {
                        return new[]
                        {
                            GetVersionSpec(RendererOpenGLVersion.GLES3),
                            GetVersionSpec(RendererOpenGLVersion.GLES2)
                        };
                    }

                    var requestedVersion = (RendererOpenGLVersion) Clyde.Cfg.GetCVar(CVars.DisplayOpenGLVersion);
                    if (requestedVersion != RendererOpenGLVersion.Auto)
                    {
                        return new[]
                        {
                            GetVersionSpec(requestedVersion)
                        };
                    }

                    return new[]
                    {
                        GetVersionSpec(RendererOpenGLVersion.GL33),
                        GetVersionSpec(RendererOpenGLVersion.GL31),
                        GetVersionSpec(RendererOpenGLVersion.GLES3),
                        GetVersionSpec(RendererOpenGLVersion.GLES2),
                    };
                }
            }

            public override bool RequireWindowGL => true;
            // ANGLE does not support main window sRGB.
            public override bool HasBrokenWindowSrgb(RendererOpenGLVersion version) => OpenGLVersionIsGLES(version) && OperatingSystem.IsWindows();

            public override string SawmillCategory => "clyde.ogl.window";

            public GLContextWindow(Clyde clyde) : base(clyde)
            {
            }

            protected override GLContextSpec? SpecWithOpenGLVersion(RendererOpenGLVersion version)
            {
                return GetVersionSpec(version);
            }

            public override void UpdateVSync(bool vSync)
            {
                if (Clyde.MainWindow == null)
                    return;

                Clyde.Windowing!.GLMakeContextCurrent(Clyde.MainWindow);
                Clyde.Windowing.GLSwapInterval(Clyde.MainWindow, vSync ? 1 : 0);
            }

            public override void WindowCreated(GLContextSpec? spec, WindowReg reg)
            {
                reg.RenderTarget.MakeGLFence = true;

                var data = new WindowData
                {
                    Reg = reg
                };

                _windowData[reg.Id] = data;

                if (reg.IsMainWindow)
                {
                    Clyde.Windowing!.GLMakeContextCurrent(reg);
                    InitOpenGL(spec!.Value.OpenGLVersion);
                }
                else
                {
                    Clyde.Windowing!.GLMakeContextCurrent(Clyde.MainWindow);

                    CreateWindowRenderTexture(data);
                    InitWindowBlitThread(data);
                }
            }

            public override void WindowDestroyed(WindowReg reg)
            {
                var data = _windowData[reg.Id];
                data.BlitDoneEvent?.Set();

                _windowData.Remove(reg.Id);

                if (!(Clyde.EffectiveThreadWindowBlit || reg.IsMainWindow))
                {
                    // not main window and "blit thread" was actually main thread all along
                    // that makes this the last chance for cleanup!
                    Clyde.Windowing!.GLMakeContextCurrent(reg);
                    BlitDataCleanup(data);
                    Clyde.Windowing!.GLMakeContextCurrent(Clyde.MainWindow!);
                }
            }


            public override void Shutdown()
            {
                // Nada, window system shutdown handles it.
            }

            public override void SwapAllBuffers()
            {
                BlitSecondaryWindows();

                Clyde.Windowing!.WindowSwapBuffers(Clyde.MainWindow!);
            }

            public override void WindowResized(WindowReg reg, Vector2i oldSize)
            {
                if (reg.IsMainWindow)
                    return;

                // Recreate render texture for the window.
                var data = _windowData[reg.Id];
                data.RenderTexture!.Dispose();
                CreateWindowRenderTexture(data);
            }

            public override unsafe void* GetProcAddress(string name)
            {
                return Clyde.Windowing!.GLGetProcAddress(name);
            }

            public override void BindWindowRenderTarget(WindowId rtWindowId)
            {
                var data = _windowData[rtWindowId];
                if (data.Reg.IsMainWindow)
                {
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                    Clyde.CheckGlError();
                }
                else
                {
                    var loaded = Clyde.RtToLoaded(data.RenderTexture!);
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, loaded.FramebufferHandle.Handle);
                }
            }

            public override void BeforeSharedWindowCreateUnbind()
            {
                Clyde.Windowing!.GLMakeContextCurrent(null);
            }

            private void BlitSecondaryWindows()
            {
                // Only got main window.
                if (Clyde.Windows.Count == 1)
                    return;

                if (!Clyde.HasGL.FenceSync && Clyde.Cfg.GetCVar(CVars.DisplayForceSyncWindows))
                {
                    GL.Finish();
                }

                if (Clyde.EffectiveThreadWindowBlit)
                {
                    foreach (var window in _windowData.Values)
                    {
                        if (window.Reg.IsMainWindow)
                            continue;

                        window.BlitDoneEvent!.Reset();
                        window.BlitStartEvent!.Set();
                        window.BlitDoneEvent.Wait();
                    }
                }
                else
                {
                    foreach (var window in _windowData.Values)
                    {
                        if (window.Reg.IsMainWindow)
                            continue;

                        Clyde.Windowing!.GLMakeContextCurrent(window.Reg);
                        BlitThreadDoSecondaryWindowBlit(window);
                    }

                    Clyde.Windowing!.GLMakeContextCurrent(Clyde.MainWindow!);
                }
            }

            private void BlitThreadDoSecondaryWindowBlit(WindowData window)
            {
                if (Clyde.HasGL.FenceSync)
                {
                    // 0xFFFFFFFFFFFFFFFFUL is GL_TIMEOUT_IGNORED
                    var rt = window.Reg.RenderTarget;
                    var sync = rt.LastGLSync;
                    GL.WaitSync(sync, WaitSyncFlags.None, unchecked((long) 0xFFFFFFFFFFFFFFFFUL));
                    Clyde.CheckGlError();
                }

                GL.Viewport(0, 0, window.Reg.FramebufferSize.X, window.Reg.FramebufferSize.Y);
                Clyde.CheckGlError();

                var tex = Clyde.TextureToGLHandle(window.RenderTexture!.Texture.TextureId);
                GL.BindTexture(TextureTarget.Texture2D, tex.Handle);
                Clyde.CheckGlError();

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
                Clyde.CheckGlError();

                window.BlitDoneEvent?.Set();
                Clyde.Windowing!.WindowSwapBuffers(window.Reg);
            }

            private unsafe void BlitThreadInit(WindowData reg)
            {
                Clyde.Windowing!.GLMakeContextCurrent(reg.Reg);
                Clyde.Windowing.GLSwapInterval(reg.Reg, 0);

                Clyde.SetupDebugCallback();

                if (!Clyde.HasGL.GLES)
                    GL.Enable(EnableCap.FramebufferSrgb);

                Span<float> winVertices = stackalloc[]
                {
                    -1.0f, 1.0f, 0.0f, 1.0f,
                    -1.0f, -1.0f, 0.0f, 0.0f,
                    1.0f, 1.0f, 1.0f, 1.0f,
                    1.0f, -1.0f, 1.0f, 0.0f
                };

                GL.GenBuffers(1, out uint windowVBO);
                GL.BindBuffer(BufferTarget.ArrayBuffer, windowVBO);

                var byteSpan = MemoryMarshal.AsBytes(winVertices);

                unsafe
                {
                    fixed (byte* ptr = byteSpan)
                    {
                        GL.BufferData(BufferTarget.ArrayBuffer, byteSpan.Length, (IntPtr) ptr, BufferUsageHint.StaticDraw);
                    }
                }

                Clyde.CheckGlError();

                var vao = Clyde.HasGL.GenVertexArray();
                Clyde.HasGL.BindVertexArray(vao);
                // Vertex Coords
                GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
                GL.EnableVertexAttribArray(0);
                // Texture Coords.
                GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
                GL.EnableVertexAttribArray(1);

                var shaderVtx = GL.CreateShader(ShaderType.VertexShader);
                var header = Clyde.HasGL.ShaderHeader;
                if (Clyde.HasGL.HasVaryingAttribute) {
                    GL.ShaderSource((int) shaderVtx, header + "attribute vec2 aPos; attribute vec2 tCoord; varying vec2 UV; void main() { UV = tCoord; gl_Position = vec4(aPos, 0.0, 1.0); }");
                } else {
                    GL.ShaderSource((int) shaderVtx, header + "in vec2 aPos; in vec2 tCoord; out vec2 UV; void main() { UV = tCoord; gl_Position = vec4(aPos, 0.0, 1.0); }");
                }
                GL.CompileShader(shaderVtx);
                var shaderFrg = GL.CreateShader(ShaderType.FragmentShader);
                if (Clyde.HasGL.HasVaryingAttribute) {
                    GL.ShaderSource((int) shaderFrg, header + "varying highp vec2 UV; uniform sampler2D tex; void main() { gl_FragColor = texture2D(tex, UV); }");
                } else {
                    GL.ShaderSource((int) shaderFrg, header + "out highp vec4 colourOutput; in highp vec2 UV; uniform sampler2D tex; void main() { colourOutput = texture(tex, UV); }");
                }
                GL.CompileShader(shaderFrg);

                var program = GL.CreateProgram();
                GL.AttachShader(program, shaderVtx);
                GL.AttachShader(program, shaderFrg);
                GL.LinkProgram(program);
                GL.UseProgram(program);

                GL.DeleteShader(shaderVtx);
                GL.DeleteShader(shaderFrg);

                var tex = Clyde.TextureToGLHandle(reg.RenderTexture!.Texture.TextureId);
                GL.BindTexture(TextureTarget.Texture2D, tex.Handle);
                Clyde.CheckGlError();

                var loc = GL.GetUniformLocation(program, "tex");
                GL.Uniform1(loc, 0);

                reg.WindowVAO = vao;
                reg.WindowVBO = windowVBO;
                reg.Program = program;
            }

            private void InitWindowBlitThread(WindowData reg)
            {
                if (Clyde.EffectiveThreadWindowBlit)
                {
                    reg.BlitStartEvent = new ManualResetEventSlim();
                    reg.BlitDoneEvent = new ManualResetEventSlim();
                    reg.BlitThread = new Thread(() => BlitThread(reg))
                    {
                        Name = $"WinBlitThread ID:{reg.Reg.Id}",
                        IsBackground = true
                    };

                    // System.Console.WriteLine("A");
                    reg.BlitThread.Start();
                    // Wait for thread to finish init.
                    reg.BlitDoneEvent.Wait();
                }
                else
                {
                    // Binds GL context.
                    BlitThreadInit(reg);

                    Clyde.Windowing!.GLMakeContextCurrent(Clyde.MainWindow!);
                }
            }

            private void BlitThread(WindowData reg)
            {
                BlitThreadInit(reg);

                reg.BlitDoneEvent!.Set();

                try
                {
                    while (true)
                    {
                        reg.BlitStartEvent!.Wait();
                        if (reg.Reg.IsDisposed)
                        {
                            BlitThreadCleanup(reg);
                            return;
                        }

                        reg.BlitStartEvent!.Reset();

                        // Do channel blit.
                        BlitThreadDoSecondaryWindowBlit(reg);
                    }
                }
                catch (AggregateException e)
                {
                    // ok channel closed, we exit.
                    e.Handle(ec => ec is ChannelClosedException);
                }
                finally
                {
                    BlitDataCleanup(reg);
                }
            }

            private static void BlitThreadCleanup(WindowData reg)
            {
                reg.BlitDoneEvent!.Dispose();
                reg.BlitStartEvent!.Dispose();
            }

            /// Assuming we're on the window's context, cleanup the blit data.
            private void BlitDataCleanup(WindowData reg)
            {
                GL.DeleteProgram(reg.Program);
                Clyde.HasGL.DeleteVertexArray(reg.WindowVAO);
                GL.DeleteBuffer(reg.WindowVBO);
            }

            private void CreateWindowRenderTexture(WindowData reg)
            {
                reg.RenderTexture?.Dispose();

                reg.RenderTexture = Clyde.CreateWindowRenderTarget(reg.Reg.FramebufferSize);
                // Necessary to correctly sync multi-context blitting.
                reg.RenderTexture.MakeGLFence = true;
            }

            private sealed class WindowData
            {
                public WindowReg Reg = default!;

                public RenderTexture? RenderTexture;
                // Used EXCLUSIVELY to run the two rendering commands to blit to the window.
                public Thread? BlitThread;
                public ManualResetEventSlim? BlitStartEvent;
                public ManualResetEventSlim? BlitDoneEvent;
                // Resources held/used for blitter thread.
                public uint WindowVAO = default;
                public uint WindowVBO = default;
                public int Program = default;
            }
        }
    }
}
