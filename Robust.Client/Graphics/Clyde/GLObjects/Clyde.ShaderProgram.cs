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
    internal GLShaderProgram? _currentProgram;
}

/// <summary>
///     This is an utility class. It does not check whether the OpenGL state machine is set up correctly.
///     You've been warned:
///     using things like <see cref="SetUniformTexture" /> if this buffer isn't bound WILL mess things up!
/// </summary>
internal sealed class GLShaderProgram
{
    private readonly sbyte?[] _uniformIntCache = new sbyte?[InternedUniforms.UniCount];
    private readonly Dictionary<string, int> _uniformCache = new();
    public uint Handle = 0;
    public string? Name { get; }
    private readonly PAL _pal;

    public GLShaderProgram(PAL clyde, string vertexSource, string fragmentSource, (string, uint)[] attribLocations, string? name = null)
    {
        _pal = clyde;
        Name = name;

        uint vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
        try
        {
            uint fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);
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

    public void Use()
    {
        if (_pal._currentProgram == this)
        {
            return;
        }

        ForceUse();
    }

    public void ForceUse()
    {
        DebugTools.Assert(Handle != 0);

        _pal._currentProgram = this;
        GL.UseProgram(Handle);
        _pal.CheckGlError();
    }

    public void Delete()
    {
        if (Handle == 0)
        {
            return;
        }

        GL.DeleteProgram(Handle);
        _pal.CheckGlError();
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

    public int GetUniform(int id)
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

    public bool TryGetUniform(int id, out int index)
    {
        DebugTools.Assert(Handle != 0);
        DebugTools.Assert(id < InternedUniforms.UniCount);

        var value = _uniformIntCache[id];
        if (value.HasValue)
        {
            index = value.Value;
            return index != -1;
        }

        return InitIntUniform(id, out index);
    }

    private bool InitIntUniform(int id, out int index)
    {
        string name;
        switch (id)
        {
            case InternedUniforms.UniIModUV:
                name = InternedUniforms.UniModUV;
                break;
            case InternedUniforms.UniILightTexture:
                name = InternedUniforms.UniLightTexture;
                break;
            case InternedUniforms.UniIMainTexture:
                name = InternedUniforms.UniMainTexture;
                break;
            case InternedUniforms.UniIModelMatrix:
                name = InternedUniforms.UniModelMatrix;
                break;
            case InternedUniforms.UniITexturePixelSize:
                name = InternedUniforms.UniTexturePixelSize;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        index = GL.GetUniformLocation(Handle, name);
        _pal.CheckGlError();
        _uniformIntCache[id] = (sbyte)index;
        return index != -1;
    }

    public bool HasUniform(string name) => TryGetUniform(name, out _);
    public bool HasUniform(int id) => TryGetUniform(id, out _);

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
        GL.Uniform1(uniformId, integer);
        _pal.CheckGlError();
    }

    public void SetUniform(string uniformName, float single)
    {
        var uniformId = GetUniform(uniformName);
        GL.Uniform1(uniformId, single);
        _pal.CheckGlError();
    }

    public void SetUniform(int uniformName, float single)
    {
        var uniformId = GetUniform(uniformName);
        GL.Uniform1(uniformId, single);
    }

    public void SetUniform(string uniformName, float[] singles)
    {
        var uniformId = GetUniform(uniformName);
        GL.Uniform1(uniformId, singles.Length, singles);
        _pal.CheckGlError();
    }

    public void SetUniform(int uniformName, float[] singles)
    {
        var uniformId = GetUniform(uniformName);
        GL.Uniform1(uniformId, singles.Length, singles);
    }

    public void SetUniform(string uniformName, in Matrix3x2 matrix)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, matrix);
    }

    public void SetUniform(int uniformName, in Matrix3x2 matrix)
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
        GL.UniformMatrix3(slot, 1, false, (float*)buf);
        _pal.CheckGlError();
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
        GL.UniformMatrix4(uniformId, 1, false, (float*) &tmpTranspose);
        _pal.CheckGlError();
    }

    public void SetUniform(string uniformName, in Vector4 vector)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, vector);
    }

    public void SetUniform(int uniformName, in Vector4 vector)
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
                GL.Uniform4(slot, 1, (float*)ptr);
                _pal.CheckGlError();
            }
        }
    }

    public void SetUniform(string uniformName, in Color color, bool convertToLinear = true)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, color, convertToLinear);
    }

    public void SetUniform(int uniformName, in Color color, bool convertToLinear = true)
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
            GL.Uniform4(slot, 1, (float*) &converted);
            _pal.CheckGlError();
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
                GL.Uniform3(slot, 1, (float*)ptr);
                _pal.CheckGlError();
            }
        }
    }

    public void SetUniform(string uniformName, in Vector2 vector)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, vector);
    }

    public void SetUniform(int uniformName, in Vector2 vector)
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
                GL.Uniform2(slot, 1, (float*)ptr);
                _pal.CheckGlError();
            }
        }
    }

    public void SetUniform(string uniformName, Vector2[] vector)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformDirect(uniformId, vector);
    }

    public void SetUniform(int uniformName, Vector2[] vector)
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
                GL.Uniform2(slot, vectors.Length, (float*)ptr);
                _pal.CheckGlError();
            }
        }
    }

    public void SetUniformTexture(string uniformName, TextureUnit textureUnit)
    {
        var uniformId = GetUniform(uniformName);
        SetUniformTextureDirect(uniformId, textureUnit);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetUniformTextureDirect(int slot, TextureUnit value)
    {
        GL.Uniform1(slot, value - TextureUnit.Texture0);
        _pal.CheckGlError();
    }

    public void SetUniformTextureMaybe(string uniformName, TextureUnit value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformTextureDirect(slot, value);
        }
    }

    public void SetUniformTextureMaybe(int uniformName, TextureUnit value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformTextureDirect(slot, value);
        }
    }

    public void SetUniformMaybe(string uniformName, in Vector4 value)
    {
        if (TryGetUniform(uniformName, out var slot))
        {
            SetUniformDirect(slot, value);
        }
    }

    public void SetUniformMaybe(int uniformName, in Vector4 value)
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

    public void SetUniformMaybe(int uniformName, in Color value)
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

    public void SetUniformMaybe(int uniformName, in Matrix3x2 value)
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

    public void SetUniformMaybe(int uniformName, in Vector2 value)
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
