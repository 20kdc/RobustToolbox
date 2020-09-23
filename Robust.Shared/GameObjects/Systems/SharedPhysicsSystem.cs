using System;
using System.Collections.Generic;
using System.Linq;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects.Components;
using Robust.Shared.Interfaces.Map;
using Robust.Shared.Interfaces.Physics;
using Robust.Shared.Interfaces.Random;
using Robust.Shared.Interfaces.Timing;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Robust.Shared.GameObjects.Systems
{
    public abstract class SharedPhysicsSystem : EntitySystem
    {
        [Dependency] private readonly ITileDefinitionManager _tileDefinitionManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IPhysicsManager _physicsManager = default!;
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly IGameTiming _timing = default!;

        private const float Epsilon = 1.0e-6f;

        // Runtime stuff that gets cleared and such, ugly but good on the GC
        private readonly HashSet<ICollidableComponent> _awakeBodies = new HashSet<ICollidableComponent>();
        private readonly List<ICollidableComponent> _awakeBodiesAddCache = new List<ICollidableComponent>();

        /// <summary>
        /// Simulates the physical world for a given amount of time.
        /// </summary>
        /// <param name="deltaTime">Delta Time in seconds of how long to simulate the world.</param>
        /// <param name="physicsComponents">List of all possible physics bodes </param>
        /// <param name="prediction">Should only predicted entities be considered in this simulation step?</param>
        protected void SimulateWorld(float deltaTime, bool prediction)
        {
            List<ICollidableComponent> physicsComponents;
            if (!prediction)
            {
                physicsComponents = EntityManager.ComponentManager.EntityQuery<ICollidableComponent>().ToList();
            }
            else
            {
                physicsComponents = EntityManager.ComponentManager.EntityQuery<ICollidableComponent>().Where(p => p.Predict).ToList();
            }
            _awakeBodies.Clear();

            // GITD: If a body doesn't have a controller, don't give it
            var bodiesWithControllers = new List<ICollidableComponent>();

            // This is the main "apply forces and do stuff" loop.
            foreach (var body in physicsComponents)
            {
                if (prediction && !body.Predict)
                    continue;

                if (body.Controllers.Values.Count > 0)
                    bodiesWithControllers.Add(body);

                if (!body.Awake)
                    continue;

                _awakeBodies.Add(body);

                // running prediction updates will not cause a body to go to sleep.
                if (!prediction)
                    body.SleepAccumulator++;

                // if the body cannot move, nothing to do here
                if (!body.CanMove())
                    continue;

                var linearVelocity = Vector2.Zero;

                foreach (var controller in body.Controllers.Values)
                {
                    controller.UpdateBeforeProcessing();
                    linearVelocity += controller.LinearVelocity;
                }

                // i'm not sure if this is the proper way to solve this, but
                // these are not kinematic bodies, so we need to preserve the previous
                // velocity.
                //if (body.LinearVelocity.LengthSquared < linearVelocity.LengthSquared)
                    body.LinearVelocity = linearVelocity;

                // Integrate forces
                body.LinearVelocity += body.Force * body.InvMass * deltaTime;
                body.AngularVelocity += body.Torque * body.InvI * deltaTime;

                // forces are instantaneous, so these properties are cleared
                // once integrated. If you want to apply a continuous force,
                // it has to be re-applied every tick.
                body.Force = Vector2.Zero;
                body.Torque = 0f;

                // Process frictional forces
                ProcessFriction(body, deltaTime);
            }

            foreach (var physics in physicsComponents)
            {
                var wasAwakeBefore = physics.Awake;
                foreach (var controller in physics.Controllers.Values)
                {
                    controller.UpdateAfterProcessing();
                }
                // PhysGITD: Try to keep _awakeBodies as accurate as possible
                if (physics.Awake && !wasAwakeBefore)
                {
                    _awakeBodies.Add(physics);
                }
            }

            // Remove all entities that were deleted due to the controller
            physicsComponents.RemoveAll(p => p.Deleted);

            /* PhysGITG: Commented because not using right now
            const int solveIterationsAt60 = 2;

            var multiplier = deltaTime / (1f / 60);

            var divisions = (int) MathHelper.Clamp(
                MathF.Round(solveIterationsAt60 * multiplier, MidpointRounding.AwayFromZero),
                1,
                4
            );

            if (_timing.InSimulation || prediction) divisions = 1;*/

            var outerDivisions = 1;
            var innerDivisions = 2;

            for (var i = 0; i < outerDivisions; i++)
            {
                foreach (var physics in _awakeBodies)
                {
                    if (physics.Deleted)
                        continue;
                    if (physics.Awake && physics.CanMove())
                    {
                        MoveAndSlide(physics, deltaTime / outerDivisions, innerDivisions);
                    }
                }
                foreach (var body in _awakeBodiesAddCache)
                {
                    _awakeBodies.Add(body);
                }
            }
        }

        // Alternates between moving the target body as far as it can in a given direction, and altering it's velocity to make it slide.
        private void MoveAndSlide(ICollidableComponent body, float deltaTime, int innerDivisions)
        {
            for (var i = 0; i < innerDivisions; i++)
            {
                if (deltaTime < Epsilon)
                    return;
                if (body.LinearVelocity.LengthSquared < Epsilon && MathF.Abs(body.AngularVelocity) < Epsilon)
                    return;

                // PhysGITD: This is written knowing CCD isn't available.
                // If CCD is in use, this won't make use of it.
                // Keep in mind, the MoveAndSlide function assumes that everything else remains still,
                //  since everything else will later do it's own collision checks when it moves.

                var startPos = body.WorldPosition;
                var startRot = body.WorldRotation;

                var velPos = body.LinearVelocity;
                var velRot = body.AngularVelocity;

                var safeDeltaTime = deltaTime;
                if (body.Hard)
                {
                    var hit = false;
                    var hitNormal = Vector2.Zero;
                    foreach (var b in _physicsManager.GetCollidingEntities(body, velPos * deltaTime))
                    {
                        // Entities that got here passed broadphase. We don't yet have any idea if we're actually colliding with them though.
                        var bCollidable = b.GetComponent<ICollidableComponent>();
                        if ((bCollidable == body) || !bCollidable.Hard)
                            continue;
                        var collisionWindow = TimeToImpact.Calculate(body, velPos, bCollidable);
                        var windowEffectiveStart = collisionWindow.Start - 0.001f;
                        if (collisionWindow.Valid && (collisionWindow.Start >= 0) && (windowEffectiveStart < safeDeltaTime))
                        {
                            safeDeltaTime = windowEffectiveStart;
                            hit = true;
                            hitNormal = collisionWindow.Normal;
                        }
                    }
                    if (hit)
                    {
                        // let's just do this the simple way
                        // note that this won't have an effect on velPos/velRot, which is critically important
                        body.LinearVelocity *= new Vector2(Math.Abs(hitNormal.Y), Math.Abs(hitNormal.X));
                    }
                }

                // Confirm.
                safeDeltaTime = Math.Max(0, safeDeltaTime);
                body.WorldPosition += velPos * safeDeltaTime;
                body.WorldRotation += velRot * safeDeltaTime;
                deltaTime -= safeDeltaTime;
            }
        }
/*
            var targets = new HashSet<ICollidableComponent>();

            foreach (var b in _physicsManager.GetCollidingEntities(aCollidable, Vector2.Zero))
            {
                var aUid = aCollidable.Entity.Uid;
                var bUid = b.Uid;

                if (bUid.CompareTo(aUid) > 0)
                {
                    var tmpUid = bUid;
                    bUid = aUid;
                    aUid = tmpUid;
                }

                if (!combinations.Add((aUid, bUid)))
                {
                    continue;
                }

                var bCollidable = b.GetComponent<ICollidableComponent>();
                _collisionCache.Add(new Manifold(aCollidable, bCollidable, aCollidable.Hard && bCollidable.Hard));
            }

            var counter = 0;
            while(GetNextCollision(_collisionCache, counter, out var collision))
            {
                collision.A.WakeBody();
                if (!collision.B.Awake) {
                    collision.B.WakeBody();
                    _awakeBodiesAddCache.Add(collision.B);
                }

                counter++;
                var impulse = _physicsManager.SolveCollisionImpulse(collision);
                if (collision.A.CanMove())
                {
                    collision.A.Momentum -= impulse;
                }

                if (collision.B.CanMove())
                {
                    collision.B.Momentum += impulse;
                }
            }

            var collisionsWith = new Dictionary<ICollideBehavior, int>();
            foreach (var collision in _collisionCache)
            {
                // Apply onCollide behavior
                var aBehaviors = collision.A.Entity.GetAllComponents<ICollideBehavior>();
                foreach (var behavior in aBehaviors)
                {
                    var entity = collision.B.Entity;
                    if (entity.Deleted) continue;
                    behavior.CollideWith(entity);
                    if (collisionsWith.ContainsKey(behavior))
                    {
                        collisionsWith[behavior] += 1;
                    }
                    else
                    {
                        collisionsWith[behavior] = 1;
                    }
                }
                var bBehaviors = collision.B.Entity.GetAllComponents<ICollideBehavior>();
                foreach (var behavior in bBehaviors)
                {
                    var entity = collision.A.Entity;
                    if (entity.Deleted) continue;
                    behavior.CollideWith(entity);
                    if (collisionsWith.ContainsKey(behavior))
                    {
                        collisionsWith[behavior] += 1;
                    }
                    else
                    {
                        collisionsWith[behavior] = 1;
                    }
                }
            }

            foreach (var behavior in collisionsWith.Keys)
            {
                behavior.PostCollide(collisionsWith[behavior]);
            }
        }
*/
        private bool GetNextCollision(IReadOnlyList<Manifold> collisions, int counter, out Manifold collision)
        {
            // The *4 is completely arbitrary
            if (counter > collisions.Count * 4)
            {
                collision = default;
                return false;
            }
            var indexes = new List<int>();
            for (int i = 0; i < collisions.Count; i++)
            {
                indexes.Add(i);
            }
            _random.Shuffle(indexes);
            foreach (var index in indexes)
            {
                if (collisions[index].Unresolved)
                {
                    collision = collisions[index];
                    return true;
                }
            }

            collision = default;
            return false;
        }

        private void ProcessFriction(ICollidableComponent body, float deltaTime)
        {
            if (body.LinearVelocity == Vector2.Zero) return;

            // sliding friction coefficient, and current gravity at current location
            var (friction, gravity) = GetFriction(body);

            // friction between the two objects
            var effectiveFriction = friction * body.Friction;

            // current acceleration due to friction
            var fAcceleration = effectiveFriction * gravity;

            // integrate acceleration
            var fVelocity = fAcceleration * deltaTime;

            // Clamp friction because friction can't make you accelerate backwards
            friction = Math.Min(fVelocity, body.LinearVelocity.Length);

            // No multiplication/division by mass here since that would be redundant.
            var frictionVelocityChange = body.LinearVelocity.Normalized * -friction;

            body.LinearVelocity += frictionVelocityChange;
        }

        // DO NOT RUN if the body cannot move! The checks were removed.
        private static void UpdatePosition(IPhysBody body, float frameTime)
        {
            if (body.LinearVelocity.LengthSquared < Epsilon && MathF.Abs(body.AngularVelocity) < Epsilon)
                return;

            if (body.LinearVelocity != Vector2.Zero)
            {
                var ent = body.Entity;

                var entityMoveMessage = new EntityMovementMessage();
                ent.SendMessage(ent.Transform, entityMoveMessage);

                if (ContainerHelpers.IsInContainer(ent))
                {
                    var relayEntityMoveMessage = new RelayMovementEntityMessage(ent);
                    ent.Transform.Parent!.Owner.SendMessage(ent.Transform, relayEntityMoveMessage);
                    // This prevents redundant messages from being sent if solveIterations > 1 and also simulates the entity "colliding" against the locker door when it opens.
                    body.LinearVelocity = Vector2.Zero;
                }
            }

            body.WorldRotation += body.AngularVelocity * frameTime;
            body.WorldPosition += body.LinearVelocity * frameTime;
        }

        // Based off of Randy Gaul's ImpulseEngine code
        private void FixClipping(List<Manifold> collisions)
        {
            const float allowance = 1 / 128f;
            foreach (var collision in collisions)
            {
                if (!collision.Hard)
                {
                    continue;
                }

                var penetration = _physicsManager.CalculatePenetration(collision.A, collision.B);

                if (penetration <= allowance)
                    continue;

                var correction = collision.Normal * Math.Abs(penetration);
                // PhysGITD: This bit is interesting.
                // I tried making this realistically adjust based on mass.
                // This did not work out.
                // What I've worked out is, if we're not exceptionally careful to try and force "pushers" to move instead of "pushees",
                //  the whole thing can derail very, very quickly.
                var cwA = 0.5f;
                var cwB = 0.5f;
                if (!collision.A.CanMove())
                {
                    cwA = 0;
                    cwB = collision.B.CanMove() ? 1 : 0;
                }
                else if (!collision.B.CanMove())
                {
                    cwA = collision.A.CanMove() ? 1 : 0;
                    cwB = 0;
                }
                else if (collision.A.LinearVelocity.LengthSquared < collision.B.LinearVelocity.LengthSquared)
                {
                    cwA = 0;
                    cwB = 1;
                }
                else
                {
                    cwA = 1;
                    cwB = 0;
                }
                if (cwA != 0)
                    collision.A.Owner.Transform.WorldPosition -= correction * cwA;
                if (cwB != 0)
                    collision.B.Owner.Transform.WorldPosition += correction * cwB;
            }
        }

        private (float friction, float gravity) GetFriction(ICollidableComponent body)
        {
            if (!body.OnGround)
                return (0f, 0f);

            var location = body.Owner.Transform;
            var grid = _mapManager.GetGrid(location.Coordinates.GetGridId(EntityManager));
            var tile = grid.GetTileRef(location.Coordinates);
            var tileDef = _tileDefinitionManager[tile.Tile.TypeId];
            return (tileDef.Friction, grid.HasGravity ? 9.8f : 0f);
        }
    }
}
