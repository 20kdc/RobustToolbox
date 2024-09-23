using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using JetBrains.Annotations;
using OpenToolkit.Graphics.ES20;

namespace Robust.Client.Graphics;

/// <summary>
/// Honestly, this would be better referred to as GPUMesh.
/// But I don't have that luxury since it's not quite a mesh.
/// However, if you were making a GPUMesh class, chances are you'd probably use one of these in it.
/// </summary>
[PublicAPI]
public abstract class GPUVertexArrayObject : GPUResource
{
    /// <summary>Index buffer (aka ElementArrayBuffer).</summary>
    public abstract GPUBuffer? IndexBuffer { set; }

    /// <summary>Sets a vertex attribute.</summary>
    public abstract void SetVertexAttrib(int index, GPUVertexAttrib? value);
}

[PublicAPI]
public struct GPUVertexAttrib(GPUBuffer buffer, int size, GPUVertexAttrib.Type component, bool normalized, int stride, uint offset)
{
    /// <summary>Buffer.</summary>
    public GPUBuffer Buffer = buffer;

    /// <summary>Component count.</summary>
    public int Size = size;

    /// <summary>Type.</summary>
    public Type Component = component;

    /// <summary>Normalized.</summary>
    public bool Normalized = normalized;

    /// <summary>Stride.</summary>
    public int Stride = stride;

    /// <summary>Offset.</summary>
    public uint Offset = offset;

    /// <summary>Vertex attribute component type.</summary>
    public enum Type
    {
        Byte = VertexAttribPointerType.Byte,
        UnsignedByte = VertexAttribPointerType.UnsignedByte,
        Short = VertexAttribPointerType.Short,
        UnsignedShort = VertexAttribPointerType.UnsignedShort,
        Float = VertexAttribPointerType.Float
    }
}
