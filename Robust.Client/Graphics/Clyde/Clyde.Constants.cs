namespace Robust.Client.Graphics.Clyde
{
    internal sealed partial class Clyde
    {
        private static readonly (string, uint)[] BaseShaderAttribLocations =
        {
            ("aPos", 0),
            ("tCoord", 1),
            ("tCoord2", 2),
            ("modulate", 3)
        };

        private const string UniProjViewMatrices = "projectionViewMatrices";
        private const string UniUniformConstants = "uniformConstants";

        private const int BindingIndexProjView = 0;
        private const int BindingIndexUniformConstants = 1;

        // To be clear: You shouldn't change this. This just helps with understanding where Primitive Restart is being used.
        private const ushort PrimitiveRestartIndex = ushort.MaxValue;
    }
}
