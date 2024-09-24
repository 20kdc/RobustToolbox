using System;
using System.Runtime.CompilerServices;
using OpenToolkit.Graphics.OpenGL4;
using TKStencilOp = OpenToolkit.Graphics.OpenGL4.StencilOp;

namespace Robust.Client.Graphics.Clyde;

internal partial class PAL
{
    private bool _isStencilling;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DisableStencil()
    {
        if (_isStencilling)
        {
            GL.Disable(EnableCap.StencilTest);
            CheckGlError();
            _isStencilling = false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ApplyStencilParameters(in StencilParameters sp)
    {
        // Handle stencil parameters.
        if (sp.Enabled)
        {
            if (!_isStencilling)
            {
                GL.Enable(EnableCap.StencilTest);
                CheckGlError();
                _isStencilling = true;
            }

            GL.StencilMask(sp.WriteMask);
            CheckGlError();
            GL.StencilFunc(ToGLStencilFunc(sp.Func), sp.Ref, sp.ReadMask);
            CheckGlError();
            GL.StencilOp(TKStencilOp.Keep, TKStencilOp.Keep, ToGLStencilOp(sp.Op));
            CheckGlError();
        }
        else if (_isStencilling)
        {
            GL.Disable(EnableCap.StencilTest);
            CheckGlError();
            _isStencilling = false;
        }
    }

    private static TKStencilOp ToGLStencilOp(StencilOp op)
    {
        return op switch
        {
            StencilOp.Keep => TKStencilOp.Keep,
            StencilOp.Zero => TKStencilOp.Zero,
            StencilOp.Replace => TKStencilOp.Replace,
            StencilOp.IncrementClamp => TKStencilOp.Incr,
            StencilOp.IncrementWrap => TKStencilOp.IncrWrap,
            StencilOp.DecrementClamp => TKStencilOp.Decr,
            StencilOp.DecrementWrap => TKStencilOp.DecrWrap,
            StencilOp.Invert => TKStencilOp.Invert,
            _ => throw new NotSupportedException()
        };
    }

    private static StencilFunction ToGLStencilFunc(StencilFunc op)
    {
        return op switch
        {
            StencilFunc.Never => StencilFunction.Never,
            StencilFunc.Always => StencilFunction.Always,
            StencilFunc.Less => StencilFunction.Less,
            StencilFunc.LessOrEqual => StencilFunction.Lequal,
            StencilFunc.Greater => StencilFunction.Greater,
            StencilFunc.GreaterOrEqual => StencilFunction.Gequal,
            StencilFunc.NotEqual => StencilFunction.Notequal,
            StencilFunc.Equal => StencilFunction.Equal,
            _ => throw new NotSupportedException()
        };
    }

    internal static PrimitiveType MapPrimitiveType(DrawPrimitiveTopology type)
    {
        return type switch
        {
            DrawPrimitiveTopology.TriangleList => PrimitiveType.Triangles,
            DrawPrimitiveTopology.TriangleFan => PrimitiveType.TriangleFan,
            DrawPrimitiveTopology.TriangleStrip => PrimitiveType.TriangleStrip,
            DrawPrimitiveTopology.LineList => PrimitiveType.Lines,
            DrawPrimitiveTopology.LineStrip => PrimitiveType.LineStrip,
            DrawPrimitiveTopology.LineLoop => PrimitiveType.LineLoop,
            DrawPrimitiveTopology.PointList => PrimitiveType.Points,
            _ => PrimitiveType.Triangles
        };
    }

    public void ExecuteDraw(in GPUDrawCall draw)
    {
        var rt = _clyde.RtToLoaded((Clyde.RenderTargetBase) draw.RenderTarget);
        _clyde.BindRenderTargetImmediate(rt);

        // Do nothing for now...
        switch (draw.TextureCount)
        {
            case 8: goto T7;
            case 7: goto T6;
            case 6: goto T5;
            case 5: goto T4;
            case 4: goto T3;
            case 3: goto T2;
            case 2: goto T1;
            case 1: goto T0;
            case 0: goto TN;
            default: throw new IndexOutOfRangeException("Too many textures!");
        }
        T7:
        if (draw.Texture7 != null)
            _clyde.SetTexture(TextureUnit.Texture7, draw.Texture7);
        T6:
        if (draw.Texture6 != null)
            _clyde.SetTexture(TextureUnit.Texture6, draw.Texture6);
        T5:
        if (draw.Texture5 != null)
            _clyde.SetTexture(TextureUnit.Texture5, draw.Texture5);
        T4:
        if (draw.Texture4 != null)
            _clyde.SetTexture(TextureUnit.Texture4, draw.Texture4);
        T3:
        if (draw.Texture3 != null)
            _clyde.SetTexture(TextureUnit.Texture3, draw.Texture3);
        T2:
        if (draw.Texture2 != null)
            _clyde.SetTexture(TextureUnit.Texture2, draw.Texture2);
        T1:
        if (draw.Texture1 != null)
            _clyde.SetTexture(TextureUnit.Texture1, draw.Texture1);
        T0:
        if (draw.Texture0 != null)
            _clyde.SetTexture(TextureUnit.Texture0, draw.Texture0);
        TN:
        ((GLShaderProgram) draw.Program).Use();
        ((GLVAOBase) draw.VAO).Use();
        ApplyStencilParameters(draw.Stencil);
        if (draw.Scissor != null)
        {
            var value = draw.Scissor!.Value;
            GL.Scissor(value.Left, value.Bottom, value.Width, value.Height);
            CheckGlError();
            GL.Enable(EnableCap.ScissorTest);
            CheckGlError();
        }
        GL.Viewport(draw.Viewport.Left, draw.Viewport.Bottom, draw.Viewport.Width, draw.Viewport.Height);
        if (draw.Indexed)
        {
            GL.DrawElements(MapPrimitiveType(draw.Topology), draw.Count, DrawElementsType.UnsignedShort, draw.Offset);
        }
        else
        {
            GL.DrawArrays(MapPrimitiveType(draw.Topology), draw.Offset, draw.Count);
        }
        CheckGlError();
        DisableStencil();
        if (draw.Scissor != null)
        {
            GL.Disable(EnableCap.ScissorTest);
            CheckGlError();
        }
    }
}
