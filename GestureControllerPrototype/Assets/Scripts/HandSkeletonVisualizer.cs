using System.Collections.Generic;
using UnityEngine;

public class HandSkeletonVisualizer : MonoBehaviour
{
    [Header("Input")]
    public UDPReceiver receiver;

    [Header("Modular 3D Assets (Optional)")]
    [Tooltip("Drop your 3D models here. If empty, uses standard spheres.")]
    public GameObject palmPrefab;
    public GameObject knucklePrefab;
    public GameObject fingertipPrefab;

    [Header("Placement (first-person XR-style)")]
    public Transform cameraTransform;
    public Vector3 leftHandOffset = new Vector3(-0.18f, -0.10f, 0.55f);
    public Vector3 rightHandOffset = new Vector3(0.18f, -0.10f, 0.55f);
    public float anchorMoveScale = 0.35f;

    [Header("Landmark mapping")]
    public bool preferWorldLandmarks = true;
    public float xyScale = 0.7f;
    public float zScale = 0.7f;
    public float smoothing = 18f;
    public float worldScale = 2.5f;
    public bool mirrorWorldX = false;

    [Header("Rendering (Fallback if no Prefabs)")]
    public float jointRadius = 0.02f;
    public float lineWidth = 0.008f;
    public Color leftColor = new Color(0.35f, 0.85f, 1f, 1f);
    public Color rightColor = new Color(1f, 0.55f, 0.35f, 1f);

    private static readonly (int a, int b)[] Connections =
    {
        (0, 1), (1, 2), (2, 3), (3, 4),       // Thumb
        (0, 5), (5, 6), (6, 7), (7, 8),       // Index
        (5, 9), (9, 10), (10, 11), (11, 12),  // Middle
        (9, 13), (13, 14), (14, 15), (15, 16),// Ring
        (13, 17), (17, 18), (18, 19), (19, 20),// Pinky
        (0, 17)                               // Palm edge
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

            Vector3 baseOffset = IsLeft(hp.handedness) ? leftHandOffset : rightHandOffset;
            Vector2 palmDelta = PalmDelta01(lmSet[9], IsWorld(lmSet, hp));
            Vector3 drift = new Vector3(palmDelta.x, palmDelta.y, 0f) * anchorMoveScale;

            Vector3 anchor = cameraTransform.TransformPoint(baseOffset + drift);
            Quaternion camRot = cameraTransform.rotation;
            bool world = IsWorld(lmSet, hp);
            Vector3 palmLocal = LandmarkToLocal(lmSet[9], world);

            // 1. Position all the joints
            for (int li = 0; li < 21; li++)
            {
                Landmark lm = lmSet[li];
                Vector3 targetLocal = LandmarkToLocal(lm, world) - palmLocal;

                float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
                viz.smoothedLocal[li] = Vector3.Lerp(viz.smoothedLocal[li], targetLocal, t);

                Vector3 worldPos = anchor + (camRot * viz.smoothedLocal[li]);
                viz.joints[li].transform.position = worldPos;
            }

            // 2. Draw lines and Orient the 3D meshes using LookAt!
            for (int ci = 0; ci < Connections.Length; ci++)
            {
                (int a, int b) = Connections[ci];
                
                // Draw connecting lines
                LineRenderer lr = viz.lines[ci];
                lr.SetPosition(0, viz.joints[a].transform.position);
                lr.SetPosition(1, viz.joints[b].transform.position);

                // IMPORTANT: Make the 3D mesh point directly at the next joint!
                viz.joints[a].transform.LookAt(viz.joints[b].transform.position);
            }

            // 3. Fingertips have no "next joint" to look at, so they copy the rotation of the joint behind them.
            viz.joints[4].transform.rotation = viz.joints[3].transform.rotation;
            viz.joints[8].transform.rotation = viz.joints[7].transform.rotation;
            viz.joints[12].transform.rotation = viz.joints[11].transform.rotation;
            viz.joints[16].transform.rotation = viz.joints[15].transform.rotation;
            viz.joints[20].transform.rotation = viz.joints[19].transform.rotation;
        }

        foreach (var kv in _byKey)
        {
            if (!updatedKeys.Contains(kv.Key)) kv.Value.root.SetActive(false);
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
            GameObject j = null;

            // Decide which 3D model to spawn
            if (i == 4 || i == 8 || i == 12 || i == 16 || i == 20) {
                if (fingertipPrefab != null) j = Instantiate(fingertipPrefab);
            } else if (i == 0 || i == 9) {
                if (palmPrefab != null) j = Instantiate(palmPrefab);
            } else {
                if (knucklePrefab != null) j = Instantiate(knucklePrefab);
            }

            // Fallback to default spheres if no prefab is assigned
            if (j == null)
            {
                j = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                j.transform.localScale = Vector3.one * (jointRadius * 2f);
                var r = j.GetComponent<Renderer>();
                if (r != null) r.material.color = c;
            }

            j.name = $"J{i}";
            j.transform.SetParent(root.transform, worldPositionStays: true);

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

        HandViz viz = new HandViz { root = root, joints = joints, lines = lines, smoothedLocal = smoothed, color = c };
        _byKey[key] = viz;
        return viz;
    }

    private Vector3 LandmarkToLocal(Landmark lm, bool world)
    {
        if (!world) return new Vector3((lm.x - 0.5f) * xyScale, -(lm.y - 0.5f) * xyScale, -lm.z * zScale);
        return new Vector3(mirrorWorldX ? -lm.x : lm.x, -lm.y, -lm.z) * worldScale;
    }

    private static bool IsLeft(string handedness) => !string.IsNullOrEmpty(handedness) && handedness.ToLowerInvariant().Contains("left");

    private Vector2 PalmDelta01(Landmark palmCenterLm, bool world)
    {
        if (!world) return new Vector2(palmCenterLm.x - 0.5f, -(palmCenterLm.y - 0.5f));
        return new Vector2(mirrorWorldX ? -palmCenterLm.x : palmCenterLm.x, -palmCenterLm.y);
    }

    private Landmark[] SelectLandmarks(HandPacket hp)
    {
        if (hp == null) return null;
        if (preferWorldLandmarks && hp.world_landmarks != null && hp.world_landmarks.Length >= 21) return hp.world_landmarks;
        return hp.landmarks;
    }

    private static bool IsWorld(Landmark[] chosen, HandPacket hp) => hp != null && chosen == hp.world_landmarks;

    private static string NormalizeKey(string handedness, int fallbackIndex) => string.IsNullOrEmpty(handedness) || handedness == "Unknown" ? $"Hand{fallbackIndex}" : handedness;
}