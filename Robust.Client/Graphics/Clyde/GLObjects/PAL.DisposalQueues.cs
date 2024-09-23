using OpenToolkit.Graphics.OpenGL4;
using System.Collections.Concurrent;
using ES20 = OpenToolkit.Graphics.ES20;

namespace Robust.Client.Graphics.Clyde;

internal sealed partial class PAL
{
    internal readonly ConcurrentQueue<GLHandle> _textureDisposeQueue = new();
    internal readonly ConcurrentQueue<uint> _bufferDisposeQueue = new();
    internal readonly ConcurrentQueue<uint> _vaoDisposeQueue = new();

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

        if (_hasGL.VertexArrayObject)
        {
            while (_vaoDisposeQueue.TryDequeue(out var handle))
            {
                GL.DeleteVertexArray(handle);
                _hasGL.CheckGlError();
            }
        }
        else if (_hasGL.VertexArrayObjectOes)
        {
            while (_vaoDisposeQueue.TryDequeue(out var handle))
            {
                ES20.GL.Oes.DeleteVertexArray(handle);
                _hasGL.CheckGlError();
            }
        }
    }
}
