namespace Robust.Client.Graphics.Clyde;

/// <summary>Interned uniform names.</summary>
internal sealed class InternedUniform
{
    public static readonly InternedUniform UniIModUV = new(0, "modifyUV");
    public static readonly InternedUniform UniIModelMatrix = new(1, "modelMatrix");
    public static readonly InternedUniform UniITexturePixelSize = new(2, "TEXTURE_PIXEL_SIZE");
    public static readonly InternedUniform UniIMainTexture = new(3, "TEXTURE");
    public static readonly InternedUniform UniILightTexture = new(4, "lightMap");
    public const int UniCount = 5;

    public int Index { get; private set; }
    public string Name { get; private set; }

    private InternedUniform(int index, string name)
    {
        Index = index;
        Name = name;
    }
}
