using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

public class AutopilotTestUI : MonoBehaviour
{
    public AircraftAutopilot autopilot;
    public MissionInterface interfaceLink;

    [FormerlySerializedAs("headingInput")]
    public TMP_InputField destinationLatitudeInput;

    [FormerlySerializedAs("altitudeInput")]
    public TMP_InputField destinationLongitudeInput;

    [FormerlySerializedAs("speedInput")]
    public TMP_InputField destinationAltitudeInput;

    public TextMeshProUGUI statusText;

    [Header("Destination Helper")]
    public float aheadDistance = 9000f;

    private MockMissionComputer mock;
    private string message = "";
    private float messageUntil;

    private bool MockActive =>
        mock != null && mock.isActiveAndEnabled;

    private void Start()
    {
        if (autopilot == null ||
            interfaceLink == null ||
            destinationLatitudeInput == null ||
            destinationLongitudeInput == null ||
            destinationAltitudeInput == null ||
            statusText == null)
        {
            Debug.LogError(
                "AutopilotTestUI: Assign interface, AP, fields and text.",
                this
            );

            enabled = false;
            return;
        }

        mock = interfaceLink.GetComponent<MockMissionComputer>();
    }

    private void SetMessage(string text)
    {
        message = text;
        messageUntil = Time.unscaledTime + 6f;
    }

    public void FillAheadDestination()
    {
        if (!interfaceLink.HasAircraftState)
        {
            SetMessage("Aircraft state is not ready.");
            return;
        }

        float distance = Mathf.Clamp(aheadDistance, 6000f, 30000f);

        MissionWaypoint point = MissionInterfaceRules.PointAhead(
            interfaceLink.CurrentState,
            distance
        );

        destinationLatitudeInput.SetTextWithoutNotify(
            point.waypoint_latitude.ToString(
                "F6",
                CultureInfo.InvariantCulture
            )
        );

        destinationLongitudeInput.SetTextWithoutNotify(
            point.waypoint_longitude.ToString(
                "F6",
                CultureInfo.InvariantCulture
            )
        );

        destinationAltitudeInput.SetTextWithoutNotify(
            point.waypoint_altitude.ToString(
                "F1",
                CultureInfo.InvariantCulture
            )
        );

        SetMessage(
            $"Final destination filled {distance / 1000f:F1} km ahead."
        );
    }

    public void ApplyTargets()
    {
        bool latitudeOK = float.TryParse(
            destinationLatitudeInput.text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float latitude
        );

        bool longitudeOK = float.TryParse(
            destinationLongitudeInput.text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float longitude
        );

        bool altitudeOK = float.TryParse(
            destinationAltitudeInput.text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float altitude
        );

        if (!latitudeOK || !longitudeOK || !altitudeOK)
        {
            SetMessage("Enter destination latitude, longitude and altitude.");
            return;
        }

        if (mock == null)
            mock = interfaceLink.GetComponent<MockMissionComputer>();

        if (MockActive &&
            !mock.ValidateDestination(
                latitude,
                longitude,
                altitude,
                out string validationMessage))
        {
            SetMessage(validationMessage);
            return;
        }

        interfaceLink.StartMission(
            latitude,
            longitude,
            altitude,
            out string result
        );

        SetMessage(result);
    }

    public void EngageAP()
    {
        interfaceLink.EngageAP(out string result);
        SetMessage(result);
    }

    public void DisengageAP()
    {
        interfaceLink.DisengageAP();
        SetMessage("Manual control. Mission exchange continues.");
    }

    private void Update()
    {
        if (!interfaceLink.HasAircraftState)
        {
            statusText.color = Color.white;
            statusText.text = "AP OFF\nWaiting for aircraft state...";
            return;
        }

        UnityAircraftState state = interfaceLink.CurrentState;
        MissionCommand received = interfaceLink.LastReceived;

        bool hasCommand = interfaceLink.HasReceivedCommand;

        bool guidanceReady = interfaceLink.TryGetGuidance(
            out MissionCommand guidance,
            out string guidanceReason
        );

        string mission = hasCommand
            ? ((MissionStateCode)received.mission_state).ToString()
            : "NO S2U";

        string data = hasCommand
            ? ((DataStatusCode)received.data_status).ToString()
            : "UNKNOWN";

        bool complete =
            hasCommand &&
            received.mission_state ==
                (byte)MissionStateCode.MISSION_COMPLETE;

        string targetAltitude = "--";
        string targetSpeed = "--";
        string targetHeading = "--";

        if (autopilot.IsEngaged)
        {
            targetAltitude = $"{autopilot.TargetAltitude:F1}";
            targetSpeed = $"{autopilot.TargetSpeed:F1}";
            targetHeading = $"{autopilot.TargetHeading:F1}";
        }
        else if (guidanceReady)
        {
            targetAltitude = $"{guidance.target_altitude:F1}";
            targetSpeed = $"{guidance.target_speed:F1}";
            targetHeading = $"{guidance.target_heading:F1}";
        }

        var waypoints = interfaceLink.Waypoints;

        string waypointLine = "WP --";
        string coordinateLine = "TARGET POS --";

        if (hasCommand &&
            received.current_waypoint_index < waypoints.Count)
        {
            int index = received.current_waypoint_index;
            MissionWaypoint target = waypoints[index];

            double distance = MissionInterfaceRules.DistanceMeters(
                state.current_latitude,
                state.current_longitude,
                target.waypoint_latitude,
                target.waypoint_longitude
            );

            waypointLine = complete
                ? $"WP {index + 1}/{waypoints.Count} / COMPLETE"
                : $"WP {index + 1}/{waypoints.Count} / DIST {distance:F0} m";

            coordinateLine =
                $"TARGET POS {target.waypoint_latitude:F5}, " +
                $"{target.waypoint_longitude:F5}";
        }

        string trackLine = "";
        string localLines = "";

        if (MockActive)
        {
            trackLine = mock.TrackAvailable
                ? $"TRK {mock.GroundTrack:F1} deg / " +
                  $"DRIFT {mock.HeadingDrift:+0.0;-0.0;0.0}\n"
                : "TRK -- / ESTIMATING\n";

            string progressLine = complete
                ? "ROUTE COMPLETE\n"
                : $"XTE {mock.CrossTrackError:+0;-0;0} m / " +
                  $"ALONG {mock.RemainingAlongTrack:F0} m\n";

            localLines =
                progressLine +
                $"MOCK: {mock.LocalGuidanceStatus}\n" +
                $"PASSED {mock.PassedWaypointCount}/{waypoints.Count}\n";
        }

        string notice;

        if (!string.IsNullOrEmpty(interfaceLink.LastError))
        {
            notice = interfaceLink.LastError;
        }
        else if (Time.unscaledTime < messageUntil)
        {
            notice = message;
        }
        else if (complete)
        {
            notice = "Mission complete. AP OFF returns manual control.";
        }
        else if (!guidanceReady)
        {
            notice = guidanceReason;
        }
        else
        {
            notice = "";
        }

        statusText.text =
            $"<b>AP {(autopilot.IsEngaged ? "ON" : "OFF")}</b> / F1: MANUAL\n" +
            $"ALT {state.current_altitude:F1} -> {targetAltitude} m\n" +
            $"SPD {state.current_speed:F1} -> {targetSpeed} m/s\n" +
            $"HDG {state.current_heading:F1} -> {targetHeading} deg\n" +
            trackLine +
            $"MISSION: {mission} / DATA: {data}\n" +
            waypointLine + "\n" +
            coordinateLine + "\n" +
            localLines +
            autopilot.Status + "\n" +
            notice;

        statusText.color =
            MockActive && mock.WaypointMissed
                ? Color.yellow
                : autopilot.IsEngaged
                    ? Color.green
                    : Color.white;
    }
}