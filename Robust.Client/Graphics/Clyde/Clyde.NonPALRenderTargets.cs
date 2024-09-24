
using Robust.Shared.Graphics;
using Robust.Shared.Maths;

namespace Robust.Client.Graphics.Clyde
{
    internal partial class Clyde
    {
        // This is always kept up-to-date, except in CreateRenderTarget (because it restores the old value)
        // It, like _mainWindowRenderTarget, is initialized in Clyde's constructor.
        // It is used by CopyRenderTextureToTexture, along with by sRGB emulation.
        internal PAL.RenderTargetBase _currentBoundRenderTarget;

        IRenderTexture IClyde.CreateRenderTarget(Vector2i size, RenderTargetFormatParameters format,
            TextureSampleParameters? sampleParameters, string? name)
        {
            return _pal.CreateRenderTarget(size, format, sampleParameters, name);
        }

        PAL.RenderTexture IWindowingHost.CreateWindowRenderTarget(Vector2i size)
        {
            return _pal.CreateRenderTarget(size, new RenderTargetFormatParameters
            {
                ColorFormat = RenderTargetColorFormat.Rgba8Srgb,
                HasDepthStencil = true
            });
        }
    }
}
