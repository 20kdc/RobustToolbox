using System;
using System.IO;
using Robust.Shared.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Log;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Robust.Shared.Configuration;

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

    void IWindowingHost.SetupDebugCallback()
    {
        SetupDebugCallback();
    }

    IConfigurationManager IWindowingHost.Cfg => _cfg;
    ILogManager IWindowingHost.LogManager => _logManager;

    PAL.RenderTexture IWindowingHost.CreateWindowRenderTarget(Vector2i size)
    {
        return _pal.CreateRenderTarget(size, new RenderTargetFormatParameters
        {
            ColorFormat = RenderTargetColorFormat.Rgba8Srgb,
            HasDepthStencil = true
        });
    }
}

internal sealed partial class PAL
{
    [Dependency] internal readonly Clyde _clyde = default!;
}
