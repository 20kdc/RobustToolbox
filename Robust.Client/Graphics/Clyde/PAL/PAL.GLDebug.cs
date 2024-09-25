using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenToolkit.Graphics.OpenGL4;

namespace Robust.Client.Graphics.Clyde;

internal partial class PAL
{
    [Conditional("DEBUG")]
    internal unsafe void SetupDebugCallback()
    {
        if (!_hasGL.KhrDebug)
        {
            _sawmillOgl.Debug("KHR_debug not present, OpenGL debug logging not enabled.");
            return;
        }

        GL.Enable(EnableCap.DebugOutput);
        GL.Enable(EnableCap.DebugOutputSynchronous);

        _debugMessageCallbackInstance ??= DebugMessageCallback;

        // OpenTK seemed to have trouble marshalling the delegate so do it manually.

        var procName = _hasGL.KhrDebugESExtension ? "glDebugMessageCallbackKHR" : "glDebugMessageCallback";
        var glDebugMessageCallback = (delegate* unmanaged[Stdcall] <nint, nint, void>) LoadGLProc(procName);
        var funcPtr = Marshal.GetFunctionPointerForDelegate(_debugMessageCallbackInstance);
        glDebugMessageCallback(funcPtr, new IntPtr(0x3005));
    }

    private void DebugMessageCallback(DebugSource source, DebugType type, int id, DebugSeverity severity,
        int length, IntPtr message, IntPtr userParam)
    {
        var contents = $"{source}: " + Marshal.PtrToStringAnsi(message, length);

        var category = "ogl.debug";
        switch (type)
        {
            case DebugType.DebugTypePerformance:
                category += ".performance";
                break;
            case DebugType.DebugTypeOther:
                category += ".other";
                break;
            case DebugType.DebugTypeError:
                category += ".error";
                break;
            case DebugType.DebugTypeDeprecatedBehavior:
                category += ".deprecated";
                break;
            case DebugType.DebugTypeUndefinedBehavior:
                category += ".ub";
                break;
            case DebugType.DebugTypePortability:
                category += ".portability";
                break;
            case DebugType.DebugTypeMarker:
            case DebugType.DebugTypePushGroup:
            case DebugType.DebugTypePopGroup:
                // These are inserted by our own code so I imagine they're not necessary to log?
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        var sawmill = _logManager.GetSawmill(category);

        switch (severity)
        {
            case DebugSeverity.DontCare:
                sawmill.Info(contents);
                break;
            case DebugSeverity.DebugSeverityNotification:
                sawmill.Info(contents);
                break;
            case DebugSeverity.DebugSeverityHigh:
                sawmill.Error(contents);
                break;
            case DebugSeverity.DebugSeverityMedium:
                sawmill.Error(contents);
                break;
            case DebugSeverity.DebugSeverityLow:
                sawmill.Warning(contents);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(severity), severity, null);
        }
    }

    private static DebugProc? _debugMessageCallbackInstance;

    internal PopDebugGroup DebugGroup(string group)
    {
        PushDebugGroupMaybe(group);
        return new PopDebugGroup(this);
    }

    [Conditional("DEBUG")]
    private void PushDebugGroupMaybe(string group)
    {
        // ANGLE spams console log messages when using debug groups, so let's only use them if we're debugging GL.
        if (!_hasGL.KhrDebug || !_hasGL.DebuggerPresent)
            return;

        if (_hasGL.KhrDebugESExtension)
        {
            GL.Khr.PushDebugGroup((DebugSource) DebugSourceExternal.DebugSourceApplication, 0, group.Length, group);
        }
        else
        {
            GL.PushDebugGroup(DebugSourceExternal.DebugSourceApplication, 0, group.Length, group);
        }
    }

    [Conditional("DEBUG")]
    private void PopDebugGroupMaybe()
    {
        if (!_hasGL.KhrDebug || !_hasGL.DebuggerPresent)
            return;

        if (_hasGL.KhrDebugESExtension)
        {
            GL.Khr.PopDebugGroup();
        }
        else
        {
            GL.PopDebugGroup();
        }
    }

    private nint LoadGLProc(string name)
    {
        var proc = _glContext!.BindingsContext.GetProcAddress(name);
        if (proc == IntPtr.Zero || proc == new IntPtr(1) || proc == new IntPtr(2))
        {
            throw new InvalidOperationException($"Unable to load GL function '{name}'!");
        }

        return proc;
    }

    internal struct PopDebugGroup : IDisposable
    {
        private readonly PAL _pal;

        public PopDebugGroup(PAL clyde)
        {
            _pal = clyde;
        }

        public void Dispose()
        {
            _pal.PopDebugGroupMaybe();
        }
    }
}
