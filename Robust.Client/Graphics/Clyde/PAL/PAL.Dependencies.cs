using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.IoC;
using Robust.Shared.ContentPack;
using Robust.Shared.Localization;
using Robust.Client.Input;

namespace Robust.Client.Graphics.Clyde;

internal partial class PAL
{
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IResourceManager _resManager = default!;
    [Dependency] private readonly IDependencyCollection _deps = default!;
    [Dependency] private readonly ILocalizationManager _loc = default!;
    [Dependency] private readonly IInputManager _inputManager = default!;

    ILogManager IWindowingHost.LogManager => _logManager;
    IConfigurationManager IWindowingHost.Cfg => _cfg;
}
