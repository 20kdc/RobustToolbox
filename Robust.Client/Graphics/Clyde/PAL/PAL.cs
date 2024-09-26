using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.Log;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using System;
using Robust.Shared;

namespace Robust.Client.Graphics.Clyde;

/// <summary>'Sanity layer' over GL, windowing, etc.</summary>
internal sealed partial class PAL : IGPUAbstraction, IWindowingHost, IWindowing, IPALInternal
{
    internal Thread? _gameThread;
    internal ISawmill _sawmillOgl = default!;
    internal ISawmill _sawmillWin = default!;

    private long _nextRid = 1;

    public bool HasPrimitiveRestart => _hasGL.PrimitiveRestart;
    public bool HasSrgb => _hasGL.Srgb;
    public bool HasFloatFramebuffers => _hasGL.FloatFramebuffers;
    public bool HasUniformBuffers => _hasGL.UniformBuffers;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsMainThread()
    {
        return Thread.CurrentThread == _gameThread;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void CheckGlError([CallerFilePath] string? path = null, [CallerLineNumber] int line = default)
    {
        _hasGL.CheckGlError(path, line);
    }

    ClydeHandle IWindowingHost.AllocRid()
    {
        return new(_nextRid++);
    }

    Task<string> IClipboardManager.GetText()
    {
        return _windowing?.ClipboardGetText(_mainWindow!) ?? Task.FromResult("");
    }

    void IClipboardManager.SetText(string text)
    {
        _windowing?.ClipboardSetText(_mainWindow!, text);
    }

    public bool InitializePreWindowing()
    {
        _sawmillOgl = _logManager.GetSawmill("clyde.ogl");
        _sawmillWin = _logManager.GetSawmill("clyde.win");

        _cfg.OnValueChanged(CVars.DisplayVSync, VSyncChanged, true);
        _cfg.OnValueChanged(CVars.DisplayWindowMode, WindowModeChanged, true);
        // I can't be bothered to tear down and set these threads up in a cvar change handler.

        // Windows and Linux can be trusted to not explode with threaded windowing,
        // macOS cannot.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            _cfg.OverrideDefault(CVars.DisplayThreadWindowApi, true);

        _threadWindowBlit = _cfg.GetCVar(CVars.DisplayThreadWindowBlit);
        _threadWindowApi = _cfg.GetCVar(CVars.DisplayThreadWindowApi);

        InitKeys();

        return InitWindowing();
    }

    public bool InitializePostWindowing()
    {
        _gameThread = Thread.CurrentThread;

        InitGLContextManager();

        return InitMainWindowAndRenderer();
    }

    public void EnterWindowLoop()
    {
        _windowing!.EnterWindowLoop();
    }

    public string WindowingDescription => _windowing!.GetDescription();

    public void PollEventsAndCleanupResources()
    {
        if (!_threadWindowApi)
        {
            _windowing!.PollEvents();
        }

        FlushDispose();
    }

    public void TerminateWindowLoop()
    {
        _windowing!.TerminateWindowLoop();
    }

    public void Shutdown()
    {
        _glContext?.Shutdown();
        ShutdownWindowing();
    }
}
