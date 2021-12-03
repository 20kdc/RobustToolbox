using Robust.Shared.IoC;
using Robust.Shared.Utility;

namespace Robust.Shared.GameObjects
{
    /// <summary>
    /// Defines a component that has "map initialization" behavior.
    /// Basically irreversible behavior that moves the map from "map editor" to playable,
    /// like spawning preset objects.
    /// </summary>
    public interface IMapInit
    {
        void MapInit();
    }

    /// <summary>
    ///     Raised directed on an entity when the map is initialized.
    /// </summary>
    public class MapInitEvent : EntityEventArgs
    {
    }

    public static class MapInitExt
    {
        private static readonly MapInitEvent MapInit = new MapInitEvent();

        public static void RunMapInit(this IEntity entity)
        {
            DebugTools.Assert((!IoCManager.Resolve<IEntityManager>().EntityExists(entity.Uid) ? EntityLifeStage.Deleted : IoCManager.Resolve<IEntityManager>().GetComponent<MetaDataComponent>(entity.Uid).EntityLifeStage) == EntityLifeStage.Initialized);
            IoCManager.Resolve<IEntityManager>().GetComponent<MetaDataComponent>(entity.Uid).EntityLifeStage = EntityLifeStage.MapInitialized;

            IoCManager.Resolve<IEntityManager>().EventBus.RaiseLocalEvent(entity.Uid, MapInit, false);
            foreach (var init in entity.GetAllComponents<IMapInit>())
            {
                init.MapInit();
            }
        }
    }
}
