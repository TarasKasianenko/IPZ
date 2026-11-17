using System.Collections.Generic;
using UnityEngine;

namespace IPZ.Interaction
{
    public class Grabbable : MonoBehaviour
    {
        public static readonly List<Grabbable> All = new List<Grabbable>();
        public static Grabbable Selected { get; private set; }

        [SerializeField, Range(0f, 1f)] float hoverHighlight = 0.3f;
        [SerializeField, Range(0f, 1f)] float heldHighlight = 0.55f;

        Renderer rend;
        Color baseColor;
        Rigidbody body;
        bool wasKinematic;
        int hoverFrame = -1;

        public int HeldBy { get; private set; } = -1;
        public bool IsHeld => HeldBy >= 0;
        public Color BaseColor => baseColor;

        void Awake()
        {
            rend = GetComponent<Renderer>();
            if (rend != null)
                baseColor = rend.material.color;
            body = GetComponent<Rigidbody>();
        }

        void OnEnable() => All.Add(this);

        void OnDisable()
        {
            All.Remove(this);
            if (Selected == this)
                Selected = null;
        }

        public void Hover() => hoverFrame = Time.frameCount;

        public void SetBaseColor(Color color) => baseColor = color;

        public void Grab(int hand)
        {
            HeldBy = hand;
            Selected = this;
            if (body != null)
            {
                wasKinematic = body.isKinematic;
                body.isKinematic = true;
            }
        }

        public void Release()
        {
            HeldBy = -1;
            if (body != null)
                body.isKinematic = wasKinematic;
        }

        void LateUpdate()
        {
            if (rend == null)
                return;
            float highlight = IsHeld ? heldHighlight : hoverFrame >= Time.frameCount - 1 ? hoverHighlight : 0f;
            rend.material.color = Color.Lerp(baseColor, Color.white, highlight);
        }
    }
}
