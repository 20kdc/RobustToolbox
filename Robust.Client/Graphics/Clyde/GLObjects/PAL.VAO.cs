using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Shared.Utility;
using ES20 = OpenToolkit.Graphics.ES20;

namespace Robust.Client.Graphics.Clyde;

internal partial class PAL
{
    GLVAOBase? _currentVAO = null;

    GPUVertexArrayObject IGPUAbstraction.CreateVAO(string? name) => CreateVAO(name);

    /// <summary>Creates a Vertex Array Object.</summary>
    public GLVAOBase CreateVAO(string? name)
    {
        if (_hasGL.VertexArrayObject)
        {
            return new GLVAOCore(this, name);
        }
        else
        {
            DebugTools.Assert(_hasGL.VertexArrayObjectOes);
            return new GLVAOExtension(this, name);
        }
    }

    private sealed class GLVAOCore : GLVAOBase
    {
        public GLVAOCore(PAL pal, string? name = null) : base(pal)
        {
            ObjectHandle = (uint) GL.GenVertexArray();
            pal.CheckGlError();

            pal._hasGL.ObjectLabelMaybe(ObjectLabelIdentifier.VertexArray, ObjectHandle, name);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Use()
        {
            if (_pal._currentVAO == this)
            {
                return;
            }

            DebugTools.Assert(ObjectHandle != 0);

            _pal._currentVAO = this;
            GL.BindVertexArray(ObjectHandle);
            _pal.CheckGlError();
        }

        protected override void Delete()
        {
            GL.DeleteVertexArray(ObjectHandle);
        }
    }

    private sealed class GLVAOExtension : GLVAOBase
    {
        public GLVAOExtension(PAL pal, string? name = null) : base(pal)
        {
            ObjectHandle = (uint) ES20.GL.Oes.GenVertexArray();
            pal.CheckGlError();

            pal._hasGL.ObjectLabelMaybe(ObjectLabelIdentifier.VertexArray, ObjectHandle, name);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Use()
        {
            if (_pal._currentVAO == this)
            {
                return;
            }

            DebugTools.Assert(ObjectHandle != 0);

            _pal._currentVAO = this;
            ES20.GL.Oes.BindVertexArray(ObjectHandle);
            _pal.CheckGlError();
        }

        protected override void Delete()
        {
            ES20.GL.Oes.DeleteVertexArray(ObjectHandle);
        }
    }
}

/// <summary>
/// Base class for actual VAO implementations.
/// This is due to differences between the OES extension and regular endpoints.
/// </summary>
[Virtual]
internal abstract class GLVAOBase : GPUVertexArrayObject
{
    protected readonly PAL _pal;
    public uint ObjectHandle { get; protected set; }

    public GLVAOBase(PAL pal)
    {
        _pal = pal;
    }

    public override GPUBuffer? IndexBuffer
    {
        set
        {
            Use();
            if (value != null)
            {
                ((PAL.GLBuffer) value).Use(BufferTarget.ElementArrayBuffer);
            }
            else
            {
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SetVertexAttrib(int index, GPUVertexAttrib? value)
    {
        Use();
        if (value != null)
        {
            var info = value!.Value;
            ((PAL.GLBuffer) info.Buffer).Use(BufferTarget.ArrayBuffer);
            GL.VertexAttribPointer(index, info.Size, (VertexAttribPointerType) info.Component, info.Normalized, info.Stride, (nint) info.Offset);
            GL.EnableVertexAttribArray(index);
        }
        else
        {
            GL.DisableVertexAttribArray(index);
        }
    }

    protected override void DisposeImpl()
    {
        if (_pal.IsMainThread())
        {
            // Main thread, do direct GL deletion.
            Delete();
            _pal.CheckGlError();
        }
        else
        {
            // Finalizer thread
            _pal._vaoDisposeQueue.Enqueue(ObjectHandle);
        }
        ObjectHandle = 0;
    }

    public abstract void Use();

    protected abstract void Delete();
}
