using System.Threading;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Robust.Client.Graphics.Clyde;

/// <summary>'Sanity layer' over GL, windowing, etc.</summary>
internal sealed partial class PAL : IGPUAbstraction
{
    /// <summary>TODO: This should be moved to Clyde.GLContext when that's migrated to PAL.</summary>
    internal GLWrapper _hasGL = default!;
    internal Thread? _gameThread;
    internal ISawmill _sawmillOgl = default!;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal bool IsMainThread()
    {
        return Thread.CurrentThread == _gameThread;
    }
}
