using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TerraFX.Interop.Windows;
using FrameEventArgs = Robust.Shared.Timing.FrameEventArgs;
using GL = OpenToolkit.Graphics.OpenGL4.GL;

namespace Robust.Client.Graphics.Clyde
{
    internal partial class Clyde
    {
        private readonly List<WindowReg> _windows = new();
        List<WindowReg> IWindowingHost.Windows => _windows;
        private readonly List<WindowHandle> _windowHandles = new();
        private readonly Dictionary<int, MonitorHandle> _monitorHandles = new();
        Dictionary<int, MonitorHandle> IWindowingHost.MonitorHandles => _monitorHandles;

        private int _primaryMonitorId;
        private WindowReg? _mainWindow;
        WindowReg? IWindowingHost.MainWindow => _mainWindow;

        private IWindowingImpl? _windowing;
        IWindowingImpl? IWindowingHost.Windowing => _windowing;
        bool IWindowingHost.ThreadWindowApi => _threadWindowApi;

#pragma warning disable 414
        // Keeping this for if/when we ever get a new renderer.
        private Renderer _chosenRenderer;
#pragma warning restore 414

        private ResPath? _windowIconPath;
        private Thread? _windowingThread;
        private bool _vSync;
        private WindowMode _windowMode;
        private bool _threadWindowBlit;
        bool IWindowingHost.EffectiveThreadWindowBlit => _threadWindowBlit && !_hasGL.GLES;

        public event Action<TextEnteredEventArgs>? TextEntered;
        public event Action<TextEditingEventArgs>? TextEditing;
        public event Action<MouseMoveEventArgs>? MouseMove;
        public event Action<MouseEnterLeaveEventArgs>? MouseEnterLeave;
        public event Action<KeyEventArgs>? KeyUp;
        public event Action<KeyEventArgs>? KeyDown;
        public event Action<MouseWheelEventArgs>? MouseWheel;
        public event Action<WindowRequestClosedEventArgs>? CloseWindow;
        public event Action<WindowDestroyedEventArgs>? DestroyWindow;
        public event Action<WindowContentScaleEventArgs>? OnWindowScaleChanged;
        public event Action<WindowResizedEventArgs>? OnWindowResized;
        public event Action<WindowFocusedEventArgs>? OnWindowFocused;

        // NOTE: in engine we pretend the framebuffer size is the screen size..
        // For practical reasons like UI rendering.
        public IClydeWindow MainWindow => _mainWindow?.Handle ??
                                          throw new InvalidOperationException("Windowing is not initialized");

        public Vector2i ScreenSize => _mainWindow?.FramebufferSize ??
                                      throw new InvalidOperationException("Windowing is not initialized");

        public bool IsFocused => _mainWindow?.IsFocused ??
                                 throw new InvalidOperationException("Windowing is not initialized");

        public IEnumerable<IClydeWindow> AllWindows => _windowHandles;

        public Vector2 DefaultWindowScale => _mainWindow?.WindowScale ??
                                             throw new InvalidOperationException("Windowing is not initialized");

        public ScreenCoordinates MouseScreenPosition
        {
            get
            {
                var window = _windowing!.CurrentHoveredWindow;
                if (window == null)
                    return default;

                return new ScreenCoordinates(window.LastMousePos, window.Id);
            }
        }

        public uint? GetX11WindowId()
        {
            return _windowing?.WindowGetX11Id(_mainWindow!) ?? null;
        }

        private bool InitWindowing()
        {
            if (OperatingSystem.IsWindows() && _cfg.GetCVar(CVars.DisplayAngleEs3On10_0))
            {
                Environment.SetEnvironmentVariable("ANGLE_FEATURE_OVERRIDES_ENABLED", "allowES3OnFL10_0");
            }

            var iconPath = _cfg.GetCVar(CVars.DisplayWindowIconSet);
            if (!string.IsNullOrWhiteSpace(iconPath))
                _windowIconPath = new ResPath(iconPath);

            _windowingThread = Thread.CurrentThread;

            var windowingApi = _cfg.GetCVar(CVars.DisplayWindowingApi);
            IWindowingImpl winImpl;

            switch (windowingApi)
            {
                case "glfw":
                    winImpl = new GlfwWindowingImpl(this, _deps);
                    break;
                case "sdl2":
                    winImpl = new Sdl2WindowingImpl(this, _deps);
                    break;
                default:
                    _logManager.GetSawmill("clyde.win").Log(
                        LogLevel.Error, "Unknown windowing API: {name}. Falling back to GLFW.", windowingApi);
                    goto case "glfw";
            }

            _windowing = winImpl;
            return _windowing.Init();
        }

        private bool TryInitMainWindow(GLContextSpec? glSpec, [NotNullWhen(false)] out string? error)
        {
            DebugTools.AssertNotNull(_glContext);

            var width = _cfg.GetCVar(CVars.DisplayWidth);
            var height = _cfg.GetCVar(CVars.DisplayHeight);
            var prevWidth = width;
            var prevHeight = height;

            IClydeMonitor? monitor = null;
            var fullscreen = false;

            if (_windowMode == WindowMode.Fullscreen)
            {
                monitor = _monitorHandles[_primaryMonitorId];
                width = monitor.Size.X;
                height = monitor.Size.Y;
                fullscreen = true;
            }

            var parameters = new WindowCreateParameters
            {
                Width = width,
                Height = height,
                Monitor = monitor,
                Fullscreen = fullscreen
            };

            var (reg, err) = SharedWindowCreate(glSpec, parameters, null, isMain: true);

            if (reg == null)
            {
                error = err!;
                return false;
            }

            DebugTools.Assert(reg.Id == WindowId.Main);

            if (fullscreen)
            {
                reg.PrevWindowSize = (prevWidth, prevHeight);
                reg.PrevWindowPos = (50, 50);
            }

            error = null;
            return true;
        }

        private unsafe bool InitMainWindowAndRenderer()
        {
            DebugTools.AssertNotNull(_windowing);
            DebugTools.AssertNotNull(_glContext);

            _chosenRenderer = Renderer.OpenGL;

            var succeeded = false;
            string? lastError = null;

            if (_glContext!.RequireWindowGL)
            {
                var specs = _glContext!.SpecsToTry;

                foreach (var glSpec in specs)
                {
                    if (!TryInitMainWindow(glSpec, out lastError))
                    {
                        Logger.DebugS("clyde.win", $"OpenGL {glSpec.OpenGLVersion} unsupported: {lastError}");
                        continue;
                    }

                    succeeded = true;
                    break;
                }
            }
            else
            {
                if (!TryInitMainWindow(null, out lastError))
                    Logger.DebugS("clyde.win", $"Failed to create window: {lastError}");
                else
                    succeeded = true;
            }

            if (!succeeded)
            {
                if (OperatingSystem.IsWindows())
                {
                    var msgBoxContent = "Failed to create the game window. " +
                                        "This probably means your GPU is too old to play the game. " +
                                        "Try to update your graphics drivers, " +
                                        "or enable compatibility mode in the launcher if that fails.\n" +
                                        $"The exact error is: {lastError}";

                    fixed (char* pText = msgBoxContent)
                    fixed (char* pCaption = "RobustToolbox: Failed to create window")
                    {
                        Windows.MessageBoxW(HWND.NULL,
                            pText,
                            pCaption,
                            MB.MB_OK | MB.MB_ICONERROR);
                    }
                }

                Logger.FatalS("clyde.win",
                    "Failed to create main game window! " +
                    "This probably means your GPU is too old to run the game. " +
                    $"That or update your graphics drivers. {lastError}");

                return false;
            }

            // We should have a main window now.
            DebugTools.AssertNotNull(_mainWindow);

            // GLFeatures must be set by _glContext.
            var glFeatures = _glContext.GLFeatures;
            DebugTools.AssertNotNull(glFeatures);

            // We're ready, copy over information...
            _hasGL = glFeatures!;
            _glBindingsContext = _glContext.BindingsContext;

            InitOpenGL();

            _sawmillOgl.Debug("Setting viewport and rendering splash...");

            GL.Viewport(0, 0, ScreenSize.X, ScreenSize.Y);
            CheckGlError();

            // Quickly do a render with _drawingSplash = true so the screen isn't blank.
            Render();

            return true;
        }

        IEnumerable<Image<Rgba32>> IWindowingHost.LoadWindowIcons()
        {
            if (OperatingSystem.IsMacOS() || _windowIconPath == null)
            {
                // Does nothing on macOS so don't bother.
                yield break;
            }

            foreach (var file in _resManager.ContentFindFiles(_windowIconPath))
            {
                if (file.Extension != "png")
                {
                    continue;
                }

                using var stream = _resManager.ContentFileRead(file);
                yield return Image.Load<Rgba32>(stream);
            }
        }

        private void ShutdownWindowing()
        {
            _windowing?.Shutdown();
        }

        public void SetWindowTitle(string title)
        {
            DebugTools.AssertNotNull(_windowing);
            DebugTools.AssertNotNull(_mainWindow);

            _windowing!.WindowSetTitle(_mainWindow!, title);
        }

        public void SetWindowMonitor(IClydeMonitor monitor)
        {
            DebugTools.AssertNotNull(_windowing);
            DebugTools.AssertNotNull(_mainWindow);

            _windowing!.WindowSetMonitor(_mainWindow!, monitor);
        }

        public void RequestWindowAttention()
        {
            DebugTools.AssertNotNull(_windowing);
            DebugTools.AssertNotNull(_mainWindow);

            _windowing!.WindowRequestAttention(_mainWindow!);
        }

        public IClydeWindow CreateWindow(WindowCreateParameters parameters)
        {
            DebugTools.AssertNotNull(_windowing);
            DebugTools.AssertNotNull(_glContext);
            DebugTools.AssertNotNull(_mainWindow);

            var glSpec = _glContext!.SpecToCreateWindowsWith;

            _glContext.BeforeSharedWindowCreateUnbind();

            var (reg, error) = SharedWindowCreate(
                glSpec,
                parameters,
                glSpec == null ? null : _mainWindow,
                isMain: false);

            // Rebinding is handed by WindowCreated in the GL context.

            if (error != null)
                throw new Exception(error);

            return reg!.Handle;
        }

        private (WindowReg?, string? error) SharedWindowCreate(
            GLContextSpec? glSpec,
            WindowCreateParameters parameters,
            WindowReg? share,
            bool isMain)
        {
            WindowReg? owner = null;
            if (parameters.Owner != null)
                owner = ((WindowHandle)parameters.Owner).Reg;

            var (reg, error) = _windowing!.WindowCreate(glSpec, parameters, share, owner);

            if (reg != null)
            {
                // Window init succeeded, do setup.
                reg.IsMainWindow = isMain;
                if (isMain)
                    _mainWindow = reg;

                _windows.Add(reg);
                _windowHandles.Add(reg.Handle);

                var rtId = AllocRid();
                _renderTargets.Add(rtId, new LoadedRenderTarget
                {
                    Size = reg.FramebufferSize,
                    IsWindow = true,
                    WindowId = reg.Id,
                    IsSrgb = true
                });

                reg.RenderTarget = new RenderWindow(this, rtId);

                _glContext!.WindowCreated(glSpec, reg);
                _glContext!.UpdateVSync(_vSync);
            }

            // Pass through result whether successful or not, caller handles it.
            return (reg, error);
        }

        void IWindowingHost.SetPrimaryMonitorId(int id)
        {
            _primaryMonitorId = id;
        }

        void IWindowingHost.UpdateVSync()
        {
            _glContext?.UpdateVSync(_vSync);
        }

        void IWindowingHost.DoDestroyWindow(WindowReg reg)
        {
            DoDestroyWindow(reg);
        }

        private void DoDestroyWindow(WindowReg reg)
        {
            if (reg.IsMainWindow)
                throw new InvalidOperationException("Cannot destroy main window.");

            if (reg.IsDisposed)
                return;

            reg.IsDisposed = true;

            _glContext!.WindowDestroyed(reg);
            _windowing!.WindowDestroy(reg);

            _windows.Remove(reg);
            _windowHandles.Remove(reg.Handle);

            var destroyed = new WindowDestroyedEventArgs(reg.Handle);
            DestroyWindow?.Invoke(destroyed);
            reg.Closed?.Invoke(destroyed);
        }

        public void ProcessInput(FrameEventArgs frameEventArgs)
        {
            _windowing?.ProcessEvents();
            DispatchEvents();
        }

        private void SwapAllBuffers()
        {
            _glContext?.SwapAllBuffers();
        }

        private void VSyncChanged(bool newValue)
        {
            _vSync = newValue;
            _glContext?.UpdateVSync(newValue);
        }

        private void WindowModeChanged(int mode)
        {
            _windowMode = (WindowMode) mode;
            if (_mainWindow != null)
                _windowing?.WindowSetMode(_mainWindow, _windowMode);
        }

        Task<string> IClipboardManager.GetText()
        {
            return _windowing?.ClipboardGetText(_mainWindow!) ?? Task.FromResult("");
        }

        void IClipboardManager.SetText(string text)
        {
            _windowing?.ClipboardSetText(_mainWindow!, text);
        }

        public IEnumerable<IClydeMonitor> EnumerateMonitors()
        {
            return _monitorHandles.Values;
        }

        public ICursor GetStandardCursor(StandardCursorShape shape)
        {
            DebugTools.AssertNotNull(_windowing);

            return _windowing!.CursorGetStandard(shape);
        }

        public ICursor CreateCursor(Image<Rgba32> image, Vector2i hotSpot)
        {
            DebugTools.AssertNotNull(_windowing);

            return _windowing!.CursorCreate(image, hotSpot);
        }

        public void SetCursor(ICursor? cursor)
        {
            DebugTools.AssertNotNull(_windowing);

            _windowing!.CursorSet(_mainWindow!, cursor);
        }


        public void RunOnWindowThread(Action a)
        {
            DebugTools.AssertNotNull(_windowing);

            _windowing!.RunOnWindowThread(a);
        }

        public void TextInputSetRect(UIBox2i rect)
        {
            DebugTools.AssertNotNull(_windowing);

            _windowing!.TextInputSetRect(rect);
        }

        public void TextInputStart()
        {
            DebugTools.AssertNotNull(_windowing);

            _windowing!.TextInputStart();
        }

        public void TextInputStop()
        {
            DebugTools.AssertNotNull(_windowing);

            _windowing!.TextInputStop();
        }

        private abstract class MonitorReg
        {
            public MonitorHandle Handle = default!;
        }
    }
}
