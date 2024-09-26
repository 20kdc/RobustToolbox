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

    // interface proxies

    public OwnedTexture LoadTextureFromImage<T>(Image<T> image, string? name = null, TextureLoadParameters? loadParams = null) where T : unmanaged, IPixel<T> => _pal.LoadTextureFromImage<T>(image, name, loadParams);
    public OwnedTexture CreateBlankTexture<T>(Vector2i size, string? name = null, in TextureLoadParameters? loadParams = null) where T : unmanaged, IPixel<T> => _pal.CreateBlankTexture<T>(size, name, loadParams);

    IRenderTexture IClyde.CreateRenderTarget(Vector2i size, RenderTargetFormatParameters format,
        TextureSampleParameters? sampleParameters, string? name)
    {
        return _pal.CreateRenderTarget(size, format, sampleParameters, name);
    }

    IClydeWindow IClyde.MainWindow => _pal.MainWindow;
    Vector2i IClyde.ScreenSize => _pal.ScreenSize;
    bool IClyde.IsFocused => _pal.IsFocused;
    IEnumerable<IClydeWindow> IClyde.AllWindows => _pal.AllWindows;
    Vector2 IClyde.DefaultWindowScale => _pal.DefaultWindowScale;
    void IClyde.SetWindowTitle(string s) => _pal.SetWindowTitle(s);
    void IClyde.SetWindowMonitor(IClydeMonitor s) => _pal.SetWindowMonitor(s);
    void IClyde.RequestWindowAttention() => _pal.RequestWindowAttention();
    public event Action<WindowResizedEventArgs>? OnWindowResized;
    public event Action<WindowFocusedEventArgs>? OnWindowFocused;
    public event Action<WindowContentScaleEventArgs>? OnWindowScaleChanged;

    ICursor IClyde.GetStandardCursor(StandardCursorShape shape) => _pal.GetStandardCursor(shape);
    ICursor IClyde.CreateCursor(Image<Rgba32> image, Vector2i hotSpot) => _pal.CreateCursor(image, hotSpot);
    void IClyde.SetCursor(ICursor? cursor) => _pal.SetCursor(cursor);
    IEnumerable<IClydeMonitor> IClyde.EnumerateMonitors() => _pal.EnumerateMonitors();
    IClydeWindow IClyde.CreateWindow(WindowCreateParameters parameters) => _pal.CreateWindow(parameters);
    void IClyde.TextInputSetRect(UIBox2i rect) => _pal.TextInputSetRect(rect);
    void IClyde.TextInputStart() => _pal.TextInputStart();
    void IClyde.TextInputStop() => _pal.TextInputStop();

    private void RegisterWindowingConnectors()
    {
        _pal.OnWindowResized += (e) => OnWindowResized?.Invoke(e);
        _pal.OnWindowFocused += (e) => OnWindowFocused?.Invoke(e);
        _pal.OnWindowScaleChanged += (e) => OnWindowScaleChanged?.Invoke(e);
    }
}
