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

try:
    import numpy as np
    import pyrealsense2 as rs
except ImportError:
    rs = None

MODEL_URL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task"
MODEL_PATH = Path(__file__).with_name("hand_landmarker.task")
CONFIG_PATH = Path(__file__).with_name("cameras.json")
PLAYERS = 2
HANDS_PER_PLAYER = 2
SIDES = ("Left", "Right")
THUMBNAIL_WIDTH = 240
THUMBNAIL_EVERY = 2
THUMBNAIL_MAX_BYTES = 60000
CAMERA_BACKENDS = {
    "win32": ("CAP_DSHOW", "CAP_MSMF"),
    "linux": ("CAP_V4L2",),
    "darwin": ("CAP_AVFOUNDATION",),
}
PROBE_RANGE = 6
CAPTURE_SIZE = (640, 480)
CAPTURE_FPS = 30
NON_COLOUR_FORMATS = {"Z16 ", "GREY", "Y8  ", "Y8I ", "Y12I", "Y16 "}
STALE_AFTER = 1.0
OPEN_ATTEMPTS = 5
REALSENSE_BASE = 100
REALSENSE_MODES = ((640, 480, 30), (640, 480, 15), (0, 0, 0))
REALSENSE_TIMEOUT_MS = 1000


def ensure_model():
    if not MODEL_PATH.exists():
        print(f"Downloading {MODEL_URL}")
        urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
    return str(MODEL_PATH)


def realsense_serials():
    if rs is None:
        return []
    try:
        devices = rs.context().query_devices()
        return sorted(d.get_info(rs.camera_info.serial_number) for d in devices
                      if any(s.is_color_sensor() for s in d.query_sensors()))
    except RuntimeError:
        return []


class RealSenseCapture:
    def __init__(self, serial):
        self.pipeline = None
        for width, height, fps in REALSENSE_MODES:
            config = rs.config()
            config.enable_device(serial)
            config.enable_stream(rs.stream.color, width, height, rs.format.bgr8, fps)
            pipeline = rs.pipeline()
            try:
                pipeline.start(config)
            except RuntimeError:
                continue
            self.pipeline = pipeline
            break

    def isOpened(self):
        return self.pipeline is not None

    def read(self):
        if self.pipeline is None:
            return False, None
        try:
            ok, frames = self.pipeline.try_wait_for_frames(REALSENSE_TIMEOUT_MS)
        except RuntimeError:
            return False, None
        colour = frames.get_color_frame() if ok else None
        if not colour:
            return False, None
        return True, np.asanyarray(colour.get_data()).copy()

    def release(self):
        if self.pipeline is not None:
            try:
                self.pipeline.stop()
            except RuntimeError:
                pass
            self.pipeline = None


def open_realsense(index):
    serials = realsense_serials()
    position = index - REALSENSE_BASE
    if not 0 <= position < len(serials):
        return None
    cap = RealSenseCapture(serials[position])
    return cap if cap.isOpened() else None


def try_open_camera(index, strict=False):
    if index >= REALSENSE_BASE:
        return open_realsense(index)
    names = CAMERA_BACKENDS.get(sys.platform, ())
    backends = [getattr(cv2, name) for name in names]
    if not strict or not backends:
        backends.append(cv2.CAP_ANY)
    for backend in backends:
        for sized in (True, False):
            cap = cv2.VideoCapture(index, backend)
            if not cap.isOpened():
                cap.release()
                break
            if sized:
                cap.set(cv2.CAP_PROP_FRAME_WIDTH, CAPTURE_SIZE[0])
                cap.set(cv2.CAP_PROP_FRAME_HEIGHT, CAPTURE_SIZE[1])
                cap.set(cv2.CAP_PROP_FPS, CAPTURE_FPS)
            if any(cap.read()[0] for _ in range(OPEN_ATTEMPTS)):
                return cap
            cap.release()
    return None


def fourcc(cap):
    if isinstance(cap, RealSenseCapture):
        return "BGR3"
    return int(cap.get(cv2.CAP_PROP_FOURCC)).to_bytes(4, "little").decode("ascii", "replace")


class Grabber:
    def __init__(self, cap):
        self.cap = cap
        self.frame = None
        self.stamp = 0.0
        self.fresh = threading.Event()
        self.running = True
        threading.Thread(target=self._run, daemon=True).start()

    def _run(self):
        try:
            while self.running:
                ok, frame = self.cap.read()
                if not ok:
                    time.sleep(0.05)
                    continue
                self.frame, self.stamp = frame, time.monotonic()
                self.fresh.set()
        finally:
            self.cap.release()

    def get(self, wait=0.0):
        if wait > 0:
            if not self.fresh.wait(wait):
                return None
            self.fresh.clear()
        if self.frame is None or time.monotonic() - self.stamp > STALE_AFTER:
            return None
        return self.frame

    def release(self):
        self.running = False


def open_camera(index):
    cap = try_open_camera(index)
    if cap is not None and fourcc(cap) in NON_COLOUR_FORMATS:
        cap.release()
        cap = None
    if cap is None:
        for other in candidate_indexes():
            if other == index or (cap := try_open_camera(other, strict=True)) is None:
                continue
            if fourcc(cap) in NON_COLOUR_FORMATS:
                cap.release()
                continue
            print(f"Camera {index} gives no frames, using camera {other}", flush=True)
            return Grabber(cap), other
        raise SystemExit(f"Cannot open camera {index}. Is it in use, or blocked in the system privacy settings?")
    return Grabber(cap), index


def is_realsense_node(index):
    try:
        return "RealSense" in Path(f"/sys/class/video4linux/video{index}/name").read_text()
    except OSError:
        return False


def candidate_indexes():
    serials = realsense_serials()
    extra = [REALSENSE_BASE + i for i in range(len(serials))]
    if sys.platform == "linux":
        found = sorted(int(m.group(1)) for p in glob.glob("/dev/video*") if (m := re.fullmatch(r"/dev/video(\d+)", p)))
        if serials:
            found = [i for i in found if not is_realsense_node(i)]
        if found or extra:
            return found + extra
    return list(range(PROBE_RANGE)) + extra


def load_config():
    try:
        data = json.loads(CONFIG_PATH.read_text())
        return int(data.get("p1", 0)), int(data.get("p2", -1))
    except (OSError, ValueError, TypeError):
        return 0, -1


def save_config(p1, p2):
    try:
        CONFIG_PATH.write_text(json.dumps({"p1": p1, "p2": p2}))
    except OSError as e:
        print(f"Cannot save {CONFIG_PATH.name}: {e}", flush=True)


class Cameras:
    def __init__(self, p1, p2):
        self.lock = threading.Lock()
        self.p2 = -1
        self.p1_cap, self.p1 = open_camera(p1)
        self.p2_cap = None
        self.available = [self.p1]
        self.error = ""
        self.pending = None
        self.scanning = False
        if p2 >= 0 and p2 != self.p1:
            self.switch_p2(p2)

    def request(self, p1, p2):
        with self.lock:
            self.pending = (p1, p2)

    def apply_pending(self):
        with self.lock:
            pending, self.pending = self.pending, None
        if pending is None:
            return
        p1, p2 = pending
        self.error = ""
        if p1 is not None and p1 != self.p1:
            self.switch_p1(p1)
        if p2 is not None and p2 != self.p2:
            self.switch_p2(p2)
        save_config(self.p1, self.p2)

    def switch_p1(self, index):
        if index < 0:
            return
        if index == self.p2:
            self.close_p2()
        cap = try_open_camera(index)
        if cap is None:
            self.error = f"Nie można otworzyć kamery {index}"
            return
        self.p1_cap.release()
        self.p1_cap, self.p1 = Grabber(cap), index

    def switch_p2(self, index):
        self.close_p2()
        if index < 0 or index == self.p1:
            return
        cap = try_open_camera(index)
        if cap is None:
            self.error = f"Nie można otworzyć kamery {index}"
            return
        self.p2_cap, self.p2 = Grabber(cap), index

    def close_p2(self):
        if self.p2_cap is not None:
            self.p2_cap.release()
        self.p2_cap, self.p2 = None, -1

    def scan(self):
        with self.lock:
            if self.scanning:
                return
            self.scanning = True
        threading.Thread(target=self._scan, daemon=True).start()

    def _scan(self):
        found = []
        for index in candidate_indexes():
            if index in (self.p1, self.p2):
                found.append(index)
                continue
            cap = try_open_camera(index, strict=True)
            if cap is None:
                continue
            ok = fourcc(cap) not in NON_COLOUR_FORMATS and cap.read()[0]
            cap.release()
            if ok:
                found.append(index)
        with self.lock:
            self.available = sorted(set(found) | {self.p1} | ({self.p2} if self.p2 >= 0 else set()))
            self.scanning = False
        print(f"Cameras found: {self.available}", flush=True)

    def state(self):
        with self.lock:
            return {"p1": self.p1, "p2": self.p2, "available": list(self.available),
                    "scanning": self.scanning, "error": self.error}

    def release(self):
        self.p1_cap.release()
        self.close_p2()


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
        if "p1" in command or "p2" in command:
            p1 = command.get("p1")
            p2 = command.get("p2")
            cameras.request(None if p1 is None else int(p1), None if p2 is None else int(p2))


def hand_label(name, mirrored):
    if mirrored:
        return name
    return {"Left": "Right", "Right": "Left"}.get(name, name)


def place_hands(slots, player, hands):
    for lms, label in sorted(hands, key=lambda h: h[0][0].x)[:HANDS_PER_PLAYER]:
        side = SIDES.index(label) if label in SIDES else 0
        if slots[player * HANDS_PER_PLAYER + side] is not None:
            side = 1 - side
        slots[player * HANDS_PER_PLAYER + side] = lms


def labelled(result, mirrored):
    return [(lms, hand_label(cats[0].category_name, mirrored))
            for lms, cats in zip(result.hand_landmarks, result.handedness)]


def assign_shared(result, mirrored):
    halves = [[] for _ in range(PLAYERS)]
    for lms, label in labelled(result, mirrored):
        halves[min(int(lms[0].x * PLAYERS), PLAYERS - 1)].append((lms, label))
    slots = [None] * (PLAYERS * HANDS_PER_PLAYER)
    for player, hands in enumerate(halves):
        place_hands(slots, player, hands)
    return slots


def assign_separate(results, mirrored):
    slots = [None] * (PLAYERS * HANDS_PER_PLAYER)
    for player, result in enumerate(results):
        if result is not None:
            place_hands(slots, player, labelled(result, mirrored))
    return slots


def build_packet(slots, timestamp_ms, cameras):
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
    return {"t": timestamp_ms, "hands": hands, "cameras": cameras}


def draw_skeleton(frame, lms, color):
    h, w = frame.shape[:2]
    pts = [(int(lm.x * w), int(lm.y * h)) for lm in lms]
    for c in vision.HandLandmarksConnections.HAND_CONNECTIONS:
        cv2.line(frame, pts[c.start], pts[c.end], (255, 255, 255), 2)
    for p in pts:
        cv2.circle(frame, p, 4, color, -1)
    return pts


PLAYER_COLORS = [(62, 129, 0), (214, 98, 29)]


def draw_hands(frame, slots, players):
    for i, lms in enumerate(slots):
        player = i // HANDS_PER_PLAYER
        if lms is None or player not in players:
            continue
        pts = draw_skeleton(frame, lms, PLAYER_COLORS[player])
        cv2.putText(frame, f"P{player + 1} {SIDES[i % HANDS_PER_PLAYER]}", (pts[0][0], pts[0][1] + 24),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, PLAYER_COLORS[player], 2)


def draw_divider(frame):
    h, w = frame.shape[:2]
    for k in range(1, PLAYERS):
        cv2.line(frame, (w * k // PLAYERS, 0), (w * k // PLAYERS, h), (200, 200, 200), 1)


def centre_strip(frame, width_share):
    h, w = frame.shape[:2]
    strip = int(w * width_share)
    left = (w - strip) // 2
    return frame[:, left:left + strip]


def side_by_side(first, second):
    h = first.shape[0]
    if second.shape[0] != h:
        second = cv2.resize(second, (second.shape[1] * h // second.shape[0], h))
    joined = cv2.hconcat([centre_strip(first, 0.5), centre_strip(second, 0.5 * first.shape[1] / second.shape[1])])
    draw_divider(joined)
    return joined


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
    parser.add_argument("--camera", type=int, default=None, help="player 1 camera, default from cameras.json or 0")
    parser.add_argument("--camera-p2", type=int, default=None, help="player 2 camera, -1 to share player 1's camera")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5052)
    parser.add_argument("--show", action="store_true", help="show camera preview window")
    parser.add_argument("--hide", action="store_true", help="no preview window, overrides --show")
    parser.add_argument("--no-mirror", action="store_true")
    args = parser.parse_args()
    if args.hide:
        args.show = False

    saved_p1, saved_p2 = load_config()
    p1 = saved_p1 if args.camera is None else args.camera
    p2 = saved_p2 if args.camera_p2 is None else args.camera_p2
    model = ensure_model()
    mirrored = not args.no_mirror

    def make_landmarker(hands):
        return vision.HandLandmarker.create_from_options(vision.HandLandmarkerOptions(
            base_options=BaseOptions(model_asset_path=model),
            running_mode=vision.RunningMode.VIDEO,
            num_hands=hands,
        ))

    def read(grabber, wait=0.0):
        frame = grabber.get(wait)
        if frame is None:
            return None
        return cv2.flip(frame, 1) if mirrored else frame.copy()

    cameras = Cameras(p1, p2)
    cameras.scan()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (args.host, args.port)
    game_preview = (args.host, args.port + 1)
    player_previews = [(args.host, args.port + 2), (args.host, args.port + 3)]
    control = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    control.bind((args.host, args.port + 4))
    threading.Thread(target=listen_for_commands, args=(control, cameras), daemon=True).start()

    frame_index = 0
    start = time.monotonic()
    print(f"Sending to udp://{args.host}:{args.port}, commands on {args.port + 4}, Ctrl+C to stop", flush=True)

    with make_landmarker(PLAYERS * HANDS_PER_PLAYER) as first_landmarker, \
            make_landmarker(HANDS_PER_PLAYER) as second_landmarker:
        try:
            while True:
                cameras.apply_pending()
                first = read(cameras.p1_cap, wait=1.0)
                if first is None:
                    continue
                timestamp_ms = int((time.monotonic() - start) * 1000)
                first_result = first_landmarker.detect_for_video(to_image(first), timestamp_ms)

                second = read(cameras.p2_cap) if cameras.p2_cap is not None else None
                if second is None:
                    slots = assign_shared(first_result, mirrored)
                else:
                    second_result = second_landmarker.detect_for_video(to_image(second), timestamp_ms)
                    slots = assign_separate([first_result, second_result], mirrored)

                packet = build_packet(slots, timestamp_ms, cameras.state())
                sock.sendto(json.dumps(packet).encode(), target)

                if second is None:
                    draw_hands(first, slots, (0, 1))
                    draw_divider(first)
                    combined = first
                else:
                    draw_hands(first, slots, (0,))
                    draw_hands(second, slots, (1,))
                    combined = side_by_side(first, second)

                if frame_index % THUMBNAIL_EVERY == 0:
                    send_thumbnail(sock, combined, game_preview)
                    send_thumbnail(sock, first, player_previews[0])
                    if second is not None:
                        send_thumbnail(sock, second, player_previews[1])
                frame_index += 1

                if args.show:
                    cv2.imshow("IPZ tracker", combined)
                    if cv2.waitKey(1) & 0xFF in (27, ord("q")):
                        break
        except KeyboardInterrupt:
            pass
    control.close()
    cameras.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
