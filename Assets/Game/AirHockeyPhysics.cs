using System;
using UnityEngine;

namespace IPZ.AirHockey
{
    /// Table simulation in table units, with the origin at the centre and goals on the short sides.
    /// Player 0 defends the left goal and player 1 the right one.
    public class AirHockeyPhysics
    {
        public const float HalfLength = 8f;
        public const float HalfWidth = 3.9f;
        public const float GoalHalfWidth = 1.3f;
        public const float PuckRadius = 0.38f;
        public const float MalletRadius = 0.62f;

        const float StepSeconds = 1f / 240f;
        const int MaxSteps = 16;
        const float Friction = 0.15f;
        const float WallBounce = 0.9f;
        const float MalletBounce = 0.9f;
        const float MaxPuckSpeed = 24f;
        const float MaxMalletSpeed = 34f;
        const float MalletResponse = 25f;
        const float SoundMinSpeed = 0.5f;

        public Vector2 PuckPosition { get; private set; }
        public Vector2 PuckVelocity { get; private set; }
        public readonly Vector2[] MalletPositions = new Vector2[2];

        readonly Vector2[] malletVelocities = new Vector2[2];

        /// Raised with the impact speed.
        public event Action<float> MalletHit;
        public event Action<float> WallHit;

        public AirHockeyPhysics()
        {
            MalletPositions[0] = Home(0);
            MalletPositions[1] = Home(1);
        }

        public static Vector2 Home(int player) => new Vector2(player == 0 ? -HalfLength * 0.5f : HalfLength * 0.5f, 0f);

        public void PlacePuck(Vector2 position)
        {
            PuckPosition = position;
            PuckVelocity = Vector2.zero;
        }

        /// Keeps the whole mallet on its owner's half.
        public static Vector2 ClampMallet(int player, Vector2 position)
        {
            float min = player == 0 ? -HalfLength + MalletRadius : MalletRadius;
            float max = player == 0 ? -MalletRadius : HalfLength - MalletRadius;
            float side = HalfWidth - MalletRadius;
            return new Vector2(Mathf.Clamp(position.x, min, max), Mathf.Clamp(position.y, -side, side));
        }

        /// Advances the table. Returns the player who scored, or -1. The puck stays put unless simulatePuck is set.
        public int Step(float deltaTime, Vector2 target0, Vector2 target1, bool simulatePuck)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime / StepSeconds), 1, MaxSteps);
            float dt = deltaTime / steps;
            target0 = ClampMallet(0, target0);
            target1 = ClampMallet(1, target1);

            for (int i = 0; i < steps; i++)
            {
                MoveMallet(0, target0, dt);
                MoveMallet(1, target1, dt);
                if (!simulatePuck)
                    continue;

                PuckVelocity *= Mathf.Exp(-Friction * dt);
                PuckPosition += PuckVelocity * dt;
                CollideMallet(0);
                CollideMallet(1);
                int scorer = CollideTable();
                if (scorer >= 0)
                    return scorer;
            }
            return -1;
        }

        void MoveMallet(int player, Vector2 target, float dt)
        {
            var position = MalletPositions[player];
            var wanted = Vector2.Lerp(position, target, 1f - Mathf.Exp(-MalletResponse * dt));
            var next = Vector2.MoveTowards(position, wanted, MaxMalletSpeed * dt);
            malletVelocities[player] = dt > 0f ? (next - position) / dt : Vector2.zero;
            MalletPositions[player] = next;
        }

        void CollideMallet(int player)
        {
            var offset = PuckPosition - MalletPositions[player];
            float reach = PuckRadius + MalletRadius;
            float distance = offset.magnitude;
            if (distance >= reach)
                return;

            // Where the mallet touches the puck sets the direction, and the mallet's speed sets the power.
            var normal = distance > 1e-5f ? offset / distance : (player == 0 ? Vector2.right : Vector2.left);
            PuckPosition = MalletPositions[player] + normal * reach;

            float approach = Vector2.Dot(PuckVelocity - malletVelocities[player], normal);
            if (approach >= 0f)
                return;

            SetVelocity(PuckVelocity - (1f + MalletBounce) * approach * normal);
            if (-approach > SoundMinSpeed)
                MalletHit?.Invoke(-approach);
        }

        int CollideTable()
        {
            var p = PuckPosition;
            var v = PuckVelocity;
            bool inMouth = Mathf.Abs(p.y) < GoalHalfWidth;

            if (!inMouth && Mathf.Abs(p.x) > HalfLength - PuckRadius && p.x * v.x > 0f)
            {
                Bounce(ref v.x);
                p.x = Mathf.Sign(p.x) * (HalfLength - PuckRadius);
            }

            float side = Mathf.Abs(p.x) > HalfLength ? GoalHalfWidth - PuckRadius : HalfWidth - PuckRadius;
            if (Mathf.Abs(p.y) > side && p.y * v.y > 0f)
            {
                Bounce(ref v.y);
                p.y = Mathf.Sign(p.y) * side;
            }

            PuckPosition = p;
            PuckVelocity = v;

            if (inMouth)
            {
                CollidePost(new Vector2(Mathf.Sign(p.x) * HalfLength, GoalHalfWidth));
                CollidePost(new Vector2(Mathf.Sign(p.x) * HalfLength, -GoalHalfWidth));
            }

            if (PuckPosition.x > HalfLength + PuckRadius)
                return 0;
            if (PuckPosition.x < -HalfLength - PuckRadius)
                return 1;
            return -1;
        }

        void CollidePost(Vector2 post)
        {
            var offset = PuckPosition - post;
            float distance = offset.magnitude;
            if (distance >= PuckRadius || distance < 1e-5f)
                return;

            var normal = offset / distance;
            PuckPosition = post + normal * PuckRadius;
            float approach = Vector2.Dot(PuckVelocity, normal);
            if (approach >= 0f)
                return;

            PuckVelocity -= (1f + WallBounce) * approach * normal;
            if (-approach > SoundMinSpeed)
                WallHit?.Invoke(-approach);
        }

        void Bounce(ref float velocity)
        {
            if (Mathf.Abs(velocity) > SoundMinSpeed)
                WallHit?.Invoke(Mathf.Abs(velocity));
            velocity = -velocity * WallBounce;
        }

        void SetVelocity(Vector2 velocity) => PuckVelocity = Vector2.ClampMagnitude(velocity, MaxPuckSpeed);
    }
}
