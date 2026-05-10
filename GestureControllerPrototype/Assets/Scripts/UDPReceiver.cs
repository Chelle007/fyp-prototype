using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

[System.Serializable]
public class TrackingData 
{
    public float hand_x;
    public float hand_y;
    public int hand_up; // Changed from 'click'
    public float head_yaw;
    public float head_pitch;

    // New: full hand packets (0-2 hands)
    public HandPacket[] hands;
}

[System.Serializable]
public class HandPacket
{
    public string handedness; // "Left" / "Right" (MediaPipe label) or "Unknown"
    public Landmark[] landmarks; // 21 landmarks
    public Landmark[] world_landmarks; // 21 world landmarks (optional)
}

[System.Serializable]
public class Landmark
{
    public float x;
    public float y;
    public float z;
}

public class UDPReceiver : MonoBehaviour
{
    Thread receiveThread;
    UdpClient client;
    public int port = 5052;

    public TrackingData currentData = new TrackingData { hands = System.Array.Empty<HandPacket>() };

    void Start()
    {
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    private void ReceiveData()
    {
        client = new UdpClient(port);
        while (true)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref anyIP);
                string text = Encoding.UTF8.GetString(data);
                
                currentData = JsonUtility.FromJson<TrackingData>(text);
                if (currentData.hands == null) currentData.hands = System.Array.Empty<HandPacket>();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(e.ToString());
            }
        }
    }

    void OnApplicationQuit()
    {
        if (receiveThread != null) receiveThread.Abort();
        if (client != null) client.Close();
    }
}