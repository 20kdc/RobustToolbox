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
    public uint Handle = 0;
    public string? Name { get; }
    private readonly PAL _pal;

    public GLShaderProgram(PAL clyde, string vertexSource, string fragmentSource, (string, uint)[] attribLocations, string[] textureUniforms, string? name = null)
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

        uint vertexShader = CompileShader(ShaderType.VertexShader, vertexHeader + vertexSource);
        try
        {
            uint fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentHeader + fragmentSource);
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

                foreach (var (varName, loc) in attribLocations)
                {
                    // OpenGL 3.1 is ass and doesn't allow you to specify layout(location = X) in shaders.
                    // So we have to manually do it here.
                    // Ugh.

                    // Reply (since KERB moves this block around): Oh no, the footgun's missing. Anyway...
                    GL.BindAttribLocation(Handle, loc, varName);
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

                // -- After this point, everything is mostly initialized, except... --

                // Below code uses this since uniforms are being set.
                GL.UseProgram(Handle);
                clyde.CheckGlError();

                try
                {
                    // Bind texture uniforms to units.
                    // By doing this we skip having to go fetch them later.
                    // By placing uniforms at preallocated spots (including dummies you don't have),
                    //  you can do stuff like Clyde's Main/Light units, without needing to intern uniforms.
                    var currentTextureUnit = 0;
                    foreach (var uniform in textureUniforms)
                    {
                        // We have to still allocate the unit even if there's no uniform.
                        // The texture uniforms array is a 1:1 mapping to texture units.
                        if (uniform != "")
                        {
                            // The use of Add here is intentional, to catch doubly-added uniforms.
                            _textureUnits.Add(uniform, currentTextureUnit);
                            SetUniformMaybe(uniform, currentTextureUnit);
                        }
                        currentTextureUnit += 1;
                    }
                }
                finally
                {
                    GL.UseProgram(_pal._backupProgram);
                    clyde.CheckGlError();
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

    public int GetUniform(string name)
    {
        if (!TryGetUniform(name, out var result))
        {
            ThrowCouldNotGetUniform(name);
        }

        return result;
    }

    public int GetUniform(InternedUniform id)
    {
        if (!TryGetUniform(id, out var result))
        {
            ThrowCouldNotGetUniform($"[id {id}]");
        }

        return result;
    }

    public bool TryGetUniform(string name, out int index)
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
    public int GetTextureUnit(string name)
    {
        if (!_textureUnits.TryGetValue(name, out var result))
            throw new ArgumentException($"Uniform \"{name}\" not assigned a texture unit!");

        return result;
    }

    /// <summary>Gets the texture unit assigned to a sampler uniform.</summary>
    public bool TryGetTextureUnit(string name, out int index) => _textureUnits.TryGetValue(name, out index);

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

    public void BindBlock(string blockName, uint blockBinding)
    {
        var index = (uint) GL.GetUniformBlockIndex(Handle, blockName);
        _pal.CheckGlError();
        GL.UniformBlockBinding(Handle, index, blockBinding);
        _pal.CheckGlError();
    }

    public void SetUniform(string uniformName, int integer)
    {
        var uniformId = GetUniform(uniformName);
        if (_pal._backupProgram != Handle)
        {
            GL.UseProgram(Handle);
            _pal.CheckGlError();
            GL.Uniform1(uniformId, integer);
            _pal.CheckGlError();
            GL.UseProgram(_pal._backupProgram);
            _pal.CheckGlError();
        }
        else
        {
            GL.Uniform1(uniformId, integer);
            _pal.CheckGlError();
        }
    }

    public void SetUniform(string uniformName, float single)
    {
        var uniformId = GetUniform(uniformName);
        if (_pal._backupProgram != Handle)
        {
            GL.UseProgram(Handle);
            _pal.CheckGlError();
            GL.Uniform1(uniformId, single);
            _pal.CheckGlError();
            GL.UseProgram(_pal._backupProgram);
            _pal.CheckGlError();
        }
        else
        {
            GL.Uniform1(uniformId, single);
            _pal.CheckGlError();
        }
    }

    public void SetUniform(InternedUniform uniformName, float single)
    {
        var uniformId = GetUniform(uniformName);
        if (_pal._backupProgram != Handle)
        {
            GL.UseProgram(Handle);
            _pal.CheckGlError();
            GL.Uniform1(uniformId, single);
            _pal.CheckGlError();
            GL.UseProgram(_pal._backupProgram);
            _pal.CheckGlError();
        }
        else
        {
            GL.Uniform1(uniformId, single);
            _pal.CheckGlError();
        }
    }

    public void SetUniform(string uniformName, float[] singles)
    {
        var uniformId = GetUniform(uniformName);
        if (_pal._backupProgram != Handle)
        {
            GL.UseProgram(Handle);
            _pal.CheckGlError();
            GL.Uniform1(uniformId, singles.Length, singles);
            _pal.CheckGlError();
            GL.UseProgram(_pal._backupProgram);
            _pal.CheckGlError();
        }
        else
        {
            GL.Uniform1(uniformId, singles.Length, singles);
            _pal.CheckGlError();
        }
    }

    public void SetUniform(InternedUniform uniformName, float[] singles)
    {
        var uniformId = GetUniform(uniformName);
        if (_pal._backupProgram != Handle)
        {
            GL.UseProgram(Handle);
            _pal.CheckGlError();
            GL.Uniform1(uniformId, singles.Length, singles);
            _pal.CheckGlError();
            GL.UseProgram(_pal._backupProgram);
            _pal.CheckGlError();
        }
        else
        {
            GL.Uniform1(uniformId, singles.Length, singles);
            _pal.CheckGlError();
        }
    }

    public void SetUniform(string uniformName, in Matrix3x2 matrix)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, matrix);
    }

    public void SetUniform(InternedUniform uniformName, in Matrix3x2 matrix)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, matrix);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void SetUniformDirect(int slot, in Matrix3x2 value)
    {
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
        if (_pal._backupProgram != Handle)
        {
            GL.UseProgram(Handle);
            _pal.CheckGlError();
            GL.UniformMatrix3(slot, 1, false, (float*)buf);
            _pal.CheckGlError();
            GL.UseProgram(_pal._backupProgram);
            _pal.CheckGlError();
        }
        else
        {
            GL.UniformMatrix3(slot, 1, false, (float*)buf);
            _pal.CheckGlError();
        }
    }

    public void SetUniform(string uniformName, in Matrix4 matrix, bool transpose=true)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, matrix, transpose);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void SetUniformDirect(int uniformId, in Matrix4 value, bool transpose=true)
    {
        Matrix4 tmpTranspose = value;
        if (transpose)
        {
            // transposition not supported on GLES2, & no access to _hasGLES
            tmpTranspose.Transpose();
        }
        if (_pal._backupProgram != Handle)
        {
            GL.UseProgram(Handle);
            _pal.CheckGlError();
            GL.UniformMatrix4(uniformId, 1, false, (float*) &tmpTranspose);
            _pal.CheckGlError();
            GL.UseProgram(_pal._backupProgram);
            _pal.CheckGlError();
        }
        else
        {
            GL.UniformMatrix4(uniformId, 1, false, (float*) &tmpTranspose);
            _pal.CheckGlError();
        }
        _pal.CheckGlError();
    }

    public void SetUniform(string uniformName, in Vector4 vector)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, vector);
    }

    public void SetUniform(InternedUniform uniformName, in Vector4 vector)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, vector);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetUniformDirect(int slot, in Vector4 vector)
    {
        unsafe
        {
            fixed (Vector4* ptr = &vector)
            {
                if (_pal._backupProgram != Handle)
                {
                    GL.UseProgram(Handle);
                    _pal.CheckGlError();
                    GL.Uniform4(slot, 1, (float*)ptr);
                    _pal.CheckGlError();
                    GL.UseProgram(_pal._backupProgram);
                    _pal.CheckGlError();
                }
                else
                {
                    GL.Uniform4(slot, 1, (float*)ptr);
                    _pal.CheckGlError();
                }
            }
        }
    }

    public void SetUniform(string uniformName, in Color color, bool convertToLinear = true)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, color, convertToLinear);
    }

    public void SetUniform(InternedUniform uniformName, in Color color, bool convertToLinear = true)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, color, convertToLinear);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetUniformDirect(int slot, in Color color, bool convertToLinear=true)
    {
        var converted = color;
        if (convertToLinear)
        {
            converted = Color.FromSrgb(color);
        }

        unsafe
        {
            if (_pal._backupProgram != Handle)
            {
                GL.UseProgram(Handle);
                _pal.CheckGlError();
                GL.Uniform4(slot, 1, (float*) &converted);
                _pal.CheckGlError();
                GL.UseProgram(_pal._backupProgram);
                _pal.CheckGlError();
            }
            else
            {
                GL.Uniform4(slot, 1, (float*) &converted);
                _pal.CheckGlError();
            }
        }
    }

    public void SetUniform(string uniformName, in Vector3 vector)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, vector);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetUniformDirect(int slot, in Vector3 vector)
    {
        unsafe
        {
            fixed (Vector3* ptr = &vector)
            {
                if (_pal._backupProgram != Handle)
                {
                    GL.UseProgram(Handle);
                    _pal.CheckGlError();
                    GL.Uniform3(slot, 1, (float*)ptr);
                    _pal.CheckGlError();
                    GL.UseProgram(_pal._backupProgram);
                    _pal.CheckGlError();
                }
                else
                {
                    GL.Uniform3(slot, 1, (float*)ptr);
                    _pal.CheckGlError();
                }
            }
        }
    }

    public void SetUniform(string uniformName, in Vector2 vector)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, vector);
    }

    public void SetUniform(InternedUniform uniformName, in Vector2 vector)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, vector);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetUniformDirect(int slot, in Vector2 vector)
    {
        unsafe
        {
            fixed (Vector2* ptr = &vector)
            {
                if (_pal._backupProgram != Handle)
                {
                    GL.UseProgram(Handle);
                    _pal.CheckGlError();
                    GL.Uniform2(slot, 1, (float*)ptr);
                    _pal.CheckGlError();
                    GL.UseProgram(_pal._backupProgram);
                    _pal.CheckGlError();
                }
                else
                {
                    GL.Uniform2(slot, 1, (float*)ptr);
                    _pal.CheckGlError();
                }
            }
        }
    }

    public void SetUniform(string uniformName, Vector2[] vector)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, vector);
    }

    public void SetUniform(InternedUniform uniformName, Vector2[] vector)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, vector);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetUniformDirect(int slot, Vector2[] vectors)
    {
        unsafe
        {
            fixed (Vector2* ptr = &vectors[0])
            {
                if (_pal._backupProgram != Handle)
                {
                    GL.UseProgram(Handle);
                    _pal.CheckGlError();
                    GL.Uniform2(slot, vectors.Length, (float*)ptr);
                    _pal.CheckGlError();
                    GL.UseProgram(_pal._backupProgram);
                    _pal.CheckGlError();
                }
                else
                {
                    GL.Uniform2(slot, vectors.Length, (float*)ptr);
                    _pal.CheckGlError();
                }
            }
        }
    }

    public void SetUniformMaybe(string uniformName, in Vector4 value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    public void SetUniformMaybe(InternedUniform uniformName, in Vector4 value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    public void SetUniformMaybe(string uniformName, in Color value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    public void SetUniformMaybe(InternedUniform uniformName, in Color value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    public void SetUniformMaybe(string uniformName, in Matrix3x2 value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    public void SetUniformMaybe(string uniformName, in Matrix4 value, bool transpose=true)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value, transpose);
        }
    }

    public void SetUniformMaybe(InternedUniform uniformName, in Matrix3x2 value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    public void SetUniformMaybe(string uniformName, in Vector2 value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    public void SetUniformMaybe(string uniformName, in Vector2i value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    public void SetUniformMaybe(InternedUniform uniformName, in Vector2 value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    public void SetUniformMaybe(string uniformName, int value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            GL.Uniform1(slot, value);
            _pal.CheckGlError();
        }
    }

    public void SetUniformMaybe(string uniformName, float value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            GL.Uniform1(slot, value);
            _pal.CheckGlError();
        }
    }

    private static void ThrowCouldNotGetUniform(string name)
    {
        throw new ArgumentException($"Could not get uniform \"{name}\"!");
    }
}
