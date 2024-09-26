using System;
using System.Collections.Generic;
using Robust.Client.Input;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Robust.Client.Graphics
{
    internal interface IClydeInternal : IClyde
    {
        // Basic main loop hooks.
        void Render();
        void FrameProcess(FrameEventArgs eventArgs);

        // Init.
        // PAL.InitializePreWindowing
        // PAL.EnterWindowLoop
        // PAL.InitializePostWindowing
        void InitializePostGL();
        void Ready();
        // PAL.Shutdown
        // PAL.TerminateWindowLoop

        ClydeHandle LoadShader(ParsedShader shader, string? name = null, Dictionary<string,string>? defines = null);

        void ReloadShader(ClydeHandle handle, ParsedShader newShader);

        /// <summary>
        ///     Creates a new instance of a shader.
        /// </summary>
        ShaderInstance InstanceShader(ShaderSourceResource handle, bool? light = null, BlendParameters? blend = null);

        IClydeDebugInfo DebugInfo { get; }

        IClydeDebugStats DebugStats { get; }

        WholeTexture GetStockTexture(ClydeStockTexture stockTexture);

        ClydeDebugLayers DebugLayers { get; set; }

        void RegisterGridEcsEvents();

        void ShutdownGridEcsEvents();
    }
}
