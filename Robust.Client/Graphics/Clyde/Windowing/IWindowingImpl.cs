using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Robust.Client.Input;
using Robust.Shared.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Robust.Client.Graphics.Clyde
{
    partial class Clyde
    {
        private interface IWindowingImpl
        {
            // Lifecycle stuff
            bool Init();
            void Shutdown();

            // Window loop
            void EnterWindowLoop();
            void PollEvents();
            void TerminateWindowLoop();

            // Event pump
            void ProcessEvents(bool single=false);
            void FlushDispose();

            // Cursor
            ICursor CursorGetStandard(StandardCursorShape shape);
            ICursor CursorCreate(Image<Rgba32> image, Vector2i hotSpot);
            void CursorSet(WindowReg window, ICursor? cursor);

            // Window API.
            (WindowReg?, string? error) WindowCreate(
                GLContextSpec? spec,
                WindowCreateParameters parameters,
                WindowReg? share,
                WindowReg? owner);

            void WindowDestroy(WindowReg reg);
            void WindowSetTitle(WindowReg window, string title);
            void WindowSetMonitor(WindowReg window, IClydeMonitor monitor);
            void WindowSetVisible(WindowReg window, bool visible);
            void WindowSetMode(WindowReg window, WindowMode mode);
            void WindowRequestAttention(WindowReg window);
            void WindowSwapBuffers(WindowReg window);
            uint? WindowGetX11Id(WindowReg window);
            nint? WindowGetX11Display(WindowReg window);
            nint? WindowGetWin32Window(WindowReg window);

            // Keyboard
            string? KeyGetName(Keyboard.Key key);

            // Clipboard
            Task<string> ClipboardGetText(WindowReg mainWindow);
            void ClipboardSetText(WindowReg mainWindow, string text);

            // OpenGL-related stuff.
            // Note: you should probably go through GLContextBase instead, which calls these functions.
            void GLMakeContextCurrent(WindowReg? reg);
            void GLSwapInterval(WindowReg reg, int interval);
            unsafe void* GLGetProcAddress(string procName);

            // Misc
            void RunOnWindowThread(Action a);

            WindowReg? CurrentHoveredWindow { get; }

            // IME
            void TextInputSetRect(UIBox2i rect);
            void TextInputStart();
            void TextInputStop();
            string GetDescription();
        }

        /// Interface that windowing implementations expect to receive. Used to control surface area expected of Clyde.
        private interface IWindowingHost {
            IWindowingImpl? Windowing { get; }
            WindowReg? MainWindow { get; }
            List<WindowReg> Windows { get; }
            bool ThreadWindowApi { get; }
            Dictionary<int, MonitorHandle> MonitorHandles { get; }

            ClydeHandle AllocRid();

            IEnumerable<Image<Rgba32>> LoadWindowIcons();

            void DoDestroyWindow(WindowReg windowReg);
            void SetPrimaryMonitorId(int id);
            void UpdateVSync();

            void SendKeyUp(KeyEventArgs ev);
            void SendKeyDown(KeyEventArgs ev);
            void SendScroll(MouseWheelEventArgs ev);
            void SendCloseWindow(WindowReg windowReg, WindowRequestClosedEventArgs ev);
            void SendWindowResized(WindowReg reg, Vector2i oldSize);
            void SendWindowContentScaleChanged(WindowContentScaleEventArgs ev);
            void SendWindowFocus(WindowFocusedEventArgs ev);
            void SendText(TextEnteredEventArgs ev);
            void SendTextEditing(TextEditingEventArgs ev);
            void SendMouseMove(MouseMoveEventArgs ev);
            void SendMouseEnterLeave(MouseEnterLeaveEventArgs ev);
            void SendInputModeChanged();
        }
    }
}
