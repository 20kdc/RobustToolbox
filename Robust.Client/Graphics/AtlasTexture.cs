using JetBrains.Annotations;
using Robust.Shared.Graphics;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Robust.Client.Graphics
{
    /// <summary>
    ///     Represents a sub region of another texture.
    ///     This can be a useful optimization in many cases.
    /// </summary>
    [PublicAPI]
    public sealed class AtlasTexture : Texture
    {
        public AtlasTexture(Texture texture, UIBox2 subRegion) : base((Vector2i) subRegion.Size)
        {
            DebugTools.Assert(SubRegion.Right < texture.Width);
            DebugTools.Assert(SubRegion.Bottom < texture.Height);
            DebugTools.Assert(SubRegion.Left >= 0);
            DebugTools.Assert(SubRegion.Top >= 0);

            SubRegion = subRegion.Translated(texture.SubRegion.TopLeft);
            SourceTexture = texture.SourceTexture;
        }

        public override WholeTexture SourceTexture { get; }

        public override UIBox2 SubRegion { get; }

        public override Color GetPixel(int x, int y)
        {
            DebugTools.Assert(x < SubRegion.Right);
            DebugTools.Assert(y < SubRegion.Top);
            int xTranslated = x + (int) SubRegion.Left;
            int yTranslated = y + (int) SubRegion.Top;
            return this.SourceTexture[xTranslated, yTranslated];
        }
    }
}
