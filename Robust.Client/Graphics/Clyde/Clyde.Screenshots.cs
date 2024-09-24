using System;
using System.Collections.Generic;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Client.Utility;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using PF = OpenToolkit.Graphics.OpenGL4.PixelFormat;
using PT = OpenToolkit.Graphics.OpenGL4.PixelType;

namespace Robust.Client.Graphics.Clyde
{
    // Contains primary screenshot and pixel-copying logic.

    internal sealed partial class Clyde
    {
        // Full-framebuffer screenshots undergo the following sequence of events:
        // 1. Screenshots are queued by content or whatever.
        // 2. When the rendering code reaches the screenshot type,
        //    we instruct the GPU driver to copy the framebuffer and asynchronously transfer it to host memory.
        // 3. Transfer finished asynchronously, we invoke the callback.
        //
        // On RAW GLES2, we cannot do this asynchronously due to lacking GL features,
        // and the game will stutter as a result. This is sadly unavoidable.
        //
        // For CopyPixels on render targets, the copy and transfer is started immediately when the function is called.

        private readonly List<QueuedScreenshot> _queuedScreenshots = new();

        public void Screenshot(ScreenshotType type, CopyPixelsDelegate<Rgb24> callback, UIBox2i? subRegion = null)
        {
            _queuedScreenshots.Add(new QueuedScreenshot(type, callback, subRegion));
        }

        private void TakeScreenshot(ScreenshotType type)
        {
            if (_queuedScreenshots.Count == 0)
            {
                return;
            }

            for (var i = 0; i < _queuedScreenshots.Count; i++)
            {
                var (qType, callback, subRegion) = _queuedScreenshots[i];
                if (qType != type)
                    continue;

                _renderState.RenderTarget!.CopyPixelsToMemory(callback, subRegion);
                _queuedScreenshots.RemoveSwap(i--);
            }
        }

        private sealed record QueuedScreenshot(
            ScreenshotType Type,
            CopyPixelsDelegate<Rgb24> Callback,
            UIBox2i? SubRegion);
    }
}
