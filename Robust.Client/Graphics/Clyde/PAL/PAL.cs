using System.Runtime.CompilerServices;
using System.Threading;
using Robust.Shared.Configuration;
using Robust.Shared.Log;

namespace Robust.Client.Graphics.Clyde;

/// <summary>'Sanity layer' over GL, windowing, etc.</summary>
internal sealed partial class PAL : IGPUAbstraction
{
    /// <summary>TODO: This should be moved to Clyde.GLContext when that's migrated to PAL.</summary>
    internal GLWrapper _hasGL = default!;
    internal Thread? _gameThread;
    internal ISawmill _sawmillOgl = default!;

    public bool HasPrimitiveRestart => _hasGL.PrimitiveRestart;
    public bool HasSrgb => _hasGL.Srgb;
    public bool HasFloatFramebuffers => _hasGL.FloatFramebuffers;
    public bool HasUniformBuffers => _hasGL.UniformBuffers;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsMainThread()
    {
        return Thread.CurrentThread == _gameThread;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void CheckGlError([CallerFilePath] string? path = null, [CallerLineNumber] int line = default)
    {
        _hasGL.CheckGlError(path, line);
    }
}
