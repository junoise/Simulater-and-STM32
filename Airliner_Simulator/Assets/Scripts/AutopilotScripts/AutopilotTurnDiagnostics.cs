using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;

[DefaultExecutionOrder(200)]
public class AutopilotTurnDiagnostics : MonoBehaviour
{
    [Header("References")]
    public AircraftAutopilot autopilot;
    public AirlinerPhysics flight;
    public MissionInterface interfaceLink;
    public MockMissionComputer mock;
    public TextMeshProUGUI diagnosticsText;

    [Header("Recording")]
    public float sampleInterval = 0.5f;
    public float historySeconds = 12f;
    public bool logWaypointChanges = true;
    public bool logMissedWaypoint = true;

    private FieldInfo commandedBankField;
    private FieldInfo automaticInputField;

    private readonly Queue<TurnSample> history =
        new Queue<TurnSample>();

    private TurnSample latest;
    private bool hasLatest;

    private bool hasPreviousAngles;
    private float previousHeading;
    private float previousBank;

    private float headingRate;
    private float bankRate;
    private float nextSampleTime;

    private int previousWaypoint;
    private int previousPassedCount;
    private int previousMissionState = -1;
    private bool previousMissed;

    private string lastEvent = "No event captured.";

    private struct TurnSample
    {
        public float time;
        public int waypoint;
        public bool apOn;

        public float heading;
        public float targetHeading;

        public float bank;
        public float targetBank;

        public float headingRate;
        public float bankRate;

        public float requestedRoll;
        public float appliedRoll;
        public bool rollLimited;

        public float crossTrack;
        public float alongTrack;

        public string status;
    }

    private void Awake()
    {
        if (autopilot == null)
            autopilot = GetComponent<AircraftAutopilot>();

        if (flight == null)
            flight = GetComponent<AirlinerPhysics>();

        if (interfaceLink == null)
            interfaceLink = GetComponent<MissionInterface>();

        if (mock == null)
            mock = GetComponent<MockMissionComputer>();

        if (autopilot == null ||
            flight == null ||
            interfaceLink == null)
        {
            Debug.LogError(
                "AutopilotTurnDiagnostics: Attach to Airliner " +
                "or assign AP, Flight and Interface references.",
                this
            );

            enabled = false;
            return;
        }

        commandedBankField = typeof(AircraftAutopilot).GetField(
            "commandedBank",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        automaticInputField = typeof(AirlinerPhysics).GetField(
            "automaticInput",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        if (commandedBankField == null ||
            automaticInputField == null ||
            commandedBankField.FieldType != typeof(float) ||
            automaticInputField.FieldType != typeof(Vector3))
        {
            Debug.LogError(
                "AutopilotTurnDiagnostics: Expected private fields " +
                "commandedBank / automaticInput were not found.",
                this
            );

            enabled = false;
        }
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        if (dt <= 0f)
            return;

        if (!hasPreviousAngles)
        {
            previousHeading = autopilot.Heading;
            previousBank = autopilot.Bank;
            hasPreviousAngles = true;
        }

        float rawHeadingRate = Mathf.DeltaAngle(
            previousHeading,
            autopilot.Heading
        ) / dt;

        float rawBankRate = Mathf.DeltaAngle(
            previousBank,
            autopilot.Bank
        ) / dt;

        previousHeading = autopilot.Heading;
        previousBank = autopilot.Bank;

        float blend = 1f - Mathf.Exp(-dt / 0.2f);

        headingRate = Mathf.Lerp(
            headingRate,
            rawHeadingRate,
            blend
        );

        bankRate = Mathf.Lerp(
            bankRate,
            rawBankRate,
            blend
        );

        int waypoint = 0;
        int missionState = -1;

        if (interfaceLink.HasReceivedCommand)
        {
            waypoint =
                interfaceLink.LastReceived.current_waypoint_index + 1;

            missionState = interfaceLink.LastReceived.mission_state;
        }

        bool mockActive = mock != null && mock.isActiveAndEnabled;

        int passedCount = mockActive
            ? mock.PassedWaypointCount
            : 0;

        bool missed = mockActive && mock.WaypointMissed;

        bool newInitialize =
            missionState == (int)MissionStateCode.INITIALIZE &&
            previousMissionState != missionState;

        bool progressReset =
            passedCount < previousPassedCount ||
            (waypoint > 0 &&
             previousWaypoint > 0 &&
             waypoint < previousWaypoint);

        if (newInitialize || progressReset)
        {
            history.Clear();
            nextSampleTime = 0f;

            previousWaypoint = 0;
            previousMissed = false;

            lastEvent = "New mission: history reset.";
        }

        float targetBank =
            (float)commandedBankField.GetValue(autopilot);

        Vector3 automaticInput =
            (Vector3)automaticInputField.GetValue(flight);

        float requestedRoll = autopilot.IsEngaged
            ? automaticInput.y
            : 0f;

        float appliedRoll = flight.ControlInput.y;

        float rollLimit = Mathf.Max(
            0.001f,
            autopilot.maximumControlInput
        );

        bool rollLimited =
            autopilot.IsEngaged &&
            Mathf.Abs(requestedRoll) >= rollLimit * 0.95f;

        latest = new TurnSample
        {
            time = Time.fixedTime,
            waypoint = waypoint,
            apOn = autopilot.IsEngaged,

            heading = autopilot.Heading,
            targetHeading = autopilot.TargetHeading,

            bank = autopilot.Bank,
            targetBank = targetBank,

            headingRate = headingRate,
            bankRate = bankRate,

            requestedRoll = requestedRoll,
            appliedRoll = appliedRoll,
            rollLimited = rollLimited,

            crossTrack = mockActive
                ? mock.CrossTrackError
                : float.NaN,

            alongTrack = mockActive
                ? mock.RemainingAlongTrack
                : float.NaN,

            status = autopilot.Status
        };

        hasLatest = true;

        bool waypointChanged =
            previousWaypoint > 0 &&
            waypoint > previousWaypoint;

        bool missedNow = missed && !previousMissed;

        bool captureTransition =
            waypointChanged && logWaypointChanges;

        bool captureMissed =
            missedNow && logMissedWaypoint;

        if (Time.fixedTime >= nextSampleTime ||
            captureTransition ||
            captureMissed)
        {
            history.Enqueue(latest);

            nextSampleTime = Time.fixedTime +
                Mathf.Max(0.1f, sampleInterval);
        }

        float oldestAllowedTime =
            Time.fixedTime - Mathf.Clamp(historySeconds, 2f, 60f);

        while (history.Count > 0 &&
               history.Peek().time < oldestAllowedTime)
        {
            history.Dequeue();
        }

        if (captureTransition)
        {
            CaptureReport(
                $"WP {previousWaypoint} -> WP {waypoint}",
                false
            );
        }

        if (captureMissed)
        {
            CaptureReport(
                $"MISSED WP {waypoint}",
                true
            );
        }

        previousWaypoint = waypoint;
        previousPassedCount = passedCount;
        previousMissionState = missionState;
        previousMissed = missed;
    }

    private void LateUpdate()
    {
        if (diagnosticsText == null)
            return;

        if (!hasLatest)
        {
            diagnosticsText.color = Color.white;
            diagnosticsText.text = "TURN DIAGNOSTICS\nWaiting for physics...";
            return;
        }

        float headingError = Mathf.DeltaAngle(
            latest.heading,
            latest.targetHeading
        );

        float bankError = Mathf.DeltaAngle(
            latest.bank,
            latest.targetBank
        );

        string targetHeadingText = latest.apOn
            ? $"{latest.targetHeading:F1}"
            : "--";

        string targetBankText = latest.apOn
            ? $"{latest.targetBank:+0.0;-0.0;0.0}"
            : "--";

        string headingErrorText = latest.apOn
            ? $"{headingError:+0.0;-0.0;0.0}"
            : "--";

        string bankErrorText = latest.apOn
            ? $"{bankError:+0.0;-0.0;0.0}"
            : "--";

        string routeText =
            float.IsNaN(latest.crossTrack)
                ? "XTE -- / ALONG --"
                : $"XTE {latest.crossTrack:+0;-0;0} m / " +
                  $"ALONG {latest.alongTrack:F0} m";

        diagnosticsText.text =
            $"<b>TURN DIAGNOSTICS / WP {latest.waypoint}</b>\n" +
            $"HDG ACT {latest.heading:F1} / CMD {targetHeadingText}" +
            $" / ERR {headingErrorText}\n" +
            $"BANK ACT {latest.bank:+0.0;-0.0;0.0}" +
            $" / CMD {targetBankText} / ERR {bankErrorText}\n" +
            $"RATE HDG {latest.headingRate:+0.00;-0.00;0.00}" +
            $" / BANK {latest.bankRate:+0.00;-0.00;0.00} deg/s\n" +
            $"ROLL REQ {latest.requestedRoll:+0.000;-0.000;0.000}" +
            $" / ACT {latest.appliedRoll:+0.000;-0.000;0.000}" +
            $" / LIMIT {(latest.rollLimited ? "YES" : "NO")}\n" +
            routeText + "\n" +
            latest.status + "\n" +
            $"LAST EVENT: {lastEvent}";

        diagnosticsText.color =
            latest.rollLimited
                ? Color.yellow
                : Color.white;
    }

    private void CaptureReport(string eventName, bool warning)
    {
        lastEvent = $"{eventName} at {Time.fixedTime:F1}s";

        StringBuilder report = new StringBuilder();

        report.AppendLine(
            $"[TurnDiagnostics] {eventName}"
        );

        report.AppendLine(
            "Time is simulation seconds. Angles are degrees."
        );

        report.AppendLine(
            "BANK / ROLL: positive=right, negative=left."
        );

        report.AppendLine(
            "t,WP,AP,HDG,HDG_CMD,BANK,BANK_CMD," +
            "HDG_RATE,BANK_RATE,ROLL_REQ,ROLL_ACT,LIMIT,XTE,ALONG"
        );

        foreach (TurnSample sample in history)
        {
            report.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:F2},{1},{2},{3:F2},{4:F2},{5:F2},{6:F2}," +
                    "{7:F3},{8:F3},{9:F4},{10:F4},{11},{12:F1},{13:F1}",
                    sample.time,
                    sample.waypoint,
                    sample.apOn ? 1 : 0,
                    sample.heading,
                    sample.targetHeading,
                    sample.bank,
                    sample.targetBank,
                    sample.headingRate,
                    sample.bankRate,
                    sample.requestedRoll,
                    sample.appliedRoll,
                    sample.rollLimited ? 1 : 0,
                    sample.crossTrack,
                    sample.alongTrack
                )
            );
        }

        if (hasLatest)
        {
            report.AppendLine("AP STATUS: " + latest.status);
        }

        if (mock != null)
        {
            report.AppendLine(
                "MOCK STATUS: " + mock.LocalGuidanceStatus
            );
        }

        if (warning)
            Debug.LogWarning(report.ToString(), this);
        else
            Debug.Log(report.ToString(), this);
    }

    [ContextMenu("Print Recent Turn Samples")]
    private void PrintRecentTurnSamples()
    {
        if (!Application.isPlaying)
        {
            Debug.Log(
                "Start Play mode before printing turn samples.",
                this
            );
            return;
        }

        CaptureReport("MANUAL CAPTURE", false);
    }
}