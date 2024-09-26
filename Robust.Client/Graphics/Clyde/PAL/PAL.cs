using System.Runtime.CompilerServices;
using System.Threading;
using Robust.Shared.Configuration;
using Robust.Shared.Log;

namespace Robust.Client.Graphics.Clyde;

/// <summary>'Sanity layer' over GL, windowing, etc.</summary>
internal sealed partial class PAL : IGPUAbstraction
{
    internal Thread? _gameThread;
    internal ISawmill _sawmillOgl = default!;
    internal ISawmill _sawmillWin = default!;

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
