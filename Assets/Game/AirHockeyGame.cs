using IPZ.Tracking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IPZ.AirHockey
{
    /// Match flow and presentation: readiness, countdown, play, goals, pause and the winner screen.
    [RequireComponent(typeof(HandTrackingReceiver), typeof(GameAudio))]
    public class AirHockeyGame : MonoBehaviour
    {
        const string PlayersKey = "ipz.players";
        const string SoundKey = "ipz.sound";
        const string LobbyScene = "Lobby";
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        enum State { Waiting, Countdown, Playing, GoalPause, Paused, GameOver }

        [Header("Scene")]
        [SerializeField] Camera gameCamera;
        [SerializeField] Transform puck;
        [SerializeField] TrailRenderer puckTrail;
        [SerializeField] Transform[] mallets;
        [SerializeField] TextMesh scoreText;
        [SerializeField] TextMesh timerText;
        [SerializeField] TextMesh namesText;
        [SerializeField] TextMesh messageText;
        [SerializeField] TextMesh detailText;
        [SerializeField] TextMesh hintText;
        [SerializeField] GameObject messagePanel;
        [SerializeField] Renderer flash;
        [SerializeField] Vector2 tableCentre = new Vector2(0f, -0.9f);
        [SerializeField] float viewHalfWidth = 10.7f;

        [Header("Match")]
        [SerializeField] int winningScore = 7;
        [SerializeField] float matchSeconds = 180f;
        [SerializeField] float countdownSeconds = 3f;
        [SerializeField] float goalPauseSeconds = 1.2f;

        [Header("Gestures")]
        [SerializeField] float readySeconds = 0.6f;
        [SerializeField] float commandSeconds = 1f;

        [Header("Hand to table")]
        [SerializeField, Tooltip("Table units per camera frame width.")]
        float gainX = 25f;
        [SerializeField, Tooltip("Table units per camera frame height.")]
        float gainY = 20f;

        [Header("Computer")]
        [SerializeField] float computerSpeed = 11f;

        readonly AirHockeyPhysics physics = new AirHockeyPhysics();
        readonly int[] score = new int[2];
        readonly bool[] ready = new bool[2];
        readonly Vector2[] anchors = new Vector2[2];
        readonly Vector2[] targets = new Vector2[2];

        HandTrackingReceiver receiver;
        GameAudio gameAudio;
        PlayerHand[] hands;
        MaterialPropertyBlock flashBlock;
        Color flashColor;
        State state;
        int humanPlayers;
        float timeLeft, stateTime, flashLevel;
        int lastTick;
        int respawnSide;
        bool newMatchCountdown;

        bool HasComputer => humanPlayers == 1;

        void Awake()
        {
            receiver = GetComponent<HandTrackingReceiver>();
            gameAudio = GetComponent<GameAudio>();
            flashBlock = new MaterialPropertyBlock();
            Application.runInBackground = true;
            AudioListener.volume = PlayerPrefs.GetInt(SoundKey, 1) == 1 ? 1f : 0f;

            humanPlayers = Mathf.Clamp(PlayerPrefs.GetInt(PlayersKey, 2), 1, 2);
            hands = HasComputer
                ? new[] { new PlayerHand(0, 1) }
                : new[] { new PlayerHand(0), new PlayerHand(1) };

            physics.MalletHit += gameAudio.PlayMallet;
            physics.WallHit += gameAudio.PlayWall;

            namesText.text = HasComputer ? "Gracz 1                    Komputer" : "Gracz 1                    Gracz 2";
            float needed = viewHalfWidth / gameCamera.aspect;
            gameCamera.orthographicSize = Mathf.Max(gameCamera.orthographicSize, needed);
            EnterWaiting();
        }

        void Update()
        {
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;

            foreach (var hand in hands)
                hand.Update(receiver, now);

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SceneManager.LoadScene(LobbyScene);
                return;
            }

            stateTime += dt;
            switch (state)
            {
                case State.Waiting: UpdateWaiting(); break;
                case State.Countdown: UpdateCountdown(); break;
                case State.Playing: UpdatePlaying(dt); break;
                case State.GoalPause: UpdateGoalPause(); break;
                case State.Paused: UpdatePaused(); break;
                case State.GameOver: UpdateGameOver(); break;
            }

            if (state != State.Paused && state != State.GameOver)
                StepTable(dt);

            UpdateView(dt);
        }

        void SetState(State next)
        {
            state = next;
            stateTime = 0f;
            foreach (var hand in hands)
                hand.Consume();
        }

        void EnterWaiting()
        {
            SetState(State.Waiting);
            score[0] = score[1] = 0;
            ready[0] = ready[1] = false;
            timeLeft = matchSeconds;
            targets[0] = AirHockeyPhysics.Home(0);
            targets[1] = AirHockeyPhysics.Home(1);
            PlacePuck(Vector2.zero);
        }

        void UpdateWaiting()
        {
            bool allOpen = true;
            bool allReady = true;
            for (int i = 0; i < hands.Length; i++)
            {
                if (hands[i].IsHolding(HandGesture.OpenPalm, readySeconds))
                {
                    // The zone is wherever the player holds their open hand, kept fresh until the match starts.
                    anchors[i] = hands[i].Palm;
                    ready[i] = true;
                }
                else
                {
                    allOpen = false;
                }
                allReady &= ready[i];
            }

            bool fist = false;
            foreach (var hand in hands)
                fist |= hand.IsHolding(HandGesture.Fist, commandSeconds);

            if (allOpen || (allReady && fist))
                StartCountdown(true);
        }

        void StartCountdown(bool newMatch)
        {
            newMatchCountdown = newMatch;
            lastTick = -1;
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
            timeLeft -= dt;
            if (timeLeft <= 0f)
            {
                timeLeft = 0f;
                EndMatch();
                return;
            }

            foreach (var hand in hands)
            {
                if (hand.IsHolding(HandGesture.Fist, commandSeconds))
                {
                    SetState(State.Paused);
                    return;
                }
            }
        }

        void UpdateGoalPause()
        {
            if (stateTime < goalPauseSeconds)
                return;
            // Between the centre line and the mallet's home, so the puck does not land under the mallet.
            PlacePuck(new Vector2(respawnSide * AirHockeyPhysics.HalfLength * 0.25f, 0f));
            SetState(State.Playing);
        }

        void UpdatePaused()
        {
            foreach (var hand in hands)
            {
                if (hand.IsHolding(HandGesture.Fist, commandSeconds))
                {
                    StartCountdown(false);
                    return;
                }
            }
        }

        void UpdateGameOver()
        {
            foreach (var hand in hands)
            {
                if (hand.IsHolding(HandGesture.ThumbUp, readySeconds))
                {
                    EnterWaiting();
                    return;
                }
                if (hand.IsHolding(HandGesture.Fist, commandSeconds))
                {
                    SceneManager.LoadScene(LobbyScene);
                    return;
                }
            }
        }

        void StepTable(float dt)
        {
            if (state != State.Waiting)
            {
                for (int i = 0; i < hands.Length; i++)
                {
                    // A lost hand leaves the target untouched, so its mallet stops where it was.
                    if (hands[i].IsPresent)
                        targets[i] = AirHockeyPhysics.Home(i) + Scale(hands[i].Palm - anchors[i]);
                }
            }
            if (HasComputer && state != State.Waiting)
                targets[1] = ComputerTarget(dt);

            int scorer = physics.Step(dt, targets[0], targets[1], state == State.Playing);
            if (scorer >= 0)
                OnGoal(scorer);
        }

        Vector2 Scale(Vector2 handOffset) => new Vector2(handOffset.x * gainX, handOffset.y * gainY);

        Vector2 ComputerTarget(float dt)
        {
            var puckPosition = physics.PuckPosition;
            var current = physics.MalletPositions[1];
            bool attack = state == State.Playing && puckPosition.x > 0f && physics.PuckVelocity.x < 6f;
            var wanted = attack
                ? puckPosition + new Vector2(0.3f, 0f)
                : new Vector2(AirHockeyPhysics.HalfLength * 0.72f, puckPosition.y * 0.5f);
            return Vector2.MoveTowards(current, wanted, computerSpeed * dt);
        }

        void OnGoal(int scorer)
        {
            score[scorer]++;
            flashLevel = 1f;
            flashColor = new Color(1f, 0.85f, 0.2f);
            gameAudio.PlayGoal();
            respawnSide = scorer == 0 ? 1 : -1;
            HidePuck();

            if (score[scorer] >= winningScore)
                EndMatch();
            else
                SetState(State.GoalPause);
        }

        void EndMatch()
        {
            gameAudio.PlayWhistle();
            SetState(State.GameOver);
        }

        void PlacePuck(Vector2 position)
        {
            physics.PlacePuck(position);
            puck.gameObject.SetActive(true);
            puck.localPosition = ToWorld(position, puck.localPosition.z);
            puckTrail.Clear();
        }

        void HidePuck() => puck.gameObject.SetActive(false);

        Vector3 ToWorld(Vector2 position, float z) => new Vector3(position.x + tableCentre.x, position.y + tableCentre.y, z);

        void UpdateView(float dt)
        {
            if (puck.gameObject.activeSelf)
                puck.localPosition = ToWorld(physics.PuckPosition, puck.localPosition.z);
            for (int i = 0; i < mallets.Length; i++)
                mallets[i].localPosition = ToWorld(physics.MalletPositions[i], mallets[i].localPosition.z);

            scoreText.text = $"{score[0]} : {score[1]}";
            int seconds = Mathf.CeilToInt(timeLeft);
            timerText.text = $"{seconds / 60}:{seconds % 60:00}";

            flashLevel = Mathf.MoveTowards(flashLevel, 0f, dt * 2.2f);
            flash.gameObject.SetActive(flashLevel > 0f);
            if (flashLevel > 0f)
            {
                flashBlock.SetColor(BaseColor, new Color(flashColor.r, flashColor.g, flashColor.b, flashLevel * 0.7f));
                flash.SetPropertyBlock(flashBlock);
            }

            UpdateMessages();
        }

        void UpdateMessages()
        {
            string message = "", detail = "";
            switch (state)
            {
                case State.Waiting:
                    message = hands.Length == 1 ? "Pokaż otwartą dłoń" : "Pokażcie otwarte dłonie";
                    detail = receiver.IsReceiving ? ReadinessText() : "Brak danych z trackera. Uruchom Tracker/run.cmd";
                    break;
                case State.Countdown:
                    message = Mathf.CeilToInt(countdownSeconds - stateTime).ToString();
                    detail = newMatchCountdown ? "Do 7 goli albo 3 minuty" : "Wznawianie gry";
                    break;
                case State.GoalPause:
                    message = "GOL!";
                    break;
                case State.Paused:
                    message = "Pauza";
                    detail = "Zaciśnij pięść, aby wznowić. Esc: menu";
                    break;
                case State.GameOver:
                    message = WinnerText();
                    detail = "Kciuk w górę: rewanż. Pięść: menu";
                    break;
            }

            messageText.text = message;
            detailText.text = detail;
            messagePanel.SetActive(message.Length > 0);
            hintText.text = LostHandText();
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

        string WinnerText()
        {
            if (score[0] == score[1])
                return "Remis!";
            if (score[0] > score[1])
                return "Wygrywa Gracz 1!";
            return HasComputer ? "Wygrywa Komputer!" : "Wygrywa Gracz 2!";
        }

        string LostHandText()
        {
            if (state != State.Playing && state != State.Countdown)
                return "";
            var text = "";
            for (int i = 0; i < hands.Length; i++)
            {
                if (!hands[i].IsPresent)
                    text += $"{(text.Length > 0 ? "   " : "")}Gracz {i + 1}: pokaż dłoń";
            }
            return text;
        }
    }
}
