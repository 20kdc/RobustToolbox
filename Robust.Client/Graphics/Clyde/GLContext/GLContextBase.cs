using System;
using OpenToolkit;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Robust.Client.Graphics.Clyde
{
    internal partial class Clyde
    {
        /// <summary>
        /// Manages OpenGL contexts for the windowing system.
        /// </summary>
        private abstract class GLContextBase
        {
            protected readonly IWindowingHost Clyde;

            public IBindingsContext BindingsContext { get; }

            public GLContextBase(IWindowingHost clyde)
            {
                Clyde = clyde;
                BindingsContext = new BindingsContextImpl(this);
            }

            public virtual bool EarlyContextInit => false;

            public abstract GLContextSpec? SpecWithOpenGLVersion(RendererOpenGLVersion version);

            public abstract void UpdateVSync(bool vSync);
            public abstract void WindowCreated(GLContextSpec? spec, WindowReg reg);
            public abstract void WindowDestroyed(WindowReg reg);

            public abstract void Shutdown();

            public abstract GLContextSpec[] SpecsToTry { get; }
            public abstract bool RequireWindowGL { get;  }

            /// If this context manager has broken sRGB on the given renderer version.
            public abstract bool HasBrokenWindowSrgb(RendererOpenGLVersion version);

            protected static GLContextSpec GetVersionSpec(RendererOpenGLVersion version)
            {
                var spec = new GLContextSpec { OpenGLVersion = version };

                switch (version)
                {
                    case RendererOpenGLVersion.GL33:
                        spec.Major = 3;
                        spec.Minor = 3;
                        spec.Profile = GLContextProfile.Core;
                        spec.CreationApi = GLContextCreationApi.Native;
                        break;

                    case RendererOpenGLVersion.GL31:
                        spec.Major = 3;
                        spec.Minor = 1;
                        spec.Profile = GLContextProfile.Compatibility;
                        spec.CreationApi = GLContextCreationApi.Native;
                        break;

                    case RendererOpenGLVersion.GLES3:
                        spec.Major = 3;
                        spec.Minor = 0;
                        spec.Profile = GLContextProfile.Es;
                        // Initializing ES on Windows EGL so that we can use ANGLE.
                        spec.CreationApi = OperatingSystem.IsWindows()
                            ? GLContextCreationApi.Egl
                            : GLContextCreationApi.Native;
                        break;

                    case RendererOpenGLVersion.GLES2:
                        spec.Major = 2;
                        spec.Minor = 0;
                        spec.Profile = GLContextProfile.Es;
                        // Initializing ES on Windows EGL so that we can use ANGLE.
                        spec.CreationApi = OperatingSystem.IsWindows()
                            ? GLContextCreationApi.Egl
                            : GLContextCreationApi.Native;
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }

                return spec;
            }

            public abstract void SwapAllBuffers();
            public abstract void WindowResized(WindowReg reg, Vector2i oldSize);

            public abstract unsafe void* GetProcAddress(string name);

            public abstract void BindWindowRenderTarget(WindowId rtWindowId);

            public virtual void BeforeSharedWindowCreateUnbind()
            {
            }

            private sealed class BindingsContextImpl : IBindingsContext
            {
                private readonly GLContextBase _context;

                public BindingsContextImpl(GLContextBase context)
                {
                    _context = context;
                }

                public unsafe IntPtr GetProcAddress(string procName)
                {
                    return (nint)_context.GetProcAddress(procName);
                }
            }
        }
    }
}
