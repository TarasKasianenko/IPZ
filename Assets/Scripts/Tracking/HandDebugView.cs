using UnityEngine;

namespace IPZ.Tracking
{
    [RequireComponent(typeof(HandTrackingReceiver))]
    public class HandDebugView : MonoBehaviour
    {
        static readonly int[] Connections =
        {
            0, 1, 1, 2, 2, 3, 3, 4,
            0, 5, 5, 6, 6, 7, 7, 8,
            5, 9, 9, 10, 10, 11, 11, 12,
            9, 13, 13, 14, 14, 15, 15, 16,
            13, 17, 0, 17, 17, 18, 18, 19, 19, 20,
        };

        [SerializeField] Vector2 areaSize = new Vector2(16f, 9f);
        [SerializeField] float pointSize = 0.25f;
        [SerializeField] float lineWidth = 0.08f;
        [SerializeField] Color lineColor = Color.white;
        [SerializeField] Material lineMaterial;
        [SerializeField] Color[] playerColors = { new Color(1f, 0.5f, 0f), new Color(0f, 0.5f, 1f) };

        HandTrackingReceiver receiver;
        readonly Vector3[] points = new Vector3[HandTrackingReceiver.LandmarkCount];
        GameObject[] roots;
        Transform[][] markers;
        LineRenderer[][] lines;

        void Awake()
        {
            receiver = GetComponent<HandTrackingReceiver>();
            if (lineMaterial == null)
                lineMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = lineColor };

            int hands = HandTrackingReceiver.HandCount;
            roots = new GameObject[hands];
            markers = new Transform[hands][];
            lines = new LineRenderer[hands][];
            for (int h = 0; h < hands; h++)
            {
                int player = HandTrackingReceiver.PlayerOf(h);
                var side = HandTrackingReceiver.IsLeft(h) ? "Left" : "Right";
                var root = new GameObject($"Player{player + 1}{side}Hand");
                root.transform.SetParent(transform, false);
                roots[h] = root;

                markers[h] = new Transform[HandTrackingReceiver.LandmarkCount];
                for (int i = 0; i < HandTrackingReceiver.LandmarkCount; i++)
                {
                    var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(sphere.GetComponent<Collider>());
                    sphere.transform.SetParent(root.transform, false);
                    sphere.transform.localScale = Vector3.one * pointSize;
                    sphere.GetComponent<Renderer>().material.color = playerColors[player % playerColors.Length];
                    markers[h][i] = sphere.transform;
                }

                lines[h] = new LineRenderer[Connections.Length / 2];
                for (int c = 0; c < lines[h].Length; c++)
                {
                    var line = new GameObject($"Bone{c}").AddComponent<LineRenderer>();
                    line.transform.SetParent(root.transform, false);
                    line.useWorldSpace = false;
                    line.positionCount = 2;
                    line.startWidth = line.endWidth = lineWidth;
                    line.sharedMaterial = lineMaterial;
                    line.startColor = line.endColor = lineColor;
                    lines[h][c] = line;
                }

                root.SetActive(false);
            }
        }

        void Update()
        {
            for (int h = 0; h < roots.Length; h++)
            {
                bool visible = receiver.TryGetHand(h, points);
                roots[h].SetActive(visible);
                if (!visible)
                    continue;

                for (int i = 0; i < points.Length; i++)
                    markers[h][i].localPosition = ToLocal(points[i]);

                for (int c = 0; c < lines[h].Length; c++)
                {
                    lines[h][c].SetPosition(0, markers[h][Connections[c * 2]].localPosition);
                    lines[h][c].SetPosition(1, markers[h][Connections[c * 2 + 1]].localPosition);
                }
            }
        }

        public Vector3 ToWorld(Vector3 normalized) => transform.TransformPoint(ToLocal(normalized));

        Vector3 ToLocal(Vector3 p) => new Vector3((p.x - 0.5f) * areaSize.x, (p.y - 0.5f) * areaSize.y, p.z * areaSize.x);
    }
}
