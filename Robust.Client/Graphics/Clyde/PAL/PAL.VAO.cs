using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Shared.Utility;
using ES20 = OpenToolkit.Graphics.ES20;

namespace Robust.Client.Graphics.Clyde;

internal partial class PAL
{
    public GPUVertexArrayObject CreateVAO(string? name) => new GLVAOBase(this, name);

    internal sealed class GLVAOBase : GPUVertexArrayObject
    {
        private readonly PAL _pal;
        public uint ObjectHandle { get; private set; }
        internal bool IndexBufferIsBound { get; private set; }

        public GLVAOBase(PAL pal, string? name)
        {
            _pal = pal;
            ObjectHandle = pal._hasGL.GenVertexArray();
            pal.CheckGlError();
            pal._hasGL.ObjectLabelMaybe(ObjectLabelIdentifier.VertexArray, ObjectHandle, name);
        }

        public override GPUBuffer? IndexBuffer
        {
            set
            {
                var valueAdj = ((PAL.GLBuffer?) value)?.ObjectHandle ?? 0;
                IndexBufferIsBound = valueAdj != 0;
                if (_pal._backupVAO != ObjectHandle)
                {
                    _pal._hasGL.BindVertexArray(ObjectHandle);
                    _pal.CheckGlError();
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, valueAdj);
                    _pal.CheckGlError();
                    _pal._hasGL.BindVertexArray(_pal._backupVAO);
                    _pal.CheckGlError();
                }
                else
                {
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, valueAdj);
                    _pal.CheckGlError();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void SetVertexAttrib(int index, GPUVertexAttrib? value)
        {
            if (_pal._backupVAO != ObjectHandle)
            {
                _pal._hasGL.BindVertexArray(ObjectHandle);
                _pal.CheckGlError();
            }
            if (value != null)
            {
                var info = value!.Value;
                var buffer = (GLBuffer) info.Buffer;
                // Same basic premise as the render state EBO check.
                if (buffer.ObjectHandle == 0)
                    throw new Exception("Attempting to bind vertex attrib to buffer 0");
                buffer.Use(BufferTarget.ArrayBuffer);
                GL.VertexAttribPointer(index, info.Size, (VertexAttribPointerType) info.Component, info.Normalized, info.Stride, (nint) info.Offset);
                _pal.CheckGlError();
                GL.EnableVertexAttribArray(index);
                _pal.CheckGlError();
            }
            else
            {
                GL.DisableVertexAttribArray(index);
                _pal.CheckGlError();
            }
            if (_pal._backupVAO != ObjectHandle)
            {
                _pal._hasGL.BindVertexArray(_pal._backupVAO);
                _pal.CheckGlError();
            }
        }

        protected override void DisposeImpl()
        {
            if (_pal.IsMainThread())
            {
                // Main thread, do direct GL deletion.
                _pal.DeleteVAO(ObjectHandle);
            }
            else
            {
                // Finalizer thread
                _pal._vaoDisposeQueue.Enqueue(ObjectHandle);
            }
            ObjectHandle = 0;
        }
    }
}
