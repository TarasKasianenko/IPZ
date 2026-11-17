using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace IPZ.Interaction
{
    public class PushButton : MonoBehaviour
    {
        public static readonly List<PushButton> All = new List<PushButton>();

        [SerializeField] Transform cap;
        [SerializeField] float radius = 1f;
        [SerializeField] float pressDepth = 0.15f;
        [SerializeField] float pressScale = 0.9f;
        [SerializeField] float downTime = 0.06f;
        [SerializeField] float holdTime = 0.08f;
        [SerializeField] float upTime = 0.3f;
        [SerializeField] float cooldown = 0.4f;
        [SerializeField, Range(0f, 1f)] float flash = 0.6f;
        [SerializeField, Range(0f, 1f)] float hoverHighlight = 0.2f;

        public UnityEvent onPressed = new UnityEvent();

        Renderer capRenderer;
        Color capColor;
        Vector3 capRest;
        Vector3 capScale;
        float pressedAt = float.NegativeInfinity;
        int hoverFrame = -1;

        public float Radius => radius;

        void Awake()
        {
            if (cap == null)
                return;
            capRest = cap.localPosition;
            capScale = cap.localScale;
            capRenderer = cap.GetComponent<Renderer>();
            if (capRenderer != null)
                capColor = capRenderer.material.color;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void Hover() => hoverFrame = Time.frameCount;

        public void SetCapColor(Color color) => capColor = color;

        public bool Press()
        {
            if (Time.time - pressedAt < cooldown)
                return false;
            pressedAt = Time.time;
            onPressed.Invoke();
            return true;
        }

        void Update()
        {
            if (cap == null)
                return;

            float since = Time.time - pressedAt;
            float depth = since < downTime
                ? since / downTime
                : 1f - Mathf.Clamp01((since - downTime - holdTime) / upTime);
            depth = Mathf.SmoothStep(0f, 1f, depth);

            cap.localPosition = capRest - Vector3.up * (pressDepth * depth);
            var squash = Mathf.Lerp(1f, pressScale, depth);
            cap.localScale = new Vector3(capScale.x * squash, capScale.y, capScale.z * squash);

            if (capRenderer != null)
            {
                float highlight = Mathf.Max(flash * depth, hoverFrame >= Time.frameCount - 1 ? hoverHighlight : 0f);
                capRenderer.material.color = Color.Lerp(capColor, Color.white, highlight);
            }
        }
    }
}
