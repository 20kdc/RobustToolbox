using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using Vector3 = Robust.Shared.Maths.Vector3;
using Vector4 = Robust.Shared.Maths.Vector4;

namespace Robust.Client.Graphics.Clyde;

internal partial class PAL
{
    /// <summary>
    /// Currently bound shader program. PAL rebinds this when a draw call or uniform set requires it.
    /// This was separated from render state to reduce uniform set overhead.
    /// </summary>
    internal GLShaderProgram? _currentProgram = null;

    private ulong _uniformBufferVersion = 1;

    public ulong AllocateUniformBufferVersion()
    {
        return _uniformBufferVersion++;
    }

    GPUShaderProgram IGPUAbstraction.Compile(GPUShaderProgram.Source source, string? name) => new GLShaderProgram(this, source, name);
}

/// <summary>
///     This is an utility class. It does not check whether the OpenGL state machine is set up correctly.
///     You've been warned:
///     using things like <see cref="SetUniformTexture" /> if this buffer isn't bound WILL mess things up!
/// </summary>
internal sealed class GLShaderProgram : GPUShaderProgram
{
    private readonly sbyte?[] _uniformIntCache = new sbyte?[InternedUniform.UniCount];
    private readonly Dictionary<string, int> _uniformCache = new();
    private readonly Dictionary<string, int> _textureUnits = new();
    /// <summary>When uniform buffers are not supported, there is a key here for each UBO, containing the buffer version (defaults to 0)</summary>
    internal Dictionary<int, ulong> UniformBufferVersions { get; } = new();
    public uint Handle = 0;
    public string? Name { get; }
    private readonly PAL _pal;

    public GLShaderProgram(PAL clyde, Source source, string? name = null)
    {
        _pal = clyde;
        Name = name;

        string vertexHeader = clyde._hasGL.ShaderHeader + "#define VERTEX_SHADER\n";
        string fragmentHeader = clyde._hasGL.ShaderHeader + "#define FRAGMENT_SHADER\n";

        if (!clyde._hasGL.HasVaryingAttribute)
        {
            // GLES2 uses the (IMO much more consistent & header-friendly) varying/attribute qualifiers.
            // GLES3 and GL3 in general doesn't. Smooth it out.
            // This used to be part of z-library.glsl, but was moved into PAL since Content may get to use this.
            vertexHeader += "#define texture2D texture\n";
            fragmentHeader += "#define texture2D texture\n";

            vertexHeader += "#define varying out\n";
            vertexHeader += "#define attribute in\n";

            fragmentHeader += "#define varying in\n";
            // For some reason, gl_FragColor isn't available on these targets.
            // Instead, you're supposed to do some sorta dance where you bind outputs to fragment colours.
            // See, say, OpenGL ES 3.2 spec, pages 376-377.
            fragmentHeader += "#define gl_FragColor palGLFragColor\n";
            fragmentHeader += "out highp vec4 palGLFragColor;\n";
        }

        uint vertexShader = CompileShader(ShaderType.VertexShader, vertexHeader + source.VertexSource);
        try
        {
            uint fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentHeader + source.FragmentSource);
            try
            {
                Handle = (uint) GL.CreateProgram();
                clyde.CheckGlError();
                if (Name != null)
                {
                    clyde._hasGL.ObjectLabelMaybe(ObjectLabelIdentifier.Program, Handle, Name);
                }

                GL.AttachShader(Handle, vertexShader);
                clyde.CheckGlError();

                GL.AttachShader(Handle, fragmentShader);
                clyde.CheckGlError();

                foreach (var pair in source.AttribLocations)
                {
                    // OpenGL 3.1 is ass and doesn't allow you to specify layout(location = X) in shaders.
                    // So we have to manually do it here.
                    // Ugh.

                    // Reply (since KERB moves this block around): Oh no, the footgun's missing. Anyway...
                    GL.BindAttribLocation(Handle, pair.Value, pair.Key);
                    clyde.CheckGlError();
                }

                GL.LinkProgram(Handle);
                clyde.CheckGlError();

                GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out var compiled);
                clyde.CheckGlError();
                if (compiled != 1)
                {
                    string log = GL.GetProgramInfoLog((int) Handle);
                    GL.DeleteProgram(Handle);
                    throw new ShaderCompilationException(log);
                }

                if (clyde._hasGL.UniformBuffers)
                {
                    foreach (var pair in source.UniformBlockBindings)
                    {
                        var index = GL.GetUniformBlockIndex(Handle, pair.Key);
                        _pal.CheckGlError();
                        GL.UniformBlockBinding((int) Handle, index, pair.Value);
                        _pal.CheckGlError();
                    }
                }
                else
                {
                    foreach (var pair in source.UniformBlockBindings)
                    {
                        UniformBufferVersions[pair.Value] = 0;
                    }
                }

                // -- After this point, everything is mostly initialized, except... --

                _pal._currentProgram = this;
                GL.UseProgram(Handle);
                clyde.CheckGlError();

                // Bind texture uniforms to units.
                // By doing this we skip having to go fetch them later.
                // By placing uniforms at preallocated spots,
                //  you can do stuff like Clyde's Main/Light units, without needing to intern uniforms.
                foreach (var pair in source.SamplerUnits)
                {
                    // The use of Add here is intentional, to catch doubly-added uniforms.
                    _textureUnits.Add(pair.Key, pair.Value);
                    SetUniformMaybe(pair.Key, pair.Value);
                }
            }
            finally
            {
                GL.DeleteShader(fragmentShader);
            }
        }
        finally
        {
            GL.DeleteShader(vertexShader);
        }

        uint CompileShader(ShaderType type, string shaderSource)
        {
            uint handle = (uint)GL.CreateShader(type);
            GL.ShaderSource((int) handle, shaderSource);
            clyde.CheckGlError();
            GL.CompileShader(handle);
            clyde.CheckGlError();

            GL.GetShader(handle, ShaderParameter.CompileStatus, out var compiled);
            clyde.CheckGlError();
            if (compiled != 1)
            {
                var message = GL.GetShaderInfoLog((int) handle);
                clyde.CheckGlError();
                GL.DeleteShader(handle);
                clyde.CheckGlError();
                File.WriteAllText("error.glsl", shaderSource);
                throw new ShaderCompilationException($"Failed to compile {type}, see error.glsl for formatted source. Error: {message}");
            }
            return handle;
        }

    }

    /// <summary>Ensures this shader program is bound.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Bind()
    {
        if (_pal._currentProgram != this)
        {
            _pal._currentProgram = this;
            GL.UseProgram(Handle);
            _pal.CheckGlError();
        }
    }

    protected override void DisposeImpl()
    {
        if (_pal.IsMainThread())
        {
            // Main thread, do direct GL deletion.
            _pal.DeleteProgram(Handle);
        }
        else
        {
            // Finalizer thread
            _pal._programDisposeQueue.Enqueue(Handle);
        }
        Handle = 0;
    }

    public int GetUniform(InternedUniform id)
    {
        if (!TryGetUniform(id, out var result))
        {
            ThrowCouldNotGetUniform($"[id {id}]");
        }

        return result;
    }

    public override bool TryGetUniform(string name, out int index)
    {
        DebugTools.Assert(Handle != 0);

        if (_uniformCache.TryGetValue(name, out index))
        {
            return index != -1;
        }

        index = GL.GetUniformLocation(Handle, name);
        _pal.CheckGlError();
        _uniformCache.Add(name, index);
        return index != -1;
    }

    /// <summary>Gets the texture unit assigned to a sampler uniform.</summary>
    public override bool TryGetTextureUnit(string name, out int index) => _textureUnits.TryGetValue(name, out index);

    public bool TryGetUniform(InternedUniform id, out int index)
    {
        DebugTools.Assert(Handle != 0);

        var value = _uniformIntCache[id.Index];
        if (value.HasValue)
        {
            index = value.Value;
            return index != -1;
        }

        return InitIntUniform(id, out index);
    }

    private bool InitIntUniform(InternedUniform id, out int index)
    {
        string name = id.Name;

        index = GL.GetUniformLocation(Handle, name);
        _pal.CheckGlError();
        _uniformIntCache[id.Index] = (sbyte)index;
        return index != -1;
    }

    public bool HasUniform(string name) => TryGetUniform(name, out _);
    public bool HasUniform(InternedUniform id) => TryGetUniform(id, out _);

    // -- SetUniformMaybe --

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(InternedUniform uniformName, int value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(InternedUniform uniformName, float value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(InternedUniform uniformName, float[] value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(InternedUniform uniformName, in Vector2 value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(InternedUniform uniformName, in Vector2i value) => SetUniformMaybe(uniformName, (Vector2) value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(InternedUniform uniformName, Vector2[] value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(InternedUniform uniformName, in Vector3 value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(InternedUniform uniformName, in Vector4 value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(InternedUniform uniformName, in Matrix3x2 value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(InternedUniform uniformName, in Matrix4 value, bool transpose = true)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value, transpose);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(InternedUniform uniformName, in Color value, bool convertToLinear = true)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value, convertToLinear);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniformMaybe(InternedUniform uniformName, bool[] value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    // -- SetUniform --

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(InternedUniform uniformName, int value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(InternedUniform uniformName, float value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(InternedUniform uniformName, float[] value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(InternedUniform uniformName, in Vector2 value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(InternedUniform uniformName, in Vector2i value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(InternedUniform uniformName, Vector2[] value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(InternedUniform uniformName, Vector3 value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(InternedUniform uniformName, Vector4 value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(InternedUniform uniformName, Matrix3x2 value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(InternedUniform uniformName, Matrix4 value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(InternedUniform uniformName, Color value) => SetUniformDirect(GetUniform(uniformName), value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetUniform(InternedUniform uniformName, bool[] value) => SetUniformDirect(GetUniform(uniformName), value);

    // -- SetUniformDirect --

    public override void SetUniformDirect(int uniformId, int integer)
    {
        Bind();
        GL.Uniform1(uniformId, integer);
        _pal.CheckGlError();
    }

    public override void SetUniformDirect(int uniformId, float single)
    {
        Bind();
        GL.Uniform1(uniformId, single);
        _pal.CheckGlError();
    }

    public override void SetUniformDirect(int uniformId, float[] singles)
    {
        Bind();
        GL.Uniform1(uniformId, singles.Length, singles);
        _pal.CheckGlError();
    }

    public override void SetUniformDirect(int slot, in Vector2 vector)
    {
        Bind();
        unsafe
        {
            fixed (Vector2* ptr = &vector)
            {
                GL.Uniform2(slot, 1, (float*)ptr);
                _pal.CheckGlError();
            }
        }
    }

    public override void SetUniformDirect(int slot, Vector2[] vectors)
    {
        Bind();
        unsafe
        {
            fixed (Vector2* ptr = &vectors[0])
            {
                GL.Uniform2(slot, vectors.Length, (float*)ptr);
                _pal.CheckGlError();
            }
        }
    }

    public override void SetUniformDirect(int slot, in Vector3 vector)
    {
        Bind();
        unsafe
        {
            fixed (Vector3* ptr = &vector)
            {
                GL.Uniform3(slot, 1, (float*)ptr);
                _pal.CheckGlError();
            }
        }
    }

    public override void SetUniformDirect(int slot, in Vector4 vector)
    {
        Bind();
        unsafe
        {
            fixed (Vector4* ptr = &vector)
            {
                GL.Uniform4(slot, 1, (float*)ptr);
                _pal.CheckGlError();
            }
        }
    }

    public override unsafe void SetUniformDirect(int slot, in Matrix3x2 value)
    {
        Bind();
        // We put the rows of the input matrix into the columns of our GPU matrices
        // this transpose is required, as in C#, we premultiply vectors with matrices
        // (vM) while GL postmultiplies vectors with matrices (Mv); however, since
        // the Matrix3x2 data is stored row-major, and GL uses column-major, the
        // memory layout is the same (or would be, if Matrix3x2 didn't have an
        // implicit column)
        var buf = stackalloc float[9]{
            value.M11, value.M12, 0,
            value.M21, value.M22, 0,
            value.M31, value.M32, 1
        };
        GL.UniformMatrix3(slot, 1, false, (float*)buf);
        _pal.CheckGlError();
    }

    public override unsafe void SetUniformDirect(int uniformId, in Matrix4 value, bool transpose=true)
    {
        Bind();
        if (transpose)
        {
            Matrix4 tmpTranspose = value;
            // transposition not supported on GLES2, & no access to _hasGLES
            tmpTranspose.Transpose();
            GL.UniformMatrix4(uniformId, 1, false, (float*) &tmpTranspose);
            _pal.CheckGlError();
        }
        else
        {
            unsafe
            {
                fixed (Matrix4* ptr = &value)
                {
                    GL.UniformMatrix4(uniformId, 1, false, (float*) ptr);
                    _pal.CheckGlError();
                }
            }
        }
    }

    public override void SetUniformDirect(int slot, in Color color, bool convertToLinear=true)
    {
        Bind();

        var converted = color;
        if (convertToLinear)
        {
            converted = Color.FromSrgb(color);
        }

        unsafe
        {
            GL.Uniform4(slot, 1, (float*) &converted);
            _pal.CheckGlError();
        }
    }

    public override void SetUniformDirect(int uniformId, bool[] bools)
    {
        Bind();

        Span<int> intBools = stackalloc int[bools.Length];

        for (var i = 0; i < bools.Length; i++)
        {
            intBools[i] = bools[i] ? 1 : 0;
        }

        unsafe
        {
            fixed (int* intBoolsPtr = intBools)
            {
                GL.Uniform1(uniformId, bools.Length, intBoolsPtr);
                _pal.CheckGlError();
            }
        }
    }
}
