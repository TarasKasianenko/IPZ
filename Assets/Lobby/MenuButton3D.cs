using UnityEngine;

namespace IPZ.Lobby
{
    public enum MenuAction
    {
        Play, Players, HowToPlay, HandCheck, Sound, Fullscreen, About, Quit, Back,
        Cameras, CameraP1Prev, CameraP1Next, CameraP2Prev, CameraP2Next, CameraScan, PlayMaze,
    }

    /// A physical menu button: a block with a label that lights up on hover and sinks when pressed.
    public class MenuButton3D : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] MenuAction action;
        [SerializeField] Transform visual;
        [SerializeField] Renderer body;
        [SerializeField] TextMesh label;
        [SerializeField] Color color = ZutColors.Deep;
        [SerializeField] Color hoverColor = ZutColors.Navy;
        [SerializeField] Color pressedColor = ZutColors.Green;
        [SerializeField] Renderer[] glow;
        [SerializeField] Color glowColor = ZutColors.Blue;
        [SerializeField] float glowIdle = 1.5f;
        [SerializeField] float glowHover = 2.8f;
        [SerializeField] float glowPressed = 3.5f;
        [SerializeField] float pressDepth = 0.18f;
        [SerializeField] float animationSpeed = 16f;

        MaterialPropertyBlock block, glowBlock;
        Color currentColor;
        float currentGlow;
        float currentDepth;
        bool hovered, pressed;

        public MenuAction Action => action;
        public string Text { set => label.text = value; }

        void Awake()
        {
            block = new MaterialPropertyBlock();
            glowBlock = new MaterialPropertyBlock();
            currentColor = color;
            currentGlow = glowIdle;
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

            if (glow == null || glow.Length == 0)
                return;
            currentGlow = Mathf.Lerp(currentGlow, pressed ? glowPressed : hovered ? glowHover : glowIdle, t);
            glowBlock.SetColor(BaseColor, ZutColors.Glow(pressed ? ZutColors.Green : glowColor, currentGlow));
            foreach (var r in glow)
                r.SetPropertyBlock(glowBlock);
        }
    }
}
