using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using SixLabors.ImageSharp.PixelFormats;

// ReSharper disable once IdentifierTypo
using RTCF = Robust.Client.Graphics.RenderTargetColorFormat;
using PIF = OpenToolkit.Graphics.OpenGL4.PixelInternalFormat;
using PF = OpenToolkit.Graphics.OpenGL4.PixelFormat;
using PT = OpenToolkit.Graphics.OpenGL4.PixelType;

namespace Robust.Client.Graphics.Clyde
{
    internal partial class PAL
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IRenderTexture IGPUAbstraction.CreateRenderTarget(Vector2i size, RenderTargetFormatParameters format,
            TextureSampleParameters? sampleParameters, string? name)
        {
            return CreateRenderTarget(size, format, sampleParameters, name);
        }

        internal RenderTexture CreateRenderTarget(Vector2i size, RenderTargetFormatParameters format,
            TextureSampleParameters? sampleParameters = null, string? name = null)
        {
            DebugTools.Assert(size.X != 0);
            DebugTools.Assert(size.Y != 0);

            // Cache currently bound framebuffers
            // so if somebody creates a framebuffer while drawing it won't ruin everything.
            // Note that this means _currentBoundRenderTarget goes temporarily out of sync here
            var boundDrawBuffer = GL.GetInteger(
                _hasGL.GLES2 ? GetPName.FramebufferBinding : GetPName.DrawFramebufferBinding);
            var boundReadBuffer = 0;
            if (_hasGL.ReadFramebuffer)
            {
                boundReadBuffer = GL.GetInteger(GetPName.ReadFramebufferBinding);
            }

            // Generate FBO.
            var fbo = new GLHandle(GL.GenFramebuffer());

            // Bind color attachment to FBO.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo.Handle);
            CheckGlError();

            _hasGL.ObjectLabelMaybe(ObjectLabelIdentifier.Framebuffer, fbo, name);

            var (width, height) = size;

            ClydeTexture textureObject;
            GLHandle depthStencilBuffer = default;

            // var estPixSize = 0L;

            // Color attachment.
            {
                var texture = new GLHandle(GL.GenTexture());
                CheckGlError();

                GL.BindTexture(TextureTarget.Texture2D, texture.Handle);
                CheckGlError();

                ApplySampleParameters(sampleParameters);

                var colorFormat = format.ColorFormat;
                if ((!_hasGL.Srgb) && (colorFormat == RTCF.Rgba8Srgb))
                {
                    // If SRGB is not supported, switch formats.
                    // The shaders will have to compensate.
                    // Note that a check is performed on the *original* format.
                    colorFormat = RTCF.Rgba8;
                }
                // This isn't good
                if (!_hasGL.FloatFramebuffers)
                {
                    switch (colorFormat)
                    {
                        case RTCF.R32F:
                        case RTCF.RG32F:
                        case RTCF.R11FG11FB10F:
                        case RTCF.Rgba16F:
                            _sawmillOgl.Warning("The framebuffer {0} [{1}] is trying to be floating-point when that's not supported. Forcing Rgba8.", name == null ? "[unnamed]" : name, size);
                            colorFormat = RTCF.Rgba8;
                            break;
                    }
                }

                // Make sure to specify the correct pixel type and formats even if we're not uploading any data.
                // Not doing this (just sending red/byte) is fine on desktop GL but illegal on ES.
                // @formatter:off
                var (internalFormat, pixFormat, pixType) = colorFormat switch
                {
                    RTCF.Rgba8 =>        (PIF.Rgba8,        PF.Rgba, PT.UnsignedByte),
                    RTCF.Rgba16F =>      (PIF.Rgba16f,      PF.Rgba, PT.Float),
                    RTCF.Rgba8Srgb =>    (PIF.Srgb8Alpha8,  PF.Rgba, PT.UnsignedByte),
                    RTCF.R11FG11FB10F => (PIF.R11fG11fB10f, PF.Rgb,  PT.Float),
                    RTCF.R32F =>         (PIF.R32f,         PF.Red,  PT.Float),
                    RTCF.RG32F =>        (PIF.Rg32f,        PF.Rg,   PT.Float),
                    RTCF.R8 =>           (PIF.R8,           PF.Red,  PT.UnsignedByte),
                    _ => throw new ArgumentOutOfRangeException(nameof(format.ColorFormat), format.ColorFormat, null)
                };
                // @formatter:on

                if (_hasGL.GLES2)
                {
                    (internalFormat, pixFormat, pixType) = colorFormat switch
                    {
                        RTCF.Rgba8 => (PIF.Rgba,      PF.Rgba,      PT.UnsignedByte),
                        RTCF.R8 =>    (PIF.Rgba,      PF.Rgba,      PT.UnsignedByte),
                        _ => throw new ArgumentOutOfRangeException(nameof(format.ColorFormat), format.ColorFormat, null)
                    };
                }

                // estPixSize += PAL.EstPixelSize(internalFormat);

                GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, width, height, 0, pixFormat,
                    pixType, IntPtr.Zero);
                CheckGlError();

                if (!_hasGL.GLES)
                {
                    GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                        texture.Handle,
                        0);
                }
                else
                {
                    // OpenGL ES uses a different name, and has an odd added target argument
                    GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                        TextureTarget.Texture2D, texture.Handle, 0);
                }
                CheckGlError();

                // Check on original format is NOT a bug, this is so srgb emulation works
                textureObject = GenTexture(texture, size, format.ColorFormat == RTCF.Rgba8Srgb, name == null ? null : $"{name}-color", TexturePixelType.RenderTarget);
            }

            // Depth/stencil buffers.
            if (format.HasDepthStencil)
            {
                depthStencilBuffer = new GLHandle(GL.GenRenderbuffer());
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthStencilBuffer.Handle);
                CheckGlError();

                _hasGL.ObjectLabelMaybe(ObjectLabelIdentifier.Renderbuffer, depthStencilBuffer,
                    name == null ? null : $"{name}-depth-stencil");

                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, width,
                    height);
                CheckGlError();

                // estPixSize += 4;

                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                    RenderbufferTarget.Renderbuffer, depthStencilBuffer.Handle);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.StencilAttachment,
                    RenderbufferTarget.Renderbuffer, depthStencilBuffer.Handle);
                CheckGlError();
            }

            // This should always pass but OpenGL makes it easy to check for once so let's.
            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            CheckGlError();
            DebugTools.Assert(status == FramebufferErrorCode.FramebufferComplete,
                $"new framebuffer has bad status {status}");

            // Re-bind previous framebuffers (thus _currentBoundRenderTarget is back in sync)
            GL.BindFramebuffer(
                _hasGL.GLES2 ? FramebufferTarget.Framebuffer : FramebufferTarget.DrawFramebuffer,
                boundDrawBuffer);
            CheckGlError();
            if (_hasGL.ReadFramebuffer)
            {
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, boundReadBuffer);
                CheckGlError();
            }

            // var pressure = estPixSize * size.X * size.Y;

            //GC.AddMemoryPressure(pressure);
            return new RenderTexture(size, textureObject, this, fbo, depthStencilBuffer, textureObject.IsSrgb);
        }

        /// <summary>To be called *only* from this file (with extreme care) and DCBindRenderTarget</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BindRenderTargetImmediate(RenderTargetBase rt)
        {
            // NOTE: It's critically important that this be the "focal point" of all framebuffer bindings.
            if (rt is RenderWindow window)
            {
                _glContext!.BindWindowRenderTarget(window.WindowId);
            }
            else if (rt is RenderTexture texture)
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, texture.FramebufferHandle.Handle);
                CheckGlError();
            }
        }

        internal abstract class RenderTargetBase(PAL clyde, bool isSrgb) : GPUResource, IRenderTarget
        {
            protected readonly PAL PAL = clyde;

            public bool MakeGLFence;
            public nint LastGLSync;

            public abstract Vector2i Size { get; }

            public abstract bool FlipY { get; }

            public bool IsSrgb { get; } = isSrgb;

            public void Fence()
            {
                if (!PAL._hasGL.FenceSync || !MakeGLFence)
                    return;

                if (LastGLSync != 0)
                    GL.DeleteSync(LastGLSync);

                LastGLSync = GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, WaitSyncFlags.None);
            }

            public void Clear(float? r = null, float? g = null, float? b = null, float? a = null, int stencilValue = 0, int stencilMask = 0, float? depth = null, UIBox2i? scissor = null)
            {
                // 'Push'
                bool pause = this != PAL._backupRenderTarget;

                if (pause)
                {
                    PAL.BindRenderTargetImmediate(this);
                }
                PAL.SetScissorImmediate(this, scissor);

                ClearBufferMask mask = 0;
                if (r != null || g != null || b != null || a != null)
                {
                    GL.ColorMask(r != null, g != null, b != null, a != null);
                    PAL.CheckGlError();
                    GL.ClearColor(r ?? 0, g ?? 0, b ?? 0, a ?? 0);
                    PAL.CheckGlError();
                    mask |= ClearBufferMask.ColorBufferBit;
                }
                if (depth != null)
                {
                    GL.DepthMask(true);
                    PAL.CheckGlError();
                    GL.ClearDepth(depth.Value);
                    PAL.CheckGlError();
                    mask |= ClearBufferMask.DepthBufferBit;
                }
                if (stencilMask != 0)
                {
                    GL.StencilMask(stencilMask);
                    PAL.CheckGlError();
                    GL.ClearStencil(stencilValue);
                    PAL.CheckGlError();
                    mask |= ClearBufferMask.StencilBufferBit;
                }
                GL.Clear(mask);
                PAL.CheckGlError();

                // Restore
                if (pause)
                {
                    PAL.BindRenderTargetImmediate(PAL._backupRenderTarget);
                }
                PAL.SetScissorImmediate(PAL._backupRenderTarget, PAL._isScissoring);
                GL.StencilMask(PAL._currentRenderState?.Stencil.WriteMask ?? int.MaxValue);
                PAL.CheckGlError();
                var cdMask = PAL._currentRenderState?.ColourDepthMask ?? ColourDepthMask.AllMask;
                PAL.SetCDMaskImmediate(cdMask);
            }

            public void CopyPixelsToMemory<T>(CopyPixelsDelegate<T> callback, UIBox2i? subRegion = null) where T : unmanaged, IPixel<T>
            {
                bool pause = this != PAL._backupRenderTarget;

                if (pause)
                {
                    PAL.BindRenderTargetImmediate(this);
                }

                PAL.DoCopyPixels(Size, subRegion, callback);

                if (pause)
                {
                    PAL.BindRenderTargetImmediate(PAL._backupRenderTarget);
                }
            }

            public void CopyPixelsToTexture(OwnedTexture target) {
                ClydeTexture ct = (ClydeTexture) target;

                bool pause = this != PAL._backupRenderTarget;

                if (pause)
                {
                    PAL.BindRenderTargetImmediate(this);
                }

                var curTexture2D = GL.GetInteger(GetPName.TextureBinding2D);

                GL.BindTexture(TextureTarget.Texture2D, ct.OpenGLObject.Handle);
                PAL.CheckGlError();
                GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 0, 0, Size.X, Size.Y);
                PAL.CheckGlError();
                GL.BindTexture(TextureTarget.Texture2D, curTexture2D);
                PAL.CheckGlError();

                if (pause)
                {
                    PAL.BindRenderTargetImmediate(PAL._backupRenderTarget);
                }
            }
        }

        internal sealed class RenderTexture : RenderTargetBase, IRenderTexture
        {
            public RenderTexture(Vector2i size, ClydeTexture texture, PAL clyde, GLHandle handle, GLHandle dsHandle, bool isSrgb)
                : base(clyde, isSrgb)
            {
                Size = size;
                Texture = texture;
                FramebufferHandle = handle;
                DepthStencilHandle = dsHandle;
            }

            public override Vector2i Size { get; }
            public override bool FlipY => false;
            public ClydeTexture Texture { get; }
            Texture IRenderTexture.Texture => Texture;

            public GLHandle FramebufferHandle;

            // Renderbuffer handle (optional)
            public GLHandle DepthStencilHandle;

            protected override void DisposeImpl()
            {
                if (PAL.IsMainThread())
                {
                    PAL.DeleteRenderTexture(this);
                }
                else
                {
                    PAL._renderTextureDisposeQueue.Enqueue(this);
                }
            }
        }

        internal sealed class RenderWindow : RenderTargetBase
        {
            public WindowId WindowId { get; }

            public override Vector2i Size => SizeActual;

            public Vector2i SizeActual { get; set; }

            public override bool FlipY => FlipYActual;

            public bool FlipYActual { get; set; }

            public RenderWindow(PAL clyde, WindowId id, bool isSrgb, Vector2i size) : base(clyde, isSrgb)
            {
                WindowId = id;
                SizeActual = size;
            }

            protected override void DisposeImpl()
            {
                // nope
            }
        }
    }
}
