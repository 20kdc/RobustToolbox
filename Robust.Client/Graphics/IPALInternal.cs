using System;
using System.Collections.Generic;
using Robust.Client.Input;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Robust.Client.Graphics;

internal interface IPALInternal : IWindowing, IClipboardManager
{
    // Init.
    bool InitializePreWindowing();
    void EnterWindowLoop();
    bool InitializePostWindowing();
    // Clyde.InitializePostWindowing
    // Clyde.Ready
    void Shutdown();
    void TerminateWindowLoop();

    string WindowingDescription { get; }

    void PollEventsAndCleanupResources();
    void ProcessInput(FrameEventArgs frameEventArgs);

    string GetKeyName(Keyboard.Key key);

    /// <returns>Null if not running on X11.</returns>
    uint? GetX11WindowId();

    void RunOnWindowThread(Action action);

    bool SeparateWindowThread { get; }

    /// <summary>
    ///     This is purely a hook for <see cref="IInputManager"/>, use that instead.
    /// </summary>
    ScreenCoordinates MouseScreenPosition { get; }

    event Action<TextEnteredEventArgs> TextEntered;
    event Action<TextEditingEventArgs> TextEditing;
    event Action<MouseMoveEventArgs> MouseMove;
    event Action<MouseEnterLeaveEventArgs> MouseEnterLeave;
    event Action<KeyEventArgs> KeyUp;
    event Action<KeyEventArgs> KeyDown;
    event Action<MouseWheelEventArgs> MouseWheel;
    event Action<WindowRequestClosedEventArgs> CloseWindow;
    event Action<WindowDestroyedEventArgs> DestroyWindow;
}
