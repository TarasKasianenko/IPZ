using IPZ.Tracking;
using UnityEngine;

namespace IPZ.AirHockey
{
    public enum HandGesture { None, OpenPalm, Fist, ThumbUp }

    /// One player's hand: palm position plus a gesture that must stay stable before it counts.
    public class PlayerHand
    {
        const int Wrist = 0, ThumbIp = 3, ThumbTip = 4, IndexKnuckle = 5, MiddleKnuckle = 9, PinkyKnuckle = 17;
        static readonly int[] FingerTips = { 8, 12, 16, 20 };
        static readonly int[] FingerMiddles = { 6, 10, 14, 18 };
        static readonly int[] PalmPoints = { Wrist, IndexKnuckle, MiddleKnuckle, 13, PinkyKnuckle };

        const float CameraAspect = 4f / 3f;
        const float LostGraceSeconds = 0.2f;
        const float GestureFlickerSeconds = 0.15f;

        readonly Vector3[] points = new Vector3[HandTrackingReceiver.LandmarkCount];
        readonly int[] slots;
        float lastSeenAt = float.NegativeInfinity;
        HandGesture candidate;
        float candidateSince;
        float stableSince;
        float heldSeconds;
        bool consumed;

        public bool IsPresent { get; private set; }
        public Vector2 Palm { get; private set; }
        public HandGesture Gesture { get; private set; }

        /// Slots are tracker hand indexes, so a single player can use a hand from either side of the frame.
        public PlayerHand(params int[] slots) => this.slots = slots;

        public void Update(HandTrackingReceiver receiver, float now)
        {
            var seen = HandGesture.None;
            bool found = false;
            foreach (int slot in slots)
            {
                if (!receiver.TryGetHand(slot, points))
                    continue;
                found = true;
                break;
            }

            if (found)
            {
                lastSeenAt = now;
                IsPresent = true;
                Palm = ReadPalm();
                seen = Classify();
            }
            else if (now - lastSeenAt > LostGraceSeconds)
            {
                IsPresent = false;
            }

            if (seen != candidate)
            {
                candidate = seen;
                candidateSince = now;
            }
            if (candidate != Gesture && now - candidateSince >= GestureFlickerSeconds)
            {
                Gesture = candidate;
                stableSince = candidateSince;
                consumed = false;
            }

            heldSeconds = now - stableSince;
        }

        public bool IsHolding(HandGesture gesture, float seconds) =>
            IsPresent && Gesture == gesture && heldSeconds >= seconds && (!consumed || gesture == HandGesture.OpenPalm);

        /// One-shot commands stop counting until the hand changes gesture, so one fist cannot fire twice.
        /// An open palm is a state rather than a command, so it keeps counting.
        public void Consume() => consumed = true;

        Vector2 ReadPalm()
        {
            var sum = Vector2.zero;
            foreach (int i in PalmPoints)
                sum += (Vector2)points[i];
            return sum / PalmPoints.Length;
        }

        HandGesture Classify()
        {
            int extended = 0;
            for (int i = 0; i < FingerTips.Length; i++)
                if (Distance(FingerTips[i], Wrist) > Distance(FingerMiddles[i], Wrist) * 1.05f)
                    extended++;

            if (extended == FingerTips.Length)
                return HandGesture.OpenPalm;
            if (extended > 0)
                return HandGesture.None;

            float handSize = Distance(Wrist, MiddleKnuckle);
            bool thumbOut = Distance(ThumbTip, PinkyKnuckle) > Distance(ThumbIp, PinkyKnuckle) * 1.02f;
            bool thumbAbove = points[ThumbTip].y > points[IndexKnuckle].y + handSize * 0.4f;
            return thumbOut && thumbAbove ? HandGesture.ThumbUp : HandGesture.Fist;
        }

        // Landmarks are normalised per axis, so x is stretched back to the camera's proportions.
        float Distance(int a, int b) =>
            new Vector2((points[a].x - points[b].x) * CameraAspect, points[a].y - points[b].y).magnitude;
    }
}
