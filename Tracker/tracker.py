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


def ensure_model():
    if not MODEL_PATH.exists():
        print(f"Downloading {MODEL_URL}")
        urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
    return str(MODEL_PATH)


def assign_players(hands):
    hands = sorted(hands, key=lambda h: h[0].x)
    slots = [None] * PLAYERS
    if len(hands) == 1:
        slots[0 if hands[0][0].x < 0.5 else 1] = hands[0]
    else:
        for i, hand in enumerate(hands[:PLAYERS]):
            slots[i] = hand
    return slots


def build_packet(slots, timestamp_ms):
    players = []
    for i, hand in enumerate(slots):
        points = []
        if hand is not None:
            for lm in hand:
                points.extend((round(lm.x, 4), round(lm.y, 4), round(lm.z, 4)))
        players.append({"id": i, "present": hand is not None, "points": points})
    return {"t": timestamp_ms, "players": players}


def draw(frame, slots):
    h, w = frame.shape[:2]
    cv2.line(frame, (w // 2, 0), (w // 2, h), (200, 200, 200), 1)
    colors = [(255, 128, 0), (0, 128, 255)]
    for i, hand in enumerate(slots):
        if hand is None:
            continue
        for lm in hand:
            cv2.circle(frame, (int(lm.x * w), int(lm.y * h)), 4, colors[i], -1)
        cv2.putText(frame, f"P{i + 1}", (int(hand[0].x * w), int(hand[0].y * h) + 24),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.8, colors[i], 2)


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
        num_hands=PLAYERS,
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
                slots = assign_players(result.hand_landmarks)
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
