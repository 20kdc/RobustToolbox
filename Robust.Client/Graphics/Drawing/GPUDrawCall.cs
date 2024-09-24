using System;
using JetBrains.Annotations;
using Robust.Shared.Maths;

namespace Robust.Client.Graphics;

/// <summary>
/// Represents a draw call.
/// </summary>
[PublicAPI]
public struct GPUDrawCall(IRenderTarget target, UIBox2i viewport, GPUShaderProgram program, GPUVertexArrayObject vao, DrawPrimitiveTopology topology, bool indexed, int offset, int count)
{
    /// <summary>Render target.</summary>
    public IRenderTarget RenderTarget = target;

    /// <summary>Shader program.</summary>
    public GPUShaderProgram Program = program;

    public WholeTexture? Texture0 { get; private set; } = null;
    public WholeTexture? Texture1 { get; private set; } = null;
    public WholeTexture? Texture2 { get; private set; } = null;
    public WholeTexture? Texture3 { get; private set; } = null;
    public WholeTexture? Texture4 { get; private set; } = null;
    public WholeTexture? Texture5 { get; private set; } = null;
    public WholeTexture? Texture6 { get; private set; } = null;
    public WholeTexture? Texture7 { get; private set; } = null;
    // If adding elements to this array, be sure to adjust PAL.DrawCall accordingly. Seriously!

    /// <summary>This is the maximum texture unit + 1 that might contain a texture.</summary>
    public int TextureCount { get; private set; } = 0;

    /// <summary>Vertex array object.</summary>
    public GPUVertexArrayObject VAO = vao;

    /// <summary>Draw topology.</summary>
    public DrawPrimitiveTopology Topology = topology;

    /// <summary>If true, indexing is used. Indexes are always unsigned shorts.</summary>
    public bool Indexed = indexed;

    /// <summary>Starting offset (in bytes!) in indexes array (if indexed) or first vertex index (not bytes!) in vertex array (if not).</summary>
    public int Offset = offset;

    /// <summary>Number of indexes to read (if indexed) or vertices (if not).</summary>
    public int Count = count;

    /// <summary>Stencil parameters.</summary>
    public StencilParameters Stencil = new();

    /// <summary>Scissor box.</summary>
    public UIBox2i? Scissor = null;

    /// <summary>Viewport box.</summary>
    public UIBox2i Viewport = viewport;

    /// <summary>Gets a texture from the draw call.</summary>
    public WholeTexture? GetTexture(int unit)
    {
        return unit switch
        {
            0 => Texture0,
            1 => Texture1,
            2 => Texture2,
            3 => Texture3,
            4 => Texture4,
            5 => Texture5,
            6 => Texture6,
            7 => Texture7,
            _ => throw new IndexOutOfRangeException($"Unit {unit} out of bounds"),
        };
    }

    /// <summary>Sets a texture in the draw call. (Unset texture units are left uninitialized. Please don't abuse this.)</summary>
    public void SetTexture(int unit, WholeTexture? value)
    {
        switch (unit)
        {
            case 0: Texture0 = value; break;
            case 1: Texture1 = value; break;
            case 2: Texture2 = value; break;
            case 3: Texture3 = value; break;
            case 4: Texture4 = value; break;
            case 5: Texture5 = value; break;
            case 6: Texture6 = value; break;
            case 7: Texture7 = value; break;
            default: throw new IndexOutOfRangeException($"Unit {unit} out of bounds");
        }
        int count = unit + 1;
        if (TextureCount < count)
        {
            TextureCount = count;
        }
    }
}
