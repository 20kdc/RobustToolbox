using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.IoC;

namespace Robust.Client.Graphics.Clyde;

internal partial class PAL
{
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
}
