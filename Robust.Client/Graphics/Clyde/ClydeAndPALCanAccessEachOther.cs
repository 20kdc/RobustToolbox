using System;
using System.IO;
using Robust.Shared.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Robust.Client.Graphics.Clyde;

// -- May this become one-way, one day. --

internal sealed partial class Clyde
{
    [Dependency] internal readonly PAL _pal = default!;

    internal GLWrapper _hasGL => _pal._hasGL;

    // interface proxies
    public OwnedTexture LoadTextureFromImage<T>(Image<T> image, string? name = null, TextureLoadParameters? loadParams = null) where T : unmanaged, IPixel<T> => _pal.LoadTextureFromImage<T>(image, name, loadParams);
    public OwnedTexture CreateBlankTexture<T>(Vector2i size, string? name = null, in TextureLoadParameters? loadParams = null) where T : unmanaged, IPixel<T> => _pal.CreateBlankTexture<T>(size, name, loadParams);

    public GPUBuffer CreateBuffer(ReadOnlySpan<byte> span, GPUBuffer.Usage usage, string? name) => _pal.CreateBuffer(span, usage, name);

    public GPUVertexArrayObject CreateVAO(string? name = null) => _pal.CreateVAO(name);

    public IGPURenderState CreateRenderState() => _pal.CreateRenderState();
}

internal sealed partial class PAL
{
    [Dependency] internal readonly Clyde _clyde = default!;
}
