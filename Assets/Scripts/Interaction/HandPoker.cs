using IPZ.Tracking;
using UnityEngine;

namespace IPZ.Interaction
{
    [RequireComponent(typeof(HandTrackingReceiver), typeof(HandDebugView), typeof(HandGrabber))]
    public class HandPoker : MonoBehaviour
    {
        const int IndexTip = 8;
        const int Hands = HandTrackingReceiver.HandCount;

        HandTrackingReceiver receiver;
        HandDebugView view;
        HandGrabber grabber;
        readonly Vector3[] points = new Vector3[HandTrackingReceiver.LandmarkCount];
        readonly PushButton[] touching = new PushButton[Hands];

        void Awake()
        {
            receiver = GetComponent<HandTrackingReceiver>();
            view = GetComponent<HandDebugView>();
            grabber = GetComponent<HandGrabber>();
        }

        void Update()
        {
            for (int p = 0; p < Hands; p++)
            {
                if (!receiver.TryGetHand(p, points) || grabber.IsHolding(p))
                {
                    touching[p] = null;
                    continue;
                }

                var tip = view.ToWorld(points[IndexTip]);
                var button = FindTouched(tip);
                if (button != null)
                {
                    button.Hover();
                    if (touching[p] != button)
                        button.Press();
                }
                touching[p] = button;
            }
        }

        static PushButton FindTouched(Vector3 tip)
        {
            foreach (var b in PushButton.All)
                if (Vector2.Distance(b.transform.position, tip) < b.Radius)
                    return b;
            return null;
        }
    }
}
