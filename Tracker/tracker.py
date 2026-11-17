import argparse
import glob
import json
import re
import socket
import sys
import threading
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
CONFIG_PATH = Path(__file__).with_name("cameras.json")
PLAYERS = 2
HANDS_PER_PLAYER = 2
SIDES = ("Left", "Right")
INDEX_TIP = 8
THUMBNAIL_WIDTH = 240
THUMBNAIL_EVERY = 2
THUMBNAIL_MAX_BYTES = 60000
CAMERA_BACKENDS = {
    "win32": ("CAP_DSHOW", "CAP_MSMF"),
    "linux": ("CAP_V4L2",),
    "darwin": ("CAP_AVFOUNDATION",),
}
PROBE_RANGE = 6
DEPTH_PLANE = 0.5


def ensure_model():
    if not MODEL_PATH.exists():
        print(f"Downloading {MODEL_URL}")
        urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
    return str(MODEL_PATH)


def try_open_camera(index, strict=False):
    names = CAMERA_BACKENDS.get(sys.platform, ())
    backends = [getattr(cv2, name) for name in names]
    if not strict or not backends:
        backends.append(cv2.CAP_ANY)
    for backend in backends:
        cap = cv2.VideoCapture(index, backend)
        if cap.isOpened():
            return cap
        cap.release()
    return None


def open_camera(index):
    cap = try_open_camera(index)
    if cap is None:
        raise SystemExit(f"Cannot open camera {index}. Is it in use, or blocked in the system privacy settings?")
    return cap


def candidate_indexes():
    if sys.platform == "linux":
        found = sorted(int(m.group(1)) for p in glob.glob("/dev/video*") if (m := re.fullmatch(r"/dev/video(\d+)", p)))
        if found:
            return found
    return list(range(PROBE_RANGE))


def load_config():
    try:
        data = json.loads(CONFIG_PATH.read_text())
        return int(data.get("xy", 0)), int(data.get("z", -1))
    except (OSError, ValueError, TypeError):
        return 0, -1


def save_config(xy, z):
    try:
        CONFIG_PATH.write_text(json.dumps({"xy": xy, "z": z}))
    except OSError as e:
        print(f"Cannot save {CONFIG_PATH.name}: {e}", flush=True)


class Cameras:
    def __init__(self, xy, z):
        self.lock = threading.Lock()
        self.xy = xy
        self.z = -1
        self.xy_cap = open_camera(xy)
        self.z_cap = None
        self.available = [xy]
        self.error = ""
        self.pending = None
        self.scanning = False
        if z >= 0 and z != xy:
            self.switch_z(z)

    def request(self, xy, z):
        with self.lock:
            self.pending = (xy, z)

    def apply_pending(self):
        with self.lock:
            pending, self.pending = self.pending, None
        if pending is None:
            return
        xy, z = pending
        self.error = ""
        if xy is not None and xy != self.xy:
            self.switch_xy(xy)
        if z is not None and z != self.z:
            self.switch_z(z)
        save_config(self.xy, self.z)

    def switch_xy(self, index):
        if index < 0:
            return
        if index == self.z:
            self.close_z()
        cap = try_open_camera(index)
        if cap is None:
            self.error = f"Nie można otworzyć kamery {index}"
            return
        self.xy_cap.release()
        self.xy_cap, self.xy = cap, index

    def switch_z(self, index):
        if index < 0 or index == self.xy:
            self.close_z()
            return
        self.close_z()
        cap = try_open_camera(index)
        if cap is None:
            self.error = f"Nie można otworzyć kamery {index}"
            return
        self.z_cap, self.z = cap, index

    def close_z(self):
        if self.z_cap is not None:
            self.z_cap.release()
        self.z_cap, self.z = None, -1

    def scan(self):
        with self.lock:
            if self.scanning:
                return
            self.scanning = True
        threading.Thread(target=self._scan, daemon=True).start()

    def _scan(self):
        found = []
        for index in candidate_indexes():
            if index in (self.xy, self.z):
                found.append(index)
                continue
            cap = try_open_camera(index, strict=True)
            if cap is None:
                continue
            ok, _ = cap.read()
            cap.release()
            if ok:
                found.append(index)
        with self.lock:
            self.available = sorted(set(found) | {self.xy} | ({self.z} if self.z >= 0 else set()))
            self.scanning = False
        print(f"Cameras found: {self.available}", flush=True)

    def state(self):
        with self.lock:
            return {"xy": self.xy, "z": self.z, "available": list(self.available),
                    "scanning": self.scanning, "error": self.error}

    def release(self):
        self.xy_cap.release()
        self.close_z()


def listen_for_commands(sock, cameras):
    while True:
        try:
            data, _ = sock.recvfrom(4096)
            command = json.loads(data.decode())
        except OSError:
            return
        except ValueError:
            continue
        if command.get("scan"):
            cameras.scan()
        if "xy" in command or "z" in command:
            xy = command.get("xy")
            z = command.get("z")
            cameras.request(None if xy is None else int(xy), None if z is None else int(z))


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


def match_depths(slots, side_hands):
    depths = [-1.0] * len(slots)
    free = list(side_hands)
    order = sorted((i for i, lms in enumerate(slots) if lms is not None), key=lambda i: slots[i][0].y)
    for i in order:
        if not free:
            break
        best = min(free, key=lambda lms: abs(lms[0].y - slots[i][0].y))
        free.remove(best)
        depths[i] = round(best[INDEX_TIP].x, 4)
    return depths


def build_packet(slots, depths, timestamp_ms, cameras):
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
            "depth": depths[i] if lms is not None else -1.0,
        })
    return {"t": timestamp_ms, "hands": hands, "cameras": cameras}


def draw_skeleton(frame, lms, color):
    h, w = frame.shape[:2]
    pts = [(int(lm.x * w), int(lm.y * h)) for lm in lms]
    for c in vision.HandLandmarksConnections.HAND_CONNECTIONS:
        cv2.line(frame, pts[c.start], pts[c.end], (255, 255, 255), 2)
    for p in pts:
        cv2.circle(frame, p, 4, color, -1)
    return pts


def draw(frame, slots):
    h, w = frame.shape[:2]
    for k in range(1, PLAYERS):
        cv2.line(frame, (w * k // PLAYERS, 0), (w * k // PLAYERS, h), (200, 200, 200), 1)
    colors = [(0, 128, 255), (255, 128, 0)]
    for i, lms in enumerate(slots):
        if lms is None:
            continue
        player = i // HANDS_PER_PLAYER
        pts = draw_skeleton(frame, lms, colors[player])
        cv2.putText(frame, f"P{player + 1} {SIDES[i % HANDS_PER_PLAYER]}", (pts[0][0], pts[0][1] + 24),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, colors[player], 2)


def draw_side(frame, side_hands):
    h, w = frame.shape[:2]
    x = int(DEPTH_PLANE * w)
    cv2.line(frame, (x, 0), (x, h), (80, 220, 120), 2)
    for lms in side_hands:
        draw_skeleton(frame, lms, (80, 220, 120))
        tip = (int(lms[INDEX_TIP].x * w), int(lms[INDEX_TIP].y * h))
        cv2.circle(frame, tip, 8, (0, 255, 255), 2)


def send_thumbnail(sock, frame, target):
    h, w = frame.shape[:2]
    small = cv2.resize(frame, (THUMBNAIL_WIDTH, THUMBNAIL_WIDTH * h // w))
    ok, jpeg = cv2.imencode(".jpg", small, [cv2.IMWRITE_JPEG_QUALITY, 55])
    if ok and jpeg.size <= THUMBNAIL_MAX_BYTES:
        sock.sendto(jpeg.tobytes(), target)


def to_image(frame):
    return mp.Image(image_format=mp.ImageFormat.SRGB, data=cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))


def main():
    parser = argparse.ArgumentParser(description="MediaPipe hand tracker -> Unity over UDP")
    parser.add_argument("--camera", type=int, default=None, help="front (XY) camera, default from cameras.json or 0")
    parser.add_argument("--camera-z", type=int, default=None, help="side (Z) camera for press depth, -1 for none")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5052)
    parser.add_argument("--show", action="store_true", help="show camera preview window")
    parser.add_argument("--hide", action="store_true", help="no preview window, overrides --show")
    parser.add_argument("--no-mirror", action="store_true")
    args = parser.parse_args()
    if args.hide:
        args.show = False

    saved_xy, saved_z = load_config()
    xy = saved_xy if args.camera is None else args.camera
    z = saved_z if args.camera_z is None else args.camera_z
    model = ensure_model()

    def make_landmarker(hands):
        return vision.HandLandmarker.create_from_options(vision.HandLandmarkerOptions(
            base_options=BaseOptions(model_asset_path=model),
            running_mode=vision.RunningMode.VIDEO,
            num_hands=hands,
        ))

    cameras = Cameras(xy, z)
    cameras.scan()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (args.host, args.port)
    xy_preview = (args.host, args.port + 1)
    z_preview = (args.host, args.port + 2)
    control = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    control.bind((args.host, args.port + 3))
    threading.Thread(target=listen_for_commands, args=(control, cameras), daemon=True).start()

    frame_index = 0
    start = time.monotonic()
    print(f"Sending to udp://{args.host}:{args.port}, commands on {args.port + 3}, Ctrl+C to stop", flush=True)

    with make_landmarker(PLAYERS * HANDS_PER_PLAYER) as landmarker, make_landmarker(PLAYERS * HANDS_PER_PLAYER) as side_landmarker:
        try:
            while True:
                cameras.apply_pending()
                ok, frame = cameras.xy_cap.read()
                if not ok:
                    time.sleep(0.05)
                    continue
                if not args.no_mirror:
                    frame = cv2.flip(frame, 1)
                timestamp_ms = int((time.monotonic() - start) * 1000)
                result = landmarker.detect_for_video(to_image(frame), timestamp_ms)
                slots = assign_hands(result, not args.no_mirror)

                side_frame, side_hands = None, []
                if cameras.z_cap is not None:
                    ok, side_frame = cameras.z_cap.read()
                    if ok:
                        side_hands = side_landmarker.detect_for_video(to_image(side_frame), timestamp_ms).hand_landmarks
                    else:
                        side_frame = None
                depths = match_depths(slots, side_hands)
                packet = build_packet(slots, depths, timestamp_ms, cameras.state())
                sock.sendto(json.dumps(packet).encode(), target)

                draw(frame, slots)
                if side_frame is not None:
                    draw_side(side_frame, side_hands)
                if frame_index % THUMBNAIL_EVERY == 0:
                    send_thumbnail(sock, frame, xy_preview)
                    if side_frame is not None:
                        send_thumbnail(sock, side_frame, z_preview)
                frame_index += 1

                if args.show:
                    cv2.imshow("IPZ tracker XY", frame)
                    if side_frame is not None:
                        cv2.imshow("IPZ tracker Z", side_frame)
                    elif cv2.getWindowProperty("IPZ tracker Z", cv2.WND_PROP_VISIBLE) > 0:
                        cv2.destroyWindow("IPZ tracker Z")
                    if cv2.waitKey(1) & 0xFF in (27, ord("q")):
                        break
        except KeyboardInterrupt:
            pass
    control.close()
    cameras.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
