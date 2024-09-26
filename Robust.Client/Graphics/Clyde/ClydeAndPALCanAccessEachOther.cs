using System;
using System.IO;
using Robust.Shared.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Log;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Robust.Shared.Configuration;
using System.Threading.Tasks;
using Robust.Client.UserInterface;
using Robust.Client.Input;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using System.Numerics;
using System.Collections.Generic;

namespace Robust.Client.Graphics.Clyde;

// -- May this become one-way, one day. --

internal sealed partial class Clyde
{
    [Dependency] internal readonly PAL _pal = default!;

    private GLWrapper _hasGL => _pal._hasGL;
    private ISawmill _sawmillOgl => _pal._sawmillOgl;

    // interface proxies
    public bool HasPrimitiveRestart => _pal.HasPrimitiveRestart;

    public OwnedTexture LoadTextureFromImage<T>(Image<T> image, string? name = null, TextureLoadParameters? loadParams = null) where T : unmanaged, IPixel<T> => _pal.LoadTextureFromImage<T>(image, name, loadParams);
    public OwnedTexture CreateBlankTexture<T>(Vector2i size, string? name = null, in TextureLoadParameters? loadParams = null) where T : unmanaged, IPixel<T> => _pal.CreateBlankTexture<T>(size, name, loadParams);

    public GPUBuffer CreateBuffer(ReadOnlySpan<byte> span, GPUBuffer.Usage usage, string? name) => _pal.CreateBuffer(span, usage, name);

    public GPUVertexArrayObject CreateVAO(string? name = null) => _pal.CreateVAO(name);

    public IGPURenderState CreateRenderState() => _pal.CreateRenderState();

    IRenderTexture IClyde.CreateRenderTarget(Vector2i size, RenderTargetFormatParameters format,
        TextureSampleParameters? sampleParameters, string? name)
    {
        return _pal.CreateRenderTarget(size, format, sampleParameters, name);
    }

    Task<string> IClipboardManager.GetText()
    {
        return _pal._windowing?.ClipboardGetText(_pal._mainWindow!) ?? Task.FromResult("");
    }

    void IClipboardManager.SetText(string text)
    {
        _pal._windowing?.ClipboardSetText(_pal._mainWindow!, text);
    }

    bool IClydeInternal.SeparateWindowThread => _pal._threadWindowApi;
    string IClydeInternal.GetKeyName(Keyboard.Key key) => _pal.GetKeyName(key);
    uint? IClydeInternal.GetX11WindowId() => _pal.GetX11WindowId();
    void IClydeInternal.RunOnWindowThread(Action action) => _pal.RunOnWindowThread(action);
    ScreenCoordinates IClydeInternal.MouseScreenPosition => _pal.MouseScreenPosition;
    void IClydeInternal.ProcessInput(FrameEventArgs frameEventArgs) => _pal.ProcessInput(frameEventArgs);

    IClydeWindow IClyde.MainWindow => _pal.MainWindow;
    Vector2i IClyde.ScreenSize => _pal.ScreenSize;
    bool IClyde.IsFocused => _pal.IsFocused;
    IEnumerable<IClydeWindow> IClyde.AllWindows => _pal.AllWindows;
    Vector2 IClyde.DefaultWindowScale => _pal.DefaultWindowScale;
    void IClyde.SetWindowTitle(string s) => _pal.SetWindowTitle(s);
    void IClyde.SetWindowMonitor(IClydeMonitor s) => _pal.SetWindowMonitor(s);
    void IClyde.RequestWindowAttention() => _pal.RequestWindowAttention();
    public event Action<WindowResizedEventArgs> OnWindowResized = delegate {};
    public event Action<WindowFocusedEventArgs> OnWindowFocused = delegate {};
    public event Action<WindowContentScaleEventArgs> OnWindowScaleChanged = delegate {};

    ICursor IClyde.GetStandardCursor(StandardCursorShape shape) => _pal.GetStandardCursor(shape);
    ICursor IClyde.CreateCursor(Image<Rgba32> image, Vector2i hotSpot) => _pal.CreateCursor(image, hotSpot);
    void IClyde.SetCursor(ICursor? cursor) => _pal.SetCursor(cursor);
    IEnumerable<IClydeMonitor> IClyde.EnumerateMonitors() => _pal.EnumerateMonitors();
    IClydeWindow IClyde.CreateWindow(WindowCreateParameters parameters) => _pal.CreateWindow(parameters);
    void IClyde.TextInputSetRect(UIBox2i rect) => _pal.TextInputSetRect(rect);
    void IClyde.TextInputStart() => _pal.TextInputStart();
    void IClyde.TextInputStop() => _pal.TextInputStop();

    public event Action<TextEnteredEventArgs> TextEntered = delegate {};
    public event Action<TextEditingEventArgs> TextEditing = delegate {};
    public event Action<MouseMoveEventArgs> MouseMove = delegate {};
    public event Action<MouseEnterLeaveEventArgs> MouseEnterLeave = delegate {};
    public event Action<KeyEventArgs> KeyUp = delegate {};
    public event Action<KeyEventArgs> KeyDown = delegate {};
    public event Action<MouseWheelEventArgs> MouseWheel = delegate {};
    public event Action<WindowRequestClosedEventArgs> CloseWindow = delegate {};
    public event Action<WindowDestroyedEventArgs> DestroyWindow = delegate {};

    private void RegisterWindowingConnectors()
    {
        _pal.OnWindowResized += OnWindowResized.Invoke;
        _pal.OnWindowFocused += OnWindowFocused.Invoke;
        _pal.OnWindowScaleChanged += OnWindowScaleChanged.Invoke;
        // --
        _pal.TextEntered += TextEntered.Invoke;
        _pal.TextEditing += TextEditing.Invoke;
        _pal.MouseMove += MouseMove.Invoke;
        _pal.MouseEnterLeave += MouseEnterLeave.Invoke;
        _pal.KeyUp += KeyUp.Invoke;
        _pal.KeyDown += KeyDown.Invoke;
        _pal.MouseWheel += MouseWheel.Invoke;
        _pal.CloseWindow += CloseWindow.Invoke;
        _pal.DestroyWindow += DestroyWindow.Invoke;
    }
}

internal sealed partial class PAL
{
    [Dependency] internal readonly Clyde _clyde = default!;

    ClydeHandle IWindowingHost.AllocRid() => _clyde.AllocRid();
}
