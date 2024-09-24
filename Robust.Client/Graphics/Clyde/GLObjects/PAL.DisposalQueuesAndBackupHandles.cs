using OpenToolkit.Graphics.OpenGL4;
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

    // Backup bindings.
    // These match the state in PAL.DrawCall.
    // They go out of sync with the GL for temporary operations and are then immediately restored.
    // They are also reset during disposal if necessary.
    internal uint _backupProgram;
    private uint _backupVAO;

    /// <summary>To be called *only* from PAL.DrawCall</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DCUseProgram(uint program)
    {
        GL.UseProgram(program);
        _hasGL.CheckGlError();
        _backupProgram = program;
    }
    /// <summary>To be called *only* from PAL.DrawCall</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DCBindVAO(uint vao)
    {
        _hasGL.BindVertexArray(vao);
        _hasGL.CheckGlError();
        _backupVAO = vao;
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
        GL.DeleteVertexArray(vao);
        _hasGL.CheckGlError();
    }

    /// <summary>Disposes of dead resources.</summary>
    internal void FlushDispose()
    {
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

        if (_hasGL.VertexArrayObject)
        {
            while (_vaoDisposeQueue.TryDequeue(out var handle))
            {
                if (handle == _backupVAO)
                    _backupVAO = 0;
                GL.DeleteVertexArray(handle);
                _hasGL.CheckGlError();
            }
        }
        else if (_hasGL.VertexArrayObjectOes)
        {
            while (_vaoDisposeQueue.TryDequeue(out var handle))
            {
                if (handle == _backupVAO)
                    _backupVAO = 0;
                ES20.GL.Oes.DeleteVertexArray(handle);
                _hasGL.CheckGlError();
            }
        }
    }
}
