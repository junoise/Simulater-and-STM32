using CesiumForUnity;
using TMPro;
using Unity.Mathematics;
using UnityEngine;

public class FlightTelemetryUI : MonoBehaviour
{
    [Header("References")]
    public Rigidbody airlinerRb;
    public Transform airlinerTransform;
    public TextMeshProUGUI telemetryText;

    [Header("Cesium Integration")]
    public CesiumGlobeAnchor cesiumGlobeAnchor;

    void Update()
    {
        if (airlinerRb == null || telemetryText == null) return;

        float speedKmh = airlinerRb.linearVelocity.magnitude * 3.6f;
        float heading = Mathf.Round(airlinerTransform.eulerAngles.y);
        if (heading >= 360f) heading = 0f;

        AirlinerPhysics physics = airlinerRb.GetComponent<AirlinerPhysics>();
        float currentThrottle = physics != null ? physics.throttle * 100f : 0f;

        double latitude = 0.0;
        double longitude = 0.0;
        double height = 0.0;

        if (cesiumGlobeAnchor != null)
        {
            double3 lonLatHeight = cesiumGlobeAnchor.longitudeLatitudeHeight;

            longitude = lonLatHeight.x;
            latitude = lonLatHeight.y;
            height = lonLatHeight.z;
        }
        else
        {
            height = airlinerTransform.position.y;
        }

        string telemetry = "<b>=== FLIGHT TELEMETRY ===</b>\n" +
                           $"* Speed: {speedKmh:F1} km/h\n" +
                           $"* Altitude: {height:F1} m\n" +
                           $"* Heading: {heading:F0}°\n" +
                           $"* Throttle: {currentThrottle:F0}%\n" +
                           $"----------------------------------\n" +
                           $"* Lat : {latitude:F6}°\n" +
                           $"* Lon : {longitude:F6}°";

        telemetryText.text = telemetry;
    }
}