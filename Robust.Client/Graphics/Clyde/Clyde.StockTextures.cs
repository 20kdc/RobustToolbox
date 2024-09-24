using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Client.Utility;
using Robust.Shared.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;
using OGLTextureWrapMode = OpenToolkit.Graphics.OpenGL.TextureWrapMode;
using PIF = OpenToolkit.Graphics.OpenGL4.PixelInternalFormat;
using PF = OpenToolkit.Graphics.OpenGL4.PixelFormat;
using PT = OpenToolkit.Graphics.OpenGL4.PixelType;
using TextureWrapMode = Robust.Shared.Graphics.TextureWrapMode;

namespace Robust.Client.Graphics.Clyde;

internal partial class Clyde
{
    private ClydeTexture _stockTextureWhite = default!;
    private ClydeTexture _stockTextureBlack = default!;
    private ClydeTexture _stockTextureTransparent = default!;

    private void LoadStockTextures()
    {
        var white = new Image<Rgba32>(1, 1);
        white[0, 0] = new Rgba32(255, 255, 255, 255);
        _stockTextureWhite = (ClydeTexture) Texture.LoadFromImage(white);

        var black = new Image<Rgba32>(1, 1);
        black[0, 0] = new Rgba32(0, 0, 0, 255);
        _stockTextureBlack = (ClydeTexture) Texture.LoadFromImage(black);

        var blank = new Image<Rgba32>(1, 1);
        blank[0, 0] = new Rgba32(0, 0, 0, 0);
        _stockTextureTransparent = (ClydeTexture) Texture.LoadFromImage(blank);
    }

    public WholeTexture GetStockTexture(ClydeStockTexture stockTexture)
    {
        return stockTexture switch
        {
            ClydeStockTexture.White => _stockTextureWhite,
            ClydeStockTexture.Transparent => _stockTextureTransparent,
            ClydeStockTexture.Black => _stockTextureBlack,
            _ => throw new ArgumentException(nameof(stockTexture))
        };
    }
}
