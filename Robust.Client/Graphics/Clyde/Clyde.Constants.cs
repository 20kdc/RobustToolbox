namespace Robust.Client.Graphics.Clyde
{
    internal sealed partial class Clyde
    {
        private static readonly (string, uint)[] BaseShaderAttribLocations =
        {
            ("aPos", 0),
            ("tCoord", 1),
            ("modulate", 2)
        };

        private const int UniIModUV = 0;
        private const int UniIModelMatrix = 1;
        private const int UniITexturePixelSize = 2;
        private const int UniIMainTexture = 3;
        private const int UniILightTexture = 4;
        private const int UniCount = 5;

        private const string UniModUV = "modifyUV";
        private const string UniModelMatrix = "modelMatrix";
        private const string UniTexturePixelSize = "TEXTURE_PIXEL_SIZE";
        private const string UniMainTexture = "TEXTURE";
        private const string UniLightTexture = "lightMap";
        private const string UniProjViewMatrices = "projectionViewMatrices";
        private const string UniUniformConstants = "uniformConstants";

        private const int BindingIndexProjView = 0;
        private const int BindingIndexUniformConstants = 1;

        // To be clear: You shouldn't change this. This just helps with understanding where Primitive Restart is being used.
        private const ushort PrimitiveRestartIndex = ushort.MaxValue;
    }
}
