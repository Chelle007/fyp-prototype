import cv2
import mediapipe as mp
import socket
import json
import math

# --- 1. SETUP UDP SOCKET ---
UDP_IP = "127.0.0.1" 
UDP_PORT = 5052      
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

# --- 2. SETUP MEDIAPIPE ---
mp_hands = mp.solutions.hands
hands = mp_hands.Hands(
    static_image_mode=False,
    max_num_hands=2,
    min_detection_confidence=0.7,
    min_tracking_confidence=0.7
)

mp_face = mp.solutions.face_mesh
face_mesh = mp_face.FaceMesh(
    static_image_mode=False,
    max_num_faces=1,         
    min_detection_confidence=0.7,
    min_tracking_confidence=0.7
)

mp_draw = mp.solutions.drawing_utils

# Helper function to calculate distance between two joints
def get_dist(p1, p2):
    return math.hypot(p1.x - p2.x, p1.y - p2.y)

# --- 3. MAIN LOOP ---
cap = cv2.VideoCapture(0)

print(f"Combined Vision Server Running. Broadcasting to port {UDP_PORT}...")

while cap.isOpened():
    success, img = cap.read()
    if not success:
        break

    img = cv2.flip(img, 1)
    img_rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
    
    hand_results = hands.process(img_rgb)
    face_results = face_mesh.process(img_rgb)

    data_payload = {
        "hand_x": 0.5, 
        "hand_y": 0.5, 
        "hand_up": 0, 
        "head_yaw": 0.0, 
        "head_pitch": 0.0,
        "hands": []
    }

    # --- PROCESS HANDS ---
    if hand_results.multi_hand_landmarks:
        for i, hand_landmarks in enumerate(hand_results.multi_hand_landmarks):
            mp_draw.draw_landmarks(img, hand_landmarks, mp_hands.HAND_CONNECTIONS)
            
            # FIXED: Track the base of the middle finger (the palm) instead of the fingertip
            palm_center = hand_landmarks.landmark[9]
            wrist = hand_landmarks.landmark[0]

            # Backwards-compatible single-point hand position (use first detected hand)
            if i == 0:
                data_payload["hand_x"] = round(palm_center.x, 3)
                data_payload["hand_y"] = round(palm_center.y, 3)

            # FIXED: Robust "Open Hand" check using distance from the wrist
            # We check if the fingertip is further away from the wrist than the knuckle is.
            # We multiply the knuckle distance by 1.2 as a buffer so slightly bent fingers don't trigger it.
            index_open = get_dist(hand_landmarks.landmark[8], wrist) > get_dist(hand_landmarks.landmark[5], wrist) * 1.2
            middle_open = get_dist(hand_landmarks.landmark[12], wrist) > get_dist(hand_landmarks.landmark[9], wrist) * 1.2
            ring_open = get_dist(hand_landmarks.landmark[16], wrist) > get_dist(hand_landmarks.landmark[13], wrist) * 1.2
            pinky_open = get_dist(hand_landmarks.landmark[20], wrist) > get_dist(hand_landmarks.landmark[17], wrist) * 1.2

            # If all 4 fingers are fully extended away from the wrist, trigger the red color!
            if index_open and middle_open and ring_open and pinky_open:
                data_payload["hand_up"] = 1

            # Handedness (aligned by index with multi_hand_landmarks)
            handedness = "Unknown"
            if hand_results.multi_handedness and i < len(hand_results.multi_handedness):
                try:
                    handedness = hand_results.multi_handedness[i].classification[0].label
                except Exception:
                    handedness = "Unknown"

            # Full 21 landmarks (normalized image coords + relative z)
            landmarks = []
            for lm in hand_landmarks.landmark:
                landmarks.append({
                    "x": round(lm.x, 3),
                    "y": round(lm.y, 3),
                    "z": round(lm.z, 3)
                })

            data_payload["hands"].append({
                "handedness": handedness,
                "landmarks": landmarks
            })

    # --- PROCESS FACE ---
    if face_results.multi_face_landmarks:
        for face_landmarks in face_results.multi_face_landmarks:
            nose = face_landmarks.landmark[1]
            left_side = face_landmarks.landmark[234]
            right_side = face_landmarks.landmark[454]
            top = face_landmarks.landmark[10]
            bottom = face_landmarks.landmark[152]

            face_width = right_side.x - left_side.x
            nose_offset_x = nose.x - left_side.x
            if face_width > 0:
                data_payload["head_yaw"] = round(((nose_offset_x / face_width) - 0.5) * 2, 3)

            face_height = bottom.y - top.y
            nose_offset_y = nose.y - top.y
            if face_height > 0:
                data_payload["head_pitch"] = round(((nose_offset_y / face_height) - 0.5) * 2, 3)

    # --- 4. SEND DATA ---
    message = json.dumps(data_payload).encode('utf-8')
    sock.sendto(message, (UDP_IP, UDP_PORT))

    cv2.imshow("Hand + Face Controller", img)
    if cv2.waitKey(1) & 0xFF == ord('q'):
        break

cap.release()
cv2.destroyAllWindows()