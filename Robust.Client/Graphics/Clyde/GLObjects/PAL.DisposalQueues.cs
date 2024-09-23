using OpenToolkit.Graphics.OpenGL4;
using System.Collections.Concurrent;

namespace Robust.Client.Graphics.Clyde;

internal sealed partial class PAL
{
    internal readonly ConcurrentQueue<GLHandle> _textureDisposeQueue = new();
    internal readonly ConcurrentQueue<uint> _bufferDisposeQueue = new();

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
    }
}
