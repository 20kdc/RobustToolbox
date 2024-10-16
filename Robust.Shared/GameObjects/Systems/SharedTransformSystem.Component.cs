using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Robust.Shared.GameObjects;

public abstract partial class SharedTransformSystem
{
    #region States

    private void ActivateLerp(TransformComponent xform)
    {
        if (xform.ActivelyLerping)
        {
            return;
        }

        xform.ActivelyLerping = true;
        RaiseLocalEvent(xform.Owner, new TransformStartLerpMessage(xform));
    }

    internal void OnGetState(EntityUid uid, TransformComponent component, ref ComponentGetState args)
    {
        args.State = new TransformComponentState(
            component.LocalPosition,
            component.LocalRotation,
            component.ParentUid,
            component.NoLocalRotation,
            component.Anchored);
    }

    internal void OnHandleState(EntityUid uid, TransformComponent component, ref ComponentHandleState args)
    {
        if (args.Current is TransformComponentState newState)
        {
            var newParentId = newState.ParentID;
            var rebuildMatrices = false;
            if (component.ParentUid != newParentId)
            {
                if (!newParentId.IsValid())
                {
                    component.DetachParentToNull();
                }
                else
                {
                    if (!Exists(newParentId))
                    {
#if !EXCEPTION_TOLERANCE
                        throw new InvalidOperationException($"Unable to find new parent {newParentId}! This probably means the server never sent it.");
#else
                        _logger.Error($"Unable to find new parent {newParentId}! Deleting {ToPrettyString(uid)}");
                        QueueDel(uid);
                        return;
#endif
                    }

                    component.AttachParent(Transform(newParentId));
                }

                rebuildMatrices = true;
            }

            if (component.LocalRotation != newState.Rotation)
            {
                component._localRotation = newState.Rotation;
                rebuildMatrices = true;
            }

            if (!component.LocalPosition.EqualsApprox(newState.LocalPosition))
            {
                var oldPos = component.Coordinates;
                component._localPosition = newState.LocalPosition;

                var ev = new MoveEvent(uid, oldPos, component.Coordinates, component);
                DeferMoveEvent(ref ev);

                rebuildMatrices = true;
            }

            component._prevPosition = newState.LocalPosition;
            component._prevRotation = newState.Rotation;

            component.Anchored = newState.Anchored;
            component._noLocalRotation = newState.NoLocalRotation;

            // This is not possible, because client entities don't exist on the server, so the parent HAS to be a shared entity.
            // If this assert fails, the code above that sets the parent is broken.
            DebugTools.Assert(!component.ParentUid.IsClientSide(), "Transform received a state, but is still parented to a client entity.");

            // Whatever happened on the client, these should still be correct
            DebugTools.Assert(component.ParentUid == newState.ParentID);
            DebugTools.Assert(component.Anchored == newState.Anchored);

            if (rebuildMatrices)
            {
                component.RebuildMatrices();
            }

            Dirty(component);
        }

        if (args.Next is TransformComponentState nextTransform)
        {
            component._nextPosition = nextTransform.LocalPosition;
            component._nextRotation = nextTransform.Rotation;
            component.LerpParent = nextTransform.ParentID;
            ActivateLerp(component);
        }
        else
        {
            // this should cause the lerp to do nothing
            component._nextPosition = null;
            component._nextRotation = null;
            component.LerpParent = EntityUid.Invalid;
        }
    }

    #endregion

    #region World Matrix

    [Pure]
    public Matrix3 GetWorldMatrix(EntityUid uid)
    {
        return Transform(uid).WorldMatrix;
    }

    // Temporary until it's moved here
    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Matrix3 GetWorldMatrix(TransformComponent component)
    {
        return component.WorldMatrix;
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Matrix3 GetWorldMatrix(EntityUid uid, EntityQuery<TransformComponent> xformQuery)
    {
        return GetWorldMatrix(xformQuery.GetComponent(uid));
    }


    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Matrix3 GetWorldMatrix(TransformComponent component, EntityQuery<TransformComponent> xformQuery)
    {
        return component.WorldMatrix;
    }

    #endregion

    #region World Position

    [Pure]
    public Vector2 GetWorldPosition(EntityUid uid)
    {
        return Transform(uid).WorldPosition;
    }

    // Temporary until it's moved here
    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 GetWorldPosition(TransformComponent component)
    {
        return component.WorldPosition;
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 GetWorldPosition(EntityUid uid, EntityQuery<TransformComponent> xformQuery)
    {
        return GetWorldPosition(xformQuery.GetComponent(uid));
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 GetWorldPosition(TransformComponent component, EntityQuery<TransformComponent> xformQuery)
    {
        return component.WorldPosition;
    }

    #endregion

    #region World Rotation

    [Pure]
    public Angle GetWorldRotation(EntityUid uid)
    {
        return Transform(uid).WorldRotation;
    }

    // Temporary until it's moved here
    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Angle GetWorldRotation(TransformComponent component)
    {
        return component.WorldRotation;
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Angle GetWorldRotation(EntityUid uid, EntityQuery<TransformComponent> xformQuery)
    {
        return GetWorldRotation(xformQuery.GetComponent(uid));
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Angle GetWorldRotation(TransformComponent component, EntityQuery<TransformComponent> xformQuery)
    {
        return component.WorldRotation;
    }

    #endregion

    #region Inverse World Matrix

    [Pure]
    public Matrix3 GetInvWorldMatrix(EntityUid uid)
    {
        return Comp<TransformComponent>(uid).InvWorldMatrix;
    }

    // Temporary until it's moved here
    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Matrix3 GetInvWorldMatrix(TransformComponent component)
    {
        return component.InvWorldMatrix;
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Matrix3 GetInvWorldMatrix(EntityUid uid, EntityQuery<TransformComponent> xformQuery)
    {
        return GetInvWorldMatrix(xformQuery.GetComponent(uid));
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Matrix3 GetInvWorldMatrix(TransformComponent component, EntityQuery<TransformComponent> xformQuery)
    {
        return component.InvWorldMatrix;
    }

    #endregion

    public MapId GetMapId(EntityUid? uid, TransformComponent? xform = null)
    {
        if (uid == null ||
            !uid.Value.IsValid() ||
            !Resolve(uid.Value, ref xform, false)) return MapId.Nullspace;

        return xform.MapID;
    }
}
