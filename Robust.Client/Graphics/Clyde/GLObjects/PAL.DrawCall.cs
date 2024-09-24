using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using TKStencilOp = OpenToolkit.Graphics.OpenGL4.StencilOp;

namespace Robust.Client.Graphics.Clyde;

// Ok, so, here's the deal.
// PAL is layered atop _hasGL.
// In PAL, there's the "backup handles" layer.
// Micro-operations like uniform updates that should be stateless (but aren't) access this layer to save/restore data.
// (Any operation that is too complicated to handle this way should be using the next layer!)
// IGPURenderState provides the next, "outer" layer.
// It basically provides contexts within contexts.
// Clyde is then layered on top of IGPURenderState.

internal partial class PAL
{
    public int LastGLDrawCalls { get; set; }

    private GLRenderState? _currentRenderState = null;

    // Some simple flags that basically just tracks the current state of glEnable(GL_STENCIL/GL_SCISSOR_TEST)
    private bool _isStencilling;
    private bool _isScissoring;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public GLRenderState CreateRenderState() => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    IGPURenderState IGPUAbstraction.CreateRenderState() => new GLRenderState(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetScissorImmediate(RenderTargetBase renderTarget, in UIBox2i? box)
    {
        if (box != null)
        {
            var val = box!.Value;
            if (!_isScissoring)
            {
                GL.Enable(EnableCap.ScissorTest);
                CheckGlError();
            }

            // Don't forget to flip it, these coordinates have bottom left as origin.
            // TODO: Broken when rendering to non-screen render targets.

            if (renderTarget.FlipY)
            {
                GL.Scissor(val.Left, val.Top, val.Width, val.Height);
            }
            else
            {
                GL.Scissor(val.Left, renderTarget.Size.Y - val.Bottom, val.Width, val.Height);
            }
            CheckGlError();
        }
        else if (_isScissoring)
        {
            _isScissoring = false;
            GL.Disable(EnableCap.ScissorTest);
            CheckGlError();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetViewportImmediate(Box2i box)
    {
        GL.Viewport(box.Left, box.Bottom, box.Width, box.Height);
        CheckGlError();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyStencilParameters(in StencilParameters sp)
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
        GL.StencilMask(sp.WriteMask);
        CheckGlError();
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

    internal sealed class GLRenderState(PAL pal) : IGPURenderState
    {
        private readonly PAL _pal = pal;

        private RenderTargetBase? _renderTarget = null;
        public IRenderTarget? RenderTarget
        {
            get => _renderTarget;
            set
            {
                _renderTarget = (RenderTargetBase?) value;
                if (_pal._currentRenderState == this && GPUResource.IsValid(_renderTarget))
                {
                    _pal.DCBindRenderTarget(_renderTarget);
                    _pal.SetScissorImmediate(_renderTarget, _scissor);
                }
            }
        }

        private GLShaderProgram? _program = null;
        public GPUShaderProgram? Program
        {
            get => _program;
            set
            {
                _program = (GLShaderProgram?) value;
                if (_pal._currentRenderState == this && GPUResource.IsValid(_program))
                {
                    _pal.DCUseProgram(_program.Handle);
                }
            }
        }

        private GLVAOBase? _vao = null;
        public GPUVertexArrayObject? VAO
        {
            get => _vao;
            set
            {
                _vao = (GLVAOBase?) value;
                if (_pal._currentRenderState == this && GPUResource.IsValid(_vao))
                {
                    _pal.DCBindVAO(_vao.ObjectHandle);
                }
            }
        }

        private StencilParameters _stencil = new();
        public StencilParameters Stencil
        {
            get => _stencil;
            set
            {
                _stencil = value;
                if (_pal._currentRenderState == this)
                    _pal.ApplyStencilParameters(value);
            }
        }

        private UIBox2i? _scissor = null;
        public UIBox2i? Scissor
        {
            get => _scissor;
            set
            {
                _scissor = value;
                if (GPUResource.IsValid(_renderTarget))
                {
                    _pal.SetScissorImmediate(_renderTarget, _scissor);
                }
            }
        }

        private Box2i _viewport = new();
        public Box2i Viewport
        {
            get => _viewport;
            set
            {
                _viewport = value;
                _pal.SetViewportImmediate(_viewport);
            }
        }

        private Dictionary<TextureUnit, WholeTexture> _textures = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WholeTexture? GetTexture(int unit)
        {
            if (_textures.TryGetValue(TextureUnit.Texture0 + unit, out var res))
            {
                return res;
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTexture(int unit, WholeTexture? value)
        {
            var tu = TextureUnit.Texture0 + unit;
            if (value != null)
            {
                _textures[tu] = value;
            }
            else
            {
                _textures.Remove(tu);
            }
            if (_pal._currentRenderState == this)
            {
                GL.ActiveTexture(tu);
                _pal.CheckGlError();
                if (value != null)
                {
                    var ct = (ClydeTexture) value!;
                    GL.BindTexture(TextureTarget.Texture2D, ct.OpenGLObject.Handle);
                }
                else
                {
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                }
                _pal.CheckGlError();
                if (unit != 0)
                    GL.ActiveTexture(TextureUnit.Texture0);
                // ActiveTexture(Texture0) is essentially guaranteed to succeed.
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Bind()
        {
            if (_pal._currentRenderState != this)
            {
                if (GPUResource.IsValid(_renderTarget))
                {
                    _pal.DCBindRenderTarget(_renderTarget);
                    _pal.SetScissorImmediate(_renderTarget, _scissor);
                }
                _pal._currentRenderState = this;
                if (_program != null)
                    _pal.DCUseProgram(_program.Handle);
                if (_vao != null)
                    _pal.DCBindVAO(_vao.ObjectHandle);
                _pal.ApplyStencilParameters(_stencil);
                _pal.SetViewportImmediate(_viewport);
                foreach (var entry in _textures)
                {
                    GL.ActiveTexture(entry.Key);
                    _pal.CheckGlError();
                    var ct = (ClydeTexture) entry.Value!;
                    GL.BindTexture(TextureTarget.Texture2D, ct.OpenGLObject.Handle);
                    _pal.CheckGlError();
                }
                GL.ActiveTexture(TextureUnit.Texture0);
                // pretty much guaranteed to succeed.
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unbind()
        {
            if (_pal._currentRenderState == this)
                _pal._currentRenderState = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawArrays(DrawPrimitiveTopology topology, int offset, int count)
        {
            Bind();
            DebugTools.AssertNotNull(_vao);
            GL.DrawArrays(MapPrimitiveType(topology), offset, count);
            _pal.CheckGlError();
            _pal.LastGLDrawCalls += 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawElements(DrawPrimitiveTopology topology, int offset, int count)
        {
            Bind();
            DebugTools.AssertNotNull(_vao);
            DebugTools.Assert(_vao!.IndexBufferIsBound);
            if (_pal._hasGL.VertexArrayObject)
                DebugTools.Assert(GL.GetInteger(GetPName.VertexArrayBinding) == _vao.ObjectHandle);
            // In the hands of a skilled evildoer, the crash that would result from this could leak arbitrary memory.
            // Just to be clear, that's bad.
            if (GL.GetInteger(GetPName.ElementArrayBufferBinding) == 0)
                throw new Exception("Somehow, ElementArrayBufferBinding == 0");
            GL.DrawElements(MapPrimitiveType(topology), count, DrawElementsType.UnsignedShort, offset);
            _pal.CheckGlError();
            _pal.LastGLDrawCalls += 1;
        }
    }
}
