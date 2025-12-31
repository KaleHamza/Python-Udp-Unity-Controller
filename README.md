# Unity UDP Controller

A lightweight Python script to control Unity projects over a network using UDP sockets.

##  Features
* **Dual Port:** Uses port `9050` for movement/clicks and `9060` for camera data.
* **WASD + Mouse:** Full keyboard and mouse tracking with normalized coordinates (0.0 - 1.0).
* **Fast I/O:** Utilizes Windows API for high-speed click detection.
* **Scene Control:** Quick commands to load scenes or select characters.

##  Setup
1. **Install dependencies:** `pip install pyautogui keyboard`
2. **Set IP:** Update `UNITY_IP` in `ClientControl.py`.
3. **Run:** `python ClientControl.py`

##  Controls
* **WASD:** Move
* **Space:** Jump
* **Mouse:** Look & Click
* **ESC:** Exit mode
