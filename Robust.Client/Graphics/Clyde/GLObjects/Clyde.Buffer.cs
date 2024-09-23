using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Shared.Utility;

namespace Robust.Client.Graphics.Clyde;

internal partial class PAL
{
    /// <summary>Creates a buffer on the GPU with the given contents. Beware: Pokes ArrayBuffer target.</summary>
    public GPUBuffer CreateBuffer(ReadOnlySpan<byte> span, GPUBuffer.Usage usage, string? name)
    {
        return new GLBuffer(this, (BufferUsageHint) usage, span, name);
    }
}

/// <summary>
///     Represents an OpenGL buffer object.
/// </summary>
[Virtual]
internal class GLBuffer : GPUBuffer
{
    private readonly PAL _pal;
    public uint ObjectHandle { get; private set; }
    public BufferUsageHint UsageHint { get; }
    public string? Name { get; }

    public GLBuffer(PAL pal, BufferUsageHint usage, string? name = null)
    {
        _pal = pal;
        Name = name;
        UsageHint = usage;

        GL.GenBuffers(1, out uint handle);
        pal.CheckGlError();
        ObjectHandle = handle;

        GL.BindBuffer(BufferTarget.ArrayBuffer, handle);
        pal.CheckGlError();

        pal._hasGL.ObjectLabelMaybe(ObjectLabelIdentifier.Buffer, ObjectHandle, name);
    }

    public GLBuffer(PAL clyde, BufferUsageHint usage, int size, string? name = null)
        : this(clyde, usage, name)
    {
        Reallocate(size);
    }

    public GLBuffer(PAL clyde, BufferUsageHint usage, ReadOnlySpan<byte> initialize,
        string? name = null)
        : this(clyde, usage, name)
    {
        Reallocate(initialize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Use(BufferTarget type)
    {
        DebugTools.Assert(ObjectHandle != 0);

        GL.BindBuffer(type, ObjectHandle);
        _pal.CheckGlError();
    }

    protected override void DisposeImpl()
    {
        if (_pal.IsMainThread())
        {
            // Main thread, do direct GL deletion.
            GL.DeleteBuffer(ObjectHandle);
            _pal.CheckGlError();
        }
        else
        {
            // Finalizer thread
            _pal._bufferDisposeQueue.Enqueue(ObjectHandle);
        }
        ObjectHandle = 0;
    }

    /// <summary>
    ///     <c>glBufferSubData</c>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteSubData(int start, ReadOnlySpan<byte> data)
    {
        Use(BufferTarget.ArrayBuffer);

        unsafe
        {
            fixed (byte* ptr = data)
            {
                GL.BufferSubData(BufferTarget.ArrayBuffer, (IntPtr) start, data.Length, (IntPtr) ptr);
            }
        }

        _pal.CheckGlError();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reallocate(ReadOnlySpan<byte> data)
    {
        Use(BufferTarget.ArrayBuffer);

        unsafe
        {
            fixed (byte* ptr = data)
            {
                GL.BufferData(BufferTarget.ArrayBuffer, data.Length, (IntPtr) ptr, UsageHint);
            }
        }

        _pal.CheckGlError();
    }

    /// <summary>
    ///     <c>glBufferData</c>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reallocate(int size)
    {
        Use(BufferTarget.ArrayBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, size, IntPtr.Zero, UsageHint);
        _pal.CheckGlError();
    }
}

/// <inheritdoc />
/// <summary>
///     Subtype of buffers so that we can have a generic constructor.
///     Functionally equivalent to <see cref="GLBuffer"/> otherwise.
/// </summary>
internal sealed class GLBuffer<T> : GLBuffer where T : unmanaged
{
    public GLBuffer(PAL clyde, BufferUsageHint usage, Span<T> initialize,
        string? name = null)
        : base(clyde, usage, MemoryMarshal.AsBytes(initialize), name)
    {
    }
}
