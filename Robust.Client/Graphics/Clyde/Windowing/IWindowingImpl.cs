using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Robust.Client.Input;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Robust.Client.Graphics.Clyde;

internal interface IWindowingImpl
{
    // Lifecycle stuff
    bool Init();
    void Shutdown();

    // Window loop
    void EnterWindowLoop();
    void PollEvents();
    void TerminateWindowLoop();

    // Event pump
    void ProcessEvents(bool single=false);
    void FlushDispose();

    // Cursor
    ICursor CursorGetStandard(StandardCursorShape shape);
    ICursor CursorCreate(Image<Rgba32> image, Vector2i hotSpot);
    void CursorSet(WindowReg window, ICursor? cursor);

    // Window API.
    (WindowReg?, string? error) WindowCreate(
        GLContextSpec? spec,
        WindowCreateParameters parameters,
        WindowReg? share,
        WindowReg? owner);

    void WindowDestroy(WindowReg reg);
    void WindowSetTitle(WindowReg window, string title);
    void WindowSetMonitor(WindowReg window, IClydeMonitor monitor);
    void WindowSetVisible(WindowReg window, bool visible);
    void WindowSetMode(WindowReg window, WindowMode mode);
    void WindowRequestAttention(WindowReg window);
    void WindowSwapBuffers(WindowReg window);
    uint? WindowGetX11Id(WindowReg window);
    nint? WindowGetX11Display(WindowReg window);
    nint? WindowGetWin32Window(WindowReg window);

    // Keyboard
    string? KeyGetName(Keyboard.Key key);

    // Clipboard
    Task<string> ClipboardGetText(WindowReg mainWindow);
    void ClipboardSetText(WindowReg mainWindow, string text);

    // OpenGL-related stuff.
    // Note: you should probably go through GLContextBase instead, which calls these functions.
    void GLMakeContextCurrent(WindowReg? reg);
    void GLSwapInterval(WindowReg reg, int interval);
    unsafe void* GLGetProcAddress(string procName);

    // Misc
    void RunOnWindowThread(Action a);

    WindowReg? CurrentHoveredWindow { get; }

    // IME
    void TextInputSetRect(UIBox2i rect);
    void TextInputStart();
    void TextInputStop();
    string GetDescription();
}

/// Interface that windowing implementations expect to receive. Used to control surface area expected of Clyde.
/// Also used by GLContext due to their heavy interrelation.
internal interface IWindowingHost {
    IWindowingImpl? Windowing { get; }
    WindowReg? MainWindow { get; }
    List<WindowReg> Windows { get; }
    bool ThreadWindowApi { get; }
    bool EffectiveThreadWindowBlit { get; }
    Dictionary<int, MonitorHandle> MonitorHandles { get; }
    IConfigurationManager Cfg { get; }
    ILogManager LogManager { get; }
    GLWrapper HasGL { get; }

    ClydeHandle AllocRid();

    IEnumerable<Image<Rgba32>> LoadWindowIcons();

    void DoDestroyWindow(WindowReg windowReg);
    void SetPrimaryMonitorId(int id);
    void UpdateVSync();

    void SendKeyUp(KeyEventArgs ev);
    void SendKeyDown(KeyEventArgs ev);
    void SendScroll(MouseWheelEventArgs ev);
    void SendCloseWindow(WindowReg windowReg, WindowRequestClosedEventArgs ev);
    void SendWindowResized(WindowReg reg, Vector2i oldSize);
    void SendWindowContentScaleChanged(WindowContentScaleEventArgs ev);
    void SendWindowFocus(WindowFocusedEventArgs ev);
    void SendText(TextEnteredEventArgs ev);
    void SendTextEditing(TextEditingEventArgs ev);
    void SendMouseMove(MouseMoveEventArgs ev);
    void SendMouseEnterLeave(MouseEnterLeaveEventArgs ev);
    void SendInputModeChanged();

    void SetupDebugCallback();
    void EnableRenderWindowFlipY(PAL.RenderWindow rw);
    PAL.LoadedRenderTarget RtToLoaded(PAL.RenderTargetBase rt);
    PAL.RenderTexture CreateWindowRenderTarget(Vector2i size);
}

internal abstract class WindowReg
{
    public bool IsDisposed;

    public WindowId Id;
    public Vector2 WindowScale;
    public Vector2 PixelRatio;
    public Vector2i FramebufferSize;
    public Vector2i WindowSize;
    public Vector2i PrevWindowSize;
    public Vector2i WindowPos;
    public Vector2i PrevWindowPos;
    public Vector2 LastMousePos;
    public bool IsFocused;
    public bool IsMinimized;
    public string Title = "";
    public bool IsVisible;
    public IClydeWindow? Owner;

    public bool DisposeOnClose;

    public bool IsMainWindow;
    public WindowHandle Handle = default!;
    public PAL.RenderWindow RenderTarget = default!;
    public Action<WindowRequestClosedEventArgs>? RequestClosed;
    public Action<WindowDestroyedEventArgs>? Closed;
    public Action<WindowResizedEventArgs>? Resized;
}

internal sealed class WindowHandle : IClydeWindowInternal
{
    // So funny story
    // When this class was a record, the C# compiler on .NET 5 stack overflowed
    // while compiling the Closed event.
    // VERY funny.

    private readonly IWindowingHost _clyde;
    public readonly WindowReg Reg;

    public bool IsDisposed => Reg.IsDisposed;
    public WindowId Id => Reg.Id;

    public WindowHandle(IWindowingHost clyde, WindowReg reg)
    {
        _clyde = clyde;
        Reg = reg;
    }

    public void Dispose()
    {
        _clyde.DoDestroyWindow(Reg);
    }

    public Vector2i Size => Reg.FramebufferSize;

    public IRenderTarget RenderTarget => Reg.RenderTarget;

    public string Title
    {
        get => Reg.Title;
        set => _clyde.Windowing!.WindowSetTitle(Reg, value);
    }

    public bool IsFocused => Reg.IsFocused;
    public bool IsMinimized => Reg.IsMinimized;

    public bool IsVisible
    {
        get => Reg.IsVisible;
        set => _clyde.Windowing!.WindowSetVisible(Reg, value);
    }

    public Vector2 ContentScale => Reg.WindowScale;

    public bool DisposeOnClose
    {
        get => Reg.DisposeOnClose;
        set => Reg.DisposeOnClose = value;
    }

    public event Action<WindowRequestClosedEventArgs> RequestClosed
    {
        add => Reg.RequestClosed += value;
        remove => Reg.RequestClosed -= value;
    }

    public event Action<WindowDestroyedEventArgs>? Destroyed
    {
        add => Reg.Closed += value;
        remove => Reg.Closed -= value;
    }

    public event Action<WindowResizedEventArgs>? Resized
    {
        add => Reg.Resized += value;
        remove => Reg.Resized -= value;
    }

    public nint? WindowsHWnd => _clyde.Windowing!.WindowGetWin32Window(Reg);
}

internal abstract class MonitorReg
{
    public MonitorHandle Handle = default!;
}

internal sealed class MonitorHandle : IClydeMonitor
{
    public MonitorHandle(int id, string name, Vector2i size, int refreshRate, VideoMode[] videoModes)
    {
        Id = id;
        Name = name;
        Size = size;
        RefreshRate = refreshRate;
        VideoModes = videoModes;
    }

    public int Id { get; }
    public string Name { get; }
    public Vector2i Size { get; }
    public int RefreshRate { get; }
    public IEnumerable<VideoMode> VideoModes { get; }
}

internal struct GLContextSpec
{
    public int Major;
    public int Minor;
    public GLContextProfile Profile;
    public GLContextCreationApi CreationApi;
    // Used by GLContextWindow to figure out which GL version managed to initialize.
    public RendererOpenGLVersion OpenGLVersion;
}

internal enum GLContextProfile
{
    Compatibility,
    Core,
    Es
}

internal enum GLContextCreationApi
{
    Native,
    Egl,
}

internal enum RendererOpenGLVersion : byte
{
    Auto = default,
    GL33 = 1,
    GL31 = 2,
    GLES3 = 3,
    GLES2 = 4,
}

internal static class RendererOpenGLVersionUtils {
    internal static bool IsGLES(RendererOpenGLVersion version) => version is RendererOpenGLVersion.GLES2 or RendererOpenGLVersion.GLES3;

    internal static bool IsCore(RendererOpenGLVersion version) => version is RendererOpenGLVersion.GL33;
}
