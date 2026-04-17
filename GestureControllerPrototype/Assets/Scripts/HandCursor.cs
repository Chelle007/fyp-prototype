using UnityEngine;

public class HandCursor : MonoBehaviour
{
    public UDPReceiver receiver;
    
    private Renderer objRenderer;

    void Start()
    {
        objRenderer = GetComponent<Renderer>();
    }

    void Update()
    {
        // 1. Get the latest data from the background thread
        TrackingData data = receiver.currentData;

        // 2. ONLY handle the color change. 
        // (All position mapping and Lerp movement has been completely deleted!)
        if (data.hand_up == 1)
        {
            if (objRenderer != null) objRenderer.material.color = Color.red;
        }
        else
        {
            if (objRenderer != null) objRenderer.material.color = Color.white;
        }
    }
}