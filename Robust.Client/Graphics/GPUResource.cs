using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using JetBrains.Annotations;
using OpenToolkit.Graphics.ES20;

namespace Robust.Client.Graphics;

/// <summary>
/// GPU resources subclass this, except for OwnedTexture (as it lives in the Texture hierarchy).
/// It implements single-dispose logic.
/// </summary>
[PublicAPI]
public abstract class GPUResource : IDisposable
{
    /// <summary>Guard to prevent this object being disposed multiple times.</summary>
    private int _disposed = 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            DisposeImpl();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Actual dispose implementation. This will only ever be called once.</summary>
    protected abstract void DisposeImpl();

    ~GPUResource()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            DisposeImpl();
    }
}
