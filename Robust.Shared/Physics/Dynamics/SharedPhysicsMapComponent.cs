/*
* Farseer Physics Engine:
* Copyright (c) 2012 Ian Qvist
*
* Original source Box2D:
* Copyright (c) 2006-2011 Erin Catto http://www.box2d.org
*
* This software is provided 'as-is', without any express or implied
* warranty.  In no event will the authors be held liable for any damages
* arising from the use of this software.
* Permission is granted to anyone to use this software for any purpose,
* including commercial applications, and to alter it and redistribute it
* freely, subject to the following restrictions:
* 1. The origin of this software must not be misrepresented; you must not
* claim that you wrote the original software. If you use this software
* in a product, an acknowledgment in the product documentation would be
* appreciated but is not required.
* 2. Altered source versions must be plainly marked as such, and must not be
* misrepresented as being the original software.
* 3. This notice may not be removed or altered from any source distribution.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Dynamics.Contacts;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Utility;
using PhysicsComponent = Robust.Shared.GameObjects.PhysicsComponent;

namespace Robust.Shared.Physics.Dynamics
{
    public abstract class SharedPhysicsMapComponent : Component
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IIslandManager _islandManager = default!;

        internal SharedPhysicsSystem _physics = default!;
        internal SharedBroadphaseSystem BroadphaseSystem = default!;

        internal ContactManager ContactManager = default!;

        public bool AutoClearForces;

        /// <summary>
        ///     Change the global gravity vector.
        /// </summary>
        public Vector2 Gravity
        {
            get => _gravity;
            set
            {
                if (_gravity.EqualsApprox(value)) return;

                // Force every body awake just in case.
                foreach (var body in Bodies)
                {
                    if (body.BodyType != BodyType.Dynamic) continue;
                    body.Awake = true;
                }

                _gravity = value;
            }
        }

        private Vector2 _gravity;

        // TODO: Given physics bodies are a common thing to be listening for on moveevents it's probably beneficial to have 2 versions; one that includes the entity
        // and one that includes the body
        private HashSet<TransformComponent> _deferredUpdates = new();

        /// <summary>
        ///     All bodies present on this map.
        /// </summary>
        public HashSet<PhysicsComponent> Bodies = new();

        /// <summary>
        ///     All awake bodies on this map.
        /// </summary>
        public HashSet<PhysicsComponent> AwakeBodies = new();

        /// <summary>
        ///     Temporary body storage during solving.
        /// </summary>
        private List<PhysicsComponent> _awakeBodyList = new();

        /// <summary>
        /// Temporary joint storage during solving
        /// </summary>
        private List<Joint> _joints = new();

        /// <summary>
        ///     Temporarily store island-bodies for easier iteration.
        /// </summary>
        private HashSet<PhysicsComponent> _islandSet = new();

        private List<PhysicsComponent> _islandBodies = new(64);
        private List<Contact> _islandContacts = new(32);
        private List<Joint> _islandJoints = new(8);

        /// <summary>
        ///     To build islands we do a depth-first search of all colliding bodies and group them together.
        ///     This stack is used to store bodies that are colliding.
        /// </summary>
        private PhysicsComponent[] _stack = new PhysicsComponent[64];

        /// <summary>
        ///     Store last tick's invDT
        /// </summary>
        private float _invDt0;

        public MapId MapId => _entityManager.GetComponent<TransformComponent>(Owner).MapID;

        #region AddRemove

        public void AddBody(PhysicsComponent body)
        {
            if (Bodies.Contains(body)) return;

            // TODO: Kinda dodgy with this and wake shit.
            if (body.Awake && body.BodyType != BodyType.Static)
            {
                AwakeBodies.Add(body);
            }

            Bodies.Add(body);
            body.PhysicsMap = this;
        }

        public void AddAwakeBody(PhysicsComponent body)
        {
            if (body.BodyType == BodyType.Static)
            {
                Logger.ErrorS("physics", $"Tried to add static body {_entityManager.ToPrettyString(body.Owner)} as an awake body to map!");
                return;
            }

            DebugTools.Assert(Bodies.Contains(body));
            if (!Bodies.Contains(body))
            {
                Logger.ErrorS("physics", $"Tried to add {_entityManager.ToPrettyString(body.Owner)} as an awake body to map when it's not contained on the map!");
                return;
            }

            AwakeBodies.Add(body);
        }

        public void RemoveBody(PhysicsComponent body)
        {
            Bodies.Remove(body);
            AwakeBodies.Remove(body);
            body.DestroyContacts();
            body.PhysicsMap = null;
        }

        public void RemoveSleepBody(PhysicsComponent body)
        {
            AwakeBodies.Remove(body);
        }

        #endregion

        /// <summary>
        ///     Where the magic happens.
        /// </summary>
        /// <param name="frameTime"></param>
        /// <param name="prediction"></param>
        public void Step(float frameTime, bool prediction)
        {
            // Box2D does this at the end of a step and also here when there's a fixture update.
            // Given external stuff can move bodies we'll just do this here.
            // Unfortunately this NEEDS to be predicted to make pushing remotely fucking good.
            BroadphaseSystem.FindNewContacts(MapId);

            var invDt = frameTime > 0.0f ? 1.0f / frameTime : 0.0f;
            var dtRatio = _invDt0 * frameTime;

            var updateBeforeSolve = new PhysicsUpdateBeforeMapSolveEvent(prediction, this, frameTime);
            _entityManager.EventBus.RaiseEvent(EventSource.Local, ref updateBeforeSolve);

            ContactManager.Collide();
            // Don't run collision behaviors during FrameUpdate?
            if (!prediction)
                ContactManager.PreSolve(frameTime);

            // Integrate velocities, solve velocity constraints, and do integration.
            Solve(frameTime, dtRatio, invDt, prediction);

            // TODO: SolveTOI

            var updateAfterSolve = new PhysicsUpdateAfterMapSolveEvent(prediction, this, frameTime);
            _entityManager.EventBus.RaiseEvent(EventSource.Local, ref updateAfterSolve);

            // Box2d recommends clearing (if you are) during fixed updates rather than variable if you are using it
            if (!prediction && AutoClearForces)
                ClearForces();

            _invDt0 = invDt;
        }

        /// <summary>
        ///     Go through all of the deferred MoveEvents and then run them
        /// </summary>
        public void ProcessQueue()
        {
            // We'll store the WorldAABB on the MoveEvent given a lot of stuff ends up re-calculating it.
            foreach (var xform in _deferredUpdates)
            {
                xform.RunDeferred();
            }

            _deferredUpdates.Clear();
        }

        private void Solve(float frameTime, float dtRatio, float invDt, bool prediction)
        {
            _islandManager.InitializePools();

            DebugTools.Assert(_islandSet.Count == 0);

            var contactNode = ContactManager._activeContacts.First;

            while (contactNode != null)
            {
                var contact = contactNode.Value;
                contactNode = contactNode.Next;
                contact.IslandFlag = false;
            }

            // Build and simulated islands from awake bodies.
            // Ideally you don't need a stack size for all bodies but we'll TODO: optimise it later.
            var stackSize = Bodies.Count;
            if (stackSize > _stack.Length)
            {
                Array.Resize(ref _stack, Math.Max(_stack.Length * 2, stackSize));
            }

            _awakeBodyList.AddRange(AwakeBodies);

            var metaQuery = _entityManager.GetEntityQuery<MetaDataComponent>();
            var jointQuery = _entityManager.GetEntityQuery<JointComponent>();

            // Build the relevant islands / graphs for all bodies.
            foreach (var seed in _awakeBodyList)
            {
                // TODO: When this gets ECSd add a helper and remove

                if (seed.Deleted)
                {
                    // This should never happen. Yet it does.
                    Logger.Error($"Deleted physics component in awake bodies set. Owner Uid: {seed.Owner}. Physics map: {_entityManager.ToPrettyString(Owner)}");
                    RemoveBody(seed);
                    continue;
                }

                // I tried not running prediction for non-contacted entities but unfortunately it looked like shit
                // when contact broke so if you want to try that then GOOD LUCK.
                if (seed.Island ||
                    metaQuery.GetComponent(seed.Owner).EntityPaused && !seed.IgnorePaused)
                {
                    continue;
                }

                if (prediction && !seed.Predict ||
                    !seed.CanCollide ||
                    seed.BodyType == BodyType.Static)
                {
                    continue;
                }

                // Start of a new island
                _islandBodies.Clear();
                _islandContacts.Clear();
                _islandJoints.Clear();
                var stackCount = 0;
                _stack[stackCount++] = seed;

                // TODO: Probably don't need _islandSet anymore.
                seed.Island = true;

                while (stackCount > 0)
                {
                    var body = _stack[--stackCount];
                    _islandBodies.Add(body);
                    _islandSet.Add(body);

                    // Static bodies don't propagate islands
                    if (body.BodyType == BodyType.Static) continue;

                    // As static bodies can never be awake (unlike Farseer) we'll set this after the check.
                    body.ForceAwake();

                    var node = body.Contacts.First;

                    while (node != null)
                    {
                        var contact = node.Value;
                        node = node.Next;

                        // Has this contact already been added to an island?
                        if (contact.IslandFlag) continue;

                        // Is this contact solid and touching?
                        if (!contact.Enabled || !contact.IsTouching) continue;

                        // Skip sensors.
                        if (contact.FixtureA?.Hard != true || contact.FixtureB?.Hard != true) continue;

                        _islandContacts.Add(contact);
                        contact.IslandFlag = true;
                        var bodyA = contact.FixtureA!.Body;
                        var bodyB = contact.FixtureB!.Body;

                        var other = bodyA == body ? bodyB : bodyA;

                        // Was the other body already added to this island?
                        if (other.Island) continue;

                        DebugTools.Assert(stackCount < stackSize);
                        _stack[stackCount++] = other;

                        other.Island = true;
                    }

                    if (!jointQuery.TryGetComponent(body.Owner, out var jointComponent)) continue;

                    foreach (var (_, joint) in jointComponent.Joints)
                    {
                        if (joint.IslandFlag) continue;

                        var other = joint.BodyA == body ? joint.BodyB : joint.BodyA;

                        // Don't simulate joints connected to inactive bodies.
                        if (!other.CanCollide) continue;

                        _islandJoints.Add(joint);
                        joint.IslandFlag = true;

                        if (other.Island) continue;

                        DebugTools.Assert(stackCount < stackSize);
                        _stack[stackCount++] = other;

                        other.Island = true;
                    }
                }

                _islandManager
                    .AllocateIsland(_islandBodies.Count, _islandContacts.Count, _islandJoints.Count)
                    .Append(_islandBodies, _islandContacts, _islandJoints);

                _joints.AddRange(_islandJoints);

                // Allow static bodies to be re-used in other islands
                for (var i = 0; i < _islandBodies.Count; i++)
                {
                    var body = _islandBodies[i];

                    // Static bodies can participate in other islands
                    if (body.BodyType == BodyType.Static)
                    {
                        body.Island = false;
                    }
                }
            }

            SolveIslands(frameTime, dtRatio, invDt, prediction);
            Cleanup(frameTime);

            ContactManager.PostSolve();
        }

        protected virtual void Cleanup(float frameTime)
        {
            foreach (var body in _islandSet)
            {
                if (!body.Island || body.Deleted)
                {
                    continue;
                }

                body.IslandIndex.Clear();
                body.Island = false;
                DebugTools.Assert(body.BodyType != BodyType.Static);

                // So Box2D would update broadphase here buutttt we'll just wait until MoveEvent queue is used.
            }

            _islandSet.Clear();
            _awakeBodyList.Clear();

            foreach (var joint in _joints)
            {
                joint.IslandFlag = false;
            }

            _joints.Clear();
        }

        private void SolveIslands(float frameTime, float dtRatio, float invDt, bool prediction)
        {
            var islands = _islandManager.GetActive;
            // Islands are already pre-sorted
            var iBegin = 0;

            while (iBegin < islands.Count)
            {
                var island = islands[iBegin];

                island.Solve(Gravity, frameTime, dtRatio, invDt, prediction);
                iBegin++;
                // TODO: Submit rest in parallel if applicable
            }

            // TODO: parallel dispatch here

            // Update bodies sequentially to avoid race conditions. May be able to do this parallel someday
            // but easier to just do this for now.
            foreach (var island in islands)
            {
                island.UpdateBodies(_deferredUpdates);
                island.SleepBodies(prediction, frameTime);
            }
        }

        private void ClearForces()
        {
            foreach (var body in AwakeBodies)
            {
                body.Force = Vector2.Zero;
                body.Torque = 0.0f;
            }
        }
    }

    [ByRefEvent]
    public readonly struct PhysicsUpdateBeforeMapSolveEvent
    {
        public readonly bool Prediction;
        public readonly SharedPhysicsMapComponent MapComponent;
        public readonly float DeltaTime;

        public PhysicsUpdateBeforeMapSolveEvent(bool prediction, SharedPhysicsMapComponent mapComponent, float deltaTime)
        {
            Prediction = prediction;
            MapComponent = mapComponent;
            DeltaTime = deltaTime;
        }
    }

    [ByRefEvent]
    public readonly struct PhysicsUpdateAfterMapSolveEvent
    {
        public readonly bool Prediction;
        public readonly SharedPhysicsMapComponent MapComponent;
        public readonly float DeltaTime;

        public PhysicsUpdateAfterMapSolveEvent(bool prediction, SharedPhysicsMapComponent mapComponent, float deltaTime)
        {
            Prediction = prediction;
            MapComponent = mapComponent;
            DeltaTime = deltaTime;
        }
    }
}
