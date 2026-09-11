using TMPro;
using UnityEngine;

public class LandingTelemetryUI : MonoBehaviour
{
    public LandingSystem landing;
    public AirlinerPhysics flight;
    public TextMeshProUGUI landingText;

    private CesiumGroundStart groundStart;

    private void Start()
    {
        if (landing != null)
        {
            groundStart = landing.GetComponent<CesiumGroundStart>();
        }
    }

    private string ContactText(bool grounded)
    {
        return grounded ? "GROUND" : "AIR";
    }

    private string ResultText()
    {
        if (landing.BodyCollision)
        {
            return "FAILED: BODY COLLISION";
        }

        if (landing.OffRunway)
        {
            return "FAILED: OFF RUNWAY";
        }

        if (landing.TouchdownCount == 0)
        {
            return landing.NoseFirst
                ? "WARNING: NOSE GEAR FIRST"
                : "WAITING FOR TOUCHDOWN";
        }

        string result = landing.IsHardLanding
            ? "HARD LANDING"
            : "NORMAL TOUCHDOWN";

        if (landing.NoseFirst)
        {
            result += " / NOSE FIRST";
        }

        return result;
    }

    private void Update()
    {
        if (landing == null || flight == null || landingText == null)
        {
            return;
        }

        if (groundStart != null &&
            groundStart.enabled &&
            groundStart.IsWaiting)
        {
            landingText.color = Color.yellow;
            landingText.text =
                "<b>=== GROUND START ===</b>\n" +
                groundStart.Status +
                "\nAircraft is held until terrain is ready.";

            return;
        }

        if (!landing.enabled)
        {
            landingText.text = "LANDING SYSTEM: CHECK INSPECTOR";
            landingText.color = Color.red;
            return;
        }

        string height = landing.HasGroundReading
            ? $"{landing.SensorHeight:F1} m"
            : "N/A";

        string sinkRate = landing.TouchdownCount > 0
            ? $"{landing.WorstTouchdownSinkRate:F2} m/s"
            : "--";

        string bank = landing.TouchdownCount > 0
            ? $"{landing.WorstTouchdownBank:F1} deg"
            : "--";

        string phase = landing.Phase == LandingPhase.Stopped
            ? "STOPPED / LANDING COMPLETE"
            : landing.Phase.ToString().ToUpperInvariant();

        landingText.text =
            "<b>=== LANDING ===</b>\n" +
            $"State: {phase}\n" +
            $"Sensor height: {height}\n" +
            $"Vertical speed: {landing.VerticalSpeed:F2} m/s\n" +
            $"Ground speed: {landing.GroundSpeed:F1} m/s\n" +
            $"Bank: {landing.BankAngle:F1} deg\n" +
            $"Nose: {ContactText(landing.NoseGrounded)}\n" +
            $"Left: {ContactText(landing.LeftGrounded)}  " +
            $"Right: {ContactText(landing.RightGrounded)}\n" +
            $"Brake: {flight.BrakeInput * 100f:F0}%\n" +
            $"Touchdowns: {landing.TouchdownCount}\n" +
            $"Worst TD sink (estimate): {sinkRate}\n" +
            $"Worst TD bank: {bank}\n" +
            ResultText();

        if (landing.Phase == LandingPhase.Failed)
        {
            landingText.color = Color.red;
        }
        else if (landing.IsHardLanding || landing.NoseFirst)
        {
            landingText.color = new Color(1f, 0.7f, 0.2f);
        }
        else if (landing.Phase == LandingPhase.Stopped)
        {
            landingText.color = Color.green;
        }
        else
        {
            landingText.color = Color.white;
        }
    }
}