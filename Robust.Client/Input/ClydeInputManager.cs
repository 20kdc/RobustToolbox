using Robust.Client.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Robust.Client.Input
{
    internal sealed class ClydeInputManager : InputManager
    {
        [Dependency] private readonly IPALInternal _pal = default!;

        public override ScreenCoordinates MouseScreenPosition => _pal.MouseScreenPosition;

        public override string GetKeyName(Keyboard.Key key)
        {
            return _pal.GetKeyName(key);
        }
    }
}
