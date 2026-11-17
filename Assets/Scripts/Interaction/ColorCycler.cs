using UnityEngine;

namespace IPZ.Interaction
{
    [RequireComponent(typeof(PushButton))]
    public class ColorCycler : MonoBehaviour
    {
        [SerializeField] Grabbable defaultTarget;
        [SerializeField] Color[] palette =
        {
            new Color(0.95f, 0.3f, 0.25f),
            new Color(0.25f, 0.6f, 0.95f),
            new Color(0.3f, 0.85f, 0.4f),
            new Color(0.95f, 0.8f, 0.2f),
            new Color(0.7f, 0.35f, 0.9f),
        };

        PushButton button;
        int next;

        void Start()
        {
            button = GetComponent<PushButton>();
            button.SetCapColor(palette[next]);
        }

        public void Next()
        {
            var target = Grabbable.Selected != null ? Grabbable.Selected : defaultTarget;
            if (target == null || palette.Length == 0)
                return;
            target.SetBaseColor(palette[next]);
            next = (next + 1) % palette.Length;
            button.SetCapColor(palette[next]);
        }
    }
}
