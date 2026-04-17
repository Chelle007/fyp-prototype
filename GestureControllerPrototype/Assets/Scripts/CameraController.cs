using UnityEngine;

public class CameraController : MonoBehaviour
{
    public UDPReceiver receiver; 
    
    [Header("Camera Settings")]
    public float yawSensitivity = 45f;   
    public float pitchSensitivity = 30f; 
    public float smoothSpeed = 5f;       

    private Quaternion targetRotation;
    private Vector3 startingRotation;

    void Start()
    {
        startingRotation = transform.localEulerAngles;
    }

    void Update()
    {
        TrackingData data = receiver.currentData;

        // FIXED: Removed the negative sign from the pitch calculation!
        float targetYaw = startingRotation.y + (data.head_yaw * yawSensitivity);
        float targetPitch = startingRotation.x + (data.head_pitch * pitchSensitivity);

        targetRotation = Quaternion.Euler(targetPitch, targetYaw, startingRotation.z);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, Time.deltaTime * smoothSpeed);
    }
}