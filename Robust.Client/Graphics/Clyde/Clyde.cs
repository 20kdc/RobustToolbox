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
    internal sealed partial class Clyde : IClydeInternal, IPostInjectInit, IEntityEventSubscriber
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

        private GPUUniformBuffer<ProjViewMatrices> ProjViewUBO = default!;
        private GPUUniformBuffer<UniformConstants> UniformConstantsUBO = default!;

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

        public Clyde()
        {
            _currentRenderTarget = default!;
            SixLabors.ImageSharp.Configuration.Default.PreferContiguousImageBuffers = true;
        }

        public bool InitializePreWindowing()
        {
            RegisterWindowingConnectors();

            _clydeSawmill = _logManager.GetSawmill("clyde");
            _pal._sawmillOgl = _logManager.GetSawmill("clyde.ogl");
            _pal._sawmillWin = _logManager.GetSawmill("clyde.win");

            _cfg.OnValueChanged(CVars.DisplayVSync, _pal.VSyncChanged, true);
            _cfg.OnValueChanged(CVars.DisplayWindowMode, _pal.WindowModeChanged, true);
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

            _pal._threadWindowBlit = _cfg.GetCVar(CVars.DisplayThreadWindowBlit);
            _pal._threadWindowApi = _cfg.GetCVar(CVars.DisplayThreadWindowApi);

            _pal.InitKeys();

            return _pal.InitWindowing();
        }

        public bool InitializePostWindowing()
        {
            _pal._gameThread = Thread.CurrentThread;

            _pal.InitGLContextManager();

            if (!_pal.InitMainWindowAndRenderer())
                return false;

            InitOpenGL();

            _clydeSawmill.Debug("Setting viewport and rendering splash...");

            ((IGPURenderState) _renderState).SetViewport(0, 0, _pal.ScreenSize.X, _pal.ScreenSize.Y);

            // Quickly do a render with _drawingSplash = true so the screen isn't blank.
            Render();

            return true;
        }

        public void EnterWindowLoop()
        {
            _pal._windowing!.EnterWindowLoop();
        }

        public void TerminateWindowLoop()
        {
            _pal._windowing!.TerminateWindowLoop();
        }

        public void FrameProcess(FrameEventArgs eventArgs)
        {
            if (!_pal._threadWindowApi)
            {
                _pal._windowing!.PollEvents();
            }

            _pal.FlushDispose();
            FlushShaderInstanceDispose();
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

        /// <summary>Called by InitializePostWindowing</summary>
        private void InitOpenGL()
        {
            _renderState = _pal.CreateRenderState();

            DebugInfo = new ClydeDebugInfo(
                _pal._hasGL.GLVersion,
                _pal._hasGL.Renderer,
                _pal._hasGL.Vendor,
                _pal._hasGL.Version,
                _pal._hasGL.Overriding,
                _pal._windowing!.GetDescription());

            _renderState.Blend = BlendParameters.Mix;

            // Primitive Restart's presence or lack thereof changes the amount of required memory.
            InitRenderingBatchBuffers();

            _clydeSawmill.Debug("Loading stock textures...");

            LoadStockTextures();

            _clydeSawmill.Debug("Loading stock shaders...");

            LoadStockShaders();

            _clydeSawmill.Debug("Creating various GL objects...");

            CreateMiscGLObjects();

            _clydeSawmill.Debug("Setting up RenderHandle...");

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

            ProjViewUBO = new GPUUniformBuffer<ProjViewMatrices>(_pal, new(), GPUBuffer.Usage.StreamDraw, nameof(ProjViewUBO));
            UniformConstantsUBO = new GPUUniformBuffer<UniformConstants>(_pal, new(), GPUBuffer.Usage.StreamDraw, nameof(UniformConstantsUBO));

            // Also must be done in ClearRenderState
            _renderState.SetUBO(BindingIndexProjView, ProjViewUBO);
            _renderState.SetUBO(BindingIndexUniformConstants, UniformConstantsUBO);

            ScreenBufferTexture = (ClydeTexture) _pal.CreateBlankTexture<Rgba32>((1, 1), "SCREEN_TEXTURE", new TextureLoadParameters() {
                Srgb = true,
                SampleParameters = new TextureSampleParameters() { Filter = false, WrapMode = TextureWrapMode.MirroredRepeat}
            });
        }

        public void Shutdown()
        {
            _pal._glContext?.Shutdown();
            _pal.ShutdownWindowing();
        }
    }
}
