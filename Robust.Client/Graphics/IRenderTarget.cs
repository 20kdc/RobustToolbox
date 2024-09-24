using System;
using Robust.Shared.Maths;
using SixLabors.ImageSharp.PixelFormats;

namespace Robust.Client.Graphics
{
    /// <summary>
    ///     Represents something that can be rendered to.
    /// </summary>
    public interface IRenderTarget : IDisposable
    {
        /// <summary>
        ///     The size of the render target, in physical pixels.
        /// </summary>
        Vector2i Size { get; }

        /// <summary>
        /// Clears the render target.
        /// </summary>
        void Clear(float? r = null, float? g = null, float? b = null, float? a = null, int stencilValue = 0, int stencilMask = 0, float? depth = null, UIBox2i? scissor = null);

        void CopyPixelsToMemory<T>(CopyPixelsDelegate<T> callback, UIBox2i? subRegion = null) where T : unmanaged, IPixel<T>;

        /// <summary>
        /// Copies this IRenderTarget's contents to a texture, assumed to be of the same size.
        /// </summary>
        void CopyPixelsToTexture(OwnedTexture target);
    }
}
