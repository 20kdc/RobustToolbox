using OpenToolkit.Graphics.OpenGL4;
using Robust.Shared.Utility;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ES20 = OpenToolkit.Graphics.ES20;

namespace Robust.Client.Graphics.Clyde;

internal sealed partial class PAL
{
    internal readonly ConcurrentQueue<GLHandle> _textureDisposeQueue = new();
    internal readonly ConcurrentQueue<uint> _bufferDisposeQueue = new();
    internal readonly ConcurrentQueue<uint> _programDisposeQueue = new();
    internal readonly ConcurrentQueue<uint> _vaoDisposeQueue = new();
    internal readonly ConcurrentQueue<RenderTexture> _renderTextureDisposeQueue = new();


    // Backup bindings.
    // These match the state in GLRenderState.
    // They go out of sync with the GL for temporary operations and are then immediately restored.
    // They are also reset during disposal if necessary.
    internal uint _backupProgram;
    private uint _backupVAO;
    internal PAL.RenderTargetBase _backupRenderTarget = default!;

    /// <summary>To be called *only* from GLRenderState</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DCUseProgram(uint program)
    {
        GL.UseProgram(program);
        _hasGL.CheckGlError();
        _backupProgram = program;
    }
    /// <summary>To be called *only* from GLRenderState</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DCBindVAO(uint vao)
    {
        _hasGL.BindVertexArray(vao);
        _hasGL.CheckGlError();
        _backupVAO = vao;
    }
    /// <summary>To be called *only* from GLRenderState</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DCBindRenderTarget(RenderTargetBase rt)
    {
        BindRenderTargetImmediate(rt);
        _backupRenderTarget = rt;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void DeleteProgram(uint program)
    {
        if (_backupProgram == program)
            _backupProgram = 0;
        GL.DeleteProgram(program);
        _hasGL.CheckGlError();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void DeleteVAO(uint vao)
    {
        if (_backupVAO == vao)
            _backupVAO = 0;
        _hasGL.DeleteVertexArray(vao);
        _hasGL.CheckGlError();
    }

    private void DeleteRenderTexture(RenderTexture renderTarget)
    {
        DebugTools.Assert(renderTarget.FramebufferHandle != default);

        GL.DeleteFramebuffer(renderTarget.FramebufferHandle.Handle);
        renderTarget.FramebufferHandle = default;
        CheckGlError();
        renderTarget.Texture!.Dispose();

        if (renderTarget.DepthStencilHandle != default)
        {
            GL.DeleteRenderbuffer(renderTarget.DepthStencilHandle.Handle);
            CheckGlError();
        }

        //GC.RemoveMemoryPressure(renderTarget.MemoryPressure);
    }

    /// <summary>Disposes of dead resources.</summary>
    internal void FlushDispose()
    {
        while (_renderTextureDisposeQueue.TryDequeue(out var handle))
        {
            DeleteRenderTexture(handle);
        }
        while (_textureDisposeQueue.TryDequeue(out var handle))
        {
            GL.DeleteTexture(handle.Handle);
            _hasGL.CheckGlError();
        }
        while (_bufferDisposeQueue.TryDequeue(out var handle))
        {
            GL.DeleteBuffer(handle);
            _hasGL.CheckGlError();
        }
        while (_programDisposeQueue.TryDequeue(out var handle))
        {
            DeleteProgram(handle);
        }
        while (_vaoDisposeQueue.TryDequeue(out var handle))
        {
            DeleteVAO(handle);
        }
    }
}
