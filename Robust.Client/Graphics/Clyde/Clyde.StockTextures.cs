using System;
using Robust.Shared.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
