﻿using System;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Log;

namespace Robust.Client.Graphics.Clyde
{
    internal sealed partial class Clyde
    {
        internal PAL.GLContextBase? _glContext;

        IConfigurationManager IWindowingHost.Cfg => _cfg;
        ILogManager IWindowingHost.LogManager => _logManager;
        GLWrapper IWindowingHost.HasGL => _hasGL;

        private void InitGLContextManager()
        {
            // Advanced GL contexts currently disabled due to lack of testing etc.
            if (OperatingSystem.IsWindows() && _cfg.GetCVar(CVars.DisplayAngle))
            {
                if (_cfg.GetCVar(CVars.DisplayAngleCustomSwapChain))
                {
                    _sawmillOgl.Debug("Trying custom swap chain ANGLE.");
                    var ctxAngle = new PAL.GLContextAngle(this);

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
                    var ctxEgl = new PAL.GLContextEgl(this);
                    ctxEgl.InitializePublic();
                    _glContext = ctxEgl;
                    return;
                }
            }

            /*
            if (OperatingSystem.IsLinux() && _cfg.GetCVar(CVars.DisplayEgl))
            {
                _sawmillOgl.Debug("Trying EGL");
                var ctxEgl = new PAL.GLContextEgl(this);
                ctxEgl.InitializePublic();
                _glContext = ctxEgl;
                return;
            }
            */

            _glContext = new PAL.GLContextWindow(this);
        }

        void IWindowingHost.SetupDebugCallback()
        {
            SetupDebugCallback();
        }
    }
}
