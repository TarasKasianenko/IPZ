# Tracker

Камера → MediaPipe Hand Landmarker (до 2 рук) → UDP `127.0.0.1:5052` → Unity (`HandTrackingReceiver`).

Гравець 1 — рука в лівій половині кадру, гравець 2 — у правій. Кадр віддзеркалений, як у дзеркалі.

## Встановлення

```bash
python3 -m venv .venv
.venv/bin/pip install -r requirements.txt
```

`tracker.py` блокує імпорт `sounddevice`: mediapipe тягне його для аудіо, а PortAudio через JACK/PipeWire вмикає real-time режим, і ядро вбиває процес (`killed`). Аудіо трекеру не потрібне.

Модель `hand_landmarker.task` завантажується сама при першому запуску.

## Запуск

```bash
.venv/bin/python tracker.py --show
```

Параметри: `--camera N`, `--host`, `--port`, `--show` (вікно з камерою, вихід — `q` або `Esc`), `--no-mirror`.

## Формат пакета

```json
{"t": 1234, "players": [{"id": 0, "present": true, "points": [x0, y0, z0, x1, y1, z1, ...]}, {"id": 1, "present": false, "points": []}]}
```

21 точка на руку, `x`/`y` нормалізовані 0..1 (y зверху вниз), `z` — глибина відносно зап'ястя.

## Unity

Додай на порожній GameObject компоненти `HandTrackingReceiver` і `HandDebugView`, постав камеру так, щоб бачити область 16×9 навколо об'єкта, і натисни Play.
