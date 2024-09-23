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
    public OwnedTexture LoadTextureFromPNGStream(Stream stream, string? name = null, TextureLoadParameters? loadParams = null) => _pal.LoadTextureFromPNGStream(stream, name, loadParams);
    public OwnedTexture LoadTextureFromImage<T>(Image<T> image, string? name = null, TextureLoadParameters? loadParams = null) where T : unmanaged, IPixel<T> => _pal.LoadTextureFromImage<T>(image, name, loadParams);
    public unsafe OwnedTexture CreateBlankTexture<T>(Vector2i size, string? name = null, in TextureLoadParameters? loadParams = null) where T : unmanaged, IPixel<T> => _pal.CreateBlankTexture<T>(size, name, loadParams);
}

internal sealed partial class PAL
{
    [Dependency] internal readonly Clyde _clyde = default!;
}
