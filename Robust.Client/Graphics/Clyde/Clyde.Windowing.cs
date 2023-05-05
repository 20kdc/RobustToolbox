﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

namespace Robust.Client.Graphics.Clyde
{
    internal partial class  Clyde
    {
        private readonly List<WindowReg> _windows = new();
        private readonly List<WindowHandle> _windowHandles = new();
        private readonly Dictionary<int, MonitorHandle> _monitorHandles = new();

        private int _primaryMonitorId;
        private WindowReg? _mainWindow;

        private IWindowingImpl? _windowing;
#pragma warning disable 414
        // Keeping this for if/when we ever get a new renderer.
        private Renderer _chosenRenderer;
#pragma warning restore 414

        private ResPath? _windowIconPath;
        private Thread? _windowingThread;
        private bool _vSync;
        private WindowMode _windowMode;
        private WindowReg? _currentHoveredWindow;

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
                var window = _currentHoveredWindow;
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

        private bool TryInitMainWindow([NotNullWhen(false)] out string? error)
        {
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

            var (reg, err) = SharedWindowCreate(parameters, isMain: true);

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

            _chosenRenderer = Renderer.OpenGL;

            var succeeded = false;
            string? lastError = null;

            if (!TryInitMainWindow(out lastError))
                Logger.DebugS("clyde.win", $"Failed to create window: {lastError}");
            else
                succeeded = true;

            // We should have a main window now.
            DebugTools.AssertNotNull(_mainWindow);

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
                            (ushort*) pText,
                            (ushort*) pCaption,
                            MB.MB_OK | MB.MB_ICONERROR);
                    }
                }

                Logger.FatalS("clyde.win",
                    "Failed to create main game window! " +
                    "This probably means your GPU is too old to run the game. " +
                    $"That or update your graphics drivers. {lastError}");

                return false;
            }

            // Quickly do a render with _drawingSplash = true so the screen isn't blank.
            Render();

            return true;
        }

        private IEnumerable<Image<Rgba32>> LoadWindowIcons()
        {
            if (OperatingSystem.IsMacOS() || _windowIconPath == null)
            {
                // Does nothing on macOS so don't bother.
                yield break;
            }

            foreach (var file in _resourceCache.ContentFindFiles(_windowIconPath))
            {
                if (file.Extension != "png")
                {
                    continue;
                }

                using var stream = _resourceCache.ContentFileRead(file);
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
            DebugTools.AssertNotNull(_mainWindow);

            var (reg, error) = SharedWindowCreate(
                parameters,
                isMain: false);

            // Rebinding is handed by WindowCreated in the GL context.

            if (error != null)
                throw new Exception(error);

            return reg!.Handle;
        }

        private (WindowReg?, string? error) SharedWindowCreate(WindowCreateParameters parameters, bool isMain)
        {
            WindowReg? owner = null;
            if (parameters.Owner != null)
                owner = ((WindowHandle)parameters.Owner).Reg;

            var (reg, error) = _windowing!.WindowCreate(parameters, owner);

            if (reg != null)
            {
                // Window init succeeded, do setup.
                reg.IsMainWindow = isMain;
                if (isMain)
                    _mainWindow = reg;

                _windows.Add(reg);
                _windowHandles.Add(reg.Handle);

                var rtId = AllocRid();
                /*
                _renderTargets.Add(rtId, new LoadedRenderTarget
                {
                    Size = reg.FramebufferSize,
                    IsWindow = true,
                    WindowId = reg.Id,
                    IsSrgb = true
                });
                */

                // reg.RenderTarget = new RenderWindow(this, rtId);

                // _glContext!.WindowCreated(glSpec, reg);
            }

            // Pass through result whether successful or not, caller handles it.
            return (reg, error);
        }

        private void DoDestroyWindow(WindowReg reg)
        {
            if (reg.IsMainWindow)
                throw new InvalidOperationException("Cannot destroy main window.");

            if (reg.IsDisposed)
                return;

            reg.IsDisposed = true;

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
        }

        private void VSyncChanged(bool newValue)
        {
            _vSync = newValue;
        }

        private void WindowModeChanged(int mode)
        {
            _windowMode = (WindowMode) mode;
            _windowing?.UpdateMainWindowMode();
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


        private void SetWindowVisible(WindowReg reg, bool visible)
        {
            DebugTools.AssertNotNull(_windowing);

            _windowing!.WindowSetVisible(reg, visible);
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

        private abstract class WindowReg
        {
            public bool IsDisposed;

            public WindowId Id;
            public Vector2 WindowScale;
            public Vector2 PixelRatio;
            public Vector2i FramebufferSize;
            public Vector2i WindowSize;
            public Vector2i PrevWindowSize;
            public Vector2i WindowPos;
            public Vector2i PrevWindowPos;
            public Vector2 LastMousePos;
            public bool IsFocused;
            public bool IsMinimized;
            public string Title = "";
            public bool IsVisible;
            public IClydeWindow? Owner;

            public bool DisposeOnClose;

            public bool IsMainWindow;
            public WindowHandle Handle = default!;
            // public RenderWindow RenderTarget = default!;
            public Action<WindowRequestClosedEventArgs>? RequestClosed;
            public Action<WindowDestroyedEventArgs>? Closed;
            public Action<WindowResizedEventArgs>? Resized;
        }

        private sealed class WindowHandle : IClydeWindowInternal
        {
            // So funny story
            // When this class was a record, the C# compiler on .NET 5 stack overflowed
            // while compiling the Closed event.
            // VERY funny.

            private readonly Clyde _clyde;
            public readonly WindowReg Reg;

            public bool IsDisposed => Reg.IsDisposed;
            public WindowId Id => Reg.Id;

            public WindowHandle(Clyde clyde, WindowReg reg)
            {
                _clyde = clyde;
                Reg = reg;
            }

            public void Dispose()
            {
                _clyde.DoDestroyWindow(Reg);
            }

            public Vector2i Size => Reg.FramebufferSize;

            public IRenderTarget RenderTarget => throw new NotImplementedException();

            public string Title
            {
                get => Reg.Title;
                set => _clyde._windowing!.WindowSetTitle(Reg, value);
            }

            public bool IsFocused => Reg.IsFocused;
            public bool IsMinimized => Reg.IsMinimized;

            public bool IsVisible
            {
                get => Reg.IsVisible;
                set => _clyde.SetWindowVisible(Reg, value);
            }

            public Vector2 ContentScale => Reg.WindowScale;

            public bool DisposeOnClose
            {
                get => Reg.DisposeOnClose;
                set => Reg.DisposeOnClose = value;
            }

            public event Action<WindowRequestClosedEventArgs> RequestClosed
            {
                add => Reg.RequestClosed += value;
                remove => Reg.RequestClosed -= value;
            }

            public event Action<WindowDestroyedEventArgs>? Destroyed
            {
                add => Reg.Closed += value;
                remove => Reg.Closed -= value;
            }

            public event Action<WindowResizedEventArgs>? Resized
            {
                add => Reg.Resized += value;
                remove => Reg.Resized -= value;
            }

            public nint? WindowsHWnd => _clyde._windowing!.WindowGetWin32Window(Reg);
        }

        private sealed class MonitorHandle : IClydeMonitor
        {
            public MonitorHandle(int id, string name, Vector2i size, int refreshRate, VideoMode[] videoModes)
            {
                Id = id;
                Name = name;
                Size = size;
                RefreshRate = refreshRate;
                VideoModes = videoModes;
            }

            public int Id { get; }
            public string Name { get; }
            public Vector2i Size { get; }
            public int RefreshRate { get; }
            public IEnumerable<VideoMode> VideoModes { get; }
        }

        private abstract class MonitorReg
        {
            public MonitorHandle Handle = default!;
        }
    }
}
