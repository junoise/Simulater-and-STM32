using UnityEngine;
using TMPro;
using UnityEngine.Rendering; 
using UnityEngine.Rendering.Universal; 

public class VisualWeatherSystem : MonoBehaviour
{
    [Header("Rain Effect")]
    public ParticleSystem rainParticles;

    [Header("UI Reference")]
    public TextMeshProUGUI weatherStatusText;

    [Header("Darkness Effect (Post-processing)")]
    public Volume globalVolume; 
    private ColorAdjustments colorAdjustments;

    private Camera mainCam;

    void Start()
    {
        mainCam = Camera.main;
        if (globalVolume != null && globalVolume.profile.TryGet(out colorAdjustments))
        {
            colorAdjustments.active = false;
        }

        SetWeatherClear();
    }

    public void SetWeatherClear()
    {
        if (rainParticles != null) rainParticles.Stop();

        if (mainCam != null)
        {
            mainCam.clearFlags = CameraClearFlags.Skybox;
        }

        if (colorAdjustments != null)
        {
            colorAdjustments.active = false;
        }

        if (weatherStatusText != null) weatherStatusText.text = "Weather: Visual Clear";
    }

    public void SetWeatherRain()
    {
        if (rainParticles != null) rainParticles.Play();

        if (mainCam != null)
        {
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0.6f, 0.6f, 0.6f);
        }

        if (colorAdjustments != null)
        {
            colorAdjustments.active = true;
            colorAdjustments.postExposure.value = -1.2f; 
        }

        if (weatherStatusText != null) weatherStatusText.text = "Weather: Rain & Dark";
    }
}