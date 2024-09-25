using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Shared.Maths;

namespace Robust.Client.Graphics.Clyde
{
    internal sealed partial class Clyde
    {
        // Sets up uniforms (It'd be nice to move this, or make some contextual stuff implicit, but things got complicated.)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetupGlobalUniformsImmediate(GLShaderProgram program, ClydeTexture? tex)
        {
            SetupGlobalUniformsImmediate(program, tex?.IsSrgb ?? false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetupGlobalUniformsImmediate(GLShaderProgram program, bool texIsSrgb)
        {
            if (!_pal.HasSrgb)
            {
                program.SetUniformMaybe("SRGB_EMU_CONFIG",
                    new Vector2(texIsSrgb ? 1 : 0, ((PAL.RenderTargetBase) _renderState.RenderTarget!).IsSrgb ? 1 : 0));
            }
        }

        // Gets the primitive type required by QuadBatchIndexWrite.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private DrawPrimitiveTopology GetQuadBatchPrimitiveType()
        {
            return _pal.HasPrimitiveRestart ? DrawPrimitiveTopology.TriangleFan : DrawPrimitiveTopology.TriangleList;
        }

        // Gets the amount of indices required by QuadBatchIndexWrite.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetQuadBatchIndexCount()
        {
            // PR: Need 5 indices per quad: 4 to draw the quad with triangle strips and another one as primitive restart.
            // no PR: Need 6 indices per quad: 2 triangles
            return _pal.HasPrimitiveRestart ? 5 : 6;
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
            if (_pal.HasPrimitiveRestart)
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
    }
}
