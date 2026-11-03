using UnityEngine;
using UnityEngine.SceneManagement;

namespace IPZ.Lobby
{
    /// 3D lobby: the buttons sit on a wheel that turns when scrolled. The hand pointer and the mouse
    /// feed the same press, drag and click logic, and buttons are found by raycasting into the scene.
    [RequireComponent(typeof(HandPointer))]
    public class LobbyMenu : MonoBehaviour
    {
        const string PlayersKey = "ipz.players";
        const string SoundKey = "ipz.sound";
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        static readonly Color StatusOk = new Color(0.30f, 0.82f, 0.49f);
        static readonly Color StatusBad = new Color(0.95f, 0.45f, 0.40f);
        static readonly Color CursorIdle = Color.white;
        static readonly Color CursorPinched = new Color(0.30f, 0.82f, 0.49f);

        [Header("Scene")]
        [SerializeField] Camera menuCamera;
        [SerializeField] Transform wheel;
        [SerializeField] MenuButton3D[] items;
        [SerializeField] MenuButton3D backButton;
        [SerializeField] GameObject infoPanel;
        [SerializeField] TextMesh infoTitle;
        [SerializeField] TextMesh infoBody;
        [SerializeField] TextMesh statusText;
        [SerializeField] TextMesh toastText;
        [SerializeField] Renderer cursor;
        [SerializeField] float cursorPlaneZ = -2f;

        [Header("Wheel")]
        [SerializeField] float wheelRadius = 5f;
        [SerializeField] float degreesPerItem = 15f;
        [SerializeField] float visibleItems = 2.7f;
        [SerializeField, Tooltip("Items kept in view on either side of the centre, so the wheel never shows a gap at its ends.")]
        int scrollMargin = 2;

        [Header("Scrolling")]
        [SerializeField, Tooltip("Items scrolled by dragging across the full screen height.")]
        float itemsPerScreen = 6f;
        [SerializeField] float dragThreshold = 0.03f;
        [SerializeField] float inertiaDamping = 5f;
        [SerializeField] float snapSpeed = 10f;

        [Header("Game")]
        [SerializeField] string gameSceneName = "Game";
        [SerializeField] float mouseOverrideSeconds = 2f;

        HandPointer hand;
        MaterialPropertyBlock cursorBlock;
        float scroll, scrollVelocity, pressScroll, pressY, toastUntil;
        float mouseActiveUntil = float.NegativeInfinity;
        Vector2 lastMouse;
        bool wasPressed, isDragging, usingHand, handCheckOpen;
        MenuButton3D pressedButton, hoveredButton;
        int players;
        bool soundOn;

        bool IsInfoOpen => infoPanel.activeSelf;
        float MinScroll => Mathf.Min(scrollMargin, (items.Length - 1) * 0.5f);
        float MaxScroll => items.Length - 1 - MinScroll;

        void Awake()
        {
            hand = GetComponent<HandPointer>();
            cursorBlock = new MaterialPropertyBlock();
            players = Mathf.Clamp(PlayerPrefs.GetInt(PlayersKey, 2), 1, 2);
            soundOn = PlayerPrefs.GetInt(SoundKey, 1) == 1;
            AudioListener.volume = soundOn ? 1f : 0f;
            lastMouse = Input.mousePosition;
            scroll = MinScroll;

            infoPanel.SetActive(false);
            toastText.text = "";
            foreach (var item in items)
            {
                if (item.Action == MenuAction.Players) item.Text = PlayersText();
                if (item.Action == MenuAction.Sound) item.Text = SoundText();
            }
        }

        void Update()
        {
            ReadPointer(out var viewport, out bool pressed, out bool cursorVisible);
            Physics.SyncTransforms();
            HandlePointer(viewport, pressed);
            UpdateWheel();
            UpdateTexts();
            UpdateCursor(viewport, pressed, cursorVisible);
        }

        /// Hand input wins unless the mouse has just been used, so a visible hand never locks out the mouse.
        void ReadPointer(out Vector2 viewport, out bool pressed, out bool cursorVisible)
        {
            Vector2 mouse = Input.mousePosition;
            float wheelDelta = Input.mouseScrollDelta.y;
            if (mouse != lastMouse || Input.GetMouseButton(0) || wheelDelta != 0f)
                mouseActiveUntil = Time.unscaledTime + mouseOverrideSeconds;
            lastMouse = mouse;

            bool useHand = hand.IsVisible && Time.unscaledTime >= mouseActiveUntil;
            if (useHand != usingHand)
            {
                // Switching source mid-gesture must not turn into a click.
                pressedButton = null;
                isDragging = false;
                wasPressed = false;
                usingHand = useHand;
            }

            viewport = useHand ? hand.Position : new Vector2(mouse.x / Screen.width, mouse.y / Screen.height);
            pressed = useHand ? hand.IsPinching : Input.GetMouseButton(0);
            cursorVisible = useHand;

            if (!useHand && !IsInfoOpen && wheelDelta != 0f)
            {
                scroll -= Mathf.Sign(wheelDelta);
                scrollVelocity = 0f;
            }
        }

        void HandlePointer(Vector2 viewport, bool pressed)
        {
            var hovered = usingHand || !hand.IsVisible || Time.unscaledTime < mouseActiveUntil
                ? FindButton(viewport)
                : null;

            if (pressed && !wasPressed)
            {
                pressedButton = hovered;
                pressY = viewport.y;
                pressScroll = scroll;
                scrollVelocity = 0f;
                isDragging = false;
            }

            if (pressed && !IsInfoOpen)
                UpdateDrag(viewport.y);

            MenuButton3D clicked = null;
            if (!pressed && wasPressed)
            {
                if (pressedButton != null && pressedButton == hovered && !isDragging)
                    clicked = pressedButton;
                pressedButton = null;
                isDragging = false;
            }
            wasPressed = pressed;

            hoveredButton = isDragging ? null : hovered;
            foreach (var item in items)
                item.SetState(item == hoveredButton, item == hoveredButton && item == pressedButton);
            backButton.SetState(backButton == hoveredButton, backButton == hoveredButton && backButton == pressedButton);

            if (clicked != null)
                Run(clicked);
        }

        MenuButton3D FindButton(Vector2 viewport)
        {
            var ray = menuCamera.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f));
            return Physics.Raycast(ray, out var hit, 100f) ? hit.collider.GetComponentInParent<MenuButton3D>() : null;
        }

        void UpdateDrag(float viewportY)
        {
            float delta = viewportY - pressY;
            if (!isDragging && Mathf.Abs(delta) > dragThreshold)
            {
                isDragging = true;
                pressedButton = null;
            }
            if (!isDragging)
                return;

            float target = pressScroll + delta * itemsPerScreen;
            if (Time.unscaledDeltaTime > 0f)
                scrollVelocity = (target - scroll) / Time.unscaledDeltaTime;
            scroll = target;
        }

        void UpdateWheel()
        {
            float dt = Time.unscaledDeltaTime;
            if (!isDragging)
            {
                scroll += scrollVelocity * dt;
                scrollVelocity *= Mathf.Exp(-inertiaDamping * dt);
                if (Mathf.Abs(scrollVelocity) < 1f)
                {
                    scrollVelocity = 0f;
                    scroll = Mathf.Lerp(scroll, Mathf.Round(scroll), 1f - Mathf.Exp(-snapSpeed * dt));
                }
            }
            scroll = Mathf.Clamp(scroll, MinScroll, MaxScroll);

            wheel.gameObject.SetActive(!IsInfoOpen);
            for (int i = 0; i < items.Length; i++)
            {
                float offset = scroll - i;
                float angle = offset * degreesPerItem;
                float radians = angle * Mathf.Deg2Rad;
                var item = items[i].transform;

                bool visible = Mathf.Abs(offset) < visibleItems;
                if (item.gameObject.activeSelf != visible)
                    item.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                item.localPosition = new Vector3(0f, wheelRadius * Mathf.Sin(radians), wheelRadius * (1f - Mathf.Cos(radians)));
                item.localRotation = Quaternion.Euler(angle, 0f, 0f);
                // Shrink towards the edge so items do not pop in and out of view.
                float edge = Mathf.Clamp01((visibleItems - Mathf.Abs(offset)) / 0.5f);
                item.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, edge);
            }
        }

        void UpdateTexts()
        {
            bool receiving = hand.IsReceiving;
            statusText.text = receiving ? "Tracker połączony" : "Tracker nie jest uruchomiony, działa tylko mysz";
            statusText.color = receiving ? StatusOk : StatusBad;

            if (handCheckOpen)
                infoBody.text = HandCheckText();

            if (toastText.text.Length > 0 && Time.unscaledTime >= toastUntil)
                toastText.text = "";
        }

        void UpdateCursor(Vector2 viewport, bool pressed, bool visible)
        {
            if (cursor.gameObject.activeSelf != visible)
                cursor.gameObject.SetActive(visible);
            if (!visible)
                return;

            var ray = menuCamera.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f));
            float distance = (cursorPlaneZ - ray.origin.z) / ray.direction.z;
            cursor.transform.position = ray.GetPoint(distance);
            cursorBlock.SetColor(BaseColor, pressed ? CursorPinched : CursorIdle);
            cursor.SetPropertyBlock(cursorBlock);
        }

        void Run(MenuButton3D button)
        {
            switch (button.Action)
            {
                case MenuAction.Play:
                    Play();
                    break;
                case MenuAction.Players:
                    players = players == 1 ? 2 : 1;
                    PlayerPrefs.SetInt(PlayersKey, players);
                    button.Text = PlayersText();
                    break;
                case MenuAction.HowToPlay:
                    OpenInfo("Jak grać",
                        "1. Uruchom tracker: tracker.py --show\n" +
                        "2. Pokaż dłoń do kamery, pojawi się kursor.\n" +
                        "3. Złącz kciuk i palec wskazujący: naciśnięcie.\n" +
                        "4. Trzymaj złączone palce i przesuwaj: przewijanie.\n\n" +
                        "Gracz 1 po lewej stronie kadru, gracz 2 po prawej.");
                    break;
                case MenuAction.HandCheck:
                    OpenInfo("Test dłoni", HandCheckText());
                    handCheckOpen = true;
                    break;
                case MenuAction.Sound:
                    soundOn = !soundOn;
                    AudioListener.volume = soundOn ? 1f : 0f;
                    PlayerPrefs.SetInt(SoundKey, soundOn ? 1 : 0);
                    button.Text = SoundText();
                    break;
                case MenuAction.Fullscreen:
                    Screen.fullScreen = !Screen.fullScreen;
                    break;
                case MenuAction.About:
                    OpenInfo("O projekcie",
                        "Inżynierski projekt zespołowy 1\n\n" +
                        "Sterowanie gestami dłoni:\n" +
                        "kamera, MediaPipe Hand Landmarker i Unity.");
                    break;
                case MenuAction.Quit:
                    Quit();
                    break;
                case MenuAction.Back:
                    infoPanel.SetActive(false);
                    handCheckOpen = false;
                    break;
            }
        }

        void OpenInfo(string title, string body)
        {
            infoTitle.text = title;
            infoBody.text = body;
            infoPanel.SetActive(true);
            scrollVelocity = 0f;
        }

        void Play()
        {
            PlayerPrefs.Save();
            if (Application.CanStreamedLevelBeLoaded(gameSceneName))
                SceneManager.LoadScene(gameSceneName);
            else
            {
                toastText.text = $"Scena „{gameSceneName}” nie została jeszcze dodana do Build Settings";
                toastUntil = Time.unscaledTime + 3f;
            }
        }

        void Quit()
        {
            PlayerPrefs.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        string PlayersText() => $"Gracze: {players}";
        string SoundText() => soundOn ? "Dźwięk: włączony" : "Dźwięk: wyłączony";

        string HandCheckText()
        {
            if (!hand.IsReceiving)
                return "Brak danych z trackera.\nUruchom tracker.py --show";
            return "Tracker połączony.\n\n" +
                   $"Dłoń gracza 1: {HandText(0)}\n" +
                   $"Dłoń gracza 2: {HandText(1)}\n" +
                   $"Złączone palce: {(hand.IsPinching ? "tak" : "nie")}";
        }

        string HandText(int player) => hand.IsHandPresent(player) ? "widoczna" : "niewidoczna";
    }
}
