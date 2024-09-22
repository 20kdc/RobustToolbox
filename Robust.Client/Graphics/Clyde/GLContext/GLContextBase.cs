using System;
using System.Globalization;
using OpenToolkit;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Shared;
using Robust.Shared.Log;
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
            protected readonly ISawmill Sawmill;

            public abstract string SawmillCategory { get; }

            public IBindingsContext BindingsContext { get; }

            public ClydeGLFeatures? GLFeatures { get; protected set; }

            /// <summary>Defaults to auto, but then changes to match whatever the main window was created with.</summary>
            private RendererOpenGLVersion _glVersionToUse;

            /// <summary>Recommended spec to create windows with, if any. Importantly, once the main window is created, this spec will try to match it.</summary>
            public GLContextSpec? SpecToCreateWindowsWith => SpecWithOpenGLVersion(_glVersionToUse);

            public GLContextBase(IWindowingHost clyde)
            {
                Clyde = clyde;
                Sawmill = clyde.LogManager.GetSawmill(SawmillCategory);
                BindingsContext = new BindingsContextImpl(this);
            }

            /// <summary>Should only be called for the main window. Sets GLFeatures. Be sure to make the context current first!</summary>
            protected void InitOpenGL(RendererOpenGLVersion glVersion)
            {
                _glVersionToUse = glVersion;

                bool isGLES = OpenGLVersionIsGLES(glVersion);
                bool isGLES2 = glVersion is RendererOpenGLVersion.GLES2;
                bool isCore = OpenGLVersionIsCore(glVersion);

                // Initialize bindings...
                GL.LoadBindings(BindingsContext);

                if (isGLES)
                {
                    // On GLES we use some OES and KHR functions so make sure to initialize them.
                    OpenToolkit.Graphics.ES20.GL.LoadBindings(BindingsContext);
                }

                var hasBrokenWindowSrgb = HasBrokenWindowSrgb(glVersion);
                GLFeatures = new ClydeGLFeatures(isGLES, isGLES2, isCore, hasBrokenWindowSrgb, Clyde.LogManager, Clyde.Cfg);
            }

            protected abstract GLContextSpec? SpecWithOpenGLVersion(RendererOpenGLVersion version);

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
