using UnityEngine;

namespace IPZ.Lobby
{
    public enum MenuAction { Play, Players, HowToPlay, HandCheck, Sound, Fullscreen, About, Quit, Back }

    /// A physical menu button: a block with a label that lights up on hover and sinks when pressed.
    public class MenuButton3D : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] MenuAction action;
        [SerializeField] Transform visual;
        [SerializeField] Renderer body;
        [SerializeField] TextMesh label;
        [SerializeField] Color color = new Color(0.16f, 0.19f, 0.27f);
        [SerializeField] Color hoverColor = new Color(0.27f, 0.33f, 0.47f);
        [SerializeField] Color pressedColor = new Color(0.36f, 0.45f, 0.65f);
        [SerializeField] float pressDepth = 0.18f;
        [SerializeField] float animationSpeed = 16f;

        MaterialPropertyBlock block;
        Color currentColor;
        float currentDepth;
        bool hovered, pressed;

        public MenuAction Action => action;
        public string Text { set => label.text = value; }

        void Awake()
        {
            block = new MaterialPropertyBlock();
            currentColor = color;
        }

        public void SetState(bool isHovered, bool isPressed)
        {
            hovered = isHovered;
            pressed = isPressed;
        }

        void Update()
        {
            float t = 1f - Mathf.Exp(-animationSpeed * Time.unscaledDeltaTime);
            currentColor = Color.Lerp(currentColor, pressed ? pressedColor : hovered ? hoverColor : color, t);
            currentDepth = Mathf.Lerp(currentDepth, pressed ? pressDepth : 0f, t);

            block.SetColor(BaseColor, currentColor);
            body.SetPropertyBlock(block);
            visual.localPosition = new Vector3(0f, 0f, currentDepth);
        }
    }
}
