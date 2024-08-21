using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Robust.Shared.Maths;
using Vector3 = Robust.Shared.Maths.Vector3;
using Vector4 = Robust.Shared.Maths.Vector4;

namespace Robust.Client.Graphics;

/// <summary>
/// Represents a shader program on the GPU.
/// </summary>
[PublicAPI]
public abstract class GPUShaderProgram : GPUResource
{
    /// <summary>Returns the uniform location for the given uniform name.</summary>
    public abstract bool TryGetUniform(string name, out int index);

    /// <summary>Returns the uniform location for the given uniform name.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetUniform(string name)
    {
        if (!TryGetUniform(name, out var result))
        {
            ThrowCouldNotGetUniform(name);
        }

        return result;
    }

    protected static void ThrowCouldNotGetUniform(string name)
    {
        throw new ArgumentException($"Could not get uniform \"{name}\"!");
    }

    /// <summary>Gets the texture unit assigned to a sampler uniform.
    /// Technically, this is redundant, as it comes from the source.
    /// However, unlike uniform blocks (which are only useful when global), there's good reason to dynamically allocate these.
    /// So it's possible (and happens in Clyde) that an API will allocate these for you and expect you to figure it out.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetTextureUnit(string name)
    {
        if (!TryGetTextureUnit(name, out var result))
            throw new ArgumentException($"Uniform \"{name}\" not assigned a texture unit!");

        return result;
    }

    /// <summary>Gets the texture unit assigned to a sampler uniform.
    /// Technically, this is redundant, as it comes from the source.
    /// However, unlike uniform blocks (which are only useful when global), there's good reason to dynamically allocate these.
    /// So it's possible (and happens in Clyde) that an API will allocate these for you and expect you to figure it out.
    /// </summary>
    public abstract bool TryGetTextureUnit(string name, out int index);

    // -- SetUniformMaybe --

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(string uniformName, int value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(string uniformName, float value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(string uniformName, float[] value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(string uniformName, in Vector2 value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(string uniformName, in Vector2i value) => SetUniformMaybe(uniformName, (Vector2) value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(string uniformName, Vector2[] value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(string uniformName, in Vector3 value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(string uniformName, in Vector4 value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(string uniformName, in Matrix3x2 value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(string uniformName, in Matrix4 value, bool transpose = true)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value, transpose);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(string uniformName, in Color value, bool convertToLinear = true)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value, convertToLinear);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(string uniformName, bool[] value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    // -- SetUniform --

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(string uniformName, int value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(string uniformName, float value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(string uniformName, float[] value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(string uniformName, in Vector2 value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(string uniformName, in Vector2i value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(string uniformName, Vector2[] value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(string uniformName, Vector3 value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(string uniformName, Vector4 value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(string uniformName, Matrix3x2 value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(string uniformName, Matrix4 value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(string uniformName, Color value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(string uniformName, bool[] value) => SetUniformDirect(GetUniform(uniformName), value);

    // -- SetUniformDirect --

    public abstract void SetUniformDirect(int uniformId, int value);
    public abstract void SetUniformDirect(int uniformId, float value);
    public abstract void SetUniformDirect(int uniformId, float[] value);
    public abstract void SetUniformDirect(int uniformId, in Vector2 value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformDirect(int uniformId, in Vector2i value) => SetUniformDirect(uniformId, (Vector2) value);
    public abstract void SetUniformDirect(int uniformId, Vector2[] value);
    public abstract void SetUniformDirect(int uniformId, in Vector3 value);
    public abstract void SetUniformDirect(int uniformId, in Vector4 value);
    public abstract void SetUniformDirect(int uniformId, in Matrix3x2 value);
    public abstract void SetUniformDirect(int uniformId, in Matrix4 value, bool transpose = true);
    public abstract void SetUniformDirect(int uniformId, in Color value, bool convertToLinear = true);
    public abstract void SetUniformDirect(int uniformId, bool[] value);

    // Added functions need to be added to the string SetUniform/SetUniformMaybe functions here,
    //  and also to the InternedUniform variants in PAL.GLShaderProgram.

    public sealed class Source
    {
        /// <summary>Vertex source.</summary>
        public string VertexSource { get; }

        /// <summary>Fragment source.</summary>
        public string FragmentSource { get; }

        /// <summary>Maps attributes to their locations.</summary>
        public Dictionary<string, uint> AttribLocations { get; } = new();

        /// <summary>Maps sampler uniforms to texture units.</summary>
        public Dictionary<string, int> SamplerUnits { get; } = new();

        /// <summary>Maps uniform block names to their binding points. This is ignored if the GPU doesn't support uniform buffers (Ironlake/GLES2).</summary>
        public Dictionary<string, int> UniformBlockBindings { get; } = new();

        public Source(string vertexSource, string fragmentSource)
        {
            VertexSource = vertexSource;
            FragmentSource = fragmentSource;
        }
    }
}
