using System.Collections.Generic;
using UnityEngine;

public class HandSkeletonVisualizer : MonoBehaviour
{
    [Header("Input")]
    public UDPReceiver receiver;

    [Header("Placement (first-person XR-style)")]
    public Transform cameraTransform;
    public Vector3 leftHandOffset = new Vector3(-0.18f, -0.10f, 0.55f);
    public Vector3 rightHandOffset = new Vector3(0.18f, -0.10f, 0.55f);
    public float anchorMoveScale = 0.35f; // how much the whole hand can drift around the offset

    [Header("Landmark mapping")]
    public bool preferWorldLandmarks = true;
    public float xyScale = 0.7f;
    public float zScale = 0.7f;
    public float smoothing = 18f;

    [Tooltip("Scale applied when using world_landmarks (meters-ish).")]
    public float worldScale = 2.5f;
    [Tooltip("If the input image is mirrored (cv2.flip(...,1)), mirror X for world landmarks.")]
    public bool mirrorWorldX = false;

    [Header("Rendering")]
    public float jointRadius = 0.02f;
    public float lineWidth = 0.008f;
    public Color leftColor = new Color(0.35f, 0.85f, 1f, 1f);
    public Color rightColor = new Color(1f, 0.55f, 0.35f, 1f);

    private static readonly (int a, int b)[] Connections =
    {
        // Thumb
        (0, 1), (1, 2), (2, 3), (3, 4),
        // Index
        (0, 5), (5, 6), (6, 7), (7, 8),
        // Middle
        (5, 9), (9, 10), (10, 11), (11, 12),
        // Ring
        (9, 13), (13, 14), (14, 15), (15, 16),
        // Pinky
        (13, 17), (17, 18), (18, 19), (19, 20),
        // Palm edge
        (0, 17)
    };

    private class HandViz
    {
        public GameObject root;
        public GameObject[] joints;
        public LineRenderer[] lines;
        public Vector3[] smoothedLocal;
        public Color color;
    }

    private readonly Dictionary<string, HandViz> _byKey = new Dictionary<string, HandViz>();
    private Material _lineMaterial;

    private void Awake()
    {
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        _lineMaterial = new Material(Shader.Find("Sprites/Default"));
    }

    private void Update()
    {
        if (receiver == null || cameraTransform == null) return;

        TrackingData data = receiver.currentData;
        HandPacket[] hands = data.hands ?? System.Array.Empty<HandPacket>();

        // Mark all as not-updated; we’ll hide any that aren’t present this frame.
        HashSet<string> updatedKeys = new HashSet<string>();

        for (int i = 0; i < hands.Length; i++)
        {
            HandPacket hp = hands[i];
            Landmark[] lmSet = SelectLandmarks(hp);
            if (hp == null || lmSet == null || lmSet.Length < 21) continue;

            string key = NormalizeKey(hp.handedness, i);
            updatedKeys.Add(key);

            HandViz viz = GetOrCreateViz(key, IsLeft(hp.handedness));
            viz.root.SetActive(true);

            // Let the whole hand move around based on palm center (landmark 9),
            // while still being biased toward a bottom-of-screen XR-style offset.
            Vector3 baseOffset = IsLeft(hp.handedness) ? leftHandOffset : rightHandOffset;
            Vector2 palmDelta = PalmDelta01(lmSet[9], IsWorld(lmSet, hp));
            Vector3 drift = new Vector3(palmDelta.x, palmDelta.y, 0f) * anchorMoveScale;

            Vector3 anchor = cameraTransform.TransformPoint(baseOffset + drift);
            Quaternion camRot = cameraTransform.rotation;

            // Pin palm center (landmark 9) to the anchor by rendering all joints relative to it.
            bool world = IsWorld(lmSet, hp);
            Vector3 palmLocal = LandmarkToLocal(lmSet[9], world);

            for (int li = 0; li < 21; li++)
            {
                Landmark lm = lmSet[li];
                Vector3 targetLocal = LandmarkToLocal(lm, world) - palmLocal;

                float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
                viz.smoothedLocal[li] = Vector3.Lerp(viz.smoothedLocal[li], targetLocal, t);

                Vector3 worldPos = anchor + (camRot * viz.smoothedLocal[li]);
                viz.joints[li].transform.position = worldPos;
            }

            for (int ci = 0; ci < Connections.Length; ci++)
            {
                (int a, int b) = Connections[ci];
                LineRenderer lr = viz.lines[ci];
                lr.SetPosition(0, viz.joints[a].transform.position);
                lr.SetPosition(1, viz.joints[b].transform.position);
            }
        }

        // Hide any visuals not present in this frame
        foreach (var kv in _byKey)
        {
            if (!updatedKeys.Contains(kv.Key))
            {
                kv.Value.root.SetActive(false);
            }
        }
    }

    private HandViz GetOrCreateViz(string key, bool isLeft)
    {
        if (_byKey.TryGetValue(key, out HandViz existing)) return existing;

        Color c = isLeft ? leftColor : rightColor;

        GameObject root = new GameObject($"HandViz_{key}");
        root.transform.SetParent(transform, worldPositionStays: true);

        var joints = new GameObject[21];
        var smoothed = new Vector3[21];
        for (int i = 0; i < 21; i++) smoothed[i] = Vector3.zero;

        for (int i = 0; i < 21; i++)
        {
            GameObject j = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            j.name = $"J{i}";
            j.transform.SetParent(root.transform, worldPositionStays: true);
            j.transform.localScale = Vector3.one * (jointRadius * 2f);

            var r = j.GetComponent<Renderer>();
            if (r != null) r.material.color = c;

            // No physics
            var col = j.GetComponent<Collider>();
            if (col != null) Destroy(col);

            joints[i] = j;
        }

        var lines = new LineRenderer[Connections.Length];
        for (int i = 0; i < Connections.Length; i++)
        {
            GameObject go = new GameObject($"L{i}");
            go.transform.SetParent(root.transform, worldPositionStays: true);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.material = _lineMaterial;
            lr.startColor = c;
            lr.endColor = c;
            lr.useWorldSpace = true;
            lines[i] = lr;
        }

        HandViz viz = new HandViz
        {
            root = root,
            joints = joints,
            lines = lines,
            smoothedLocal = smoothed,
            color = c
        };

        _byKey[key] = viz;
        return viz;
    }

    private Vector3 LandmarkToLocal(Landmark lm)
    {
        return LandmarkToLocal(lm, world: false);
    }

    private Vector3 LandmarkToLocal(Landmark lm, bool world)
    {
        if (!world)
        {
            // MediaPipe image landmarks:
            // - x,y are normalized to image (0..1), y is downwards
            // - z is roughly negative when landmark is closer to camera
            float nx = (lm.x - 0.5f);
            float y = -(lm.y - 0.5f);
            float z = -lm.z;
            return new Vector3(nx * xyScale, y * xyScale, z * zScale);
        }

        // MediaPipe world landmarks:
        // - roughly meters, origin around the wrist/palm
        // - y is up in MediaPipe world coords; Unity local is also y-up
        // - z is typically toward the camera (negative is forward-ish); flip for consistency
        float wx = mirrorWorldX ? -lm.x : lm.x;
        return new Vector3(wx, -lm.y, -lm.z) * worldScale;
    }

    private static bool IsLeft(string handedness)
    {
        return !string.IsNullOrEmpty(handedness) && handedness.ToLowerInvariant().Contains("left");
    }

    private Vector2 PalmDelta01(Landmark palmCenterLm, bool world)
    {
        if (!world)
        {
            // Map MediaPipe palm center from [0..1] to [-0.5..0.5] and flip Y to Unity-up.
            float x01 = palmCenterLm.x - 0.5f;
            float y01 = -(palmCenterLm.y - 0.5f);
            return new Vector2(x01, y01);
        }

        // World landmarks already centered; use x/y directly (scaled later by anchorMoveScale).
        // X may be mirrored at the LandmarkToLocal stage; mirror here too so drift matches.
        float xWorld = mirrorWorldX ? -palmCenterLm.x : palmCenterLm.x;
        return new Vector2(palmCenterLm.x, -palmCenterLm.y);
    }

    private Landmark[] SelectLandmarks(HandPacket hp)
    {
        if (hp == null) return null;
        if (preferWorldLandmarks && hp.world_landmarks != null && hp.world_landmarks.Length >= 21) return hp.world_landmarks;
        return hp.landmarks;
    }

    private static bool IsWorld(Landmark[] chosen, HandPacket hp)
    {
        return hp != null && chosen == hp.world_landmarks;
    }

    private static string NormalizeKey(string handedness, int fallbackIndex)
    {
        if (string.IsNullOrEmpty(handedness) || handedness == "Unknown") return $"Hand{fallbackIndex}";
        return handedness;
    }
}

