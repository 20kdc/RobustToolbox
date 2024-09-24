using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Shared.Graphics;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using ES20 = OpenToolkit.Graphics.ES20;

namespace Robust.Client.Graphics.Clyde
{
    internal sealed partial class Clyde
    {
        private void GLClearColor(Color color)
        {
            GL.ClearColor(color.R, color.G, color.B, color.A);
            CheckGlError();
        }

        internal void SetTexture(TextureUnit unit, Texture texture)
        {
            var ct = (ClydeTexture) texture;
            GL.ActiveTexture(unit);
            CheckGlError();
            GL.BindTexture(TextureTarget.Texture2D, ct.OpenGLObject.Handle);
            CheckGlError();
            if (unit != TextureUnit.Texture0)
                GL.ActiveTexture(TextureUnit.Texture0);
            // ActiveTexture(Texture0) is essentially guaranteed to succeed.
        }

        private void CopyRenderTextureToTexture(PAL.RenderTexture source, ClydeTexture target) {
            PAL.RenderTargetBase sourceLoaded = source;
            bool pause = sourceLoaded != _currentBoundRenderTarget;
            FullStoredRendererState? store = null;
            if (pause) {
                store = PushRenderStateFull();
                BindRenderTargetFull(source);
                CheckGlError();
            }

            GL.BindTexture(TextureTarget.Texture2D, target.OpenGLObject.Handle);
            CheckGlError();
            GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 0, 0, source.Size.X, source.Size.Y);
            CheckGlError();

            if (pause && store != null) {
                PopRenderStateFull((FullStoredRendererState)store);
            }
        }

        // Sets up uniforms (It'd be nice to move this, or make some contextual stuff implicit, but things got complicated.)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetupGlobalUniformsImmediate(GLShaderProgram program, ClydeTexture? tex)
        {
            SetupGlobalUniformsImmediate(program, tex?.IsSrgb ?? false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetupGlobalUniformsImmediate(GLShaderProgram program, bool texIsSrgb)
        {
            ProjViewUBO.Apply(program);
            UniformConstantsUBO.Apply(program);
            if (!_hasGL.Srgb)
            {
                program.SetUniformMaybe("SRGB_EMU_CONFIG",
                    new Vector2(texIsSrgb ? 1 : 0, _currentBoundRenderTarget.IsSrgb ? 1 : 0));
            }
        }

        // Gets the primitive type required by QuadBatchIndexWrite.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private DrawPrimitiveTopology GetQuadBatchPrimitiveType()
        {
            return _hasGL.PrimitiveRestart ? DrawPrimitiveTopology.TriangleFan : DrawPrimitiveTopology.TriangleList;
        }

        // Gets the PrimitiveType version of GetQuadBatchPrimitiveType
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PrimitiveType GetQuadGLPrimitiveType()
        {
            return _hasGL.PrimitiveRestart ? PrimitiveType.TriangleFan : PrimitiveType.Triangles;
        }

        // Gets the amount of indices required by QuadBatchIndexWrite.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetQuadBatchIndexCount()
        {
            // PR: Need 5 indices per quad: 4 to draw the quad with triangle strips and another one as primitive restart.
            // no PR: Need 6 indices per quad: 2 triangles
            return _hasGL.PrimitiveRestart ? 5 : 6;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void QuadBatchIndexWrite(Span<ushort> indexData, ref int nIdx, ushort tIdx)
        {
            QuadBatchIndexWrite(indexData, ref nIdx, tIdx, (ushort) (tIdx + 1), (ushort) (tIdx + 2),
                (ushort) (tIdx + 3));
        }

        // Writes a quad into the index buffer. Note that the 'middle line' is from tIdx0 to tIdx2.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void QuadBatchIndexWrite(
            Span<ushort> indexData,
            ref int nIdx,
            ushort tIdx0,
            ushort tIdx1,
            ushort tIdx2,
            ushort tIdx3)
        {
            var nIdxl = nIdx;
            if (_hasGL.PrimitiveRestart)
            {
                // PJB's fancy triangle fan isolated to a quad with primitive restart
                indexData[nIdxl + 4] = PrimitiveRestartIndex;
                indexData[nIdxl + 3] = tIdx3;
                indexData[nIdxl + 2] = tIdx2;
                indexData[nIdxl + 1] = tIdx1;
                indexData[nIdxl + 0] = tIdx0;
                nIdx += 5;
            }
            else
            {
                // 20kdc's boring two separate triangles
                indexData[nIdxl + 5] = tIdx3;
                indexData[nIdxl + 4] = tIdx2;
                indexData[nIdxl + 3] = tIdx0;
                indexData[nIdxl + 2] = tIdx2;
                indexData[nIdxl + 1] = tIdx1;
                indexData[nIdxl + 0] = tIdx0;
                nIdx += 6;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckGlError([CallerFilePath] string? path = null, [CallerLineNumber] int line = default)
        {
            _pal._hasGL.CheckGlError(path, line);
        }

        // Both access and mask are specified because I like prematurely optimizing and this is the most performant.
        // And easiest.
        private unsafe void* MapFullBuffer(BufferTarget buffer, int length, BufferAccess access, BufferAccessMask mask)
        {
            DebugTools.Assert(_hasGL.AnyMapBuffer);

            void* ptr;

            if (_hasGL.MapBufferRange)
            {
                ptr = (void*) GL.MapBufferRange(buffer, IntPtr.Zero, length, mask);
                CheckGlError();
            }
            else if (_hasGL.MapBuffer)
            {
                ptr = (void*) GL.MapBuffer(buffer, access);
                CheckGlError();
            }
            else
            {
                DebugTools.Assert(_hasGL.MapBufferOes);

                ptr = (void*) ES20.GL.Oes.MapBuffer((ES20.BufferTargetArb) buffer,
                    (ES20.BufferAccessArb) BufferAccess.ReadOnly);
                CheckGlError();
            }

            return ptr;
        }

        private void UnmapBuffer(BufferTarget buffer)
        {
            DebugTools.Assert(_hasGL.AnyMapBuffer);

            if (_hasGL.MapBufferRange || _hasGL.MapBuffer)
            {
                GL.UnmapBuffer(buffer);
                CheckGlError();
            }
            else
            {
                DebugTools.Assert(_hasGL.MapBufferOes);

                ES20.GL.Oes.UnmapBuffer((ES20.BufferTarget) buffer);
                CheckGlError();
            }
        }

        private nint LoadGLProc(string name)
        {
            var proc = _glBindingsContext.GetProcAddress(name);
            if (proc == IntPtr.Zero || proc == new IntPtr(1) || proc == new IntPtr(2))
            {
                throw new InvalidOperationException($"Unable to load GL function '{name}'!");
            }

            return proc;
        }
    }

    internal sealed partial class PAL
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CheckGlError([CallerFilePath] string? path = null, [CallerLineNumber] int line = default)
        {
            _hasGL.CheckGlError(path, line);
        }
    }
}
