using System;
using System.Runtime.CompilerServices;
using Robust.Shared.Interfaces.Physics;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Robust.Shared.Physics
{
    // Introduced with PhysGITD.
    // Just to be clear, the results from this thing are only completely accurate for AABB pair collisions.
    public struct TimeToImpact
    {
        public float Start;
        public float End;
        public Vector2 Normal;

        public bool Valid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Start < End;
        }

        public bool Static
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Normal == Vector2.Zero;
        }

        public readonly static TimeToImpact Invalid = new TimeToImpact(float.PositiveInfinity, float.NegativeInfinity, Vector2.Zero);

        public TimeToImpact(float s, float e, Vector2 n)
        {
            Start = s;
            End = e;
            Normal = n;
        }

        // This gives an "impact window" (start and end of impact), which is enough to infer penetration, etc.
        // Do make sure to actually check that the end of the impact is after the start (otherwise the window is invalid).
        // Very clean and more importantly very USEFUL.
        // Note that there is no "bVelocity" - instead, apply this by subtracting from aVelocity.
        // Also note that a time to impact of 0 or under means that collision is already underway.
        public static TimeToImpact Calculate(IPhysBody a, Vector2 aVelocity, IPhysBody b)
        {
            Vector2 aOffset = a.WorldPosition;
            Vector2 bOffset = b.WorldPosition;
            Angle aAngle = a.WorldRotation;
            Angle bAngle = b.WorldRotation;
            TimeToImpact val = TimeToImpact.Invalid;
            foreach (var aShape in a.PhysicsShapes)
            {
                foreach (var bShape in b.PhysicsShapes)
                {
                    var res = Calculate(aShape, aOffset, aAngle, aVelocity, bShape, bOffset, bAngle);
                    if (res.Valid && (res.Start < val.Start))
                        val = res;
                }
            }
            return val;
        }

        // See above
        private static TimeToImpact Calculate(IPhysShape a, Vector2 aOffset, Angle aAngle, Vector2 aVelocity, IPhysShape b, Vector2 bOffset, Angle bAngle)
        {
            Box2 aBox = a.CalculateLocalBounds(aAngle).Translated(aOffset);
            Box2 bBox = b.CalculateLocalBounds(bAngle).Translated(bOffset);
            return Calculate(aBox, aVelocity, bBox);
        }
 
        // See above
        private static TimeToImpact Calculate(Box2 a, Vector2 aVelocity, Box2 b)
        {
            var xWindow = TimeToImpactAxis(a.Left, a.Right, aVelocity.X, b.Left, b.Right);
            // Bottom then top is intended because of the way Y works in this coordinate system
            var yWindow = TimeToImpactAxis(a.Bottom, a.Top, aVelocity.Y, b.Bottom, b.Top);

            // The last one that hits determines the normal (i.e. this is the axis that had to contact to collide)
            return new TimeToImpact(
                Math.Max(xWindow.Item1, yWindow.Item1),
                Math.Min(xWindow.Item2, yWindow.Item2),
                (xWindow.Item1 > yWindow.Item1) ? new Vector2(aVelocity.X > 0 ? -1 : 1, 0) : new Vector2(0, aVelocity.Y > 0 ? -1 : 1)
            );
        }
 
        // See above
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (float, float) TimeToImpactAxis(float aLeft, float aRight, float aVelocity, float bLeft, float bRight)
        {
            if (aVelocity > 0)
            {
                return (
                    (bLeft - aRight) / aVelocity,
                    (bRight - aRight) / aVelocity
                );
            }
            else if (aVelocity < 0)
            {
                return (
                    (aLeft - bRight) / -aVelocity,
                    (aLeft - bLeft) / -aVelocity
                );
            }
            else
            {
                // If there's no velocity at all, we know that the object is embedded.
                bool collides = aRight >= bLeft && aLeft <= bRight;
                return (
                    collides ? float.NegativeInfinity : float.PositiveInfinity,
                    collides ? float.PositiveInfinity : float.NegativeInfinity
                );
            }
        }
    }
}
