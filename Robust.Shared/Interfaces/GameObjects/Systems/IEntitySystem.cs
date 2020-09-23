using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;

namespace Robust.Shared.Interfaces.GameObjects.Systems
{
    /// <summary>
    ///     A subsystem that acts on all components of a type at once.
    ///     Entity systems are similar to TGstation13 subsystems.
    ///     They have a set of entities to run over and run every once in a while.
    ///     They get managed by an <see cref="IEntitySystemManager" />.
    /// </summary>
    public interface IEntitySystem : IEntityEventSubscriber
    {
        IEnumerable<Type> UpdatesAfter { get; }
        IEnumerable<Type> UpdatesBefore { get; }

        /// <summary>
        ///     Called once when the system is created to initialize its state.
        /// </summary>
        void Initialize();

        /// <summary>
        ///     Called once before the system is destroyed so that the system can clean up.
        /// </summary>
        void Shutdown();

        /// <summary>
        /// Called once per frame/tick to update the system. (This is part of "Tick" at client GameController level, and "Update" at server BaseServer level.)
        /// </summary>
        /// <param name="frameTime">Time since the last call in seconds.</param>
        /// <seealso cref="IEntitySystemManager.Update(float)"/>
        void Update(float frameTime);

        /// <summary>
        /// Called once per frame/tick to update the system. (This is part of "Update" at client GameController level and is never called server-side.)
        /// </summary>
        /// <param name="frameTime">Time since the last call in seconds.</param>
        /// <seealso cref="IEntitySystemManager.FrameUpdate(float)"/>
        void FrameUpdate(float frameTime);
    }
}
