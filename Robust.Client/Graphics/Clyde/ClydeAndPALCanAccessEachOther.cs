using Robust.Shared.IoC;

namespace Robust.Client.Graphics.Clyde;

// -- May this become one-way, one day. --

internal sealed partial class Clyde
{
    [Dependency] private readonly PAL _pal = default!;
}

internal sealed partial class PAL
{
    [Dependency] private readonly Clyde _clyde = default!;
}
