using System.Globalization;
using System.Collections.Generic;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Utility;
using ES20 = OpenToolkit.Graphics.ES20;
using Robust.Shared;
using System.Runtime.CompilerServices;
using System;
using System.Diagnostics;

namespace Robust.Client.Graphics.Clyde
{
    /// <summary>
    /// This class has four responsibilities:
    /// 1. Enumerating GL version details and features.
    /// 2. Providing "extension-aware thunks" for cases of different names for the same function (VAOs).
    /// 3. Providing the universal shader header.
    /// 4. Initializing GL 'globals' that we're NEVER GOING TO TOUCH. (If we are going to touch it anyway, there needs to be a dedicated 'restore' function here.)
    /// </summary>
    internal sealed class GLWrapper
    {
        private readonly ISawmill _sawmill;

        public readonly int Major, Minor;
        public readonly OpenGLVersion GLVersion;
        public readonly string Vendor, Renderer, Version;
        public readonly bool Overriding;

        // OpenGL feature detection go here.

        public readonly bool KhrDebug;
        public readonly bool DebuggerPresent;

        // As per the extension specification, when implemented as extension in an ES context,
        // function names have to be suffixed by "KHR"
        // This keeps track of whether that's necessary.
        public readonly bool KhrDebugESExtension;
        public readonly bool TextureSwizzle;
        public readonly bool Srgb;
        public readonly bool PrimitiveRestart;
        public readonly bool ReadFramebuffer;
        public readonly bool UniformBuffers;
        public bool AnyVertexArrayObjects => VertexArrayObject || VertexArrayObjectOes;
        public readonly bool VertexArrayObject;
        public readonly bool VertexArrayObjectOes;
        public readonly bool FloatFramebuffers;
        public readonly bool GLES3Shaders;
        public bool AnyMapBuffer => MapBuffer || MapBufferRange || MapBufferOes;
        public readonly bool MapBuffer;
        public readonly bool MapBufferOes;
        public readonly bool MapBufferRange;
        public readonly bool PixelBufferObjects;
        public readonly bool StandardDerivatives;

        public readonly bool FenceSync;

        public readonly bool GLES;
        public readonly bool GLES2;
        public readonly bool Core;

        /// <summary>Mandatory shader header. This doesn't handle language changes, this is just a mixture of stuff needed to get your foot in the door and flags so you can handle compatibility yourself.</summary>
        public readonly string ShaderHeader;

        public bool HasVaryingAttribute => GLES && !GLES3Shaders;

        private bool _checkGLErrors;

        /// <summary>Probes the current context for features, and sets some initial state we want on ALL contexts.</summary>
        public GLWrapper(RendererOpenGLVersion mode, bool hasBrokenWindowSrgb, ISawmill log, IConfigurationManager cfg)
        {
            bool gles = RendererOpenGLVersionUtils.IsGLES(mode);
            bool core = RendererOpenGLVersionUtils.IsCore(mode);
            bool isES2 = mode is RendererOpenGLVersion.GLES2;

            Vendor = GL.GetString(StringName.Vendor);
            Renderer = GL.GetString(StringName.Renderer);
            Version = GL.GetString(StringName.Version);

            var major = isES2 ? 2 : GL.GetInteger(GetPName.MajorVersion);
            var minor = isES2 ? 0 : GL.GetInteger(GetPName.MinorVersion);

            _sawmill = log;

            cfg.OnValueChanged(CVars.DisplayOGLCheckErrors, b => _checkGLErrors = b, true);

            var overrideVersion = ParseGLOverrideVersion();

            if (overrideVersion != null)
            {
                (major, minor) = overrideVersion.Value;
                _sawmill.Debug("OVERRIDING detected GL version to: {0}.{1}", major, minor);
            }

            Overriding = overrideVersion != null;

            Major = major;
            Minor = minor;

            GLES = gles;
            GLES2 = isES2;
            Core = core;

            GLVersion = new OpenGLVersion((byte) Major, (byte) Minor, gles, core);

            var extensions = GetGLExtensions();

            DebuggerPresent = CheckGLDebuggerStatus(extensions);

            _sawmill.Debug("OpenGL capabilities:");

            if (!GLES)
            {
                // Desktop OpenGL capabilities.
                CheckGLCap(ref KhrDebug, "khr_debug", (4, 2), "GL_KHR_debug");
                CheckGLCap(ref TextureSwizzle, "texture_swizzle", (3, 3), "GL_ARB_texture_swizzle",
                    "GL_EXT_texture_swizzle");
                CheckGLCap(ref VertexArrayObject, "vertex_array_object", (3, 0), "GL_ARB_vertex_array_object");
                CheckGLCap(ref FenceSync, "fence_sync", (3, 2), "GL_ARB_sync");
                CheckGLCap(ref MapBuffer, "map_buffer", (2, 0));
                CheckGLCap(ref MapBufferRange, "map_buffer_range", (3, 0));
                CheckGLCap(ref PixelBufferObjects, "pixel_buffer_object", (2, 1));
                CheckGLCap(ref StandardDerivatives, "standard_derivatives", (2, 1));

                Srgb = true;
                ReadFramebuffer = true;
                PrimitiveRestart = true;
                UniformBuffers = true;
                FloatFramebuffers = true;

                GL.Enable(EnableCap.PrimitiveRestart);
                CheckGlError();
                GL.PrimitiveRestartIndex(ushort.MaxValue);
                CheckGlError();
            }
            else
            {
                // OpenGL ES capabilities.
                CheckGLCap(ref KhrDebug, "khr_debug", (3, 2), "GL_KHR_debug");
                if (!CompareVersion(3, 2, Major, Minor))
                {
                    // We're ES <3.2, KHR_debug is extension and needs KHR suffixes.
                    KhrDebugESExtension = true;
                    _sawmill.Debug("  khr_debug is ES extension!");
                }

                var primitiveRestartFixedIndex = false;

                CheckGLCap(ref VertexArrayObject, "vertex_array_object", (3, 0));
                CheckGLCap(ref VertexArrayObjectOes, "vertex_array_object_oes",
                    exts: "GL_OES_vertex_array_object");
                CheckGLCap(ref TextureSwizzle, "texture_swizzle", (3, 0));
                CheckGLCap(ref FenceSync, "fence_sync", (3, 0));
                // ReSharper disable once StringLiteralTypo
                CheckGLCap(ref MapBufferOes, "map_buffer_oes", exts: "GL_OES_mapbuffer");
                CheckGLCap(ref MapBufferRange, "map_buffer_range", (3, 0));
                CheckGLCap(ref PixelBufferObjects, "pixel_buffer_object", (3, 0));
                CheckGLCap(ref StandardDerivatives, "standard_derivatives", (3, 0), "GL_OES_standard_derivatives");
                CheckGLCap(ref ReadFramebuffer, "read_framebuffer", (3, 0));
                CheckGLCap(ref primitiveRestartFixedIndex, "primitive_restart", (3, 0));
                CheckGLCap(ref UniformBuffers, "uniform_buffers", (3, 0));
                CheckGLCap(ref FloatFramebuffers, "float_framebuffers", (3, 2), "GL_EXT_color_buffer_float");
                CheckGLCap(ref GLES3Shaders, "gles3_shaders", (3, 0));

                PrimitiveRestart = primitiveRestartFixedIndex;

                if (primitiveRestartFixedIndex)
                {
                    GL.Enable(EnableCap.PrimitiveRestartFixedIndex);
                    CheckGlError();
                }

                if (Major >= 3)
                {
                    if (hasBrokenWindowSrgb)
                    {
                        Srgb = false;
                        _sawmill.Debug("  sRGB: false (window broken sRGB)");
                    }
                    else
                    {
                        Srgb = true;
                        _sawmill.Debug("  sRGB: true");
                    }
                }
                else
                {
                    Srgb = false;
                    _sawmill.Debug("  sRGB: false");
                }
            }

            if (Srgb && !GLES)
            {
                GL.Enable(EnableCap.FramebufferSrgb);
                CheckGlError();
            }
            // Messing with row alignment is rather pointless, so disable it globally.
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            CheckGlError();
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            CheckGlError();
            GL.FrontFace(FrontFaceDirection.Cw);
            CheckGlError();

            _sawmill.Debug($"  GLES: {GLES}");

            void CheckGLCap(ref bool cap, string capName, (int major, int minor)? versionMin = null,
                params string[] exts)
            {
                var (majorMin, minorMin) = versionMin ?? (int.MaxValue, int.MaxValue);
                // Check if feature is available from the GL context.
                cap = CompareVersion(majorMin, minorMin, Major, Minor) || extensions.Overlaps(exts);

                var prev = cap;
                var cVarName = $"display.ogl_block_{capName}";
                var block = cfg.GetCVar<bool>(cVarName);

                if (block)
                {
                    cap = false;
                    _sawmill.Debug($"  {cVarName} SET, BLOCKING {capName} (was: {prev})");
                }

                _sawmill.Debug($"  {capName}: {cap}");
            }

            (int major, int minor)? ParseGLOverrideVersion()
            {
                var overrideGLVersion = cfg.GetCVar(CVars.DisplayOGLOverrideVersion);
                if (string.IsNullOrEmpty(overrideGLVersion))
                {
                    return null;
                }

                var split = overrideGLVersion.Split(".");
                if (split.Length != 2)
                {
                    _sawmill.Warning("display.ogl_override_version is in invalid format");
                    return null;
                }

                if (!int.TryParse(split[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major)
                    || !int.TryParse(split[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minor))
                {
                    _sawmill.Warning("display.ogl_override_version is in invalid format");
                    return null;
                }

                return (major, minor);
            }

            ShaderHeader = GenShaderHeader();
        }

        private static bool CompareVersion(int majorA, int minorA, int majorB, int minorB)
        {
            if (majorB > majorA)
            {
                return true;
            }

            return majorA == majorB && minorB >= minorA;
        }

        private bool CheckGLDebuggerStatus(HashSet<string> extensions)
        {
            if (!extensions.Contains("GL_EXT_debug_tool"))
                return false;

            const int GL_DEBUG_TOOL_EXT = 0x6789;
            const int GL_DEBUG_TOOL_NAME_EXT = 0x678A;

            var res = GL.IsEnabled((EnableCap)GL_DEBUG_TOOL_EXT);
            var name = GL.GetString((StringName)GL_DEBUG_TOOL_NAME_EXT);
            _sawmill.Debug($"OpenGL debugger present: {name}");
            return res;
        }

        internal static void RegisterBlockCVars(IConfigurationManager cfg)
        {
            string[] cvars =
            {
                "khr_debug",
                "sampler_objects",
                "texture_swizzle",
                "vertex_array_object",
                "vertex_array_object_oes",
                "fence_sync",
                "map_buffer",
                "map_buffer_range",
                "pixel_buffer_object",
                "map_buffer_oes",
                "standard_derivatives",
                "read_framebuffer",
                "primitive_restart",
                "uniform_buffers",
                "float_framebuffers",
                "gles3_shaders",
            };

            foreach (var cvar in cvars)
            {
                cfg.RegisterCVar($"display.ogl_block_{cvar}", false);
            }
        }

        private HashSet<string> GetGLExtensions()
        {
            if (!GLES)
            {
                var extensions = new HashSet<string>();
                var extensionsText = "";
                // Desktop OpenGL uses this API to discourage static buffers
                var count = GL.GetInteger(GetPName.NumExtensions);
                for (var i = 0; i < count; i++)
                {
                    if (i != 0)
                    {
                        extensionsText += " ";
                    }
                    var extension = GL.GetString(StringNameIndexed.Extensions, i);
                    extensionsText += extension;
                    extensions.Add(extension);
                }
                _sawmill.Debug("OpenGL Extensions: {0}", extensionsText);
                return extensions;
            }
            else
            {
                // GLES uses the (old?) API
                var extensions = GL.GetString(StringName.Extensions);
                _sawmill.Debug("OpenGL Extensions: {0}", extensions);
                return new HashSet<string>(extensions.Split(' '));
            }
        }

        /// Creates the "mandatory header" which sets the GLSL version and provides available feature flags.
        /// Notably, this doesn't do anything to smooth out the language itself.
        private string GenShaderHeader()
        {
            var versionHeader = "#version 140\n#define HAS_MOD\n";

            if (GLES)
            {
                if (GLES3Shaders)
                {
                    versionHeader = "#version 300 es\n";
                }
                else
                {
                    // GLES2 uses a different GLSL versioning scheme to desktop GL.
                    versionHeader = "#version 100\n#define HAS_VARYING_ATTRIBUTE\n";
                    if (StandardDerivatives)
                    {
                        versionHeader += "#extension GL_OES_standard_derivatives : enable\n";
                    }

                    versionHeader += "#define NO_ARRAY_PRECISION\n";
                }

            }

            if (StandardDerivatives)
            {
                versionHeader += "#define HAS_DFDX\n";
            }

            if (FloatFramebuffers)
            {
                versionHeader += "#define HAS_FLOAT_TEXTURES\n";
            }

            if (Srgb)
            {
                versionHeader += "#define HAS_SRGB\n";
            }

            if (UniformBuffers)
            {
                versionHeader += "#define HAS_UNIFORM_BUFFERS\n";
            }

            return versionHeader;
        }

        /// <summary>
        /// Makes a raw, unchecked call to GenVertexArray considering the nature of the GL.
        /// </summary>
        public uint GenVertexArray()
        {
            DebugTools.Assert(AnyVertexArrayObjects);

            int value;
            if (VertexArrayObject)
            {
                value = GL.GenVertexArray();
            }
            else
            {
                DebugTools.Assert(VertexArrayObjectOes);

                value = ES20.GL.Oes.GenVertexArray();
            }

            return (uint) value;
        }

        /// Makes a raw, unchecked call to BindVertexArray considering the nature of the GL.
        public void BindVertexArray(uint vao)
        {
            DebugTools.Assert(AnyVertexArrayObjects);

            if (VertexArrayObject)
            {
                GL.BindVertexArray(vao);
            }
            else
            {
                DebugTools.Assert(VertexArrayObjectOes);

                ES20.GL.Oes.BindVertexArray(vao);
            }
        }

        /// Makes a raw, unchecked call to DeleteVertexArray considering the nature of the GL.
        public void DeleteVertexArray(uint vao)
        {
            DebugTools.Assert(AnyVertexArrayObjects);

            if (VertexArrayObject)
            {
                GL.DeleteVertexArray(vao);
            }
            else
            {
                DebugTools.Assert(VertexArrayObjectOes);

                ES20.GL.Oes.DeleteVertexArray(vao);
            }
        }

        // Both access and mask are specified because I like prematurely optimizing and this is the most performant.
        // And easiest.
        public unsafe void* MapFullBuffer(BufferTarget buffer, int length, BufferAccess access, BufferAccessMask mask)
        {
            DebugTools.Assert(AnyMapBuffer);

            void* ptr;

            if (MapBufferRange)
            {
                ptr = (void*) GL.MapBufferRange(buffer, IntPtr.Zero, length, mask);
                CheckGlError();
            }
            else if (MapBuffer)
            {
                ptr = (void*) GL.MapBuffer(buffer, access);
                CheckGlError();
            }
            else
            {
                DebugTools.Assert(MapBufferOes);

                ptr = (void*) ES20.GL.Oes.MapBuffer((ES20.BufferTargetArb) buffer,
                    (ES20.BufferAccessArb) BufferAccess.ReadOnly);
                CheckGlError();
            }

            return ptr;
        }

        public void UnmapBuffer(BufferTarget buffer)
        {
            DebugTools.Assert(AnyMapBuffer);

            if (MapBufferRange || MapBuffer)
            {
                GL.UnmapBuffer(buffer);
                CheckGlError();
            }
            else
            {
                DebugTools.Assert(MapBufferOes);

                ES20.GL.Oes.UnmapBuffer((ES20.BufferTarget) buffer);
                CheckGlError();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CheckGlError([CallerFilePath] string? path = null, [CallerLineNumber] int line = default)
        {
            if (!_checkGLErrors)
            {
                return;
            }

            // Separate method to reduce code footprint and improve inlining of this method.
            CheckGlErrorInternal(path, line);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CheckGlErrorInternal(string? path, int line)
        {
            var err = GL.GetError();
            if (err != ErrorCode.NoError)
            {
                _sawmill.Error($"OpenGL error: {err} at {path}:{line}\n{Environment.StackTrace}");
            }
        }

        [Conditional("DEBUG")]
        internal void ObjectLabelMaybe(ObjectLabelIdentifier identifier, uint name, string? label)
        {
            if (label == null)
            {
                return;
            }

            if (!KhrDebug || !DebuggerPresent)
                return;

            if (KhrDebugESExtension)
            {
                GL.Khr.ObjectLabel((ObjectIdentifier) identifier, name, label.Length, label);
            }
            else
            {
                GL.ObjectLabel(identifier, name, label.Length, label);
            }
        }

        [Conditional("DEBUG")]
        internal void ObjectLabelMaybe(ObjectLabelIdentifier identifier, GLHandle name, string? label)
        {
            ObjectLabelMaybe(identifier, name.Handle, label);
        }
    }
}
