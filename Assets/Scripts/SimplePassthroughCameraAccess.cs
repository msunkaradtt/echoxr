using UnityEngine;
using PassthroughCameraSamples;

using TMPro;
using UnityEngine.UI;
using System.Collections;

public class SimplePassthroughCameraAccess : MonoBehaviour
{
    [SerializeField] private WebCamTextureManager webCamTextureManager;
    [SerializeField] private RawImage webCamImage;


    private IEnumerator Start()
    {
        while (webCamTextureManager.WebCamTexture == null)
        {
            yield return null;
        }

        webCamImage.texture = webCamTextureManager.WebCamTexture;

        var cameraEye = webCamTextureManager.Eye;

        var cameraDetails = PassthroughCameraUtils.GetCameraIntrinsics(cameraEye);
    }

}
