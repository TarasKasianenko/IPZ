using UnityEngine;

namespace IPZ.Tracking
{
    [RequireComponent(typeof(HandTrackingReceiver))]
    public class HandDebugView : MonoBehaviour
    {
        [SerializeField] Vector2 areaSize = new Vector2(16f, 9f);
        [SerializeField] float pointSize = 0.25f;
        [SerializeField] Color[] playerColors = { new Color(1f, 0.5f, 0f), new Color(0f, 0.5f, 1f) };

        HandTrackingReceiver receiver;
        readonly Vector3[] points = new Vector3[HandTrackingReceiver.LandmarkCount];
        Transform[][] markers;

        void Awake()
        {
            receiver = GetComponent<HandTrackingReceiver>();
            markers = new Transform[HandTrackingReceiver.PlayerCount][];
            for (int p = 0; p < markers.Length; p++)
            {
                var root = new GameObject($"Player{p + 1}Hand").transform;
                root.SetParent(transform, false);
                markers[p] = new Transform[HandTrackingReceiver.LandmarkCount];
                for (int i = 0; i < HandTrackingReceiver.LandmarkCount; i++)
                {
                    var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(sphere.GetComponent<Collider>());
                    sphere.transform.SetParent(root, false);
                    sphere.transform.localScale = Vector3.one * pointSize;
                    sphere.GetComponent<Renderer>().material.color = playerColors[p % playerColors.Length];
                    markers[p][i] = sphere.transform;
                }
                root.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            for (int p = 0; p < markers.Length; p++)
            {
                bool visible = receiver.TryGetHand(p, points);
                var root = markers[p][0].parent.gameObject;
                root.SetActive(visible);
                if (!visible)
                    continue;

                for (int i = 0; i < points.Length; i++)
                    markers[p][i].localPosition = new Vector3(
                        (points[i].x - 0.5f) * areaSize.x,
                        (points[i].y - 0.5f) * areaSize.y,
                        points[i].z * areaSize.x);
            }
        }
    }
}
