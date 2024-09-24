using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using OpenToolkit;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Client.Input;
using Robust.Client.Map;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Profiling;
using Robust.Shared.Timing;
using SixLabors.ImageSharp.PixelFormats;
using TextureWrapMode = Robust.Shared.Graphics.TextureWrapMode;

namespace Robust.Client.Graphics.Clyde
{
    /// <summary>
    ///     Responsible for most things rendering on OpenGL mode.
    /// </summary>
    internal sealed partial class Clyde : IClydeInternal, IPostInjectInit, IEntityEventSubscriber, IWindowingHost
    {
        [Dependency] private readonly IClydeTileDefinitionManager _tileDefinitionManager = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly ILightManager _lightManager = default!;
        [Dependency] private readonly ILogManager _logManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IOverlayManager _overlayManager = default!;
        [Dependency] private readonly IResourceCache _resourceCache = default!;
        [Dependency] private readonly IResourceManager _resManager = default!;
        [Dependency] private readonly IUserInterfaceManagerInternal _userInterfaceManager = default!;
        [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly ProfManager _prof = default!;
        [Dependency] private readonly IDependencyCollection _deps = default!;
        [Dependency] private readonly ILocalizationManager _loc = default!;
        [Dependency] private readonly IInputManager _inputManager = default!;

        private GLUniformBuffer<ProjViewMatrices> ProjViewUBO = default!;
        private GLUniformBuffer<UniformConstants> UniformConstantsUBO = default!;

        private GPUBuffer BatchVBO = default!;
        private GPUBuffer BatchEBO = default!;
        private GPUVertexArrayObject BatchVAO = default!;

        // VBO to draw a single quad.
        private GPUBuffer QuadVBO = default!;
        private GPUVertexArrayObject QuadVAO = default!;

        private bool _drawingSplash = true;
        private float _lightResolutionScale = 0.5f;
        private int _maxLights = 2048;
        private int _maxOccluders = 2048;
        private int _maxShadowcastingLights = 128;
        private bool _enableSoftShadows = true;

        private ISawmill _clydeSawmill = default!;

        private IBindingsContext _glBindingsContext = default!;
        private bool _threadWindowApi;

        public Clyde()
        {
            _currentBoundRenderTarget = default!;
            _currentRenderTarget = default!;
            SixLabors.ImageSharp.Configuration.Default.PreferContiguousImageBuffers = true;
        }

        public bool InitializePreWindowing()
        {
            _clydeSawmill = _logManager.GetSawmill("clyde");
            _pal._sawmillOgl = _logManager.GetSawmill("clyde.ogl");

            _cfg.OnValueChanged(CVars.DisplayVSync, VSyncChanged, true);
            _cfg.OnValueChanged(CVars.DisplayWindowMode, WindowModeChanged, true);
            _cfg.OnValueChanged(CVars.LightResolutionScale, LightResolutionScaleChanged, true);
            _cfg.OnValueChanged(CVars.MaxShadowcastingLights, MaxShadowcastingLightsChanged, true);
            _cfg.OnValueChanged(CVars.LightSoftShadows, SoftShadowsChanged, true);
            _cfg.OnValueChanged(CVars.MaxLightCount, MaxLightsChanged, true);
            _cfg.OnValueChanged(CVars.MaxOccluderCount, MaxOccludersChanged, true);
            // I can't be bothered to tear down and set these threads up in a cvar change handler.

            // Windows and Linux can be trusted to not explode with threaded windowing,
            // macOS cannot.
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                _cfg.OverrideDefault(CVars.DisplayThreadWindowApi, true);

            _threadWindowBlit = _cfg.GetCVar(CVars.DisplayThreadWindowBlit);
            _threadWindowApi = _cfg.GetCVar(CVars.DisplayThreadWindowApi);

            InitKeys();

            return InitWindowing();
        }

        public bool InitializePostWindowing()
        {
            _pal._gameThread = Thread.CurrentThread;

            InitGLContextManager();
            if (!InitMainWindowAndRenderer())
                return false;

            return true;
        }

        public bool SeparateWindowThread => _threadWindowApi;

        public void EnterWindowLoop()
        {
            _windowing!.EnterWindowLoop();
        }

        public void TerminateWindowLoop()
        {
            _windowing!.TerminateWindowLoop();
        }

        public void FrameProcess(FrameEventArgs eventArgs)
        {
            if (!_threadWindowApi)
            {
                _windowing!.PollEvents();
            }

            _windowing?.FlushDispose();
            FlushShaderInstanceDispose();
            _pal.FlushDispose();
            FlushViewportDispose();
        }

        public void Ready()
        {
            _drawingSplash = false;

            InitLighting();
        }

        public IClydeDebugInfo DebugInfo { get; private set; } = default!;
        public IClydeDebugStats DebugStats => _debugStats;

        public void PostInject()
        {
            _debugStats = new(_pal);
            // This cvar does not modify the actual GL version requested or anything,
            // it overrides the version we detect to detect GL features.
            GLWrapper.RegisterBlockCVars(_cfg);
        }

        public void RegisterGridEcsEvents()
        {
            _entityManager.EventBus.SubscribeEvent<TileChangedEvent>(EventSource.Local, this, _updateTileMapOnUpdate);
            _entityManager.EventBus.SubscribeEvent<GridStartupEvent>(EventSource.Local, this, _updateOnGridCreated);
            _entityManager.EventBus.SubscribeEvent<GridRemovalEvent>(EventSource.Local, this, _updateOnGridRemoved);
        }

        public void ShutdownGridEcsEvents()
        {
            _entityManager.EventBus.UnsubscribeEvent<TileChangedEvent>(EventSource.Local, this);
            _entityManager.EventBus.UnsubscribeEvent<GridStartupEvent>(EventSource.Local, this);
            _entityManager.EventBus.UnsubscribeEvent<GridRemovalEvent>(EventSource.Local, this);
        }

        /// <summary>Called by InitMainWindowAndRenderer</summary>
        private void InitOpenGL()
        {
            _renderState = _pal.CreateRenderState();
            _renderState.Bind();

            SetupDebugCallback();

            var vendor = _hasGL.Vendor;
            var renderer = _hasGL.Renderer;
            var version = _hasGL.Version;

            _sawmillOgl.Debug("OpenGL Vendor: {0}", vendor);
            _sawmillOgl.Debug("OpenGL Renderer: {0}", renderer);
            _sawmillOgl.Debug("OpenGL Version: {0}", version);

            DebugInfo = new ClydeDebugInfo(
                _hasGL.GLVersion,
                renderer,
                vendor,
                version,
                _hasGL.Overriding,
                _windowing!.GetDescription());

            GL.Enable(EnableCap.Blend);
            if (!_hasGL.AnyVertexArrayObjects)
            {
                _sawmillOgl.Warning("NO VERTEX ARRAY OBJECTS! Things will probably go terribly, terribly wrong (no fallback path yet)");
            }

            ResetBlendFunc();

            CheckGlError();

            // Primitive Restart's presence or lack thereof changes the amount of required memory.
            InitRenderingBatchBuffers();

            _sawmillOgl.Debug("Loading stock textures...");

            LoadStockTextures();

            _sawmillOgl.Debug("Loading stock shaders...");

            LoadStockShaders();

            _sawmillOgl.Debug("Creating various GL objects...");

            CreateMiscGLObjects();

            _sawmillOgl.Debug("Setting up RenderHandle...");

            _renderHandle = new RenderHandle(this, _entityManager);
        }

        private unsafe void CreateMiscGLObjects()
        {
            // Quad drawing.
            {
                Span<Vertex2D> quadVertices = stackalloc[]
                {
                    new Vertex2D(1, 0, 1, 1, Color.White),
                    new Vertex2D(0, 0, 0, 1, Color.White),
                    new Vertex2D(1, 1, 1, 0, Color.White),
                    new Vertex2D(0, 1, 0, 0, Color.White)
                };

                QuadVBO = _pal.CreateBuffer(MemoryMarshal.AsBytes(quadVertices),
                    GPUBuffer.Usage.StaticDraw,
                    nameof(QuadVBO));

                QuadVAO = _pal.CreateVAO(nameof(QuadVAO));
                SetupVAOLayout(QuadVAO, QuadVBO);
            }

            // Batch rendering
            {
                BatchVBO = new PAL.GLBuffer(_pal, BufferUsageHint.DynamicDraw,
                    sizeof(Vertex2D) * BatchVertexData.Length, nameof(BatchVBO));
                BatchEBO = new PAL.GLBuffer(_pal, BufferUsageHint.DynamicDraw,
                    sizeof(ushort) * BatchIndexData.Length, nameof(BatchEBO));

                BatchVAO = _pal.CreateVAO(nameof(BatchVAO));
                SetupVAOLayout(BatchVAO, BatchVBO);
                BatchVAO.IndexBuffer = BatchEBO;
            }

            ProjViewUBO = new GLUniformBuffer<ProjViewMatrices>(this, BindingIndexProjView, nameof(ProjViewUBO));
            UniformConstantsUBO = new GLUniformBuffer<UniformConstants>(this, BindingIndexUniformConstants, nameof(UniformConstantsUBO));

            ScreenBufferTexture = (ClydeTexture) _pal.CreateBlankTexture<Rgba32>((1, 1), "SCREEN_TEXTURE", new TextureLoadParameters() {
                Srgb = true,
                SampleParameters = new TextureSampleParameters() { Filter = false, WrapMode = TextureWrapMode.MirroredRepeat}
            });
        }

        [Conditional("DEBUG")]
        private unsafe void SetupDebugCallback()
        {
            if (!_hasGL.KhrDebug)
            {
                _sawmillOgl.Debug("KHR_debug not present, OpenGL debug logging not enabled.");
                return;
            }

            GL.Enable(EnableCap.DebugOutput);
            GL.Enable(EnableCap.DebugOutputSynchronous);

            _debugMessageCallbackInstance ??= DebugMessageCallback;

            // OpenTK seemed to have trouble marshalling the delegate so do it manually.

            var procName = _hasGL.KhrDebugESExtension ? "glDebugMessageCallbackKHR" : "glDebugMessageCallback";
            var glDebugMessageCallback = (delegate* unmanaged[Stdcall] <nint, nint, void>) LoadGLProc(procName);
            var funcPtr = Marshal.GetFunctionPointerForDelegate(_debugMessageCallbackInstance);
            glDebugMessageCallback(funcPtr, new IntPtr(0x3005));
        }

        private void DebugMessageCallback(DebugSource source, DebugType type, int id, DebugSeverity severity,
            int length, IntPtr message, IntPtr userParam)
        {
            var contents = $"{source}: " + Marshal.PtrToStringAnsi(message, length);

            var category = "ogl.debug";
            switch (type)
            {
                case DebugType.DebugTypePerformance:
                    category += ".performance";
                    break;
                case DebugType.DebugTypeOther:
                    category += ".other";
                    break;
                case DebugType.DebugTypeError:
                    category += ".error";
                    break;
                case DebugType.DebugTypeDeprecatedBehavior:
                    category += ".deprecated";
                    break;
                case DebugType.DebugTypeUndefinedBehavior:
                    category += ".ub";
                    break;
                case DebugType.DebugTypePortability:
                    category += ".portability";
                    break;
                case DebugType.DebugTypeMarker:
                case DebugType.DebugTypePushGroup:
                case DebugType.DebugTypePopGroup:
                    // These are inserted by our own code so I imagine they're not necessary to log?
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }

            var sawmill = _logManager.GetSawmill(category);

            switch (severity)
            {
                case DebugSeverity.DontCare:
                    sawmill.Info(contents);
                    break;
                case DebugSeverity.DebugSeverityNotification:
                    sawmill.Info(contents);
                    break;
                case DebugSeverity.DebugSeverityHigh:
                    sawmill.Error(contents);
                    break;
                case DebugSeverity.DebugSeverityMedium:
                    sawmill.Error(contents);
                    break;
                case DebugSeverity.DebugSeverityLow:
                    sawmill.Warning(contents);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(severity), severity, null);
            }
        }

        private static DebugProc? _debugMessageCallbackInstance;

        private PopDebugGroup DebugGroup(string group)
        {
            PushDebugGroupMaybe(group);
            return new PopDebugGroup(this);
        }

        [Conditional("DEBUG")]
        private void PushDebugGroupMaybe(string group)
        {
            // ANGLE spams console log messages when using debug groups, so let's only use them if we're debugging GL.
            if (!_hasGL.KhrDebug || !_hasGL.DebuggerPresent)
                return;

            if (_hasGL.KhrDebugESExtension)
            {
                GL.Khr.PushDebugGroup((DebugSource) DebugSourceExternal.DebugSourceApplication, 0, group.Length, group);
            }
            else
            {
                GL.PushDebugGroup(DebugSourceExternal.DebugSourceApplication, 0, group.Length, group);
            }
        }

        [Conditional("DEBUG")]
        private void PopDebugGroupMaybe()
        {
            if (!_hasGL.KhrDebug || !_hasGL.DebuggerPresent)
                return;

            if (_hasGL.KhrDebugESExtension)
            {
                GL.Khr.PopDebugGroup();
            }
            else
            {
                GL.PopDebugGroup();
            }
        }

        public void Shutdown()
        {
            _glContext?.Shutdown();
            ShutdownWindowing();
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal bool IsMainThread()
        {
            return _pal.IsMainThread();
        }
    }
}
