using System.Collections.Generic;
using System.Linq;
using IPZ.Tracking;
using UnityEngine;

namespace IPZ.Lobby
{
    public class CameraSettingsPanel : MonoBehaviour
    {
        static readonly Color StatusOk = new Color(0.30f, 0.82f, 0.49f);
        static readonly Color StatusBad = new Color(0.95f, 0.45f, 0.40f);
        static readonly Color StatusIdle = new Color(0.62f, 0.66f, 0.74f);

        [SerializeField] GameObject panel;
        [SerializeField] MenuButton3D[] buttons;
        [SerializeField] TextMesh xyLabel;
        [SerializeField] TextMesh zLabel;
        [SerializeField] TextMesh zPlaceholder;
        [SerializeField] TextMesh statusText;
        [SerializeField] GameObject xyScreen;
        [SerializeField] GameObject zScreen;
        [SerializeField] MenuButton3D flipButton;

        HandTrackingReceiver receiver;
        HandPointer hand;

        public bool IsOpen => panel.activeSelf;
        public IEnumerable<MenuButton3D> Buttons => buttons;

        void Awake()
        {
            receiver = GetComponent<HandTrackingReceiver>();
            hand = GetComponent<HandPointer>();
            panel.SetActive(false);
        }

        public void Open()
        {
            panel.SetActive(true);
            receiver.ScanCameras();
            Refresh();
        }

        public void Close() => panel.SetActive(false);

        public bool Handle(MenuAction action)
        {
            var state = receiver.Cameras;
            switch (action)
            {
                case MenuAction.CameraXYPrev:
                case MenuAction.CameraXYNext:
                    if (state != null)
                        receiver.SelectCameras(Step(XyOptions(state), state.xy, action == MenuAction.CameraXYNext), state.z);
                    return true;
                case MenuAction.CameraZPrev:
                case MenuAction.CameraZNext:
                    if (state != null)
                        receiver.SelectCameras(state.xy, Step(ZOptions(state), state.z, action == MenuAction.CameraZNext));
                    return true;
                case MenuAction.CameraZFlip:
                    hand.DepthFlipped = !hand.DepthFlipped;
                    PlayerPrefs.SetInt(HandPointer.DepthFlipKey, hand.DepthFlipped ? 1 : 0);
                    return true;
                case MenuAction.CameraScan:
                    receiver.ScanCameras();
                    return true;
            }
            return false;
        }

        void Update()
        {
            if (IsOpen)
                Refresh();
        }

        void Refresh()
        {
            var state = receiver.Cameras;
            flipButton.Text = hand.DepthFlipped ? "Kierunek: ←" : "Kierunek: →";

            if (state == null)
            {
                xyLabel.text = "—";
                zLabel.text = "—";
                SetActive(xyScreen, false);
                SetActive(zScreen, false);
                zPlaceholder.text = "Brak trackera";
                SetStatus(TrackerLauncher.IsLaunching ? "Uruchamianie trackera…" : "Tracker nie jest uruchomiony",
                    TrackerLauncher.IsLaunching ? StatusIdle : StatusBad);
                return;
            }

            xyLabel.text = $"Kamera {state.xy}";
            zLabel.text = state.z >= 0 ? $"Kamera {state.z}" : "Wyłączona";
            SetActive(xyScreen, true);
            SetActive(zScreen, state.z >= 0);
            zPlaceholder.text = state.z >= 0 ? "" : "Kamera Z wyłączona\nnaciskanie: złącz palce";

            if (!string.IsNullOrEmpty(state.error))
                SetStatus(state.error, StatusBad);
            else if (state.scanning)
                SetStatus("Szukam kamer…", StatusIdle);
            else if (state.z < 0)
                SetStatus($"Dostępne kamery: {Available(state)}", StatusIdle);
            else if (!hand.IsVisible)
                SetStatus("Pokaż dłoń obu kamerom", StatusIdle);
            else if (!hand.UsesDepth)
                SetStatus("Kamera Z nie widzi dłoni", StatusBad);
            else
                SetStatus(hand.IsPressing ? "Naciśnięto" : "Wsuń palec za zieloną linię", hand.IsPressing ? StatusOk : StatusIdle);
        }

        void SetStatus(string text, Color color)
        {
            statusText.text = text;
            statusText.color = color;
        }

        static void SetActive(GameObject go, bool active)
        {
            if (go.activeSelf != active)
                go.SetActive(active);
        }

        static string Available(CameraState state) =>
            state.available == null || state.available.Length == 0 ? "—" : string.Join(", ", state.available);

        static List<int> XyOptions(CameraState state)
        {
            var options = (state.available ?? new int[0]).ToList();
            if (!options.Contains(state.xy))
                options.Add(state.xy);
            options.Sort();
            return options;
        }

        static List<int> ZOptions(CameraState state)
        {
            var options = new List<int> { -1 };
            options.AddRange((state.available ?? new int[0]).Where(i => i != state.xy).OrderBy(i => i));
            return options;
        }

        static int Step(List<int> options, int current, bool forward)
        {
            int index = options.IndexOf(current);
            if (index < 0)
                return options[0];
            return options[(index + (forward ? 1 : -1) + options.Count) % options.Count];
        }
    }
}
