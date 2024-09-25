using JetBrains.Annotations;
using Robust.Shared.Maths;

namespace Robust.Client.Graphics;

/// <summary>
/// Represents the render state.
/// </summary>
[PublicAPI]
public interface IGPURenderState
{
    /// <summary>Render target.</summary>
    IRenderTarget? RenderTarget { get; set; }

    /// <summary>GPU shader program.</summary>
    GPUShaderProgram? Program { get; set; }

    /// <summary>Vertex array object.</summary>
    GPUVertexArrayObject? VAO { get; set; }

    /// <summary>Stencil parameters.</summary>
    StencilParameters Stencil { get; set; }

    /// <summary>Blend parameters.</summary>
    BlendParameters Blend { get; set; }

    /// <summary>Scissor box.</summary>
    UIBox2i? Scissor { get; set; }

    /// <summary>Viewport box. (Translated to GL as Left, Bottom, Width, Height)</summary>
    Box2i Viewport { get; set; }

    /// <summary>Colour/Depth mask.</summary>
    ColourDepthMask ColourDepthMask { get; set; }

    /// <summary>Sets viewport by X/Y/Width/Height</summary>
    void SetViewport(int x, int y, int width, int height)
    {
        Viewport = Box2i.FromDimensions(x, y, width, height);
    }

    /// <summary>Sets a texture in the render state.</summary>
    void SetTexture(int unit, WholeTexture? value);

    /// <summary>
    /// Clears the texture unit memory of the render state.
    /// Strictly speaking, the units are left in an undefined state.
    /// </summary>
    void ClearTextures();

    /// <summary>
    /// Deliberately disconnects the OpenGL state from the IGPURenderState.
    /// If you expect to "thrash" the state without doing anything, this can optimize performance.
    /// </summary>
    void Unbind();

    /// <summary>Executes a draw call.</summary>
    void DrawArrays(DrawPrimitiveTopology topology, int offset, int count);

    /// <summary>Executes a draw call. Beware that offset is in bytes. Index type is always unsigned short.</summary>
    void DrawElements(DrawPrimitiveTopology topology, int offset, int count);

    /// <summary>Resets the IGPURenderState.</summary>
    void Reset()
    {
        Unbind();
        RenderTarget = null;
        Program = null;
        VAO = null;
        Stencil = new StencilParameters();
        Blend = BlendParameters.Mix;
        Scissor = null;
        Viewport = new();
        ColourDepthMask = ColourDepthMask.RGBAMask;
        ClearTextures();
    }

    /// <summary>
    /// Copies from another IRenderState.
    /// This is also an optimization hint to switch the bound state from the other state to this one.
    /// This switch is zero-cost, since the contents are equivalent.
    /// </summary>
    void CopyFrom(IGPURenderState other);
}

[PublicAPI]
public enum ColourDepthMask
{
    RedMask = 1,
    GreenMask = 2,
    BlueMask = 4,
    AlphaMask = 8,
    DepthMask = 16,
    AllMask = 31,
    RGBAMask = 15
}
