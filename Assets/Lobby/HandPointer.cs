using IPZ.Tracking;
using UnityEngine;

namespace IPZ.Lobby
{
    /// Turns tracked hand landmarks into a screen pointer: the point between the thumb and
    /// index fingertips moves it, and pinching those fingers together acts as a press.
    [RequireComponent(typeof(HandTrackingReceiver))]
    public class HandPointer : MonoBehaviour
    {
        const int MaxTrackedHands = 4;
        const int Wrist = 0, ThumbTip = 4, IndexTip = 8, MiddleKnuckle = 9;

        [SerializeField, Tooltip("Share of the camera frame on each side that maps past the screen edge, so edges are easy to reach.")]
        float edgeMargin = 0.15f;
        [SerializeField] float smoothing = 18f;
        [SerializeField] float pinchPressRatio = 0.28f;
        [SerializeField] float pinchReleaseRatio = 0.42f;
        [SerializeField, Tooltip("Keeps the pointer alive through brief detection dropouts.")]
        float lostHandGraceSeconds = 0.2f;
        [SerializeField] float cameraAspect = 4f / 3f;

        readonly Vector3[] points = new Vector3[HandTrackingReceiver.LandmarkCount];
        HandTrackingReceiver receiver;
        float lastSeenAt = float.NegativeInfinity;
        bool hasPosition;

        /// Pointer position, 0..1 on both axes, with the origin at the bottom left.
        public Vector2 Position { get; private set; }
        public bool IsVisible { get; private set; }
        public bool IsPinching { get; private set; }
        public bool IsReceiving => receiver.IsReceiving;

        public bool IsHandPresent(int hand) => receiver.TryGetHand(hand, points);

        void Awake()
        {
            receiver = GetComponent<HandTrackingReceiver>();
            // The tracker preview window usually has focus, and Unity would otherwise stop updating.
            Application.runInBackground = true;
        }

        void Update()
        {
            if (TryReadFirstHand())
            {
                lastSeenAt = Time.unscaledTime;
                UpdatePosition();
                UpdatePinch();
                IsVisible = true;
            }
            else if (Time.unscaledTime - lastSeenAt > lostHandGraceSeconds)
            {
                IsVisible = false;
                IsPinching = false;
                hasPosition = false;
            }
        }

        bool TryReadFirstHand()
        {
            for (int hand = 0; hand < MaxTrackedHands; hand++)
                if (receiver.TryGetHand(hand, points))
                    return true;
            return false;
        }

        void UpdatePosition()
        {
            var between = (points[ThumbTip] + points[IndexTip]) * 0.5f;
            var target = new Vector2(Remap(between.x), Remap(between.y));
            Position = hasPosition
                ? Vector2.Lerp(Position, target, 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime))
                : target;
            hasPosition = true;
        }

        void UpdatePinch()
        {
            float handSize = Distance(points[Wrist], points[MiddleKnuckle]);
            if (handSize < 1e-4f)
                return;

            float ratio = Distance(points[ThumbTip], points[IndexTip]) / handSize;
            IsPinching = ratio < (IsPinching ? pinchReleaseRatio : pinchPressRatio);
        }

        float Remap(float value) => Mathf.Clamp01((value - edgeMargin) / (1f - 2f * edgeMargin));

        // Landmarks are normalised per axis, so x is stretched back to the camera's proportions.
        float Distance(Vector3 a, Vector3 b) => new Vector2((a.x - b.x) * cameraAspect, a.y - b.y).magnitude;
    }
}
