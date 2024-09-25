using OpenToolkit.Graphics.OpenGL4;

namespace Robust.Client.Graphics.Clyde;

/// <summary>Interned uniform names.</summary>
internal sealed class InternedUniform
{
    /// <summary>In Clyde-prepared shaders, this texture unit is reserved for TEXTURE.</summary>
    public const int MainTextureUnit = 0;

    /// <summary>In Clyde-prepared shaders, this texture unit is reserved for lightMap.</summary>
    public const int LightTextureUnit = 1;

    public static readonly InternedUniform UniIModelMatrix = new(0, "modelMatrix");
    public static readonly InternedUniform UniITexturePixelSize = new(1, "TEXTURE_PIXEL_SIZE");
    public static readonly InternedUniform UniIMainTexture = new(2, "TEXTURE");
    public static readonly InternedUniform UniILightTexture = new(3, "lightMap");
    public const int UniCount = 4;

    public int Index { get; private set; }
    public string Name { get; private set; }

    private InternedUniform(int index, string name)
    {
        Index = index;
        Name = name;
    }
}
