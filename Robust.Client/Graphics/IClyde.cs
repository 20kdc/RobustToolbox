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

namespace Robust.Client.Graphics
{
    public delegate void CopyPixelsDelegate<T>(Image<T> pixels) where T : unmanaged, IPixel<T>;

    public interface IClyde
    {
        /// <summary>
        ///     Make a screenshot of the game, next render frame.
        /// </summary>
        /// <param name="type">What kind of screenshot to take</param>
        /// <param name="callback">The callback to run when the screenshot has been made.</param>
        /// <param name="subRegion">
        ///     The subregion of the framebuffer to copy.
        ///     If null, the whole framebuffer is copied.
        /// </param>
        /// <seealso cref="ScreenshotAsync"/>
        /// <seealso cref="IRenderTarget.CopyPixelsToMemory{T}"/>
        void Screenshot(ScreenshotType type, CopyPixelsDelegate<Rgb24> callback, UIBox2i? subRegion = null);

        /// <summary>
        ///     Async version of <see cref="Screenshot"/>.
        /// </summary>
        Task<Image<Rgb24>> ScreenshotAsync(ScreenshotType type, UIBox2i? subRegion = null)
        {
            var tcs = new TaskCompletionSource<Image<Rgb24>>();

            Screenshot(type, image => tcs.SetResult(image));

            return tcs.Task;
        }

        IClydeViewport CreateViewport(Vector2i size, string? name = null)
        {
            return CreateViewport(size, default, name);
        }

        IClydeViewport CreateViewport(Vector2i size, TextureSampleParameters? sampleParameters, string? name = null);

        // -- Moved to IWindowing --

        [Obsolete("Moved to IWindowing")]
        IClydeWindow MainWindow { get; }

        [Obsolete("Moved to IWindowing")]
        IRenderTarget MainWindowRenderTarget => MainWindow.RenderTarget;

        [Obsolete("Moved to IWindowing")]
        Vector2i ScreenSize { get; }

        [Obsolete("Moved to IWindowing")]
        bool IsFocused { get; }

        [Obsolete("Moved to IWindowing")]
        IEnumerable<IClydeWindow> AllWindows { get; }

        [Obsolete("Moved to IWindowing")]
        Vector2 DefaultWindowScale { get; }

        [Obsolete("Moved to IWindowing")]
        void SetWindowTitle(string title);
        [Obsolete("Moved to IWindowing")]
        void SetWindowMonitor(IClydeMonitor monitor);

        [Obsolete("Moved to IWindowing")]
        void RequestWindowAttention();

        event Action<WindowResizedEventArgs> OnWindowResized;

        [Obsolete("Moved to IWindowing")]
        event Action<WindowFocusedEventArgs> OnWindowFocused;

        [Obsolete("Moved to IWindowing")]
        event Action<WindowContentScaleEventArgs> OnWindowScaleChanged;

        [Obsolete("Moved to IWindowing")]
        ICursor GetStandardCursor(StandardCursorShape shape);

        [Obsolete("Moved to IWindowing")]
        ICursor CreateCursor(Image<Rgba32> image, Vector2i hotSpot);

        [Obsolete("Moved to IWindowing")]
        void SetCursor(ICursor? cursor);

        [Obsolete("Moved to IWindowing")]
        IEnumerable<IClydeMonitor> EnumerateMonitors();

        [Obsolete("Moved to IWindowing")]
        IClydeWindow CreateWindow(WindowCreateParameters parameters);

        [Obsolete("Moved to IWindowing")]
        void TextInputSetRect(UIBox2i rect);

        [Obsolete("Moved to IWindowing")]
        void TextInputStart();

        [Obsolete("Moved to IWindowing")]
        void TextInputStop();

        // -- Moved to IGPUAbstraction --

        [Obsolete("Moved to IGPUAbstraction")]
        OwnedTexture LoadTextureFromPNGStream(Stream stream, string? name = null,
            TextureLoadParameters? loadParams = null)
        {
            // Load using Rgba32.
            using var image = Image.Load<Rgba32>(stream);

            return LoadTextureFromImage(image, name, loadParams);
        }

        [Obsolete("Moved to IGPUAbstraction")]
        OwnedTexture LoadTextureFromImage<T>(Image<T> image, string? name = null,
            TextureLoadParameters? loadParams = null) where T : unmanaged, IPixel<T>;

        [Obsolete("Moved to IGPUAbstraction")]
        OwnedTexture CreateBlankTexture<T>(
            Vector2i size,
            string? name = null,
            in TextureLoadParameters? loadParams = null)
            where T : unmanaged, IPixel<T>;

        [Obsolete("Moved to IGPUAbstraction")]
        IRenderTexture CreateRenderTarget(Vector2i size, RenderTargetFormatParameters format,
            TextureSampleParameters? sampleParameters = null, string? name = null);
    }
}
