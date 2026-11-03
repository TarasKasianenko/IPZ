import argparse
import json
import socket
import sys
import time
import urllib.request
from pathlib import Path

sys.modules["sounddevice"] = None

import cv2
import mediapipe as mp
from mediapipe.tasks.python import BaseOptions
from mediapipe.tasks.python import vision

MODEL_URL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task"
MODEL_PATH = Path(__file__).with_name("hand_landmarker.task")
PLAYERS = 2
HANDS_PER_PLAYER = 2
SIDES = ("Left", "Right")


def ensure_model():
    if not MODEL_PATH.exists():
        print(f"Downloading {MODEL_URL}")
        urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
    return str(MODEL_PATH)


def hand_label(name, mirrored):
    if not mirrored:
        return name
    return {"Left": "Right", "Right": "Left"}.get(name, name)


def assign_hands(result, mirrored):
    halves = [[] for _ in range(PLAYERS)]
    for lms, cats in zip(result.hand_landmarks, result.handedness):
        player = min(int(lms[0].x * PLAYERS), PLAYERS - 1)
        halves[player].append((lms, hand_label(cats[0].category_name, mirrored)))

    slots = [None] * (PLAYERS * HANDS_PER_PLAYER)
    for player, hands in enumerate(halves):
        for lms, label in sorted(hands, key=lambda h: h[0][0].x)[:HANDS_PER_PLAYER]:
            side = SIDES.index(label) if label in SIDES else 0
            if slots[player * HANDS_PER_PLAYER + side] is not None:
                side = 1 - side
            slots[player * HANDS_PER_PLAYER + side] = lms
    return slots


def build_packet(slots, timestamp_ms):
    hands = []
    for i, lms in enumerate(slots):
        points = []
        if lms is not None:
            for lm in lms:
                points.extend((round(lm.x, 4), round(lm.y, 4), round(lm.z, 4)))
        hands.append({
            "player": i // HANDS_PER_PLAYER,
            "side": SIDES[i % HANDS_PER_PLAYER],
            "present": lms is not None,
            "points": points,
        })
    return {"t": timestamp_ms, "hands": hands}


def draw(frame, slots):
    h, w = frame.shape[:2]
    for k in range(1, PLAYERS):
        cv2.line(frame, (w * k // PLAYERS, 0), (w * k // PLAYERS, h), (200, 200, 200), 1)
    colors = [(0, 128, 255), (255, 128, 0)]
    for i, lms in enumerate(slots):
        if lms is None:
            continue
        player = i // HANDS_PER_PLAYER
        pts = [(int(lm.x * w), int(lm.y * h)) for lm in lms]
        for c in vision.HandLandmarksConnections.HAND_CONNECTIONS:
            cv2.line(frame, pts[c.start], pts[c.end], (255, 255, 255), 2)
        for p in pts:
            cv2.circle(frame, p, 4, colors[player], -1)
        cv2.putText(frame, f"P{player + 1} {SIDES[i % HANDS_PER_PLAYER]}", (pts[0][0], pts[0][1] + 24),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, colors[player], 2)


def main():
    parser = argparse.ArgumentParser(description="MediaPipe hand tracker -> Unity over UDP")
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5052)
    parser.add_argument("--show", action="store_true", help="show camera preview window")
    parser.add_argument("--no-mirror", action="store_true")
    args = parser.parse_args()

    options = vision.HandLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=ensure_model()),
        running_mode=vision.RunningMode.VIDEO,
        num_hands=PLAYERS * HANDS_PER_PLAYER,
    )
    cap = cv2.VideoCapture(args.camera, cv2.CAP_V4L2)
    if not cap.isOpened():
        raise SystemExit(f"Cannot open camera {args.camera}")

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (args.host, args.port)
    start = time.monotonic()
    print(f"Sending to udp://{args.host}:{args.port}, Ctrl+C to stop", flush=True)

    with vision.HandLandmarker.create_from_options(options) as landmarker:
        try:
            while True:
                ok, frame = cap.read()
                if not ok:
                    break
                if not args.no_mirror:
                    frame = cv2.flip(frame, 1)
                timestamp_ms = int((time.monotonic() - start) * 1000)
                image = mp.Image(image_format=mp.ImageFormat.SRGB,
                                 data=cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
                result = landmarker.detect_for_video(image, timestamp_ms)
                slots = assign_hands(result, not args.no_mirror)
                sock.sendto(json.dumps(build_packet(slots, timestamp_ms)).encode(), target)

                if args.show:
                    draw(frame, slots)
                    cv2.imshow("IPZ tracker", frame)
                    if cv2.waitKey(1) & 0xFF in (27, ord("q")):
                        break
        except KeyboardInterrupt:
            pass
    cap.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
