using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Client.Utility;
using Robust.Shared.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;
using OGLTextureWrapMode = OpenToolkit.Graphics.OpenGL.TextureWrapMode;
using PIF = OpenToolkit.Graphics.OpenGL4.PixelInternalFormat;
using PF = OpenToolkit.Graphics.OpenGL4.PixelFormat;
using PT = OpenToolkit.Graphics.OpenGL4.PixelType;
using TextureWrapMode = Robust.Shared.Graphics.TextureWrapMode;

namespace Robust.Client.Graphics.Clyde
{
    internal partial class PAL
    {
        public OwnedTexture LoadTextureFromImage<T>(Image<T> image, string? name = null,
            TextureLoadParameters? loadParams = null) where T : unmanaged, IPixel<T>
        {
            DebugTools.Assert(_gameThread == Thread.CurrentThread);

            var actualParams = loadParams ?? TextureLoadParameters.Default;
            var pixelType = typeof(T);

            if (!_hasGL.TextureSwizzle)
            {
                // If texture swizzle isn't available we have to pre-process the images to apply it ourselves
                // and then upload as RGBA8.
                // Yes this is inefficient but the alternative is modifying the shaders,
                // which I CBA to do.
                // Even 8 year old iGPUs support texture swizzle.
                if (pixelType == typeof(A8))
                {
                    // Disable sRGB so stuff doesn't get interpreter wrong.
                    actualParams.Srgb = false;
                    using var img = ApplyA8Swizzle((Image<A8>) (object) image);
                    return LoadTextureFromImage(img, name, loadParams);
                }

                if (pixelType == typeof(L8) && !actualParams.Srgb)
                {
                    using var img = ApplyL8Swizzle((Image<L8>) (object) image);
                    return LoadTextureFromImage(img, name, loadParams);
                }
            }

            // Flip image because OpenGL reads images upside down.
            using var copy = FlipClone(image);

            var curTexture2D = GL.GetInteger(GetPName.TextureBinding2D);

            var texture = CreateBaseTextureInternal<T>(image.Width, image.Height, actualParams, name);

            unsafe
            {
                var span = copy.GetPixelSpan();
                fixed (T* ptr = span)
                {
                    // Still bound.
                    DoTexUpload(copy.Width, copy.Height, actualParams.Srgb, ptr);
                }
            }

            GL.BindTexture(TextureTarget.Texture2D, curTexture2D);

            return texture;
        }

        public unsafe OwnedTexture CreateBlankTexture<T>(
            Vector2i size,
            string? name = null,
            in TextureLoadParameters? loadParams = null)
            where T : unmanaged, IPixel<T>
        {
            var actualParams = loadParams ?? TextureLoadParameters.Default;
            if (!_hasGL.TextureSwizzle)
            {
                // Actually create RGBA32 texture if missing texture swizzle.
                // This is fine (TexturePixelType that's stored) because all other APIs do the same.
                if (typeof(T) == typeof(A8) || typeof(T) == typeof(L8))
                {
                    return CreateBlankTexture<Rgba32>(size, name, loadParams);
                }
            }

            var curTexture2D = GL.GetInteger(GetPName.TextureBinding2D);

            var texture = CreateBaseTextureInternal<T>(
                size.X, size.Y,
                actualParams,
                name);

            // Texture still bound, run glTexImage2D with null data param to specify bounds.
            DoTexUpload<T>(size.X, size.Y, actualParams.Srgb, null);

            GL.BindTexture(TextureTarget.Texture2D, curTexture2D);

            return texture;
        }

        private unsafe void DoTexUpload<T>(int width, int height, bool srgb, T* ptr) where T : unmanaged, IPixel<T>
        {
            if (sizeof(T) < 4)
            {
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                CheckGlError();
            }

            var (pif, pf, pt) = PixelEnums<T>(srgb);
            GL.TexImage2D(TextureTarget.Texture2D, 0, pif, width, height, 0, pf, pt, (IntPtr) ptr);
            CheckGlError();

            if (sizeof(T) < 4)
            {
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
                CheckGlError();
            }
        }

        private ClydeTexture CreateBaseTextureInternal<T>(
            int width, int height,
            in TextureLoadParameters loadParams,
            string? name = null)
            where T : unmanaged, IPixel<T>
        {
            var texture = new GLHandle((uint) GL.GenTexture());
            CheckGlError();
            GL.BindTexture(TextureTarget.Texture2D, texture.Handle);
            CheckGlError();
            ApplySampleParameters(loadParams.SampleParameters);

            var (pif, pf, pt) = PixelEnums<T>(loadParams.Srgb);
            var pixelType = typeof(T);
            var texPixType = GetTexturePixelType<T>();
            var isActuallySrgb = false;

            if (pixelType == typeof(Rgba32))
            {
                isActuallySrgb = loadParams.Srgb;
            }
            else if (pixelType == typeof(A8))
            {
                DebugTools.Assert(_hasGL.TextureSwizzle);

                // TODO: Does it make sense to default to 1 for RGB parameters?
                // It might make more sense to pass some options to change swizzling.
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int) All.One);
                CheckGlError();
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleG, (int) All.One);
                CheckGlError();
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int) All.One);
                CheckGlError();
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int) All.Red);
                CheckGlError();
            }
            else if (pixelType == typeof(L8) && !loadParams.Srgb)
            {
                DebugTools.Assert(_hasGL.TextureSwizzle);

                // Can only use R8 for L8 if sRGB is OFF.
                // Because OpenGL doesn't provide sRGB single/dual channel image formats.
                // Vulkan when?

                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int) All.Red);
                CheckGlError();
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleG, (int) All.Red);
                CheckGlError();
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int) All.Red);
                CheckGlError();
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int) All.One);
                CheckGlError();
            }
            else
            {
                throw new NotSupportedException($"Unable to handle pixel type '{pixelType.Name}'");
            }

            var pressureEst = EstPixelSize(pif) * width * height;

            return GenTexture(texture, (width, height), isActuallySrgb, name, texPixType, pressureEst);
        }

        internal static long EstPixelSize(PixelInternalFormat format)
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

        internal void ApplySampleParameters(TextureSampleParameters? sampleParameters)
        {
            var actualParams = sampleParameters ?? TextureSampleParameters.Default;
            if (actualParams.Filter)
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                    (int) TextureMinFilter.Linear);
                CheckGlError();
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                    (int) TextureMagFilter.Linear);
                CheckGlError();
            }
            else
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                    (int) TextureMinFilter.Nearest);
                CheckGlError();
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                    (int) TextureMagFilter.Nearest);
                CheckGlError();
            }

            switch (actualParams.WrapMode)
            {
                case TextureWrapMode.None:
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                        (int) OGLTextureWrapMode.ClampToEdge);
                    CheckGlError();
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                        (int) OGLTextureWrapMode.ClampToEdge);
                    CheckGlError();
                    break;
                case TextureWrapMode.Repeat:
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                        (int) OGLTextureWrapMode.Repeat);
                    CheckGlError();
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                        (int) OGLTextureWrapMode.Repeat);
                    CheckGlError();
                    break;
                case TextureWrapMode.MirroredRepeat:
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                        (int) OGLTextureWrapMode.MirroredRepeat);
                    CheckGlError();
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                        (int) OGLTextureWrapMode.MirroredRepeat);
                    CheckGlError();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            CheckGlError();
        }

        private (PIF pif, PF pf, PT pt) PixelEnums<T>(bool srgb)
            where T : unmanaged, IPixel<T>
        {
            if (_hasGL.GLES2)
            {
                return default(T) switch
                {
                    Rgba32 => (PIF.Rgba, PF.Rgba, PT.UnsignedByte),
                    L8 => (PIF.Luminance, PF.Red, PT.UnsignedByte),
                    _ => throw new NotSupportedException("Unsupported pixel type."),
                };
            }

            return default(T) switch
            {
                // Note that if _hasGLSrgb is off, we import an sRGB texture as non-sRGB.
                // Shaders are expected to compensate for this
                Rgba32 => (srgb && _hasGL.Srgb ? PIF.Srgb8Alpha8 : PIF.Rgba8, PF.Rgba, PT.UnsignedByte),
                A8 or L8 => (PIF.R8, PF.Red, PT.UnsignedByte),
                _ => throw new NotSupportedException("Unsupported pixel type."),
            };
        }

        internal ClydeTexture GenTexture(
            GLHandle glHandle,
            Vector2i size,
            bool srgb,
            string? name,
            TexturePixelType pixType,
            long memoryPressure = 0)
        {
            if (name != null)
            {
                _hasGL.ObjectLabelMaybe(ObjectLabelIdentifier.Texture, glHandle, name);
            }

            return new ClydeTexture(glHandle, size, srgb, pixType, name, this);
        }

        internal unsafe void SetSubImage<T>(
            ClydeTexture texture,
            Vector2i dstTl,
            Image<T> img,
            in UIBox2i srcBox)
            where T : unmanaged, IPixel<T>
        {
            if (srcBox.Left < 0 ||
                srcBox.Top < 0 ||
                srcBox.Right > srcBox.Width ||
                srcBox.Bottom > srcBox.Height)
            {
                throw new ArgumentOutOfRangeException(nameof(srcBox), "Source rectangle out of bounds.");
            }

            var size = srcBox.Width * srcBox.Height;

            T[]? pooled = null;
            // C# won't let me use an if due to the stackalloc.
            var copyBuffer = size < 16 * 16
                ? stackalloc T[size]
                : (pooled = ArrayPool<T>.Shared.Rent(size)).AsSpan(0, size);

            var srcSpan = img.GetPixelSpan();
            var w = img.Width;
            FlipCopySubRegion(srcBox, w, srcSpan, copyBuffer);

            SetSubImageImpl<T>(texture, dstTl, (srcBox.Width, srcBox.Height), copyBuffer);

            if (pooled != null)
                ArrayPool<T>.Shared.Return(pooled);
        }

        internal unsafe void SetSubImage<T>(
            ClydeTexture texture,
            Vector2i dstTl,
            Vector2i size,
            ReadOnlySpan<T> buf)
            where T : unmanaged, IPixel<T>
        {
            T[]? pooled = null;
            // C# won't let me use an if due to the stackalloc.
            var copyBuffer = buf.Length < 16 * 16
                ? stackalloc T[buf.Length]
                : (pooled = ArrayPool<T>.Shared.Rent(buf.Length)).AsSpan(0, buf.Length);

            FlipCopy(buf, copyBuffer, size.X, size.Y);

            SetSubImageImpl<T>(texture, dstTl, size, copyBuffer);

            if (pooled != null)
                ArrayPool<T>.Shared.Return(pooled);
        }

        private unsafe void SetSubImageImpl<T>(
            ClydeTexture texture,
            Vector2i dstTl,
            Vector2i size,
            ReadOnlySpan<T> buf)
            where T : unmanaged, IPixel<T>
        {
            if (!_hasGL.TextureSwizzle && (typeof(T) == typeof(A8) || typeof(T) == typeof(L8)))
            {
                var swizzleBuf = ArrayPool<Rgba32>.Shared.Rent(buf.Length);

                var destSpan = swizzleBuf.AsSpan(0, buf.Length);
                if (typeof(T) == typeof(A8))
                    ApplyA8Swizzle(MemoryMarshal.Cast<T, A8>(buf), destSpan);
                else if (typeof(T) == typeof(L8))
                    ApplyL8Swizzle(MemoryMarshal.Cast<T, L8>(buf), destSpan);

                SetSubImageImpl<Rgba32>(texture, dstTl, size, destSpan);
                ArrayPool<Rgba32>.Shared.Return(swizzleBuf);
                return;
            }

            var loaded = texture;
            var pixType = GetTexturePixelType<T>();

            if (pixType != loaded.TexturePixelType)
            {
                if (loaded.TexturePixelType == TexturePixelType.RenderTarget)
                    throw new InvalidOperationException("Cannot modify texture for render target directly.");

                throw new InvalidOperationException("Mismatching pixel type for texture.");
            }

            if (loaded.Width < dstTl.X + size.X || loaded.Height < dstTl.Y + size.Y)
                throw new ArgumentOutOfRangeException(nameof(size), "Destination rectangle out of bounds.");

            if (sizeof(T) != 4)
            {
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                CheckGlError();
            }

            // sRGB doesn't matter since that only changes the internalFormat, which we don't need here.
            var (_, pf, pt) = PixelEnums<T>(srgb: false);

            GL.BindTexture(TextureTarget.Texture2D, loaded.OpenGLObject.Handle);
            CheckGlError();

            fixed (T* aPtr = buf)
            {
                var dstY = loaded.Height - dstTl.Y - size.Y;
                GL.TexSubImage2D(
                    TextureTarget.Texture2D,
                    0,
                    dstTl.X, dstY,
                    size.X, size.Y,
                    pf, pt,
                    (IntPtr) aPtr);
                CheckGlError();
            }

            if (sizeof(T) != 4)
            {
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
                CheckGlError();
            }
        }

        private static TexturePixelType GetTexturePixelType<T>() where T : unmanaged, IPixel<T>
        {
            return default(T) switch
            {
                Rgba32 => TexturePixelType.Rgba32,
                L8 => TexturePixelType.L8,
                A8 => TexturePixelType.A8,
                _ => throw new NotSupportedException("Unsupported pixel type."),
            };
        }

        /// <summary>
        ///     Makes a clone of the image that is also flipped.
        /// </summary>
        private static Image<T> FlipClone<T>(Image<T> source) where T : unmanaged, IPixel<T>
        {
            var w = source.Width;
            var h = source.Height;

            var copy = new Image<T>(w, h);

            var srcSpan = source.GetPixelSpan();
            var dstSpan = copy.GetPixelSpan();

            FlipCopy(srcSpan, dstSpan, w, h);

            return copy;
        }

        internal static void FlipCopy<T>(ReadOnlySpan<T> srcSpan, Span<T> dstSpan, int w, int h)
        {
            var dr = h - 1;
            for (var r = 0; r < h; r++, dr--)
            {
                var si = r * w;
                var di = dr * w;
                var srcRow = srcSpan[si..(si + w)];
                var dstRow = dstSpan[di..(di + w)];

                srcRow.CopyTo(dstRow);
            }
        }

        private static void FlipCopySubRegion<T>(
            UIBox2i srcBox,
            int w,
            ReadOnlySpan<T> srcSpan,
            Span<T> copyBuffer)
            where T : unmanaged, IPixel<T>
        {
            var subH = srcBox.Height;
            var subW = srcBox.Width;

            var dr = subH - 1;
            for (var r = 0; r < subH; r++, dr--)
            {
                var si = r * w + srcBox.Left;
                var di = dr * subW;
                var srcRow = srcSpan[si..(si + subW)];
                var dstRow = copyBuffer[di..(di + subW)];

                srcRow.CopyTo(dstRow);
            }
        }

        private static Image<Rgba32> ApplyA8Swizzle(Image<A8> source)
        {
            var newImage = new Image<Rgba32>(source.Width, source.Height);
            var sourceSpan = source.GetPixelSpan();
            var destSpan = newImage.GetPixelSpan();

            ApplyA8Swizzle(sourceSpan, destSpan);

            return newImage;
        }

        private static Image<Rgba32> ApplyL8Swizzle(Image<L8> source)
        {
            var newImage = new Image<Rgba32>(source.Width, source.Height);
            var sourceSpan = source.GetPixelSpan();
            var destSpan = newImage.GetPixelSpan();

            ApplyL8Swizzle(sourceSpan, destSpan);

            return newImage;
        }

        private static void ApplyL8Swizzle(ReadOnlySpan<L8> src, Span<Rgba32> dst)
        {
            for (var i = 0; i < src.Length; i++)
            {
                var px = src[i].PackedValue;
                dst[i] = new Rgba32(px, px, px, 255);
            }
        }

        private static void ApplyA8Swizzle(ReadOnlySpan<A8> src, Span<Rgba32> dst)
        {
            for (var i = 0; i < src.Length; i++)
            {
                var px = src[i].PackedValue;
                dst[i] = new Rgba32(255, 255, 255, px);
            }
        }
    }

    internal enum TexturePixelType : byte
    {
        RenderTarget = 0,
        Rgba32,
        A8,
        L8,
    }

    internal sealed class ClydeTexture : OwnedTexture
    {
        private readonly PAL _pal;
        public readonly bool IsSrgb;

        public GLHandle OpenGLObject;
        public string? Name;
        internal TexturePixelType TexturePixelType;

        public override void SetSubImage<T>(Vector2i topLeft, Image<T> sourceImage, in UIBox2i sourceRegion)
        {
            _pal.SetSubImage(this, topLeft, sourceImage, sourceRegion);
        }

        public override void SetSubImage<T>(Vector2i topLeft, Vector2i size, ReadOnlySpan<T> buffer)
        {
            _pal.SetSubImage(this, topLeft, size, buffer);
        }

        protected override void DisposeImpl()
        {
            if (_pal.IsMainThread())
            {
                // Main thread, do direct GL deletion.
                GL.DeleteTexture(OpenGLObject.Handle);
                _pal.CheckGlError();
            }
            else
            {
                // Finalizer thread
                _pal._textureDisposeQueue.Enqueue(OpenGLObject);
            }
        }

        internal ClydeTexture(GLHandle gl, Vector2i size, bool srgb, TexturePixelType texturePixelType, string? name, PAL clyde) : base(size)
        {
            OpenGLObject = gl;
            Name = name;
            IsSrgb = srgb;
            TexturePixelType = texturePixelType;
            _pal = clyde;
        }

        public override string ToString() => $"ClydeTexture: {Name} ({OpenGLObject})";

        public override unsafe Color GetPixel(int x, int y)
        {
            var curTexture2D = GL.GetInteger(GetPName.TextureBinding2D);
            var bufSize = 4 * Size.X * Size.Y;
            var buffer = ArrayPool<byte>.Shared.Rent(bufSize);

            GL.BindTexture(TextureTarget.Texture2D, OpenGLObject.Handle);

            fixed (byte* p = buffer)
            {
                GL.GetTexImage(TextureTarget.Texture2D, 0, PF.Rgba, PT.UnsignedByte, (IntPtr) p);
            }

            GL.BindTexture(TextureTarget.Texture2D, curTexture2D);

            var pixelPos = (Size.X * (Size.Y - y - 1) + x) * 4;
            var color = new Color(buffer[pixelPos+0], buffer[pixelPos+1], buffer[pixelPos+2], buffer[pixelPos+3]);
            ArrayPool<byte>.Shared.Return(buffer);
            return color;
        }
    }
}
