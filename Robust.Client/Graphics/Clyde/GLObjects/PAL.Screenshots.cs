using System;
using System.Collections.Generic;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Client.Utility;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using PF = OpenToolkit.Graphics.OpenGL4.PixelFormat;
using PT = OpenToolkit.Graphics.OpenGL4.PixelType;
using ES20 = OpenToolkit.Graphics.ES20;

namespace Robust.Client.Graphics.Clyde;

// Contains primary screenshot and pixel-copying logic.

internal sealed partial class PAL
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

    private readonly List<TransferringPixelCopy> _transferringPixelCopies = new();

    /// This is used by CopyPixelsToMemory in order to copy out of the current framebuffer.
    private unsafe void DoCopyPixels<T>(
        Vector2i fbSize,
        UIBox2i? subRegion,
        CopyPixelsDelegate<T> callback)
        where T : unmanaged, IPixel<T>
    {
        var (pf, pt) = default(T) switch
        {
            Rgba32 => (PF.Rgba, PT.UnsignedByte),
            Rgb24 => (PF.Rgb, PT.UnsignedByte),
            _ => throw new ArgumentException("Unsupported pixel type.")
        };

        var size = ClydeBase.ClampSubRegion(fbSize, subRegion);

        var bufferLength = size.X * size.Y;
        if (!(_hasGL.FenceSync && _hasGL.AnyMapBuffer && _hasGL.PixelBufferObjects))
        {
            _sawmillOgl.Debug("clyde.ogl",
                "Necessary features for async screenshots not available, falling back to blocking path.");

            // We need these 3 features to be able to do asynchronous screenshots, if we don't have them,
            // we'll have to fall back to a crappy synchronous stalling method of glReadnPixels().

            var buffer = new T[bufferLength];
            fixed (T* ptr = buffer)
            {
                var bufSize = sizeof(T) * bufferLength;
                // Pack alignment is set by GLWrapper to 1.
                GL.ReadnPixels(
                    0, 0,
                    size.X, size.Y,
                    pf, pt,
                    bufSize,
                    (nint) ptr);

                CheckGlError();
            }

            var image = new Image<T>(size.X, size.Y);
            var imageSpan = image.GetPixelSpan();

            FlipCopy(buffer, imageSpan, size.X, size.Y);

            callback(image);
            return;
        }

        GL.GenBuffers(1, out uint pbo);
        CheckGlError();

        GL.BindBuffer(BufferTarget.PixelPackBuffer, pbo);
        CheckGlError();

        GL.BufferData(
            BufferTarget.PixelPackBuffer,
            bufferLength * sizeof(Rgba32), IntPtr.Zero,
            BufferUsageHint.StreamRead);
        CheckGlError();

        // Pack alignment is set by GLWrapper to 1.
        GL.ReadPixels(0, 0, size.X, size.Y, pf, pt, IntPtr.Zero);
        CheckGlError();

        var fence = GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, WaitSyncFlags.None);
        CheckGlError();

        GL.BindBuffer(BufferTarget.PixelPackBuffer, 0);
        CheckGlError();

        var transferring = new TransferringPixelCopy(pbo, fence, size, FinishPixelTransfer<T>, callback);
        _transferringPixelCopies.Add(transferring);
    }

    internal unsafe void CheckTransferringScreenshots()
    {
        if (_transferringPixelCopies.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _transferringPixelCopies.Count; i++)
        {
            var transferring = _transferringPixelCopies[i];

            // Check if transfer done (sync signalled)
            int status;
            GL.GetSync(transferring.Sync, SyncParameterName.SyncStatus, sizeof(int), null, &status);
            CheckGlError();

            if (status != (int) All.Signaled)
                continue;

            transferring.TransferContinue(transferring);
            _transferringPixelCopies.RemoveSwap(i--);
        }
    }

    private unsafe void FinishPixelTransfer<T>(TransferringPixelCopy transferring) where T : unmanaged, IPixel<T>
    {
        var (pbo, fence, (width, height), _, callback) = transferring;

        var bufLen = width * height;
        var bufSize = sizeof(T) * bufLen;

        GL.BindBuffer(BufferTarget.PixelPackBuffer, pbo);
        CheckGlError();

        var ptr = _hasGL.MapFullBuffer(
            BufferTarget.PixelPackBuffer,
            bufSize,
            BufferAccess.ReadOnly,
            BufferAccessMask.MapReadBit);

        var packSpan = new ReadOnlySpan<T>(ptr, width * height);

        var image = new Image<T>(width, height);
        var imageSpan = image.GetPixelSpan();

        FlipCopy(packSpan, imageSpan, width, height);

        _hasGL.UnmapBuffer(BufferTarget.PixelPackBuffer);

        GL.BindBuffer(BufferTarget.PixelPackBuffer, 0);
        CheckGlError();

        GL.DeleteBuffer(pbo);
        CheckGlError();

        GL.DeleteSync(fence);
        CheckGlError();

        var castCallback = (CopyPixelsDelegate<T>) callback;
        castCallback(image);
    }

    private sealed record QueuedScreenshot(
        ScreenshotType Type,
        CopyPixelsDelegate<Rgb24> Callback,
        UIBox2i? SubRegion);

    private sealed record TransferringPixelCopy(
        uint Pbo,
        nint Sync,
        Vector2i Size,
        // Funny callback dance to handle the generics.
        Action<TransferringPixelCopy> TransferContinue,
        Delegate Callback);
}
