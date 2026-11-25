using System.Collections.Generic;
using IPZ.AirHockey;
using IPZ.Tracking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IPZ.Maze
{
    [RequireComponent(typeof(HandTrackingReceiver), typeof(GameAudio))]
    public class MazeGame : MonoBehaviour
    {
        const string PlayersKey = "ipz.players";
        const string SoundKey = "ipz.sound";
        const string LobbyScene = "Lobby";
        const float Cell = 1f;
        const float BallRadius = 0.3f;
        const float WallHeight = 0.6f;
        const float HoleRadius = 0.42f;
        const float FinishRadius = 0.4f;

        enum State { Waiting, Countdown, Playing, Falling, LevelDone, Paused, Finished }

        [Header("Scene")]
        [SerializeField] Transform board;
        [SerializeField] Transform ball;
        [SerializeField] TextMesh levelText;
        [SerializeField] TextMesh timerText;
        [SerializeField] TextMesh fallsText;
        [SerializeField] TextMesh messageText;
        [SerializeField] TextMesh detailText;
        [SerializeField] TextMesh hintText;
        [SerializeField] TextMesh yLabel;
        [SerializeField] TextMesh xLabel;
        [SerializeField] GameObject messagePanel;
        [SerializeField] Transform yMarker;
        [SerializeField] Transform xMarker;
        [SerializeField] float markerRange = 2f;

        [Header("Materials")]
        [SerializeField] Material floorMaterial;
        [SerializeField] Material wallMaterial;
        [SerializeField] Material holeMaterial;
        [SerializeField] Material holeRimMaterial;
        [SerializeField] Material finishMaterial;
        [SerializeField] Material startMaterial;

        [Header("Tilt")]
        [SerializeField] float maxTilt = 12f;
        [SerializeField] float handRange = 0.14f;
        [SerializeField] float deadZone = 0.12f;
        [SerializeField] float tiltSharpness = 8f;

        [Header("Ball")]
        [SerializeField] float gravity = 24f;
        [SerializeField] float damping = 0.7f;
        [SerializeField] float maxSpeed = 7f;
        [SerializeField] float wallBounce = 0.35f;
        [SerializeField] float holePull = 10f;

        [Header("Flow")]
        [SerializeField] float readySeconds = 0.6f;
        [SerializeField] float commandSeconds = 1f;
        [SerializeField] float menuSeconds = 3f;
        [SerializeField] float countdownSeconds = 3f;
        [SerializeField] float fallSeconds = 0.8f;
        [SerializeField] float levelDoneSeconds = 2f;

        readonly List<Vector2> holes = new List<Vector2>();
        readonly Vector2[] anchors = new Vector2[2];
        readonly bool[] ready = new bool[2];

        HandTrackingReceiver receiver;
        GameAudio gameAudio;
        PlayerHand[] hands;
        Transform levelRoot;
        bool[,] walls;
        int cols, rows;
        Vector2 start, finish;
        Vector2 position, velocity, fallFrom, fallHole;
        Vector2 tilt;
        Vector2 input;
        State state;
        int level, falls, lastTick;
        float stateTime, elapsed;
        bool singlePlayer;

        public int LevelCount => MazeLevels.All.Length;

        void Awake()
        {
            receiver = GetComponent<HandTrackingReceiver>();
            gameAudio = GetComponent<GameAudio>();
            Application.runInBackground = true;
            AudioListener.volume = PlayerPrefs.GetInt(SoundKey, 1) == 1 ? 1f : 0f;

            singlePlayer = Mathf.Clamp(PlayerPrefs.GetInt(PlayersKey, 2), 1, 2) == 1;
            hands = singlePlayer
                ? new[] { new PlayerHand(1, 0, 3, 2) }
                : new[] { new PlayerHand(1, 0), new PlayerHand(3, 2) };
            yLabel.text = "Gracz 1\ngóra – dół";
            xLabel.text = singlePlayer ? "Gracz 1: lewo – prawo" : "Gracz 2: lewo – prawo";

            BuildLevel(0);
            EnterWaiting();
        }

        void Update()
        {
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            foreach (var hand in hands)
                hand.Update(receiver, now);

            if (Input.GetKeyDown(KeyCode.Escape) || WantsMenu())
            {
                SceneManager.LoadScene(LobbyScene);
                return;
            }

            stateTime += dt;
            input = ReadInput();
            switch (state)
            {
                case State.Waiting: UpdateWaiting(); break;
                case State.Countdown: UpdateCountdown(); break;
                case State.Playing: UpdatePlaying(dt); break;
                case State.Falling: UpdateFalling(); break;
                case State.LevelDone: UpdateLevelDone(); break;
                case State.Paused: UpdatePaused(); break;
                case State.Finished: UpdateFinished(); break;
            }

            bool live = state == State.Playing || state == State.Falling;
            var target = live ? input * maxTilt : Vector2.zero;
            tilt = Vector2.Lerp(tilt, target, 1f - Mathf.Exp(-tiltSharpness * dt));
            board.localRotation = Quaternion.Euler(tilt.y, -tilt.x, 0f);

            UpdateView();
        }

        public void BuildLevel(int index)
        {
            level = index;
            if (levelRoot != null)
                Remove(levelRoot.gameObject);
            levelRoot = new GameObject("Level").transform;
            levelRoot.SetParent(board, false);

            var map = MazeLevels.All[index];
            rows = map.Length;
            cols = map[0].Length;
            walls = new bool[cols, rows];
            holes.Clear();

            var floor = Primitive(PrimitiveType.Quad, "Floor", Vector3.zero, floorMaterial);
            floor.localScale = new Vector3(cols * Cell, rows * Cell, 1f);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var centre = CellCentre(c, r);
                    switch (map[r][c])
                    {
                        case MazeLevels.Wall:
                            walls[c, r] = true;
                            var wall = Primitive(PrimitiveType.Cube, "Wall", new Vector3(centre.x, centre.y, -WallHeight * 0.5f), wallMaterial);
                            wall.localScale = new Vector3(Cell, Cell, WallHeight);
                            break;
                        case MazeLevels.Hole:
                            holes.Add(centre);
                            Disc("Hole Rim", centre, HoleRadius + 0.08f, -0.004f, holeRimMaterial);
                            Disc("Hole", centre, HoleRadius, -0.008f, holeMaterial);
                            break;
                        case MazeLevels.Start:
                            start = centre;
                            Disc("Start", centre, 0.38f, -0.004f, startMaterial);
                            break;
                        case MazeLevels.Finish:
                            finish = centre;
                            Disc("Finish", centre, 0.45f, -0.004f, finishMaterial);
                            break;
                    }
                }
            }

            ResetBall();
        }

        void ResetBall()
        {
            position = start;
            velocity = Vector2.zero;
            ball.localScale = Vector3.one * BallRadius * 2f;
            PlaceBall(-BallRadius);
        }

        void EnterWaiting()
        {
            SetState(State.Waiting);
            ready[0] = ready[1] = false;
            ResetBall();
        }

        bool WantsMenu()
        {
            if (state != State.Waiting && state != State.Paused)
                return false;
            foreach (var hand in hands)
                if (hand.HasHeld(HandGesture.Fist, menuSeconds))
                    return true;
            return false;
        }

        void SetState(State next)
        {
            state = next;
            stateTime = 0f;
            foreach (var hand in hands)
                hand.Consume();
        }

        void UpdateWaiting()
        {
            bool allReady = true;
            for (int i = 0; i < hands.Length; i++)
            {
                if (hands[i].IsHolding(HandGesture.OpenPalm, readySeconds))
                {
                    anchors[i] = hands[i].Palm;
                    ready[i] = true;
                }
                allReady &= ready[i];
            }

            if (allReady || Input.GetKeyDown(KeyCode.Space))
            {
                elapsed = 0f;
                falls = 0;
                StartCountdown();
            }
        }

        void StartCountdown()
        {
            lastTick = -1;
            ResetBall();
            SetState(State.Countdown);
        }

        void UpdateCountdown()
        {
            int tick = Mathf.CeilToInt(countdownSeconds - stateTime);
            if (tick != lastTick && tick > 0)
            {
                lastTick = tick;
                gameAudio.PlayTick();
            }
            if (stateTime >= countdownSeconds)
                SetState(State.Playing);
        }

        void UpdatePlaying(float dt)
        {
            elapsed += dt;
            foreach (var hand in hands)
            {
                if (hand.IsHolding(HandGesture.Fist, commandSeconds))
                {
                    SetState(State.Paused);
                    return;
                }
            }
            if (Input.GetKeyDown(KeyCode.P))
            {
                SetState(State.Paused);
                return;
            }

            StepBall(dt);
            PlaceBall(-BallRadius);

            foreach (var hole in holes)
            {
                if ((position - hole).magnitude < HoleRadius - BallRadius * 0.35f)
                {
                    fallFrom = position;
                    fallHole = hole;
                    falls++;
                    gameAudio.PlayWhistle();
                    SetState(State.Falling);
                    return;
                }
            }

            if ((position - finish).magnitude < FinishRadius)
            {
                gameAudio.PlayGoal();
                SetState(level + 1 < LevelCount ? State.LevelDone : State.Finished);
            }
        }

        void UpdateFalling()
        {
            float t = Mathf.Clamp01(stateTime / fallSeconds);
            position = Vector2.Lerp(fallFrom, fallHole, Mathf.SmoothStep(0f, 1f, t * 2f));
            ball.localScale = Vector3.one * BallRadius * 2f * (1f - t * 0.7f);
            PlaceBall(Mathf.Lerp(-BallRadius, BallRadius * 2f, t));
            if (t >= 1f)
            {
                ResetBall();
                SetState(State.Playing);
            }
        }

        void UpdateLevelDone()
        {
            if (stateTime < levelDoneSeconds)
                return;
            BuildLevel(level + 1);
            StartCountdown();
        }

        void UpdatePaused()
        {
            foreach (var hand in hands)
            {
                if (hand.IsHolding(HandGesture.ThumbUp, readySeconds))
                {
                    SetState(State.Playing);
                    return;
                }
            }
            if (Input.GetKeyDown(KeyCode.P))
                SetState(State.Playing);
        }

        void UpdateFinished()
        {
            bool again = Input.GetKeyDown(KeyCode.Space);
            foreach (var hand in hands)
            {
                again |= hand.IsHolding(HandGesture.ThumbUp, readySeconds);
                if (hand.IsHolding(HandGesture.Fist, commandSeconds))
                {
                    SceneManager.LoadScene(LobbyScene);
                    return;
                }
            }
            if (!again)
                return;
            BuildLevel(0);
            elapsed = 0f;
            falls = 0;
            StartCountdown();
        }

        Vector2 ReadInput()
        {
            var result = Vector2.zero;
            if (singlePlayer)
            {
                result = Axis(0, true, true);
            }
            else
            {
                result.y = Axis(0, false, true).y;
                result.x = Axis(1, true, false).x;
            }

            float keyX = (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D) ? 1f : 0f)
                         - (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A) ? 1f : 0f);
            float keyY = (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W) ? 1f : 0f)
                         - (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S) ? 1f : 0f);
            if (keyX != 0f)
                result.x = keyX;
            if (keyY != 0f)
                result.y = keyY;
            return result;
        }

        Vector2 Axis(int player, bool useX, bool useY)
        {
            var hand = hands[player];
            if (!hand.IsPresent || !ready[player])
                return Vector2.zero;
            var offset = (hand.Palm - anchors[player]) / handRange;
            return new Vector2(useX ? Shape(offset.x) : 0f, useY ? Shape(offset.y) : 0f);
        }

        float Shape(float value)
        {
            float magnitude = Mathf.Clamp01((Mathf.Abs(value) - deadZone) / (1f - deadZone));
            return Mathf.Sign(value) * magnitude;
        }

        void StepBall(float dt)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.004f), 1, 24);
            float h = dt / steps;
            var slope = new Vector2(Mathf.Sin(tilt.x * Mathf.Deg2Rad), Mathf.Sin(tilt.y * Mathf.Deg2Rad)) * gravity;

            for (int i = 0; i < steps; i++)
            {
                var accel = slope;
                foreach (var hole in holes)
                {
                    var toHole = hole - position;
                    if (toHole.magnitude < HoleRadius)
                        accel += toHole * holePull;
                }

                velocity += accel * h;
                velocity *= Mathf.Exp(-damping * h);
                velocity = Vector2.ClampMagnitude(velocity, maxSpeed);
                position += velocity * h;
                CollideWalls();
            }
        }

        void CollideWalls()
        {
            var cell = ToCell(position);
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int c = cell.x + dx, r = cell.y + dy;
                    if (!IsWall(c, r))
                        continue;

                    var centre = CellCentre(c, r);
                    var half = new Vector2(Cell * 0.5f, Cell * 0.5f);
                    var closest = new Vector2(
                        Mathf.Clamp(position.x, centre.x - half.x, centre.x + half.x),
                        Mathf.Clamp(position.y, centre.y - half.y, centre.y + half.y));
                    var offset = position - closest;
                    float distance = offset.magnitude;
                    if (distance >= BallRadius)
                        continue;

                    var normal = distance > 1e-5f ? offset / distance : (position - centre).normalized;
                    position = closest + normal * BallRadius;
                    float approach = Vector2.Dot(velocity, normal);
                    if (approach < 0f)
                    {
                        velocity -= (1f + wallBounce) * approach * normal;
                        if (-approach > 1f)
                            gameAudio.PlayWall(-approach * 2f);
                    }
                }
            }
        }

        bool IsWall(int c, int r) => c < 0 || r < 0 || c >= cols || r >= rows || walls[c, r];

        Vector2Int ToCell(Vector2 p) => new Vector2Int(
            Mathf.RoundToInt(p.x / Cell + (cols - 1) * 0.5f),
            Mathf.RoundToInt((rows - 1) * 0.5f - p.y / Cell));

        Vector2 CellCentre(int c, int r) => new Vector2((c - (cols - 1) * 0.5f) * Cell, ((rows - 1) * 0.5f - r) * Cell);

        void PlaceBall(float z) => ball.localPosition = new Vector3(position.x, position.y, z);

        void UpdateView()
        {
            levelText.text = $"Poziom {level + 1}/{LevelCount}";
            int seconds = Mathf.FloorToInt(elapsed);
            timerText.text = $"{seconds / 60}:{seconds % 60:00}";
            fallsText.text = $"Upadki: {falls}";

            yMarker.localPosition = new Vector3(0f, input.y * markerRange, yMarker.localPosition.z);
            xMarker.localPosition = new Vector3(input.x * markerRange, 0f, xMarker.localPosition.z);

            string message = "", detail = "";
            switch (state)
            {
                case State.Waiting:
                    message = hands.Length == 1 ? "Pokaż otwartą dłoń" : "Pokażcie otwarte dłonie";
                    detail = receiver.IsReceiving ? ReadinessText() + "\nTrzymaj pięść: menu"
                        : TrackerLauncher.IsLaunching ? "Uruchamianie trackera…"
                        : "Brak danych z trackera. Spacja: gra klawiaturą (strzałki)";
                    break;
                case State.Countdown:
                    message = Mathf.CeilToInt(countdownSeconds - stateTime).ToString();
                    detail = $"Poziom {level + 1}: doprowadźcie kulkę do zielonej mety";
                    break;
                case State.Falling:
                    message = "Kulka wpadła!";
                    break;
                case State.LevelDone:
                    message = "Poziom ukończony!";
                    detail = $"Czas: {timerText.text}   Upadki: {falls}";
                    break;
                case State.Paused:
                    message = "Pauza";
                    detail = "Kciuk w górę: wznów. Trzymaj pięść: menu";
                    break;
                case State.Finished:
                    message = "Labirynt ukończony!";
                    detail = $"Czas: {timerText.text}   Upadki: {falls}\nKciuk w górę: jeszcze raz. Pięść: menu";
                    break;
            }

            messageText.text = message;
            detailText.text = detail;
            messagePanel.SetActive(message.Length > 0);
            hintText.text = state == State.Playing ? LostHandText() : "";
        }

        string ReadinessText()
        {
            var text = "";
            for (int i = 0; i < hands.Length; i++)
            {
                if (i > 0)
                    text += "\n";
                text += $"Gracz {i + 1}: {(ready[i] ? "gotowy" : hands[i].IsPresent ? "otwórz dłoń" : "nie widać dłoni")}";
            }
            return text;
        }

        string LostHandText()
        {
            var text = "";
            for (int i = 0; i < hands.Length; i++)
            {
                if (!hands[i].IsPresent)
                    text += $"{(text.Length > 0 ? "   " : "")}Gracz {i + 1}: pokaż dłoń";
            }
            return text;
        }

        Transform Primitive(PrimitiveType type, string name, Vector3 position, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Remove(go.GetComponent<Collider>());
            go.transform.SetParent(levelRoot, false);
            go.transform.localPosition = position;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        void Disc(string name, Vector2 centre, float radius, float z, Material material)
        {
            var disc = Primitive(PrimitiveType.Cylinder, name, new Vector3(centre.x, centre.y, z), material);
            disc.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.localScale = new Vector3(radius * 2f, 0.002f, radius * 2f);
        }

        static void Remove(Object target)
        {
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}
