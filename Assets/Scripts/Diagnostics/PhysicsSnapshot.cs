using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Diagnostics
{
    /// <summary>
    /// One frame of tracked physics state. Kept deliberately small — this gets
    /// serialized and sent over the wire, so every field has to earn its place.
    /// </summary>
    [Serializable]
    public struct BodyState
    {
        public string name;
        public Vector2 position;
        public Vector2 velocity;
        public float angularVelocity;
        public float speed;        // velocity.magnitude, precomputed so the model doesn't have to
        public float height;       // simulated vertical (jump arc / ball flight) — the rigidbody is court-plane only
        public bool isGrounded;

        public override string ToString()
        {
            return $"{name}: pos=({position.x:F2},{position.y:F2}) " +
                   $"vel=({velocity.x:F2},{velocity.y:F2}) speed={speed:F2} " +
                   $"h={height:F2} grounded={isGrounded}";
        }
    }

    /// <summary>
    /// A discrete thing that happened — collision, score, turnover, knockout.
    /// Events are what you actually ask questions about; raw frames are context.
    /// </summary>
    [Serializable]
    public struct GameEvent
    {
        public float time;
        public string kind;          // "collision", "hit", "catch", "turnover", "knockout", ...
        public string description;
        public Vector2 point;        // where it happened
        public Vector2 relativeVelocity;
        public float impactSpeed;

        public override string ToString()
        {
            string s = $"[{time:F3}s] {kind}: {description}";
            if (impactSpeed > 0f)
                s += $" | impact={impactSpeed:F2} at ({point.x:F2},{point.y:F2})" +
                     $" relVel=({relativeVelocity.x:F2},{relativeVelocity.y:F2})";
            return s;
        }
    }

    [Serializable]
    public struct Frame
    {
        public float time;
        public List<BodyState> bodies;
        public List<GameEvent> events;
    }
}
