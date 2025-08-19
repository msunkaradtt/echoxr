using System.Collections;
using System.Collections.Generic;
using PassthroughCameraSamples;
using UnityEngine;
// Required for sending web requests
using UnityEngine.Networking;
// Required for JSON serialization helper classes
using System;
using TMPro;
using OVRSimpleJSON;
using Meta.XR.ImmersiveDebugger.UserInterface.Generic;

public class GetImagedata : MonoBehaviour
{
    // --- Corrected helper classes for parsing the Roboflow JSON response ---

    // The main class that represents the entire JSON object.
    // It contains the "outputs" array.
    [System.Serializable]
    public class DetectionResult
    {
        public OutputData[] outputs;
    }

    // Corresponds to each object inside the "outputs" array.
    [System.Serializable]
    public class OutputData
    {
        public int count_objects;
        public string output_image;
        public PredictionsData predictions;
    }

    // Corresponds to the nested "predictions" object.
    [System.Serializable]
    public class PredictionsData
    {
        public ImageDetails image;
        public Prediction[] predictions;
    }

    // Corresponds to the "image" object inside "predictions".
    [System.Serializable]
    public class ImageDetails
    {
        public int width;
        public int height;
    }

    // Corresponds to each object inside the nested "predictions" array.
    [System.Serializable]
    public class Prediction
    {
        public int width;
        public int height;
        public float x;
        public float y;
        public float confidence;
        public int class_id;
        // Use '@class' because 'class' is a reserved keyword in C#.
        public string @class;
        public string detection_id;
        public string parent_id;
    }

    /// <summary>
    /// SOMETHING SOMETHING
    /// </summary>
    public WebCamTextureManager webcamManager;
    public GameObject targetUIElement;
    public GameObject targetUIElement2;
    public GameObject Panel1;
    private Texture2D picture;
    public TextMeshProUGUI jsonDisplay;
    public TextMeshProUGUI jsonDisplay2;
    public GameObject anything;
    // Helper classes to structure the JSON payload for Roboflow
    // This makes the code cleaner and less prone to formatting errors.
    [Serializable]
    private class RoboflowRequest
    {
        public string api_key;
        public RoboflowInputs inputs;
    }

    [Serializable]
    private class RoboflowInputs
    {
        public RoboflowImage image;
    }

    [Serializable]
    private class RoboflowImage
    {
        public string type;
        public string value;
    }

    // Update is called once per frame
    void Update()
    {
        // Ensure the webcam texture is active and has been updated
        if (webcamManager.WebCamTexture != null && webcamManager.WebCamTexture.didUpdateThisFrame)
        {
            if (OVRInput.GetDown(OVRInput.Button.One))
            {
                // Start the coroutine to handle the web request
                StartCoroutine(TakePictureAndSend());
                targetUIElement.SetActive(!targetUIElement.activeSelf);
            }
        }
    }

    /// <summary>
    /// Captures a picture from the webcam, encodes it, and sends it to the Roboflow API.
    /// This is a Coroutine to handle the network request asynchronously.
    /// </summary>
    IEnumerator TakePictureAndSend()
    {
        // --- 1. CAPTURE IMAGE FROM WEBCAM ---
        int width = webcamManager.WebCamTexture.width;
        int height = webcamManager.WebCamTexture.height;

        if (picture == null)
        {
            picture = new Texture2D(width, height);
        }

        // Wait for the end of the frame to ensure the texture is fully rendered
        yield return new WaitForEndOfFrame();

        // Copy pixels from the webcam feed to our texture
        Color32[] pixels = webcamManager.WebCamTexture.GetPixels32();
        picture.SetPixels32(pixels);
        picture.Apply();

        // Encode the texture to a PNG byte array
        byte[] bytes = picture.EncodeToPNG();

        // --- 2. PREPARE DATA FOR ROBOFLOW API ---

        // Convert the image bytes to a Base64 string for the JSON payload
        string base64Image = Convert.ToBase64String(bytes);

        // Your Roboflow endpoint and API key
        string url = "https://serverless.roboflow.com/infer/workflows/xr-cologne/detect-count-and-visualize";
        string apiKey = "WwuxVYOPzaLgSYScB9ty";

        // Create the request object with your data
        RoboflowRequest requestData = new RoboflowRequest
        {
            api_key = apiKey,
            inputs = new RoboflowInputs
            {
                image = new RoboflowImage
                {
                    type = "base64",
                    value = base64Image
                }
            }
        };

        // Serialize the C# object into a JSON string
        string jsonPayload = JsonUtility.ToJson(requestData);

        // --- 3. SEND THE WEB REQUEST ---
        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            // Convert the JSON string to a byte array
            byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(jsonPayload);
            www.uploadHandler = new UploadHandlerRaw(jsonToSend);
            www.downloadHandler = new DownloadHandlerBuffer();

            // Set the content type header
            www.SetRequestHeader("Content-Type", "application/json");

            Debug.Log("Sending image to Roboflow...");

            // Send the request and wait for a response
            yield return www.SendWebRequest();

            // --- 4. HANDLE THE RESPONSE ---
            if (www.result != UnityWebRequest.Result.Success)
            {
                // Log any errors
                Debug.LogError("Error sending image to Roboflow: " + www.error);
                Debug.LogError("Response Body: " + www.downloadHandler.text);
                targetUIElement.SetActive(false);
                targetUIElement2.SetActive(targetUIElement2.activeSelf);
            }
            else
            {
                // Log the successful response from the server
                targetUIElement2.SetActive(false);
                // Debug.Log("Successfully sent image!");
                //Debug.Log("Roboflow Response: " + www.downloadHandler.text);
                string jsonResponse = www.downloadHandler.text;
                Debug.Log("Roboflow Response: " + jsonResponse);
                if (jsonDisplay != null)
                {
                    
                    targetUIElement2.SetActive(false);
                    //jsonDisplay.enabled = true;
                    if (Panel1 != null)
                    {
                        Panel1.SetActive(false);
                    }
                    jsonDisplay.text = jsonResponse;

                    // Deserialize the JSON string into our root C# object.
                    DetectionResult result = JsonUtility.FromJson<DetectionResult>(jsonResponse);

                    // Now you can access the data by iterating through the arrays.
                    if (result.outputs.Length > 0)
                    {
                        // Get the first output object.
                        OutputData firstOutput = result.outputs[0];

                        if (firstOutput.predictions.predictions.Length > 0)
                        {
                            // Get the first prediction from that output.
                            Prediction firstPrediction = firstOutput.predictions.predictions[0];
                            jsonDisplay.enabled = false;
                            Debug.Log("Detected Object: " + firstPrediction.@class);
                            Debug.Log("Confidence: " + firstPrediction.confidence);
                            Debug.Log("Object located in image at (x, y): (" + firstPrediction.x + ", " + firstPrediction.y + ")");
                            //jsonDisplay2.enabled = true;
                            jsonDisplay2.text = firstPrediction.@class.ToString();

                            var general = firstPrediction.@class.ToString();
                            var bot = anything.GetComponent<CompleteSceneSetup>();
                            bot.OnImageDetected(general);
                        }
                    }

                }
                
            }
        }
    }
}