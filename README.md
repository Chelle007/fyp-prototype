# FYP prototype: vision server + Unity

This folder contains two parts that talk over **UDP on port 5052**:

- **`python-vision-server`** — captures webcam video, runs MediaPipe (hands + face), and sends JSON tracking data to `127.0.0.1:5052`.
- **`GestureControllerPrototype`** — Unity project with a `UDPReceiver` that listens on **5052** and exposes `hand_x`, `hand_y`, `hand_up`, `head_yaw`, and `head_pitch` to the scene.

Run **both** for the full pipeline: Unity receives packets while the Python process is sending them.

---

## Prerequisites

- **Unity 6** — this project was saved with editor version **6000.3.13f1** (Unity Hub can prompt you to install that or a compatible 6000.x editor).
- **Python 3.11 or 3.12** — MediaPipe 0.10.21 does not ship wheels for Python 3.13 on PyPI.
- A **webcam** (the vision server uses OpenCV device `0`).

---

## 1. Unity (`GestureControllerPrototype`)

1. Open **Unity Hub** → **Add project from disk** → select the folder  
   `fyp-prototype/GestureControllerPrototype`  
   (the folder that contains `Assets` and `ProjectSettings`, not the repo root).
2. Open the project with Unity **6000.x** when prompted.
3. Open the main scene if needed: `Assets/Scenes/SampleScene.unity`.
4. Ensure your scene has the components that use `UDPReceiver` (as set up in your prototype).
5. Press **Play**. The receiver binds to **UDP port 5052** while the editor is in Play mode.

Keep Play mode running while you use the vision server.

---

## 2. Python vision server (`python-vision-server`)

From a terminal:

```bash
cd python-vision-server
```

### Option A — use the existing virtual environment (if `myenv` is present)

```bash
source myenv/bin/activate   # Windows: myenv\Scripts\activate
python vision_server.py
```

### Option B — new environment

```bash
python3.11 -m venv .venv
source .venv/bin/activate   # Windows: .venv\Scripts\activate
pip install -r requirements.txt
python vision_server.py
```

You should see a window titled **“Hand + Face Controller”** and console text that the server is broadcasting to port **5052**. Press **`q`** in the OpenCV window to quit.

---

## Typical workflow

1. Start **Unity** and enter **Play** mode (listener on **5052**).
2. Start **`vision_server.py`** (sender to **127.0.0.1:5052**).

If you start the Python server before Play mode, early UDP packets may be dropped until Unity’s receiver is listening; starting Unity first avoids that.

---

## Troubleshooting

| Issue | What to try |
|--------|-------------|
| Unity shows no reaction | Confirm Play mode is on and `UDPReceiver` **port** is **5052** (matches `vision_server.py`). |
| Python cannot open camera | Close other apps using the webcam; on macOS check **System Settings → Privacy & Security → Camera**. |
| `pip install` fails on Python 3.13 | Use **Python 3.11 or 3.12** (see comment in `requirements.txt`). |
| Port already in use | Another process is bound to **5052**; stop it or change **both** `UDP_PORT` in `vision_server.py` and `port` on `UDPReceiver` in Unity to the same value. |
