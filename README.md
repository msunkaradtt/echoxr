# EchoXR

EchoXR is an augmented reality application built with Unity, designed to deliver an interactive and immersive experience. The project leverages the Meta Quest's passthrough capabilities to blend the real and virtual worlds. It incorporates advanced features like voice-controlled interactions, AI-powered conversations, and real-time object recognition to create a truly engaging user experience.

![EchoXR Arch](https://github.com/msunkaradtt/echoxr/blob/main/images/Echoxr_arch.png)

---

## Features

- **Passthrough AR:** Utilizes the Meta Quest's cameras to display the user's surroundings, creating a foundation for mixed-reality experiences.
- **Voice-driven Conversations:** Integrates Deepgram's speech-to-text and text-to-speech services, allowing users to interact with the application using natural language.
- **AI Chatbot:** Powered by Botpress, the application can engage in intelligent and context-aware conversations with the user.
- **Object Recognition:** Employs Roboflow for real-time object detection, enabling the application to identify and react to objects in the user's environment.
- **Landmark-based Triggers:** The application can initiate conversations and interactions based on the detection of specific landmarks.

---

## Technical Stack

- **Game Engine:** Unity
- **Platform:** Meta Quest
- **Core XR Frameworks:**
    - Meta XR Core SDK
    - Meta XR Interaction SDK
    - XR Interaction Toolkit
- **External Services:**
    - **Botpress:** For conversational AI.
    - **Deepgram:** For real-time speech-to-text and text-to-speech.
    - **Roboflow:** For computer vision and object recognition.

---

## Key Scripts

The core functionality of EchoXR is driven by a set of well-defined scripts that manage everything from API interactions to conversation flow.

### `CompleteSceneSetup.cs`
This helper script is responsible for setting up all the necessary components in the scene. It ensures that all the modules are correctly configured and linked, providing a seamless and hassle-free setup process.

### `GetImagedata.cs`
This script captures images from the passthrough camera, encodes them, and sends them to the Roboflow API for object detection. It handles the entire process of capturing, preparing, and transmitting image data for analysis.

### `LandmarkDetectionBridge.cs`
This script acts as a bridge between the object detection system and the conversation manager. When a specific landmark is detected, this script triggers the `VoiceConversationManager` to initiate a conversation, creating a context-aware and interactive experience.

### `VoiceConversationManager.cs`
This is the central orchestrator of the voice interaction system. It manages the conversation flow by coordinating between Deepgram's voice services and Botpress's conversational AI. It handles the entire lifecycle of a conversation, from initiation to conclusion.

### `BotpressApi.cs`
This script serves as the interface to the Botpress API. It manages the creation of conversations, sends user messages, and fetches responses from the chatbot, ensuring smooth communication between the user and the AI.

### `DeepgramWebSocketHandler.cs`
This script handles the real-time communication with Deepgram's WebSocket servers. It manages the streaming of audio data for speech-to-text and receives the synthesised speech for text-to-speech, enabling a responsive and natural voice interaction experience.

---

## Passthrough Camera Samples

The project also includes a comprehensive set of samples from the Passthrough Camera API, showcasing a wide range of capabilities:

- **Brightness Estimation:** Demonstrates how to estimate the brightness of the user's surroundings, which can be used to adjust lighting in the virtual environment.
- **Camera to World Mapping:** Provides examples of how to map coordinates from the camera's perspective to the world space, which is essential for placing virtual objects in the real world.
- **Camera Viewer:** A simple tool for viewing the passthrough camera feed directly, useful for debugging and testing.
- **Shader Samples:** Includes various shader examples that can be applied to the passthrough feed to create interesting visual effects.
- **Start Scene:** A pre-built start scene with a user interface for navigating through the different samples and features of the application.

These samples serve as a great resource for developers looking to explore the full potential of the Passthrough API and build their own immersive AR experiences.

---

## External Services Integration

EchoXR seamlessly integrates with three powerful external services to deliver its advanced features:

### Botpress
Botpress is a flexible and developer-friendly platform for building conversational AI. In EchoXR, it powers the chatbot's ability to understand and respond to user queries naturally and engagingly.

### Deepgram
Deepgram provides fast and accurate speech-to-text and text-to-speech services. This allows users to interact with the application using their voice, making the experience more intuitive and hands-free.

### Roboflow
Roboflow is a powerful platform for building and deploying computer vision models. EchoXR uses it to detect objects and landmarks in the user's environment, triggering context-aware interactions and conversations.

By combining the strengths of these services, EchoXR can create a rich and interactive augmented reality experience that pushes the boundaries of what's possible with the Meta Quest platform.
