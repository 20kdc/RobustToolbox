using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using JetBrains.Annotations;
using OpenToolkit.Graphics.ES20;

namespace Robust.Client.Graphics;

/// <summary>
/// Represents a buffer on the GPU.
/// Notably, data cannot be retrieved from these buffers. (Technically this only applies in compatibility mode, but as that's still used to work around driver bugs...)
/// </summary>
[PublicAPI]
public abstract class GPUBuffer : IDisposable
{
    /// <summary>Guard to prevent this buffer being disposed multiple times.</summary>
    private int _disposed = 0;

    /// <summary>Writes data into the buffer at the given address. The GL will verify the access is in-bounds. (Internal callers beware: The target used is ArrayBuffer.)</summary>
    public abstract void WriteSubData(int start, ReadOnlySpan<byte> data);

    /// <summary>Reallocates the buffer with the given array. (Internal callers beware: The target used is ArrayBuffer.)</summary>
    public abstract void Reallocate(ReadOnlySpan<byte> data);

    /// <summary>Writes data into the buffer at the given address. The GL will verify the access is in-bounds. (Internal callers beware: The target used is ArrayBuffer.)</summary>
    public void WriteSubData<T>(int start, in T data) where T : unmanaged => WriteSubData(start, MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in data)));

    /// <summary>Reallocates the buffer with the given value. (Internal callers beware: The target used is ArrayBuffer.)</summary>
    public void Reallocate<T>(in T data) where T : unmanaged => Reallocate(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in data)));

    /// <summary>Reallocates the buffer with the given array. (Internal callers beware: The target used is ArrayBuffer.)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reallocate<T>(ReadOnlySpan<T> data) where T : unmanaged => Reallocate(MemoryMarshal.AsBytes(data));

    /// <summary>Writes data into the buffer at the given address. The GL will verify the access is in-bounds. (Internal callers beware: The target used is ArrayBuffer.)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSubData<T>(int start, ReadOnlySpan<T> data) where T : unmanaged => WriteSubData(start, MemoryMarshal.AsBytes(data));

    /// <summary>Rewrites the buffer with the given data without reallocating it. (Internal callers beware: The target used is ArrayBuffer.)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSubData<T>(ReadOnlySpan<T> data) where T : unmanaged => WriteSubData(0, MemoryMarshal.AsBytes(data));

    /// <summary>Reallocates the buffer with the given array. (Internal callers beware: The target used is ArrayBuffer.)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reallocate<T>(Span<T> data) where T : unmanaged => Reallocate(MemoryMarshal.AsBytes(data));

    /// <summary>Writes data into the buffer at the given address. The GL will verify the access is in-bounds. (Internal callers beware: The target used is ArrayBuffer.)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSubData<T>(int start, Span<T> data) where T : unmanaged => WriteSubData(start, MemoryMarshal.AsBytes(data));

    /// <summary>Rewrites the buffer with the given data without reallocating it. (Internal callers beware: The target used is ArrayBuffer.)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSubData<T>(Span<T> data) where T : unmanaged => WriteSubData(0, MemoryMarshal.AsBytes(data));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            DisposeImpl();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Actual dispose implementation. This will only ever be called once.</summary>
    protected virtual void DisposeImpl()
    {
    }

    ~GPUBuffer()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            DisposeImpl();
    }

    /// <summary>Represents the intended usage mode of the buffer.</summary>
    public enum Usage
    {
        /// <summary>Optimize for reallocation.</summary>
        StreamDraw = BufferUsageHint.StreamDraw,
        /// <summary>Optimize for writing to the buffer once and then reusing it many times.</summary>
        StaticDraw = BufferUsageHint.StaticDraw,
        /// <summary>Optimize for writing into the buffer and drawing from it repeatedly.</summary>
        DynamicDraw = BufferUsageHint.DynamicDraw,
    }
}
