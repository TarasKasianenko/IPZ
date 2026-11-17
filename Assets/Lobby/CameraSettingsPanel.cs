using System.Collections.Generic;
using System.Linq;
using IPZ.Tracking;
using UnityEngine;

namespace IPZ.Lobby
{
    public class CameraSettingsPanel : MonoBehaviour
    {
        static readonly Color StatusOk = ZutColors.Light(ZutColors.Green);
        static readonly Color StatusBad = ZutColors.Red;
        static readonly Color StatusIdle = ZutColors.Muted;

        [SerializeField] GameObject panel;
        [SerializeField] MenuButton3D[] buttons;
        [SerializeField] TextMesh p1Label;
        [SerializeField] TextMesh p2Label;
        [SerializeField] TextMesh p2Placeholder;
        [SerializeField] TextMesh statusText;
        [SerializeField] GameObject p1Screen;
        [SerializeField] GameObject p2Screen;

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
                case MenuAction.CameraP1Prev:
                case MenuAction.CameraP1Next:
                    if (state != null)
                        receiver.SelectCameras(Step(P1Options(state), state.p1, action == MenuAction.CameraP1Next), state.p2);
                    return true;
                case MenuAction.CameraP2Prev:
                case MenuAction.CameraP2Next:
                    if (state != null)
                        receiver.SelectCameras(state.p1, Step(P2Options(state), state.p2, action == MenuAction.CameraP2Next));
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
            if (state == null)
            {
                p1Label.text = "—";
                p2Label.text = "—";
                SetActive(p1Screen, false);
                SetActive(p2Screen, false);
                p2Placeholder.text = "";
                SetStatus(TrackerLauncher.IsLaunching ? "Uruchamianie trackera…" : "Tracker nie jest uruchomiony",
                    TrackerLauncher.IsLaunching ? StatusIdle : StatusBad);
                return;
            }

            bool separate = state.p2 >= 0;
            p1Label.text = $"Kamera {state.p1}";
            p2Label.text = separate ? $"Kamera {state.p2}" : "Wspólna";
            SetActive(p1Screen, true);
            SetActive(p2Screen, separate);
            p2Placeholder.text = separate ? "" : "Brak drugiej kamery\nGracz 2 gra w prawej\npołowie kamery gracza 1";

            if (!string.IsNullOrEmpty(state.error))
                SetStatus(state.error, StatusBad);
            else if (state.scanning)
                SetStatus("Szukam kamer…", StatusIdle);
            else
                SetStatus($"Gracz 1: {PlayerText(0)}    Gracz 2: {PlayerText(1)}", StatusOk);
        }

        string PlayerText(int player)
        {
            int first = player * HandTrackingReceiver.HandsPerPlayer;
            return hand.IsHandPresent(first) || hand.IsHandPresent(first + 1) ? "widać dłoń" : "brak dłoni";
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

        static List<int> P1Options(CameraState state)
        {
            var options = (state.available ?? new int[0]).ToList();
            if (!options.Contains(state.p1))
                options.Add(state.p1);
            options.Sort();
            return options;
        }

        static List<int> P2Options(CameraState state)
        {
            var options = new List<int> { -1 };
            options.AddRange((state.available ?? new int[0]).Where(i => i != state.p1).OrderBy(i => i));
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
