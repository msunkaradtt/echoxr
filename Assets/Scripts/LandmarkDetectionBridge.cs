using UnityEngine;
using System.Collections;

/// <summary>
/// Bridge component for your friend's image detection system to trigger conversations.
/// This shows how to integrate with the voice conversation system.
/// </summary>
public class LandmarkDetectionBridge : MonoBehaviour
{
    [Header("Components")]
    public VoiceConversationManager conversationManager;
    
    [Header("Detection Settings")]
    [Tooltip("Cooldown time after conversation ends before allowing new detection")]
    public float detectionCooldownSeconds = 3f;
    
    [Tooltip("Test landmarks for manual triggering")]
    public string[] testLandmarks = { "cologne_cathedral", "hohenzollern_bridge" };
    
    private bool canDetect = true;
    private float lastConversationEndTime = 0f;
    
    void Start()
    {
        if (conversationManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            conversationManager = FindFirstObjectByType<VoiceConversationManager>();
            if (conversationManager == null)
                conversationManager = FindAnyObjectByType<VoiceConversationManager>();
#else
            conversationManager = FindObjectOfType<VoiceConversationManager>(); // Older Unity fallback
#endif
            if (conversationManager == null)
            {
                Debug.LogError("VoiceConversationManager not found!");
                enabled = false;
                return;
            }
        }

        conversationManager.OnConversationStarted += OnConversationStarted;
        conversationManager.OnConversationEnded += OnConversationEnded;
    }
    
    /// <summary>
    /// Call this method when your image detection system detects landmarks
    /// </summary>
    public void OnLandmarksDetected(string[] detectedLandmarks)
    {
        if (!canDetect)
        {
            Debug.Log("Detection blocked - conversation active or in cooldown");
            return;
        }
        
        if (detectedLandmarks == null || detectedLandmarks.Length == 0)
        {
            Debug.LogWarning("No landmarks provided");
            return;
        }
        
        // Check cooldown
        if (Time.time - lastConversationEndTime < detectionCooldownSeconds)
        {
            Debug.Log($"In cooldown period. Wait {detectionCooldownSeconds - (Time.time - lastConversationEndTime):F1} more seconds");
            return;
        }
        
        Debug.Log($"Landmarks detected: {string.Join(", ", detectedLandmarks)}");
        conversationManager.OnLandmarksDetected(detectedLandmarks);
    }
    
    /// <summary>
    /// Example method showing different detection scenarios
    /// Your friend's code would call OnLandmarksDetected with appropriate data
    /// </summary>
    public void SimulateDetection(int scenario)
    {
        switch (scenario)
        {
            case 1: // Cathedral only
                OnLandmarksDetected(new string[] { "cologne_cathedral" });
                break;
                
            case 2: // Bridge only
                OnLandmarksDetected(new string[] { "hohenzollern_bridge" });
                break;
                
            case 3: // Both landmarks
                OnLandmarksDetected(new string[] { "cologne_cathedral", "hohenzollern_bridge" });
                break;
                
            case 4: // Custom test landmarks
                OnLandmarksDetected(testLandmarks);
                break;
                
            default:
                Debug.Log("Invalid scenario");
                break;
        }
    }
    
    void OnConversationStarted(string[] landmarks)
    {
        canDetect = false;
        Debug.Log($"Conversation started with: {string.Join(", ", landmarks)}");
    }
    
    void OnConversationEnded()
    {
        lastConversationEndTime = Time.time;
        StartCoroutine(EnableDetectionAfterCooldown());
        Debug.Log($"Conversation ended. Detection will be enabled after {detectionCooldownSeconds} seconds");
    }
    
    IEnumerator EnableDetectionAfterCooldown()
    {
        yield return new WaitForSeconds(detectionCooldownSeconds);
        canDetect = true;
        Debug.Log("Detection enabled - ready for new landmarks");
    }
    
    // Debug UI for testing
    void OnGUI()
    {
        if (!Application.isEditor) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        
        GUILayout.Label($"Can Detect: {canDetect}");
        GUILayout.Label($"Conversation Active: {conversationManager?.isConversationActive ?? false}");
        
        if (canDetect)
        {
            if (GUILayout.Button("Simulate: Cathedral Only"))
                SimulateDetection(1);
                
            if (GUILayout.Button("Simulate: Bridge Only"))
                SimulateDetection(2);
                
            if (GUILayout.Button("Simulate: Both Landmarks"))
                SimulateDetection(3);
        }
        else
        {
            GUILayout.Label("Wait for conversation to end...");
        }
        
        if (conversationManager?.isConversationActive ?? false)
        {
            if (GUILayout.Button("Force End Conversation"))
                conversationManager.EndConversation();
        }
        
        GUILayout.EndArea();
    }
    
    void OnDestroy()
    {
        if (conversationManager != null)
        {
            conversationManager.OnConversationStarted -= OnConversationStarted;
            conversationManager.OnConversationEnded -= OnConversationEnded;
        }
    }
}