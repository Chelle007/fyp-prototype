using UnityEngine;

public class HandRigDriver : MonoBehaviour
{
    [Header("Input")]
    public UDPReceiver receiver;
    public string handedness = "Right"; // "Left" or "Right"

    [Header("Rig roots")]
    public Transform handRoot; // overall hand transform (optional)
    public Transform wrist; // optional

    [Header("Finger bones (proximal->intermediate->distal)")]
    public Transform[] thumb = new Transform[3];
    public Transform[] index = new Transform[3];
    public Transform[] middle = new Transform[3];
    public Transform[] ring = new Transform[3];
    public Transform[] pinky = new Transform[3];

    [Header("Tuning")]
    public float rotationSmoothing = 18f;
    public bool applyHandRootRotation = true;

    [Header("Bending (curl-based, more reliable)")]
    public bool useCurlBend = true;
    [Tooltip("Max bend angle (degrees) for proximal/intermediate/distal bones.")]
    public Vector3 maxBendAngles = new Vector3(55f, 70f, 45f);
    [Tooltip("Extra bend multiplier if motion feels weak.")]
    public float bendStrength = 1.2f;

    private bool _didAutoBind;
    private bool _didCacheRestPose;

    private struct BoneRest
    {
        public Transform bone;
        public Vector3 restDirInParent;
        public Quaternion restLocalRotation;
    }

    private BoneRest[] _thumbRest;
    private BoneRest[] _indexRest;
    private BoneRest[] _middleRest;
    private BoneRest[] _ringRest;
    private BoneRest[] _pinkyRest;

    private void Start()
    {
        // Make setup as close to “drop-in” as possible.
        if (receiver == null) receiver = FindFirstObjectByType<UDPReceiver>();
        AutoBindIfNeeded();
    }

    private void Update()
    {
        if (receiver == null) return;

        AutoBindIfNeeded();
        CacheRestPoseIfNeeded();

        HandPacket hp = FindHand(receiver.currentData, handedness);
        if (hp == null || hp.landmarks == null || hp.landmarks.Length < 21) return;

        // Build a palm basis from wrist(0), index_mcp(5), pinky_mcp(17)
        Vector3 p0 = ToVec3(hp.landmarks[0]);
        Vector3 p5 = ToVec3(hp.landmarks[5]);
        Vector3 p17 = ToVec3(hp.landmarks[17]);

        Vector3 xAxis = (p5 - p17).normalized; // across palm
        Vector3 yAxis = (p5 + p17) * 0.5f - p0; // roughly forward/up along palm
        yAxis = yAxis.normalized;
        Vector3 zAxis = Vector3.Cross(xAxis, yAxis).normalized;
        yAxis = Vector3.Cross(zAxis, xAxis).normalized;

        Quaternion palmRot = Quaternion.LookRotation(zAxis, yAxis);

        if (applyHandRootRotation && handRoot != null)
        {
            handRoot.rotation = Smooth(handRoot.rotation, palmRot, rotationSmoothing);
        }

        // Palm normal in tracker space (used to define a stable finger hinge axis)
        Vector3 palmNormalTracker = Vector3.Cross((p5 - p0), (p17 - p0)).normalized;

        // Drive finger chains by rotating each bone from its cached rest direction
        // toward the tracked joint direction, expressed in the bone's parent space.
        Transform basis = (wrist != null) ? wrist : (handRoot != null ? handRoot : transform);
        DriveChainRest(_thumbRest, hp, new[] { 1, 2, 3, 4 }, basis, palmNormalTracker);
        DriveChainRest(_indexRest, hp, new[] { 5, 6, 7, 8 }, basis, palmNormalTracker);
        DriveChainRest(_middleRest, hp, new[] { 9, 10, 11, 12 }, basis, palmNormalTracker);
        DriveChainRest(_ringRest, hp, new[] { 13, 14, 15, 16 }, basis, palmNormalTracker);
        DriveChainRest(_pinkyRest, hp, new[] { 17, 18, 19, 20 }, basis, palmNormalTracker);
    }

    private void CacheRestPoseIfNeeded()
    {
        if (_didCacheRestPose) return;
        if (!_didAutoBind) return;

        _thumbRest = BuildRestChain(thumb);
        _indexRest = BuildRestChain(index);
        _middleRest = BuildRestChain(middle);
        _ringRest = BuildRestChain(ring);
        _pinkyRest = BuildRestChain(pinky);

        _didCacheRestPose = true;
    }

    private static BoneRest[] BuildRestChain(Transform[] bones)
    {
        if (bones == null) return System.Array.Empty<BoneRest>();

        // Allow 2-bone thumbs etc.: only include consecutive non-null bones with a child.
        var list = new System.Collections.Generic.List<BoneRest>(3);
        for (int i = 0; i < bones.Length; i++)
        {
            Transform b = bones[i];
            if (b == null) break;

            Transform child = null;
            if (i + 1 < bones.Length) child = bones[i + 1];
            if (child == null)
            {
                // fallback: first child transform (end bone), if any
                if (b.childCount > 0) child = b.GetChild(0);
            }
            if (child == null) break;

            Transform parent = b.parent;
            if (parent == null) break;

            Vector3 restDirParent = parent.InverseTransformDirection((child.position - b.position).normalized);
            list.Add(new BoneRest
            {
                bone = b,
                restDirInParent = restDirParent.normalized,
                restLocalRotation = b.localRotation
            });
        }

        return list.ToArray();
    }

    private void AutoBindIfNeeded()
    {
        if (_didAutoBind) return;

        // If this script is placed on a child GameObject (common when clicking "Add Component"
        // on a selected child), the bones may live on the parent/siblings. Search upwards.
        Transform searchRoot = transform;
        for (int up = 0; up < 4; up++)
        {
            if (FindDeepChild(searchRoot, "HandRig") != null || FindDeepChild(searchRoot, "Wrist") != null)
                break;
            if (searchRoot.parent == null) break;
            searchRoot = searchRoot.parent;
        }

        // If any bone is missing, try to locate a known rig hierarchy.
        if (handRoot == null) handRoot = FindDeepChild(searchRoot, "HandRig");
        if (wrist == null) wrist = FindDeepChild(searchRoot, "Wrist");

        // If already configured, don’t touch.
        bool configured =
            thumb[0] != null && thumb[1] != null &&
            index[0] != null && index[1] != null && index[2] != null &&
            middle[0] != null && middle[1] != null && middle[2] != null &&
            ring[0] != null && ring[1] != null && ring[2] != null &&
            pinky[0] != null && pinky[1] != null && pinky[2] != null;

        if (configured)
        {
            _didAutoBind = true;
            return;
        }

        // Auto-bind for the "Stylized - Simple Hands" rig (names from your hierarchy screenshot).
        // Path root: HandRig/Wrist/Hand/<Finger...>
        Transform root = FindDeepChild(searchRoot, "Hand");
        if (root == null) root = searchRoot;

        // Thumb is shorter in this asset; distal end may be named Bone.003_end.
        thumb[0] ??= FindDeepChild(root, "Thumb");
        thumb[1] ??= FindDeepChild(root, "Thumb2");
        thumb[2] ??= FindDeepChild(root, "Bone.003_end");

        index[0] ??= FindDeepChild(root, "IndexFinger");
        index[1] ??= FindDeepChild(root, "Index2");
        index[2] ??= FindDeepChild(root, "Index3");

        middle[0] ??= FindDeepChild(root, "MiddleFinger");
        middle[1] ??= FindDeepChild(root, "Middle2");
        middle[2] ??= FindDeepChild(root, "Middle3");

        ring[0] ??= FindDeepChild(root, "RingFinger");
        ring[1] ??= FindDeepChild(root, "Ring2");
        ring[2] ??= FindDeepChild(root, "Ring3");

        pinky[0] ??= FindDeepChild(root, "LittleFinger");
        pinky[1] ??= FindDeepChild(root, "Little2");
        pinky[2] ??= FindDeepChild(root, "Little3");

        // Consider it done if we found the main chains (thumb[2] is optional).
        bool ok =
            thumb[0] != null && thumb[1] != null &&
            index[0] != null && index[1] != null && index[2] != null &&
            middle[0] != null && middle[1] != null && middle[2] != null &&
            ring[0] != null && ring[1] != null && ring[2] != null &&
            pinky[0] != null && pinky[1] != null && pinky[2] != null;

        if (ok) _didAutoBind = true;
    }

    private void DriveChainRest(BoneRest[] chain, HandPacket hp, int[] idx, Transform basis, Vector3 palmNormalTracker)
    {
        if (chain == null || chain.Length == 0) return;
        if (basis == null) return;

        Vector3 a = ToVec3(hp.landmarks[idx[0]]);
        Vector3 b = ToVec3(hp.landmarks[idx[1]]);
        Vector3 c = ToVec3(hp.landmarks[idx[2]]);
        Vector3 d = ToVec3(hp.landmarks[idx[3]]);

        // Curl factor (0=open, 1=closed-ish) using distances to wrist.
        // This works well even when landmark Z isn't great.
        Vector3 w = ToVec3(hp.landmarks[0]);
        float mcpDist = (a - w).magnitude + 1e-6f;
        float tipDist = (d - w).magnitude;
        float curl = Mathf.Clamp01((1f - (tipDist / mcpDist)) * bendStrength);

        Vector3 palmNormalWorld = basis.TransformDirection(palmNormalTracker.normalized);

        ApplyBone(chain, 0, basis, palmNormalWorld, curl, maxBendAngles.x);
        ApplyBone(chain, 1, basis, palmNormalWorld, curl, maxBendAngles.y);
        ApplyBone(chain, 2, basis, palmNormalWorld, curl, maxBendAngles.z);
    }

    private void ApplyBone(BoneRest[] chain, int index, Transform basis, Vector3 palmNormalWorld, float curl01, float maxAngleDeg)
    {
        if (index < 0 || index >= chain.Length) return;
        Transform bone = chain[index].bone;
        if (bone == null || bone.parent == null) return;

        // Define a hinge axis in the bone's parent space:
        // hingeAxis = palmNormal x restDir (so it bends "toward the palm")
        Vector3 palmNormalParent = bone.parent.InverseTransformDirection(palmNormalWorld).normalized;
        Vector3 hingeAxisParent = Vector3.Cross(palmNormalParent, chain[index].restDirInParent).normalized;
        if (hingeAxisParent.sqrMagnitude < 1e-6f) return;

        float angle = curl01 * maxAngleDeg;
        Quaternion delta = Quaternion.AngleAxis(angle, hingeAxisParent);
        Quaternion targetLocal = delta * chain[index].restLocalRotation;

        float t = 1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime);
        bone.localRotation = Quaternion.Slerp(bone.localRotation, targetLocal, t);
    }

    private static Quaternion Smooth(Quaternion current, Quaternion target, float smoothing)
    {
        float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        return Quaternion.Slerp(current, target, t);
    }

    private static Vector3 ToVec3(Landmark lm)
    {
        // Keep in tracker normalized space; best results if this driver is used
        // with a calibrated mapping later. This script is a starting point.
        return new Vector3(lm.x - 0.5f, -(lm.y - 0.5f), -lm.z);
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent == null) return null;
        if (parent.name == name) return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            Transform result = FindDeepChild(child, name);
            if (result != null) return result;
        }

        return null;
    }

    private static HandPacket FindHand(TrackingData data, string desiredHandedness)
    {
        if (data == null || data.hands == null) return null;

        for (int i = 0; i < data.hands.Length; i++)
        {
            HandPacket hp = data.hands[i];
            if (hp == null) continue;
            if (!string.IsNullOrEmpty(hp.handedness) &&
                hp.handedness.ToLowerInvariant().Contains(desiredHandedness.ToLowerInvariant()))
            {
                return hp;
            }
        }

        // No fallback: if the requested handedness isn't present, do not animate.
        return null;
    }
}

