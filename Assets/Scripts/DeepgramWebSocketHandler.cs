using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using NativeWebSocket;

/// <summary>
/// Enhanced Deepgram WebSocket handler with proper turn management and echo cancellation
/// </summary>
public class DeepgramWebSocketHandler : MonoBehaviour
{
    [Header("Deepgram Configuration")]
    public string apiKey = "acce28246b2e53330182e6af5805c24b92b6bbc6";
    
    [Header("Audio Configuration")]
    public int sampleRate = 16000;
    public int microphoneBufferSeconds = 10; // Increased buffer
    
    [Header("Voice Activity Detection")]
    [Tooltip("Silence duration before Deepgram ends speech (ms)")]
    public int vadSilenceMs = 1000;

    [Header("STT Keyword Boosting")]
    [Tooltip("Optional phrases to boost recognition accuracy (Deepgram keywords param)")]
    public string[] sttKeywords = new string[] { "30 second story", "thirty second story", "photo angle", "photo", "story" };
    
    // WebSocket connections
    private WebSocket sttWebSocket;
    private WebSocket ttsWebSocket;
    
    // Audio handling
    private AudioClip microphoneClip;
    private int lastMicPosition = 0;
    private bool isRecording = false;
    private bool shouldListen = false;
    
    // TTS audio queue
    private Queue<float[]> ttsAudioQueue = new Queue<float[]>();
    private AudioSource audioSource;
    private bool isPlayingTTS = false;
    
    // Turn management
    private bool isBotSpeaking = false;
    private bool isUserSpeaking = false; // set true when we detect first non-empty transcript chunk until finalization
    
    // Connection management
    private bool sttConnected = false;
    private Coroutine keepAliveCoroutine;
    
    // Events
    public event Action<string> OnTranscriptionReceived;
    public event Action<string> OnFinalTranscript;
    public event Action<float[]> OnTTSAudioReceived;
    public event Action OnSTTConnected;
    public event Action OnTTSConnected;
    public event Action OnSTTDisconnected;
    public event Action OnTTSDisconnected;
    public event Action OnSpeechStarted;
    public event Action OnSpeechEnded;
    
    private Coroutine ttsPlaybackCoroutine;
    
    void Awake()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.volume = 1.0f;
    }
    
    void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
        if (sttWebSocket != null && sttWebSocket.State == WebSocketState.Open)
            sttWebSocket.DispatchMessageQueue();
            
        if (ttsWebSocket != null && ttsWebSocket.State == WebSocketState.Open)
            ttsWebSocket.DispatchMessageQueue();
        #endif
        
        // Send microphone data continuously when conditions are met
        if (isRecording && shouldListen && !isBotSpeaking && sttConnected)
        {
            SendMicrophoneData();
        }
    }
    
    public async void ConnectSTT()
    {
        try
        {
            // Simpler URL without VAD events to avoid server errors
            var sbUrl = new System.Text.StringBuilder();
            sbUrl.Append("wss://api.deepgram.com/v1/listen?")
                .Append("encoding=linear16&")
                .Append($"sample_rate={sampleRate}&")
                .Append("channels=1&")
                .Append("model=nova-2&")
                .Append("language=en&")
                .Append("punctuate=true&")
                .Append("smart_format=true&")
                .Append("interim_results=false&") // Only final results
                .Append($"endpointing={vadSilenceMs}"); // Auto end detection
            if (sttKeywords != null && sttKeywords.Length > 0)
            {
                foreach (var kw in sttKeywords)
                {
                    if (string.IsNullOrWhiteSpace(kw)) continue;
                    string enc = Uri.EscapeDataString(kw.Trim());
                    sbUrl.Append("&keywords=").Append(enc);
                }
            }
            string sttUrl = sbUrl.ToString();
            
            var headers = new Dictionary<string, string>
            {
                { "Authorization", $"Token {apiKey}" }
            };
            
            sttWebSocket = new WebSocket(sttUrl, headers);
            
            sttWebSocket.OnOpen += () =>
            {
                Debug.Log("STT WebSocket connected");
                sttConnected = true;
                OnSTTConnected?.Invoke();
                
                // Start keep-alive
                if (keepAliveCoroutine != null) StopCoroutine(keepAliveCoroutine);
                keepAliveCoroutine = StartCoroutine(SendKeepAlive());
                
                // Start recording if not already
                if (!isRecording)
                {
                    StartRecording();
                }
                
                shouldListen = false; // Don't listen until explicitly told
            };
            
            sttWebSocket.OnMessage += (bytes) =>
            {
                string json = Encoding.UTF8.GetString(bytes);
                ProcessSTTResponse(json);
            };
            
            sttWebSocket.OnError += (error) =>
            {
                Debug.LogError($"STT WebSocket error: {error}");
                sttConnected = false;
            };
            
            sttWebSocket.OnClose += (code) =>
            {
                Debug.Log($"STT WebSocket closed: {code}");
                sttConnected = false;
                OnSTTDisconnected?.Invoke();
                
                if (keepAliveCoroutine != null)
                {
                    StopCoroutine(keepAliveCoroutine);
                    keepAliveCoroutine = null;
                }
                
                // Try to reconnect if conversation is active
                if (shouldListen && code != WebSocketCloseCode.Normal)
                {
                    Debug.Log("Attempting to reconnect STT...");
                    StartCoroutine(ReconnectSTT());
                }
            };
            
            await sttWebSocket.Connect();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to connect STT: {e.Message}");
            sttConnected = false;
        }
    }
    
    IEnumerator ReconnectSTT()
    {
        yield return new WaitForSeconds(1f);
        if (!sttConnected)
        {
            ConnectSTT();
        }
    }
    
    IEnumerator SendKeepAlive()
    {
        while (sttConnected && sttWebSocket != null)
        {
            yield return new WaitForSeconds(5f);
            
            if (sttWebSocket != null && sttWebSocket.State == WebSocketState.Open)
            {
                try
                {
                    // Send a keep-alive message (JSON)
                    string keepAlive = "{\"type\":\"KeepAlive\"}";
                    sttWebSocket.SendText(keepAlive);
                }
                catch
                {
                    // Ignore keep-alive errors
                }
            }
        }
    }
    
    public async void ConnectTTS()
    {
        try
        {
            string ttsUrl = "wss://api.deepgram.com/v1/speak?" +
                "encoding=linear16&" +
                "sample_rate=16000&" +
                "container=none&" +
                "model=aura-asteria-en";
            
            var headers = new Dictionary<string, string>
            {
                { "Authorization", $"Token {apiKey}" }
            };
            
            ttsWebSocket = new WebSocket(ttsUrl, headers);
            
            ttsWebSocket.OnOpen += () =>
            {
                Debug.Log("TTS WebSocket connected");
                OnTTSConnected?.Invoke();
            };
            
            ttsWebSocket.OnMessage += (bytes) =>
            {
                ProcessTTSResponse(bytes);
            };
            
            ttsWebSocket.OnError += (error) =>
            {
                Debug.LogError($"TTS WebSocket error: {error}");
            };
            
            ttsWebSocket.OnClose += (code) =>
            {
                Debug.Log($"TTS WebSocket closed: {code}");
                OnTTSDisconnected?.Invoke();
            };
            
            await ttsWebSocket.Connect();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to connect TTS: {e.Message}");
        }
    }
    
    public void StartListening()
    {
        Debug.Log("StartListening called");
        
        // Ensure STT is connected
        if (!sttConnected || sttWebSocket == null || sttWebSocket.State != WebSocketState.Open)
        {
            Debug.Log("STT not connected, reconnecting...");
            ConnectSTT();
            StartCoroutine(DelayedStartListening());
            return;
        }
        
        // Ensure recording is active
        if (!isRecording)
        {
            StartRecording();
        }
        
        shouldListen = true;
        isUserSpeaking = false;
        lastMicPosition = Microphone.GetPosition(null); // Reset position
        Debug.Log("Now actively listening for user input");
    }
    
    IEnumerator DelayedStartListening()
    {
        yield return new WaitForSeconds(1f);
        if (sttConnected)
        {
            StartListening();
        }
    }
    
    public void StopListening()
    {
        shouldListen = false;
        isUserSpeaking = false;
        Debug.Log("Stopped listening for user input");
    }
    
    void StartRecording()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone detected!");
            return;
        }
        
        string micDevice = Microphone.devices[0];
        
        // Stop any existing recording
        if (Microphone.IsRecording(micDevice))
        {
            Microphone.End(micDevice);
        }
        
        microphoneClip = Microphone.Start(micDevice, true, microphoneBufferSeconds, sampleRate);
        
        // Wait until recording actually starts
        while (!(Microphone.GetPosition(micDevice) > 0)) { }
        
        isRecording = true;
        lastMicPosition = 0;
        
        Debug.Log($"Started recording from: {micDevice}");
    }
    
    void StopRecording()
    {
        if (isRecording)
        {
            Microphone.End(null);
            isRecording = false;
            Debug.Log("Stopped recording");
        }
    }
    
    void SendMicrophoneData()
    {
        if (microphoneClip == null || !shouldListen || !sttConnected) return;
        
        if (sttWebSocket == null || sttWebSocket.State != WebSocketState.Open)
        {
            Debug.LogWarning("STT WebSocket not open, cannot send audio");
            return;
        }
        
        int currentPosition = Microphone.GetPosition(null);
        
        // Handle wraparound
        if (currentPosition < lastMicPosition)
        {
            // Wrapped around, read to end first
            int samplesToEnd = microphoneClip.samples - lastMicPosition;
            if (samplesToEnd > 0)
            {
                SendAudioChunk(lastMicPosition, samplesToEnd);
            }
            lastMicPosition = 0;
        }
        
        // Read from last position to current
        int samplesToRead = currentPosition - lastMicPosition;
        if (samplesToRead > 0)
        {
            SendAudioChunk(lastMicPosition, samplesToRead);
            lastMicPosition = currentPosition;
        }
    }
    
    void SendAudioChunk(int startPosition, int sampleCount)
    {
        float[] samples = new float[sampleCount];
        microphoneClip.GetData(samples, startPosition);
        
        // Convert to 16-bit PCM
        byte[] pcmData = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short pcmValue = (short)(Mathf.Clamp(samples[i] * 32767f, -32768f, 32767f));
            BitConverter.GetBytes(pcmValue).CopyTo(pcmData, i * 2);
        }
        
        try
        {
            if (sttWebSocket != null && sttWebSocket.State == WebSocketState.Open)
            {
                sttWebSocket.Send(pcmData);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error sending audio: {e.Message}");
        }
    }
    
    void ProcessSTTResponse(string json)
    {
        try
        {
            // Check for transcript
            if (json.Contains("\"transcript\":"))
            {
                string transcript = ExtractTranscript(json);
                if (!string.IsNullOrEmpty(transcript) && shouldListen)
                {
                    Debug.Log($"Transcript received: {transcript}");
                    if (!isUserSpeaking)
                    {
                        isUserSpeaking = true;
                        OnSpeechStarted?.Invoke();
                        Debug.Log("User speech started (heuristic)");
                    }
                    
                    // Since we're using endpointing, each transcript is complete
                    isUserSpeaking = false;
                    shouldListen = false; // Stop listening after receiving transcript
                    
                    OnSpeechEnded?.Invoke();
                    OnFinalTranscript?.Invoke(transcript);
                    OnTranscriptionReceived?.Invoke(transcript);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error processing STT response: {e.Message}");
        }
    }
    
    string ExtractTranscript(string json)
    {
        int start = json.IndexOf("\"transcript\":\"") + 14;
        if (start < 14) return "";
        
        int end = json.IndexOf("\"", start);
        if (end > start)
        {
            return json.Substring(start, end - start);
        }
        return "";
    }
    
    public async void SendTextToTTS(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        
        if (ttsWebSocket == null || ttsWebSocket.State != WebSocketState.Open)
        {
            Debug.LogWarning("TTS WebSocket not connected");
            return;
        }
        
        try
        {
            // Mark bot as speaking
            isBotSpeaking = true;
            shouldListen = false;
            
            // Stop any current playback
            StopTTSPlayback();
            
            string jsonMessage = $"{{\"type\":\"Speak\",\"text\":\"{EscapeJson(text)}\"}}";
            await ttsWebSocket.SendText(jsonMessage);
            Debug.Log($"Sent to TTS: {text}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to send TTS: {e.Message}");
            isBotSpeaking = false;
        }
    }
    
    void ProcessTTSResponse(byte[] audioData)
    {
        try
        {
            // Check if this is metadata
            if (audioData.Length < 100)
            {
                string possibleJson = Encoding.UTF8.GetString(audioData);
                if (possibleJson.Contains("\"type\":"))
                {
                    if (possibleJson.Contains("Flushed"))
                    {
                        Debug.Log("TTS audio complete (metadata Flushed)");
                        StartCoroutine(WaitForTTSCompletion());
                    }
                    return;
                }
            }
            
            // Convert 16-bit PCM to float samples
            float[] samples = new float[audioData.Length / 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short pcmValue = BitConverter.ToInt16(audioData, i * 2);
                samples[i] = pcmValue / 32768f;
            }
            
            ttsAudioQueue.Enqueue(samples);
            Debug.Log($"TTS audio chunk enqueued: {samples.Length} samples (queue={ttsAudioQueue.Count})");
            OnTTSAudioReceived?.Invoke(samples);
            
            // START playback if not already active
            if (!isPlayingTTS)
            {
                if (ttsPlaybackCoroutine != null) StopCoroutine(ttsPlaybackCoroutine);
                ttsPlaybackCoroutine = StartCoroutine(PlayTTSAudio());
            }
        }
        catch
        {
            // Ignore parsing errors (likely metadata)
        }
    }
    
    IEnumerator PlayTTSAudio()
    {
        isPlayingTTS = true;
        isBotSpeaking = true;
        
        while (ttsAudioQueue.Count > 0)
        {
            float[] samples = ttsAudioQueue.Dequeue();
            
            AudioClip clip = AudioClip.Create("TTS", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            
            audioSource.clip = clip;
            audioSource.Play();
            
            yield return new WaitWhile(() => audioSource.isPlaying);
        }
        
        isPlayingTTS = false;
        
        // Bot finished speaking
        if (isBotSpeaking)
        {
            isBotSpeaking = false;
            Debug.Log("TTS playback complete, starting to listen");
            StartListening();
        }
        
        ttsPlaybackCoroutine = null; // clear handle
    }
    
    IEnumerator WaitForTTSCompletion()
    {
        // Wait for queue to empty and playback to finish
        yield return new WaitWhile(() => isPlayingTTS || ttsAudioQueue.Count > 0);
        yield return new WaitForSeconds(0.2f);
        
        isBotSpeaking = false;
        
        // Start listening after bot finishes
        Debug.Log("Bot finished speaking, enabling user input");
        StartListening();
    }
    
    public void StopTTSPlayback()
    {
        if (ttsPlaybackCoroutine != null)
        {
            StopCoroutine(ttsPlaybackCoroutine);
            ttsPlaybackCoroutine = null;
        }
        audioSource.Stop();
        ttsAudioQueue.Clear();
        isPlayingTTS = false;
        isBotSpeaking = false;
    }
    
    public async void DisconnectSTT()
    {
        sttConnected = false;
        shouldListen = false;
        StopRecording();
        
        if (keepAliveCoroutine != null)
        {
            StopCoroutine(keepAliveCoroutine);
            keepAliveCoroutine = null;
        }
        
        if (sttWebSocket != null && sttWebSocket.State == WebSocketState.Open)
        {
            await sttWebSocket.Close();
        }
        sttWebSocket = null;
    }
    
    public async void DisconnectTTS()
    {
        StopTTSPlayback();
        if (ttsWebSocket != null && ttsWebSocket.State == WebSocketState.Open)
        {
            await ttsWebSocket.Close();
        }
        ttsWebSocket = null;
    }
    
    public void DisconnectAll()
    {
        DisconnectSTT();
        DisconnectTTS();
    }
    
    string EscapeJson(string text)
    {
        return text.Replace("\\", "\\\\")
                   .Replace("\"", "\\\"")
                   .Replace("\n", "\\n")
                   .Replace("\r", "\\r")
                   .Replace("\t", "\\t");
    }
    
    void OnDestroy()
    {
        DisconnectAll();
    }
    
    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            DisconnectAll();
        }
    }
}