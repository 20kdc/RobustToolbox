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

    /// <summary>Stencil parameters. Be aware that the stencil mask is still used.</summary>
    StencilParameters Stencil { get; set; }

    /// <summary>Scissor box.</summary>
    UIBox2i? Scissor { get; set; }

    /// <summary>Viewport box. (Translated to GL as Left, Bottom, Width, Height)</summary>
    Box2i Viewport { get; set; }

    /// <summary>Sets viewport by X/Y/Width/Height</summary>
    void SetViewport(int x, int y, int width, int height)
    {
        Viewport = Box2i.FromDimensions(x, y, width, height);
    }

    /// <summary>Gets a texture from the render state.</summary>
    WholeTexture? GetTexture(int unit);

    /// <summary>Sets a texture in the render state.</summary>
    void SetTexture(int unit, WholeTexture? value);

    /// <summary>Deliberately disconnects the OpenGL state from the IGPURenderState.</summary>
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
        Scissor = null;
        Viewport = new();
    }
}
