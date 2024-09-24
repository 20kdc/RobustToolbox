using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Robust.Shared.Graphics;
using Robust.Shared.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Robust.Client.Graphics;

public interface IGPUAbstraction
{
    // <summary>If the GPU has primitive restart (index 65535 resets primitive state machine)</summary>
    bool HasPrimitiveRestart { get; }

    OwnedTexture LoadTextureFromPNGStream(Stream stream, string? name = null,
        TextureLoadParameters? loadParams = null)
    {
        // Load using Rgba32.
        using var image = Image.Load<Rgba32>(stream);

        return LoadTextureFromImage(image, name, loadParams);
    }

    OwnedTexture LoadTextureFromImage<T>(Image<T> image, string? name = null,
        TextureLoadParameters? loadParams = null) where T : unmanaged, IPixel<T>;

    /// <summary>
    ///     Creates a blank texture of the specified parameters.
    ///     This texture can later be modified using <see cref="OwnedTexture.SetSubImage{T}"/>
    /// </summary>
    /// <param name="size">The size of the new texture, in pixels.</param>
    /// <param name="name">A name for the texture that can show up in debugging tools like renderdoc.</param>
    /// <param name="loadParams">
    ///     Load parameters for the texture describing stuff such as sample mode.
    /// </param>
    /// <typeparam name="T">
    ///     The type of pixels to "store" in the texture.
    ///     This is the same type you should pass to <see cref="OwnedTexture.SetSubImage{T}"/>,
    ///     and also determines how the texture is stored internally.
    /// </typeparam>
    /// <returns>
    ///     An owned, mutable texture object.
    /// </returns>
    OwnedTexture CreateBlankTexture<T>(
        Vector2i size,
        string? name = null,
        in TextureLoadParameters? loadParams = null)
        where T : unmanaged, IPixel<T>;

    /// <summary>Creates a buffer on the GPU with the given contents. Buffer objects store vertex, index, and uniform (if not GLES2) data. They are highly mutable and can be reallocated at will.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    GPUBuffer CreateBuffer(ReadOnlySpan<byte> data, GPUBuffer.Usage usage, string? name = null);

    /// <summary>Creates a buffer on the GPU with the given contents. Buffer objects store vertex, index, and uniform (if not GLES2) data. They are highly mutable and can be reallocated at will.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    GPUBuffer CreateBuffer<T>(ReadOnlySpan<T> data, GPUBuffer.Usage usage, string? name = null) where T : unmanaged => CreateBuffer(MemoryMarshal.AsBytes(data), usage, name);

    /// <summary>Creates a buffer on the GPU with the given contents. Buffer objects store vertex, index, and uniform (if not GLES2) data. They are highly mutable and can be reallocated at will.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    GPUBuffer CreateBuffer<T>(Span<T> data, GPUBuffer.Usage usage, string? name = null) where T : unmanaged => CreateBuffer(MemoryMarshal.AsBytes((ReadOnlySpan<T>) data), usage, name);

    /// <summary>Creates a buffer on the GPU with the given contents. Buffer objects store vertex, index, and uniform (if not GLES2) data. They are highly mutable and can be reallocated at will.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    GPUBuffer CreateBuffer<T>(in T data, GPUBuffer.Usage usage, string? name = null) where T : unmanaged => CreateBuffer(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in data)), usage, name);

    /// <summary>Creates a Vertex Array Object. This object specifies the vertex data of a draw call.</summary>
    GPUVertexArrayObject CreateVAO(string? name = null);

    /// <summary>Creates a new render state. This is similar to render passes in WebGPU, but you can do all sorts of fun stuff.</summary>
    IGPURenderState CreateRenderState();

    IRenderTexture CreateRenderTarget(Vector2i size, RenderTargetFormatParameters format,
        TextureSampleParameters? sampleParameters = null, string? name = null);
}
