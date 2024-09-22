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

        private void SetTexture(TextureUnit unit, Texture texture)
        {
            var ct = (ClydeTexture) texture;
            SetTexture(unit, ct.TextureId);
            CheckGlError();
        }

        private void SetTexture(TextureUnit unit, ClydeHandle textureId)
        {
            var glHandle = _loadedTextures[textureId].OpenGLObject;
            GL.ActiveTexture(unit);
            CheckGlError();
            GL.BindTexture(TextureTarget.Texture2D, glHandle.Handle);
            CheckGlError();
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        private void CopyRenderTextureToTexture(RenderTexture source, ClydeTexture target) {
            LoadedRenderTarget sourceLoaded = RtToLoaded(source);
            bool pause = sourceLoaded != _currentBoundRenderTarget;
            FullStoredRendererState? store = null;
            if (pause) {
                store = PushRenderStateFull();
                BindRenderTargetFull(sourceLoaded);
                CheckGlError();
            }

            GL.BindTexture(TextureTarget.Texture2D, _loadedTextures[target.TextureId].OpenGLObject.Handle);
            CheckGlError();
            GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 0, 0, source.Size.X, source.Size.Y);
            CheckGlError();

            if (pause && store != null) {
                PopRenderStateFull((FullStoredRendererState)store);
            }
        }

        private static long EstPixelSize(PixelInternalFormat format)
        {
            return format switch
            {
                PixelInternalFormat.Rgba8 => 4,
                PixelInternalFormat.Rgba16f => 8,
                PixelInternalFormat.Srgb8Alpha8 => 4,
                PixelInternalFormat.R11fG11fB10f => 4,
                PixelInternalFormat.R32f => 4,
                PixelInternalFormat.Rg32f => 8,
                PixelInternalFormat.R8 => 1,
                _ => 0
            };
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
        private BatchPrimitiveType GetQuadBatchPrimitiveType()
        {
            return _hasGL.PrimitiveRestart ? BatchPrimitiveType.TriangleFan : BatchPrimitiveType.TriangleList;
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
            _hasGL.CheckGlError(path, line);
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

        private uint GenVertexArray()
        {
            uint res = _hasGL.GenVertexArray();
            CheckGlError();
            return res;
        }

        private void BindVertexArray(uint vao)
        {
            _hasGL.BindVertexArray(vao);
            CheckGlError();
        }

        private void DeleteVertexArray(uint vao)
        {
            _hasGL.DeleteVertexArray(vao);
            CheckGlError();
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
}
