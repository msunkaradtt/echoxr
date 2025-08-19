using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

/// <summary>
/// Main orchestrator that connects Botpress conversations with Deepgram voice I/O.
/// Handles the complete conversation lifecycle from landmark detection to conversation end.
/// </summary>
public class VoiceConversationManager : MonoBehaviour
{
    [Header("Components")]
    public BotpressApi botpressApi;
    public DeepgramWebSocketHandler deepgramHandler;

    [Header("Conversation State")]
    public bool isConversationActive = false;
    public string currentConversationId;

    [Header("Configuration")]

    [Tooltip("Keywords that might indicate user wants to end")]
    public string[] endPhrases = { "that's all", "goodbye", "bye", "see you", "stop", "end conversation", "that's it" };

    // State tracking
    private bool isProcessingBotResponse = false;
    private Queue<string> botMessageQueue = new Queue<string>();
    private BotpressApi.ChoiceOption[] lastChoiceOptions; // cache of last presented choices

    // Events for external systems
    public event Action<string[]> OnConversationStarted;
    public event Action OnConversationEnded;
    public event Action<string> OnUserSpoke;
    public event Action<string> OnBotSpoke;

    void Start()
    {
        // Validate components
        if (botpressApi == null)
            botpressApi = GetComponent<BotpressApi>();
        if (deepgramHandler == null)
            deepgramHandler = GetComponent<DeepgramWebSocketHandler>();

        if (botpressApi == null || deepgramHandler == null)
        {
            Debug.LogError("Missing required components!");
            enabled = false;
            return;
        }

        // Subscribe to Deepgram events - use final transcript for better accuracy
        deepgramHandler.OnFinalTranscript += HandleFinalTranscript;
        deepgramHandler.OnSTTConnected += OnSTTReady;
        deepgramHandler.OnTTSConnected += OnTTSReady;
        deepgramHandler.OnSpeechStarted += OnUserStartedSpeaking;
        deepgramHandler.OnSpeechEnded += OnUserStoppedSpeaking;

        // Initialize WebSocket connections
        deepgramHandler.ConnectSTT();
        deepgramHandler.ConnectTTS();
    }

    /// <summary>
    /// Call this when landmarks are detected to start a new conversation
    /// </summary>
    public void StartConversation(string[] detectedLandmarks)
    {
        if (isConversationActive)
        {
            Debug.LogWarning("Conversation already active. Ending current conversation first.");
            EndConversation();
            StartCoroutine(DelayedStart(detectedLandmarks));
            return;
        }

        StartCoroutine(InitializeConversation(detectedLandmarks));
    }

    IEnumerator DelayedStart(string[] landmarks)
    {
        yield return new WaitForSeconds(1f);
        StartCoroutine(InitializeConversation(landmarks));
    }

    IEnumerator InitializeConversation(string[] landmarks)
    {
        Debug.Log($"Starting conversation with landmarks: {string.Join(", ", landmarks)}");

        isConversationActive = true;

        // Create conversation
        string convId = null;
        yield return botpressApi.CreateConversation(id => convId = id);

        if (string.IsNullOrEmpty(convId))
        {
            Debug.LogError("Failed to create conversation");
            EndConversation();
            yield break;
        }

        currentConversationId = convId;
        botpressApi.conversationId = convId;

        // Send landmark event
        yield return botpressApi.SendLandmarkEvent(convId, landmarks);

        // Start polling for bot's initial message
        StartCoroutine(ContinuousMessagePolling());

        OnConversationStarted?.Invoke(landmarks);
    }

    /// <summary>
    /// Continuously poll for new messages from Botpress
    /// </summary>
    IEnumerator ContinuousMessagePolling()
    {
        while (isConversationActive && !string.IsNullOrEmpty(currentConversationId))
        {
            yield return botpressApi.FetchMessagesOnce(
                currentConversationId,
                (text, isChoice, options) => HandleBotMessage(text, isChoice, options)
            );

            yield return new WaitForSeconds(0.8f);
        }
    }

    /// <summary>
    /// Handle incoming message from Botpress
    /// </summary>
    void HandleBotMessage(string text, bool isChoice, BotpressApi.ChoiceOption[] options)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Check for end sentinel
        if (text.Trim() == "[[END]]")
        {
            Debug.Log("Received END sentinel from Botpress");
            EndConversation();
            return;
        }

        // Queue the message for TTS
        if (isChoice)
        {
            lastChoiceOptions = options; // cache for next user reply
        }

        if (!isProcessingBotResponse)
        {
            StartCoroutine(ProcessBotMessage(text, isChoice, options));
        }
        else
        {
            botMessageQueue.Enqueue(text);
        }
    }

    IEnumerator ProcessBotMessage(string text, bool isChoice, BotpressApi.ChoiceOption[] options)
    {
        isProcessingBotResponse = true;

        Debug.Log($"Bot says: {text}");
        OnBotSpoke?.Invoke(text);

        // Stop listening while bot speaks
        deepgramHandler.StopListening();

        // Send to TTS
        deepgramHandler.SendTextToTTS(text);

        // If it's a choice, log the options (you could create UI buttons here)
        if (isChoice && options != null && options.Length > 0)
        {
            Debug.Log($"Options available: {string.Join(" | ", Array.ConvertAll(options, o => o.label))}");
            // TODO: You could create UI buttons for quick responses here
        }

        // Wait a bit before allowing next message
        yield return new WaitForSeconds(0.5f);

        // Process any queued messages
        if (botMessageQueue.Count > 0)
        {
            string nextMessage = botMessageQueue.Dequeue();
            StartCoroutine(ProcessBotMessage(nextMessage, false, null));
        }
        else
        {
            isProcessingBotResponse = false;
            // Listening will automatically resume when TTS finishes playing
        }
    }

    /// <summary>
    /// Handle final transcript from Deepgram STT (with VAD)
    /// </summary>
    void HandleFinalTranscript(string transcript)
    {
        if (!isConversationActive || string.IsNullOrEmpty(currentConversationId))
            return;

        if (string.IsNullOrEmpty(transcript.Trim()))
            return;

        Debug.Log($"User said (final): {transcript}");

        // Normalize common mis-hearings for intents
        string normalized = NormalizeUserIntent(transcript);
        if (normalized != transcript)
        {
            Debug.Log($"Intent normalized to: {normalized}");
            transcript = normalized;
        }

        // Check for end phrases
        string lowerTranscript = transcript.ToLower();
        foreach (string endPhrase in endPhrases)
        {
            if (lowerTranscript.Contains(endPhrase))
            {
                Debug.Log("End phrase detected in user speech");
                break;
            }
        }

        OnUserSpoke?.Invoke(transcript);

        // Send to Botpress immediately
        StartCoroutine(SendUserMessage(transcript));
    }

    IEnumerator SendUserMessage(string message)
    {
        float sendStart = Time.time;
        yield return botpressApi.SendUserText(currentConversationId, message);
        Debug.Log($"User message dispatch duration: {Time.time - sendStart:0.00}s");

        // Start a watchdog: if no bot response within X seconds, attempt to resume listening (maybe Botpress silent)
        StartCoroutine(BotResponseWatchdog(7f));
    }

    string NormalizeUserIntent(string raw)
    {
        string t = raw.ToLowerInvariant().Trim();
        // Remove trailing punctuation
        t = t.Trim('.', '!', '?');

        // Map phonetic / misheard variants
        if (t.Contains("thirty") && t.Contains("second") && t.Contains("story")) return "30 second story";
        if (t.Contains("30") && t.Contains("second") && t.Contains("story")) return "30 second story";
        if (t.Contains("photo") && (t.Contains("angle") || t.Contains("picture") || t.Contains("shot"))) return "photo angle";
        // Try to map to any cached choice option labels (case-insensitive contains / Levenshtein-lite)
        if (lastChoiceOptions != null && lastChoiceOptions.Length > 0)
        {
            foreach (var opt in lastChoiceOptions)
            {
                if (opt == null || string.IsNullOrEmpty(opt.label)) continue;
                var lowerLabel = opt.label.ToLowerInvariant();
                if (SimilarityScore(t, lowerLabel) >= 0.7f)
                {
                    Debug.Log($"Intent matched choice label '{opt.label}' -> will send value '{opt.value}'");
                    return opt.value; // send choice value preferred by Botpress
                }
            }
        }
        // fallback
        return raw;
    }

    // Very lightweight similarity: Jaccard over word set; can be improved as needed
    float SimilarityScore(string a, string b)
    {
        var setA = new HashSet<string>(a.Split(' '));
        var setB = new HashSet<string>(b.Split(' '));
        int intersect = 0;
        foreach (var w in setA) if (setB.Contains(w)) intersect++;
        int union = setA.Count + setB.Count - intersect;
        if (union == 0) return 0f;
        return (float)intersect / union;
    }

    IEnumerator BotResponseWatchdog(float timeoutSeconds)
    {
        float start = Time.time;
        // Wait until either processing bot response started or timeout
        while (Time.time - start < timeoutSeconds)
        {
            if (isProcessingBotResponse) yield break; // bot responded
            yield return null;
        }
        if (!isProcessingBotResponse && isConversationActive)
        {
            Debug.LogWarning($"BotResponseWatchdog: No bot reply within {timeoutSeconds}s. Re-listening.");
            deepgramHandler.StartListening();
        }
    }

    void OnUserStartedSpeaking()
    {
        // Stop any bot TTS when user starts speaking
        deepgramHandler.StopTTSPlayback();
    }

    void OnUserStoppedSpeaking()
    {
        // User stopped, waiting for bot response
        Debug.Log("User stopped speaking, waiting for bot response");
    }

    /// <summary>
    /// End the current conversation and reset state
    /// </summary>
    public void EndConversation()
    {
        if (!isConversationActive) return;

        Debug.Log("Ending conversation");

        isConversationActive = false;
        currentConversationId = null;
        botpressApi.conversationId = null;
        isProcessingBotResponse = false;
        botMessageQueue.Clear();

        // Stop all audio
        deepgramHandler.StopTTSPlayback();
        deepgramHandler.StopListening();

        // Stop all coroutines related to polling
        StopAllCoroutines();

        OnConversationEnded?.Invoke();

        Debug.Log("Conversation ended. Ready for new detection.");
    }

    void OnSTTReady()
    {
        Debug.Log("STT is ready");
    }

    void OnTTSReady()
    {
        Debug.Log("TTS is ready");
    }

    /// <summary>
    /// Public method for your friend's image detection system to call
    /// </summary>
    public void OnLandmarksDetected(string[] landmarks)
    {
        if (!isConversationActive)
        {
            StartConversation(landmarks);
        }
        else
        {
            Debug.Log("Conversation already active, ignoring new detection");
        }
    }

    void OnDestroy()
    {
        if (deepgramHandler != null)
        {
            deepgramHandler.OnFinalTranscript -= HandleFinalTranscript;
            deepgramHandler.OnSpeechStarted -= OnUserStartedSpeaking;
            deepgramHandler.OnSpeechEnded -= OnUserStoppedSpeaking;
            deepgramHandler.DisconnectAll();
        }

        EndConversation();
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            EndConversation();
        }
    }
}

