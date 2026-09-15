using UnityEngine;
using TMPro;

public class TurbulenceSystem : MonoBehaviour
{
    public Rigidbody aircraftRb;

    [Header("Turbulence Intensity (0 = Clear)")]
    public float turbulenceIntensity = 0f;

    [Header("Physics Details")]
    public float positionalForce = 150000f;
    public float rotationalForce = 500000f;
    public float noiseSpeed = 0.5f;

    [Header("UI Reference")]
    public TextMeshProUGUI weatherStatusText;

    private float offsetX, offsetY, offsetZ;

    void Start()
    {
        if (aircraftRb == null) aircraftRb = GetComponent<Rigidbody>();

        offsetX = Random.Range(0f, 9999f);
        offsetY = Random.Range(0f, 9999f);
        offsetZ = Random.Range(0f, 9999f);

        UpdateWeatherText("Weather: Clear");
    }

    void FixedUpdate()
    {
        if (turbulenceIntensity <= 0f) return;

        float forwardSpeed = Vector3.Dot(aircraftRb.linearVelocity, transform.forward);
        float speedFactor = Mathf.Clamp01(forwardSpeed / 50f);

        float noiseX = Mathf.PerlinNoise(Time.time * noiseSpeed + offsetX, 0) * 2f - 1f;
        float noiseY = Mathf.PerlinNoise(0, Time.time * noiseSpeed + offsetY) * 2f - 1f;
        float noiseZ = Mathf.PerlinNoise(Time.time * noiseSpeed + offsetZ, Time.time * noiseSpeed) * 2f - 1f;

        Vector3 turbulenceForce = new Vector3(noiseX, noiseY, noiseZ) * positionalForce * turbulenceIntensity * speedFactor;
        aircraftRb.AddForce(turbulenceForce);

        Vector3 turbulenceTorque = new Vector3(noiseX, noiseY, noiseZ) * rotationalForce * turbulenceIntensity * speedFactor;
        aircraftRb.AddTorque(turbulenceTorque);
    }

    private void UpdateWeatherText(string status)
    {
        if (weatherStatusText != null)
        {
            weatherStatusText.text = status;
        }
    }

    public void SetWeatherClear()
    {
        turbulenceIntensity = 0f;
        UpdateWeatherText("Weather: Clear");
    }

    public void SetWeatherLight()
    {
        turbulenceIntensity = 0.5f;
        UpdateWeatherText("Weather: Light Turbulence");
    }

    public void SetWeatherHeavy()
    {
        turbulenceIntensity = 2.5f;
        UpdateWeatherText("Weather: Heavy Turbulence!");
    }
}