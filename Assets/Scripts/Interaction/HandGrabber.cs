using IPZ.Tracking;
using UnityEngine;

namespace IPZ.Interaction
{
    [RequireComponent(typeof(HandTrackingReceiver), typeof(HandDebugView))]
    public class HandGrabber : MonoBehaviour
    {
        const int Wrist = 0;
        const int ThumbTip = 4;
        const int IndexTip = 8;
        const int MiddleBase = 9;
        const int Hands = HandTrackingReceiver.HandCount;

        [SerializeField] float pinchStart = 0.35f;
        [SerializeField] float pinchEnd = 0.5f;
        [SerializeField] float grabRadius = 1.5f;
        [SerializeField] float followSharpness = 20f;
        [SerializeField] bool rotateWithHand = true;

        HandTrackingReceiver receiver;
        HandDebugView view;
        readonly Vector3[] points = new Vector3[HandTrackingReceiver.LandmarkCount];
        readonly bool[] pinching = new bool[Hands];
        readonly Grabbable[] held = new Grabbable[Hands];
        readonly Vector3[] offsets = new Vector3[Hands];
        readonly float[] startAngles = new float[Hands];
        readonly Quaternion[] startRotations = new Quaternion[Hands];

        public bool IsHolding(int hand) => held[hand] != null;

        void Awake()
        {
            receiver = GetComponent<HandTrackingReceiver>();
            view = GetComponent<HandDebugView>();
        }

        void Update()
        {
            for (int p = 0; p < Hands; p++)
            {
                if (!receiver.TryGetHand(p, points))
                {
                    pinching[p] = false;
                    Drop(p);
                    continue;
                }

                float size = Vector2.Distance(points[Wrist], points[MiddleBase]);
                float pinch = size > 1e-4f ? Vector2.Distance(points[ThumbTip], points[IndexTip]) / size : 1f;
                pinching[p] = pinching[p] ? pinch < pinchEnd : pinch < pinchStart;
                Vector3 cursor = view.ToWorld((points[ThumbTip] + points[IndexTip]) * 0.5f);
                float angle = HandAngle();

                if (held[p] != null)
                {
                    if (!pinching[p])
                    {
                        Drop(p);
                        continue;
                    }
                    Follow(p, cursor, angle);
                    continue;
                }

                var nearest = FindNearest(cursor);
                if (nearest == null)
                    continue;
                nearest.Hover();
                if (pinching[p])
                {
                    held[p] = nearest;
                    offsets[p] = nearest.transform.position - cursor;
                    startAngles[p] = angle;
                    startRotations[p] = nearest.transform.rotation;
                    nearest.Grab(p);
                }
            }
        }

        void Follow(int p, Vector3 cursor, float angle)
        {
            var t = held[p].transform;
            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);

            var target = cursor + offsets[p];
            target.z = t.position.z;
            t.position = Vector3.Lerp(t.position, target, k);

            if (rotateWithHand)
            {
                float delta = Mathf.DeltaAngle(startAngles[p], angle);
                var targetRotation = Quaternion.AngleAxis(delta, Vector3.forward) * startRotations[p];
                t.rotation = Quaternion.Slerp(t.rotation, targetRotation, k);
            }
        }

        float HandAngle()
        {
            Vector3 dir = view.ToWorld(points[MiddleBase]) - view.ToWorld(points[Wrist]);
            return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        }

        void OnDisable()
        {
            for (int p = 0; p < Hands; p++)
                Drop(p);
        }

        void Drop(int hand)
        {
            if (held[hand] == null)
                return;
            held[hand].Release();
            held[hand] = null;
        }

        Grabbable FindNearest(Vector3 cursor)
        {
            Grabbable best = null;
            float bestDist = grabRadius;
            foreach (var g in Grabbable.All)
            {
                if (g.IsHeld)
                    continue;
                float d = Vector2.Distance(g.transform.position, cursor);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = g;
                }
            }
            return best;
        }
    }
}
