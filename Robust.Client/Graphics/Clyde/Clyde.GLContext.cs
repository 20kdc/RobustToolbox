using System;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Log;

namespace Robust.Client.Graphics.Clyde
{
    internal sealed partial class Clyde
    {
        private GLContextBase? _glContext;

        IConfigurationManager IWindowingHost.Cfg => _cfg;
        ILogManager IWindowingHost.LogManager => _logManager;
        ClydeGLFeatures IWindowingHost.HasGL => _hasGL;

        private void InitGLContextManager()
        {
            // Advanced GL contexts currently disabled due to lack of testing etc.
            if (OperatingSystem.IsWindows() && _cfg.GetCVar(CVars.DisplayAngle))
            {
                if (_cfg.GetCVar(CVars.DisplayAngleCustomSwapChain))
                {
                    _sawmillOgl.Debug("Trying custom swap chain ANGLE.");
                    var ctxAngle = new GLContextAngle(this);

                    if (ctxAngle.TryInitialize())
                    {
                        _sawmillOgl.Debug("Successfully initialized custom ANGLE");
                        _glContext = ctxAngle;
                        return;
                    }
                }

                if (_cfg.GetCVar(CVars.DisplayEgl))
                {
                    _sawmillOgl.Debug("Trying EGL");
                    var ctxEgl = new GLContextEgl(this);
                    ctxEgl.InitializePublic();
                    _glContext = ctxEgl;
                    return;
                }
            }

            /*
            if (OperatingSystem.IsLinux() && _cfg.GetCVar(CVars.DisplayEgl))
            {
                _sawmillOgl.Debug("Trying EGL");
                var ctxEgl = new GLContextEgl(this);
                ctxEgl.InitializePublic();
                _glContext = ctxEgl;
                return;
            }
            */

            _glContext = new GLContextWindow(this);
        }

        void IWindowingHost.CheckGlError()
        {
            CheckGlError();
        }

        void IWindowingHost.SetupDebugCallback()
        {
            SetupDebugCallback();
        }

        void IWindowingHost.EnableRenderWindowFlipY(RenderWindow rw)
        {
            var rt = RtToLoaded(rw);
            rt.FlipY = true;
        }

        GLHandle IWindowingHost.TextureToGLHandle(ClydeHandle texture) => _loadedTextures[texture].OpenGLObject;
        private struct GLContextSpec
        {
            public int Major;
            public int Minor;
            public GLContextProfile Profile;
            public GLContextCreationApi CreationApi;
            // Used by GLContextWindow to figure out which GL version managed to initialize.
            public RendererOpenGLVersion OpenGLVersion;
        }

        private enum GLContextProfile
        {
            Compatibility,
            Core,
            Es
        }

        private enum GLContextCreationApi
        {
            Native,
            Egl,
        }
    }
}
