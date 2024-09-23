using OpenToolkit.Graphics.OpenGL4;
using System.Collections.Concurrent;

namespace Robust.Client.Graphics.Clyde;

internal sealed partial class PAL
{
    internal readonly ConcurrentQueue<GLHandle> _textureDisposeQueue = new();

    /// <summary>Disposes of dead resources.</summary>
    internal void FlushDispose()
    {
        while (_textureDisposeQueue.TryDequeue(out var handle))
        {
            GL.DeleteTexture(handle.Handle);
            _hasGL.CheckGlError();
        }
    }
}
