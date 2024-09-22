using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Robust.Client.Graphics.Clyde
{
    internal sealed class ClydeGLFeatures
    {
        [Dependency] private readonly ILogManager _logManager = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;

        private readonly ISawmill _sawmill;

        public readonly int Major, Minor;

        public readonly OpenGLVersion GLVersion;

        // OpenGL feature detection go here.

        public readonly bool KhrDebug;
        public readonly bool DebuggerPresent;

        // As per the extension specification, when implemented as extension in an ES context,
        // function names have to be suffixed by "KHR"
        // This keeps track of whether that's necessary.
        public readonly bool KhrDebugESExtension;
        public readonly bool TextureSwizzle;
        public readonly bool SamplerObjects;
        public readonly bool Srgb;
        public readonly bool PrimitiveRestart;
        public readonly bool PrimitiveRestartFixedIndex;
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

        // These are set from Clyde.Windowing.
        public readonly bool GLES;
        public readonly bool GLES2;
        public readonly bool Core;

        public ClydeGLFeatures(int major, int minor, bool gles, bool gles2, bool core, bool hasBrokenWindowSrgb, IDependencyCollection deps)
        {
            Major = major;
            Minor = minor;
            GLES = gles;
            GLES2 = gles2;
            Core = core;

            GLVersion = new OpenGLVersion((byte) major, (byte) minor, gles, core);

            deps.InjectDependencies(this, true);

            _sawmill = _logManager.GetSawmill("clyde.ogl.features");

            var extensions = GetGLExtensions();

            DebuggerPresent = CheckGLDebuggerStatus(extensions);

            _sawmill.Debug("OpenGL capabilities:");

            if (!GLES)
            {
                // Desktop OpenGL capabilities.
                CheckGLCap(ref KhrDebug, "khr_debug", (4, 2), "GL_KHR_debug");
                CheckGLCap(ref SamplerObjects, "sampler_objects", (3, 3), "GL_ARB_sampler_objects");
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
            }
            else
            {
                // OpenGL ES capabilities.
                CheckGLCap(ref KhrDebug, "khr_debug", (3, 2), "GL_KHR_debug");
                if (!CompareVersion(3, 2, major, minor))
                {
                    // We're ES <3.2, KHR_debug is extension and needs KHR suffixes.
                    KhrDebugESExtension = true;
                    _sawmill.Debug("  khr_debug is ES extension!");
                }

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
                CheckGLCap(ref PrimitiveRestartFixedIndex, "primitive_restart", (3, 0));
                CheckGLCap(ref UniformBuffers, "uniform_buffers", (3, 0));
                CheckGLCap(ref FloatFramebuffers, "float_framebuffers", (3, 2), "GL_EXT_color_buffer_float");
                CheckGLCap(ref GLES3Shaders, "gles3_shaders", (3, 0));

                if (major >= 3)
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

            _sawmill.Debug($"  GLES: {GLES}");

            void CheckGLCap(ref bool cap, string capName, (int major, int minor)? versionMin = null,
                params string[] exts)
            {
                var (majorMin, minorMin) = versionMin ?? (int.MaxValue, int.MaxValue);
                // Check if feature is available from the GL context.
                cap = CompareVersion(majorMin, minorMin, major, minor) || extensions.Overlaps(exts);

                var prev = cap;
                var cVarName = $"display.ogl_block_{capName}";
                var block = _cfg.GetCVar<bool>(cVarName);

                if (block)
                {
                    cap = false;
                    _sawmill.Debug($"  {cVarName} SET, BLOCKING {capName} (was: {prev})");
                }

                _sawmill.Debug($"  {capName}: {cap}");
            }
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
    }
}
