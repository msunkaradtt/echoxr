using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Text;

/// <summary>
/// Enhanced Botpress Chat API helper for Unity with voice conversation support.
/// </summary>
public class BotpressApi : MonoBehaviour
{
    [Header("Botpress Chat API")]
    [Tooltip("Your Chat integration webhookId (GUID from Botpress Chat integration)")]
    public string webhookId = "897a1416-3ee1-4922-9dea-ef7f24ad14d7";

    [Tooltip("x-user-key returned by POST /users (persist per user)")]
    [TextArea(2, 4)]
    public string xUserKey =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpZCI6ImhhY2thdGhvbi11c2VyLTEiLCJpYXQiOjE3NTU1MTA2MTB9.EJhhos6WwsVcJcjaVB9CTUS9HVlFb0LOjtET_iKe4oE";

    [Header("Message Tracking")]
    private string lastProcessedMessageId = "";
    
    // Public property for conversation ID
    [NonSerialized] public string conversationId;

    string BaseUrl => $"https://chat.botpress.cloud/{webhookId}";

    // ===== JSON models (Unity JsonUtility: public fields, matching names) =====
    [Serializable] public class ConversationObj { public string id; public string createdAt; public string updatedAt; }
    [Serializable] public class CreateConversationRes { public ConversationObj conversation; }

    [Serializable] public class LandmarkPayload { public string type; public string[] landmarks; }
    [Serializable] public class EventReq { public string conversationId; public LandmarkPayload payload; }

    [Serializable] public class TextPayload { public string type = "text"; public string text; }
    [Serializable] public class MsgReq { public string conversationId; public TextPayload payload; }

    [Serializable] public class ChoiceOption { public string label; public string value; }
    [Serializable] public class MsgPayload
    {
        public string type;         // "text" or "choice"
        public string text;         // present for both text & choice cards
        public ChoiceOption[] options; // present for choice
    }
    [Serializable] public class Message
    {
        public string id;
        public string createdAt;
        public string conversationId;
        public string userId;       // assistant usually "user_..."
        public MsgPayload payload;
    }
    [Serializable] public class MessagesResponse { public Message[] messages; }

    // ===== Public API Methods =====

    /// <summary>
    /// Create a new conversation and return its ID via callback
    /// </summary>
    public IEnumerator CreateConversation(Action<string> onId)
    {
        var url = $"{BaseUrl}/conversations";
        using var www = new UnityWebRequest(url, "POST");
        www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("x-user-key", xUserKey);

        yield return www.SendWebRequest();
        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"CreateConversation error: {www.responseCode} {www.error} {www.downloadHandler.text}");
            onId?.Invoke(null);
            yield break;
        }

        var res = JsonUtility.FromJson<CreateConversationRes>(www.downloadHandler.text);
        Debug.Log($"CreateConversation OK: {res.conversation.id}");
        lastProcessedMessageId = ""; // Reset for new conversation
        onId?.Invoke(res.conversation.id);
    }

    /// <summary>
    /// Send landmark event to trigger the conversation flow
    /// </summary>
    public IEnumerator SendLandmarkEvent(string convId, string[] landmarks)
    {
        var url = $"{BaseUrl}/events";
        var ev = new EventReq
        {
            conversationId = convId,
            payload = new LandmarkPayload { type = "custom.landmark", landmarks = landmarks }
        };

        var json = JsonUtility.ToJson(ev);
        using var www = new UnityWebRequest(url, "POST");
        www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("x-user-key", xUserKey);

        yield return www.SendWebRequest();
        if (www.result != UnityWebRequest.Result.Success)
            Debug.LogError($"SendLandmarkEvent error: {www.responseCode} {www.error} {www.downloadHandler.text}");
        else
            Debug.Log($"SendLandmarkEvent OK for landmarks: {string.Join(", ", landmarks)}");
    }

    /// <summary>
    /// Send user text message to the conversation
    /// </summary>
    public IEnumerator SendUserText(string convId, string text)
    {
        var url = $"{BaseUrl}/messages";
        var req = new MsgReq { conversationId = convId, payload = new TextPayload { text = text } };
        var json = JsonUtility.ToJson(req);

        using var www = new UnityWebRequest(url, "POST");
        www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("x-user-key", xUserKey);

        yield return www.SendWebRequest();
        if (www.result != UnityWebRequest.Result.Success)
            Debug.LogError($"SendUserText error: {www.responseCode} {www.error} {www.downloadHandler.text}");
        else
            Debug.Log($"User message sent: {text}");
    }

    /// <summary>
    /// Fetch messages and process only new assistant messages
    /// </summary>
    public IEnumerator FetchMessagesOnce(string convId, Action<string, bool, ChoiceOption[]> onAssistant)
    {
        var url = $"{BaseUrl}/conversations/{convId}/messages";
        using var www = UnityWebRequest.Get(url);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("x-user-key", xUserKey);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"FetchMessages error: {www.responseCode} {www.error} {www.downloadHandler.text}");
            yield break;
        }

        var json = www.downloadHandler.text;
        MessagesResponse res = null;
        try { res = JsonUtility.FromJson<MessagesResponse>(json); }
        catch (Exception e)
        {
            Debug.LogError($"[FetchMessages] Parse error: {e.Message}");
            yield break;
        }

        if (res?.messages == null || res.messages.Length == 0)
        {
            yield break;
        }

        // Diagnostic: log a compact summary of returned messages (ids & userIds)
        // This helps debug situations where the bot appears to stop responding.
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[FetchMessages] total=").Append(res.messages.Length)
              .Append(" lastProcessed=").Append(string.IsNullOrEmpty(lastProcessedMessageId)?"<none>":lastProcessedMessageId);
            for (int i = 0; i < res.messages.Length; i++)
            {
                var mm = res.messages[i];
                if (mm == null) continue;
                sb.Append(" | ").Append(i).Append(":").Append(mm.id).Append(" (u:").Append(mm.userId).Append(")");
            }
            Debug.Log(sb.ToString());
        }
        #endif

        // New logic: messages array appears to be newest-first. We want to process NEW assistant
        // messages in chronological order. We'll iterate from end (oldest) to start (newest),
        // collecting messages after lastProcessedMessageId. The first qualifying assistant
        // payload we encounter (oldest new) is delivered; others will be seen on subsequent polls.
        bool markerFound = string.IsNullOrEmpty(lastProcessedMessageId);
        for (int i = res.messages.Length - 1; i >= 0; i--)
        {
            var m = res.messages[i];
            if (m?.payload == null) continue;

            if (!markerFound)
            {
                if (m.id == lastProcessedMessageId)
                {
                    markerFound = true; // newer messages will be processed in remaining iterations
                }
                continue;
            }
            if (m.id == lastProcessedMessageId) continue; // skip already processed id if empty marker logic

            // Assistant detection heuristic
            if (string.IsNullOrEmpty(m.userId) || !m.userId.StartsWith("user_")) continue;

            var p = m.payload;
            if (p.type == "text" && !string.IsNullOrEmpty(p.text))
            {
                lastProcessedMessageId = m.id;
                Debug.Log($"[FetchMessages] Assistant TEXT: {p.text}");
                onAssistant?.Invoke(p.text, false, null);
                yield break;
            }
            if (p.type == "choice" && !string.IsNullOrEmpty(p.text))
            {
                lastProcessedMessageId = m.id;
                if (p.options != null && p.options.Length > 0)
                {
                    var optSummary = new System.Text.StringBuilder();
                    foreach (var opt in p.options)
                    {
                        if (opt == null) continue;
                        optSummary.Append("[").Append(opt.label).Append(" => ").Append(opt.value).Append("] ");
                    }
                    Debug.Log($"[FetchMessages] Assistant CHOICE: {p.text} Options: {optSummary}");
                }
                onAssistant?.Invoke(p.text, true, p.options);
                yield break;
            }
        }
    }

    /// <summary>
    /// Reset the message tracking when starting a new conversation
    /// </summary>
    public void ResetMessageTracking()
    {
        lastProcessedMessageId = "";
    }
}