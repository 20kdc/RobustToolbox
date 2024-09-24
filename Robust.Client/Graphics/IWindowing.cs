using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Robust.Shared.Graphics;
using Robust.Shared.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Robust.Client.Graphics;

/// <summary>
/// This contains the "windowing stuff".
/// </summary>
public interface IWindowing
{
    IClydeWindow MainWindow { get; }
    IRenderTarget MainWindowRenderTarget => MainWindow.RenderTarget;

    Vector2i ScreenSize { get; }

    bool IsFocused { get; }

    IEnumerable<IClydeWindow> AllWindows { get; }

    /// <summary>
    ///     The default scale ratio for window contents, given to us by the OS.
    /// </summary>
    Vector2 DefaultWindowScale { get; }

    void SetWindowTitle(string title);
    void SetWindowMonitor(IClydeMonitor monitor);

    /// <summary>
    ///     This is the magic method to make the game window ping you in the task bar.
    /// </summary>
    void RequestWindowAttention();

    event Action<WindowResizedEventArgs> OnWindowResized;

    event Action<WindowFocusedEventArgs> OnWindowFocused;

    event Action<WindowContentScaleEventArgs> OnWindowScaleChanged;

    // Cursor API.
    /// <summary>
    ///     Gets a cursor object representing standard cursors that match the OS styling.
    /// </summary>
    /// <remarks>
    ///     Cursor objects returned from this method are cached and you cannot not dispose them.
    /// </remarks>
    ICursor GetStandardCursor(StandardCursorShape shape);

    /// <summary>
    ///     Create a custom cursor object from an image.
    /// </summary>
    /// <param name="image"></param>
    /// <param name="hotSpot"></param>
    /// <returns></returns>
    ICursor CreateCursor(Image<Rgba32> image, Vector2i hotSpot);

    /// <summary>
    ///     Sets the active cursor for the primary window.
    /// </summary>
    /// <param name="cursor">The cursor to set to, or <see langword="null"/> to reset to the default cursor.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the cursor object passed has been disposed.</exception>
    void SetCursor(ICursor? cursor);

    IEnumerable<IClydeMonitor> EnumerateMonitors();

    IClydeWindow CreateWindow(WindowCreateParameters parameters);

    /// <summary>
    /// Set the active text input area in window pixel coordinates.
    /// </summary>
    /// <param name="rect">
    /// This information is used by the OS to position overlays like IMEs or emoji pickers etc.
    /// </param>
    void TextInputSetRect(UIBox2i rect);

    /// <summary>
    /// Indicate that the game should start accepting text input on the currently focused window.
    /// </summary>
    /// <remarks>
    /// On some platforms, this will cause an on-screen keyboard to appear.
    /// The game will also start accepting IME input if configured by the user.
    /// </remarks>
    /// <seealso cref="TextInputStop"/>
    void TextInputStart();

    /// <summary>
    /// Stop text input, opposite of <see cref="TextInputStart"/>.
    /// </summary>
    /// <seealso cref="TextInputStart"/>
    void TextInputStop();
}
