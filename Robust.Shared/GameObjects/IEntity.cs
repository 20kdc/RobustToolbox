using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Shared.ViewVariables;

namespace Robust.Shared.GameObjects
{
    [CopyByRef, Serializable]
    public sealed class IEntity
    {
        #region Members

        [ViewVariables]
        public EntityUid Uid { get; }

        public EntityLifeStage LifeStage
        {
            get => !IoCManager.Resolve<IEntityManager>().EntityExists(Uid) ? EntityLifeStage.Deleted : IoCManager.Resolve<IEntityManager>().GetComponent<MetaDataComponent>(Uid).EntityLifeStage;
            internal set => IoCManager.Resolve<IEntityManager>().GetComponent<MetaDataComponent>(Uid).EntityLifeStage = value;
        }

        [ViewVariables]
        public GameTick LastModifiedTick { get => IoCManager.Resolve<IEntityManager>().GetComponent<MetaDataComponent>(Uid).EntityLastModifiedTick; internal set => IoCManager.Resolve<IEntityManager>().GetComponent<MetaDataComponent>(Uid).EntityLastModifiedTick = value; }


        [ViewVariables]
        public EntityPrototype? Prototype
        {
            get => IoCManager.Resolve<IEntityManager>().GetComponent<MetaDataComponent>(Uid).EntityPrototype;
            internal set => IoCManager.Resolve<IEntityManager>().GetComponent<MetaDataComponent>(Uid).EntityPrototype = value;
        }

        [ViewVariables(VVAccess.ReadWrite)]
        public string Description
        {
            get => IoCManager.Resolve<IEntityManager>().GetComponent<MetaDataComponent>(Uid).EntityDescription;
            set => IoCManager.Resolve<IEntityManager>().GetComponent<MetaDataComponent>(Uid).EntityDescription = value;
        }

        [ViewVariables(VVAccess.ReadWrite)]
        public string Name
        {
            get => IoCManager.Resolve<IEntityManager>().GetComponent<MetaDataComponent>(Uid).EntityName;
            set => IoCManager.Resolve<IEntityManager>().GetComponent<MetaDataComponent>(Uid).EntityName = value;
        }

        public bool Initialized => LifeStage >= EntityLifeStage.Initialized;

        public bool Initializing => LifeStage == EntityLifeStage.Initializing;

        public bool Deleted => LifeStage >= EntityLifeStage.Deleted;

        [ViewVariables]
        public bool Paused { get => Deleted || IoCManager.Resolve<IEntityManager>().GetComponent<MetaDataComponent>(Uid).EntityPaused; set => IoCManager.Resolve<IEntityManager>().GetComponent<MetaDataComponent>(Uid).EntityPaused = value; }

        [ViewVariables]
        public TransformComponent Transform => IoCManager.Resolve<IEntityManager>().GetComponent<TransformComponent>(Uid);

        #endregion Members

        #region Initialization

        public IEntity(EntityUid uid)
        {
            Uid = uid;
        }

        public bool IsValid()
        {
            return IoCManager.Resolve<IEntityManager>().EntityExists(Uid);
        }

        #endregion Initialization

        #region Component Messaging

        [Obsolete("Component Messages are deprecated, use Entity Events instead.")]
        public void SendMessage(IComponent? owner, ComponentMessage message)
        {
            var components = IoCManager.Resolve<IEntityManager>().GetComponents(Uid);
            foreach (var component in components)
            {
                if (owner != component)
                    component.HandleMessage(message, owner);
            }
        }

        [Obsolete("Component Messages are deprecated, use Entity Events instead.")]
        public void SendNetworkMessage(IComponent owner, ComponentMessage message, INetChannel? channel = null)
        {
            IoCManager.Resolve<IEntityManager>().EntityNetManager?.SendComponentNetworkMessage(channel, this, owner, message);
        }

        #endregion Component Messaging

        #region Components

        /// <summary>
        ///     Public method to add a component to an entity.
        ///     Calls the component's onAdd method, which also adds it to the component manager.
        /// </summary>
        /// <param name="component">The component to add.</param>
        public void AddComponent(Component component)
        {
            IoCManager.Resolve<IEntityManager>().AddComponent(this, component);
        }

        public T AddComponent<T>()
            where T : Component, new()
        {
            return IoCManager.Resolve<IEntityManager>().AddComponent<T>(this);
        }

        public void RemoveComponent<T>()
        {
            IoCManager.Resolve<IEntityManager>().RemoveComponent<T>(Uid);
        }

        public bool HasComponent<T>()
        {
            return IoCManager.Resolve<IEntityManager>().HasComponent<T>(Uid);
        }

        public bool HasComponent(Type type)
        {
            return IoCManager.Resolve<IEntityManager>().HasComponent(Uid, type);
        }

        public T GetComponent<T>()
        {
            DebugTools.Assert(!Deleted, "Tried to get component on a deleted entity.");

            return IoCManager.Resolve<IEntityManager>().GetComponent<T>(Uid);
        }

        public IComponent GetComponent(Type type)
        {
            DebugTools.Assert(!Deleted, "Tried to get component on a deleted entity.");

            return IoCManager.Resolve<IEntityManager>().GetComponent(Uid, type);
        }

        public bool TryGetComponent<T>([NotNullWhen(true)] out T? component) where T : class
        {
            DebugTools.Assert(!Deleted, "Tried to get component on a deleted entity.");

            return IoCManager.Resolve<IEntityManager>().TryGetComponent(Uid, out component);
        }

        public T? GetComponentOrNull<T>() where T : class, IComponent
        {
            return IoCManager.Resolve<IEntityManager>().GetComponentOrNull<T>(Uid);
        }

        public bool TryGetComponent(Type type, [NotNullWhen(true)] out IComponent? component)
        {
            DebugTools.Assert(!Deleted, "Tried to get component on a deleted entity.");

            return IoCManager.Resolve<IEntityManager>().TryGetComponent(Uid, type, out component);
        }

        public void QueueDelete()
        {
            IoCManager.Resolve<IEntityManager>().QueueDeleteEntity(Uid);
        }

        public void Delete()
        {
            IoCManager.Resolve<IEntityManager>().DeleteEntity(Uid);
        }

        public IEnumerable<IComponent> GetAllComponents()
        {
            return IoCManager.Resolve<IEntityManager>().GetComponents(Uid);
        }

        public IEnumerable<T> GetAllComponents<T>()
        {
            return IoCManager.Resolve<IEntityManager>().GetComponents<T>(Uid);
        }

        #endregion Components

        public override string ToString()
        {
            return IoCManager.Resolve<IEntityManager>().ToPrettyString(Uid);
        }
    }
}
