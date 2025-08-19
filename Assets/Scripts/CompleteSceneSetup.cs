using UnityEngine;

/// <summary>
/// Helper script to set up the complete voice conversation system in your Unity scene.
/// Attach this to an empty GameObject and it will configure everything.
/// </summary>
public class CompleteSceneSetup : MonoBehaviour
{
    [Header("Quick Setup")]
    [Tooltip("Automatically set up all components on Start")]
    public bool autoSetup = true;
    
    [Header("Botpress Configuration")]
    public string botpressWebhookId = "897a1416-3ee1-4922-9dea-ef7f24ad14d7";
    public string botpressUserKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpZCI6ImhhY2thdGhvbi11c2VyLTEiLCJpYXQiOjE3NTU1MTA2MTB9.EJhhos6WwsVcJcjaVB9CTUS9HVlFb0LOjtET_iKe4oE";
    
    [Header("Deepgram Configuration")]
    public string deepgramApiKey = "acce28246b2e53330182e6af5805c24b92b6bbc6";
    
    [Header("Component References")]
    public BotpressApi botpressApi;
    public DeepgramWebSocketHandler deepgramHandler;
    public VoiceConversationManager conversationManager;
    public LandmarkDetectionBridge detectionBridge;
    

    void Start()
    {
        if (autoSetup)
        {
            SetupComponents();
        }
    }
    
    [ContextMenu("Setup All Components")]
    public void SetupComponents()
    {
        // Check if components exist, if not add them
        if (botpressApi == null)
            botpressApi = GetComponent<BotpressApi>() ?? gameObject.AddComponent<BotpressApi>();
            
        if (deepgramHandler == null)
            deepgramHandler = GetComponent<DeepgramWebSocketHandler>() ?? gameObject.AddComponent<DeepgramWebSocketHandler>();
            
        if (conversationManager == null)
            conversationManager = GetComponent<VoiceConversationManager>() ?? gameObject.AddComponent<VoiceConversationManager>();
            
        if (detectionBridge == null)
            detectionBridge = GetComponent<LandmarkDetectionBridge>() ?? gameObject.AddComponent<LandmarkDetectionBridge>();
        
        // Configure Botpress
        botpressApi.webhookId = botpressWebhookId;
        botpressApi.xUserKey = botpressUserKey;
        
        // Configure Deepgram
        deepgramHandler.apiKey = deepgramApiKey;
        
        // Link components
        conversationManager.botpressApi = botpressApi;
        conversationManager.deepgramHandler = deepgramHandler;
        detectionBridge.conversationManager = conversationManager;
        
        Debug.Log("All components configured successfully!");
    }
    
    /// <summary>
    /// Example method showing how your friend's detection code should integrate
    /// </summary>
    public void OnImageDetected(string detectedLandmark)
    {
        // Convert single landmark to array and trigger
        if (detectionBridge != null)
        {
            detectionBridge.OnLandmarksDetected(new string[] { detectedLandmark });

        }
    }
    
    /// <summary>
    /// Example for multiple simultaneous detections
    /// </summary>
    public void OnMultipleImagesDetected(string[] detectedLandmarks)
    {
        if (detectionBridge != null)
        {
            detectionBridge.OnLandmarksDetected(detectedLandmarks);
        }
    }
}