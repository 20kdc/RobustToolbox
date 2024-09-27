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
    /// <summary>
    /// Amount of render state resets.
    /// This is the "main" PAL overhead, so we want to keep a close eye on these.
    /// </summary>
    public int LastRenderStateResets { get; set; }

    /// <summary>
    /// Currently bound render state. PAL rebinds this when a draw call requires it.
    /// </summary>
    private GLRenderState? _currentRenderState = null;

    // Some simple flags that basically just tracks the current state of glEnable(GL_STENCIL/GL_SCISSOR_TEST)
    private bool _isStencilling;
    private UIBox2i? _isScissoring;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public GLRenderState CreateRenderState() => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    IGPURenderState IGPUAbstraction.CreateRenderState() => new GLRenderState(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DCSetScissor(RenderTargetBase renderTarget, in UIBox2i? box)
    {
        SetScissorImmediate(renderTarget, box);
        _isScissoring = box;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetScissorImmediate(RenderTargetBase renderTarget, in UIBox2i? box)
    {
        if (box != null)
        {
            var val = box!.Value;
            GL.Enable(EnableCap.ScissorTest);
            CheckGlError();

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
        else
        {
            GL.Disable(EnableCap.ScissorTest);
            CheckGlError();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetCDMaskImmediate(ColourDepthMask mask)
    {
        var red = (mask & ColourDepthMask.RedMask) != 0;
        var green = (mask & ColourDepthMask.GreenMask) != 0;
        var blue = (mask & ColourDepthMask.BlueMask) != 0;
        var alpha = (mask & ColourDepthMask.AlphaMask) != 0;
        var depth = (mask & ColourDepthMask.DepthMask) != 0;
        GL.ColorMask(red, green, blue, alpha);
        CheckGlError();
        GL.DepthMask(depth);
        CheckGlError();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetCullFaceImmediate(CullFace cf)
    {
        if (cf != CullFace.None)
        {
            GL.Enable(EnableCap.CullFace);
            CheckGlError();
            GL.CullFace((CullFaceMode) cf);
            CheckGlError();
        }
        else
        {
            GL.Disable(EnableCap.CullFace);
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
    private void SetStencilImmediate(in StencilParameters sp)
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

            GL.StencilFunc((StencilFunction) sp.Func, sp.Ref, sp.ReadMask);
            CheckGlError();
            GL.StencilOp(TKStencilOp.Keep, TKStencilOp.Keep, (TKStencilOp) sp.Op);
            CheckGlError();
            GL.StencilMask(sp.WriteMask);
            CheckGlError();
        }
        else if (_isStencilling)
        {
            GL.Disable(EnableCap.StencilTest);
            CheckGlError();
            _isStencilling = false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetBlendImmediate(in BlendParameters sp)
    {
        if (sp.Enabled)
        {
            GL.Enable(EnableCap.Blend);
            CheckGlError();
            GL.BlendFuncSeparate((BlendingFactorSrc) sp.SrcRGB, (BlendingFactorDest) sp.DstRGB, (BlendingFactorSrc) sp.SrcAlpha, (BlendingFactorDest) sp.DstAlpha);
            CheckGlError();
            GL.BlendEquationSeparate((BlendEquationMode) sp.EquationRGB, (BlendEquationMode) sp.EquationAlpha);
            CheckGlError();
            GL.BlendColor(sp.Constant.R, sp.Constant.G, sp.Constant.B, sp.Constant.A);
            CheckGlError();
        }
        else
        {
            GL.Disable(EnableCap.Blend);
            CheckGlError();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetDepthImmediate(in DepthParameters sp)
    {
        if (sp.Enabled)
        {
            GL.Enable(EnableCap.DepthTest);
            CheckGlError();
            GL.DepthFunc((DepthFunction) sp.Func);
            CheckGlError();
        }
        else
        {
            GL.Disable(EnableCap.DepthTest);
            CheckGlError();
        }
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
                    _pal.DCSetScissor(_renderTarget, _scissor);
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
                    _pal.SetStencilImmediate(value);
            }
        }

        private BlendParameters _blend = new();
        public BlendParameters Blend
        {
            get => _blend;
            set
            {
                _blend = value;
                if (_pal._currentRenderState == this)
                    _pal.SetBlendImmediate(value);
            }
        }

        private DepthParameters _depth = new();
        public DepthParameters Depth
        {
            get => _depth;
            set
            {
                _depth = value;
                if (_pal._currentRenderState == this)
                    _pal.SetDepthImmediate(value);
            }
        }

        private UIBox2i? _scissor = null;
        public UIBox2i? Scissor
        {
            get => _scissor;
            set
            {
                _scissor = value;
                if (_pal._currentRenderState == this && GPUResource.IsValid(_renderTarget))
                {
                    _pal.DCSetScissor(_renderTarget, _scissor);
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
                if (_pal._currentRenderState == this)
                {
                    _pal.SetViewportImmediate(_viewport);
                }
            }
        }

        private ColourDepthMask _colourDepthMask = ColourDepthMask.RGBAMask;
        public ColourDepthMask ColourDepthMask
        {
            get => _colourDepthMask;
            set
            {
                _colourDepthMask = value;
                if (_pal._currentRenderState == this)
                {
                    _pal.SetCDMaskImmediate(value);
                }
            }
        }

        private CullFace _cullFace = CullFace.None;
        public CullFace CullFace
        {
            get => _cullFace;
            set
            {
                _cullFace = value;
                if (_pal._currentRenderState == this)
                {
                    _pal.SetCullFaceImmediate(value);
                }
            }
        }

        private Dictionary<TextureUnit, WholeTexture> _textures = new();

        private Dictionary<int, GPUUniformBufferBase> _uniformBuffers = new();

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
        public void ClearTextures() => _textures.Clear();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetUBO(int index, GPUUniformBufferBase buf)
        {
            _uniformBuffers[index] = buf;
            if (_pal._hasGL.UniformBuffers && _pal._currentRenderState == this)
            {
                GL.BindBufferBase(BufferRangeTarget.UniformBuffer, index, (int) ((GLBuffer) buf.Buffer!).ObjectHandle);
                _pal.CheckGlError();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearUBOs() => _uniformBuffers.Clear();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Bind()
        {
            if (_pal._currentRenderState != this)
            {
                _pal.LastRenderStateResets += 1;
                if (GPUResource.IsValid(_renderTarget))
                {
                    _pal.DCBindRenderTarget(_renderTarget);
                    _pal.DCSetScissor(_renderTarget, _scissor);
                }
                _pal._currentRenderState = this;
                if (_vao != null)
                    _pal.DCBindVAO(_vao.ObjectHandle);
                _pal.SetStencilImmediate(_stencil);
                _pal.SetBlendImmediate(_blend);
                _pal.SetDepthImmediate(_depth);
                _pal.SetViewportImmediate(_viewport);
                _pal.SetCDMaskImmediate(_colourDepthMask);
                _pal.SetCullFaceImmediate(_cullFace);
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

                if (_pal._hasGL.UniformBuffers)
                {
                    foreach (var entry in _uniformBuffers)
                    {
                        GL.BindBufferBase(BufferRangeTarget.UniformBuffer, entry.Key, (int) ((GLBuffer) entry.Value.Buffer!).ObjectHandle);
                        _pal.CheckGlError();
                    }
                }
            }
        }

        /// <summary>
        /// Prepares draw call state that can't be or isn't maintained by the render state itself.
        /// In practice, this means the UBO emulator. Any other way = massive state thrashing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PrepForDrawCall()
        {
            _program!.Bind();
            if (!_pal._hasGL.UniformBuffers && _program != null)
            {
                // UBOs not supported.
                // The UBO emulator uses a versioning scheme to avoid repeatedly setting uniforms.
                foreach (var entry in _uniformBuffers)
                {
                    if (_program.UniformBufferVersions.TryGetValue(entry.Key, out var version))
                    {
                        // The program does support this uniform buffer
                        if (version != entry.Value.Version)
                        {
                            // Incorrect version; update
                            entry.Value.ApplyIntoShader(_program);
                            _program.UniformBufferVersions[entry.Key] = version;
                        }
                    }
                }
            }
            // If this assert triggers, it is likely that ApplyIntoShader did something it shouldn't.
            DebugTools.AssertEqual(_program, _pal._currentProgram);
            DebugTools.AssertNotNull(_vao);
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
            PrepForDrawCall();
            GL.DrawArrays((PrimitiveType) topology, offset, count);
            _pal.CheckGlError();
            _pal.LastGLDrawCalls += 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawElements(DrawPrimitiveTopology topology, int offset, int count)
        {
            Bind();
            PrepForDrawCall();
            DebugTools.Assert(_vao!.IndexBufferIsBound);
            if (_pal._hasGL.VertexArrayObject)
                DebugTools.Assert(GL.GetInteger(GetPName.VertexArrayBinding) == _vao.ObjectHandle);
            // In the hands of a skilled evildoer, the crash that would result from this could read arbitrary memory.
            // Just to be clear, that's bad.
            if (GL.GetInteger(GetPName.ElementArrayBufferBinding) == 0)
                throw new Exception("Somehow, ElementArrayBufferBinding == 0");
            GL.DrawElements((PrimitiveType) topology, count, DrawElementsType.UnsignedShort, offset);
            _pal.CheckGlError();
            _pal.LastGLDrawCalls += 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyFrom(IGPURenderState other)
        {
            GLRenderState grs = (GLRenderState) other;
            _renderTarget = grs._renderTarget;
            _program = grs._program;
            _vao = grs._vao;
            _stencil = grs._stencil;
            _blend = grs._blend;
            _depth = grs._depth;
            _scissor = grs._scissor;
            _viewport = grs._viewport;
            _colourDepthMask = grs._colourDepthMask;
            _cullFace = grs._cullFace;
            _textures.Clear();
            foreach (var kvp in grs._textures)
            {
                _textures[kvp.Key] = kvp.Value;
            }
            _uniformBuffers.Clear();
            foreach (var kvp in grs._uniformBuffers)
            {
                _uniformBuffers[kvp.Key] = kvp.Value;
            }
            if (_pal._currentRenderState == other)
            {
                _pal._currentRenderState = this;
            }
            else if (_pal._currentRenderState == this)
            {
                _pal._currentRenderState = null;
            }
        }
    }
}
