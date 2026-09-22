using System;
using System.Collections.ObjectModel;
using Unity.Mathematics;
using UnityEngine;

[DefaultExecutionOrder(-140)]
[RequireComponent(typeof(AircraftAutopilot))]
public class MissionInterface : MonoBehaviour
{
    [Header("Mission Exchange")]
    public float transmitInterval = 0.1f;
    public float guidanceTimeout = 1f;

    public UnityAircraftState CurrentState { get; private set; }
    public UnityMissionRequest LastMissionRequest { get; private set; }
    public MissionCommand LastReceived { get; private set; }

    public bool HasAircraftState { get; private set; }
    public bool HasReceivedCommand { get; private set; }
    public bool MissionRequested { get; private set; }

    public string LastError { get; private set; } = "";

    public ReadOnlyCollection<MissionWaypoint> Waypoints =>
        Array.AsReadOnly(waypoints);

    public event Action<UnityAircraftState> AircraftStateProduced;
    public event Action<UnityMissionRequest> MissionStartProduced;

    public Func<bool> ExternalPeerReady { get; set; }

    public Func<UnityMissionRequest, string>
        ExternalMissionStartCheck
    { get; set; }

    private AircraftAutopilot autopilot;
    private Rigidbody rb;
    private FuelSystem fuel;

    private MissionWaypoint[] waypoints = Array.Empty<MissionWaypoint>();

    private bool routeHealthy;
    private bool hasGuidance;
    private bool completeLatched;

    private MissionCommand acceptedGuidance;

    private float nextTransmitTime;
    private float lastPacketTime = float.NegativeInfinity;
    private float lastGoodGuidanceTime = float.NegativeInfinity;

    private void Awake()
    {
        autopilot = GetComponent<AircraftAutopilot>();
        rb = GetComponent<Rigidbody>();
        fuel = GetComponent<FuelSystem>();
    }

    private bool ReadAircraftState(out UnityAircraftState state)
    {
        state = default;

        if (!autopilot.isActiveAndEnabled ||
            autopilot.georeference == null ||
            fuel == null ||
            fuel.maxFuelMass <= 0f)
        {
            return false;
        }

        var geo = autopilot.georeference;
        Vector3 position = geo.transform.InverseTransformPoint(rb.position);

        double3 ecef =
            geo.TransformUnityPositionToEarthCenteredEarthFixed(
                new double3(position.x, position.y, position.z));

        double3 llh =
            geo.ellipsoid.CenteredFixedToLongitudeLatitudeHeight(ecef);

        state = new UnityAircraftState
        {
            current_latitude = (float)llh.y,
            current_longitude = (float)llh.x,
            current_altitude = (float)llh.z,
            current_heading = autopilot.Heading,
            current_speed = autopilot.GroundSpeed,
            current_fuel = fuel.currentFuelMass / fuel.maxFuelMass * 100f
        };

        return MissionInterfaceRules.StateValid(state);
    }

    private void FixedUpdate()
    {
        HasAircraftState = ReadAircraftState(out UnityAircraftState state);

        if (HasAircraftState)
        {
            CurrentState = state;

            if (Time.unscaledTime >= nextTransmitTime)
            {
                nextTransmitTime =
                    Time.unscaledTime + Mathf.Max(0.02f, transmitInterval);

                AircraftStateProduced?.Invoke(CurrentState);
            }
        }

        if (!autopilot.IsEngaged)
            return;

        if (!TryGetGuidance(out MissionCommand command, out string reason))
        {
            autopilot.Disengage();
            LastError = reason;
            return;
        }

        if (!autopilot.TrySetTargets(
                command.target_heading,
                command.target_altitude,
                command.target_speed,
                out string result))
        {
            autopilot.Disengage();
            LastError = "AP target rejected: " + result;
        }
    }

    public void ClearMission(string reason = "")
    {
        if (autopilot != null)
            autopilot.Disengage();

        MissionRequested = false;
        HasReceivedCommand = false;

        routeHealthy = false;
        hasGuidance = false;
        completeLatched = false;

        waypoints = Array.Empty<MissionWaypoint>();

        LastMissionRequest = default;
        LastReceived = default;
        acceptedGuidance = default;

        lastPacketTime = float.NegativeInfinity;
        lastGoodGuidanceTime = float.NegativeInfinity;

        LastError = reason;
    }

    public bool StartMission(
        float latitude,
        float longitude,
        float altitude,
        out string message)
    {
        if (!isActiveAndEnabled)
        {
            message = "Mission interface is disabled.";
            return false;
        }

        if (!HasAircraftState)
        {
            message = "Aircraft state is not ready.";
            return false;
        }

        if (!MissionInterfaceRules.PositionValid(latitude, longitude) ||
            !MissionInterfaceRules.Finite(altitude))
        {
            message = "Invalid destination coordinates or altitude.";
            return false;
        }

        if (ExternalPeerReady != null && !ExternalPeerReady())
        {
            message = "Board is not connected.";
            return false;
        }

        if (MissionStartProduced == null)
        {
            message = "Connect the board or enable MockMissionComputer.";
            return false;
        }

        UnityMissionRequest request = new UnityMissionRequest
        {
            destination_latitude = latitude,
            destination_longitude = longitude,
            destination_altitude = altitude,
            mission_start_command = 1
        };

        if (ExternalMissionStartCheck != null)
        {
            string rejection = ExternalMissionStartCheck(request);

            if (!string.IsNullOrEmpty(rejection))
            {
                message = rejection;
                return false;
            }
        }

        ClearMission();

        MissionRequested = true;
        LastMissionRequest = request;

        MissionStartProduced.Invoke(request);

        if (!MissionRequested)
        {
            message = string.IsNullOrEmpty(LastError)
                ? "Mission request cancelled."
                : LastError;

            return false;
        }

        message = "Mission queued. Wait for new route and NAVIGATE.";
        return true;
    }

    public void ReceiveWaypointList(MissionWaypoint[] waypoint_list)
    {
        if (!MissionRequested || completeLatched)
            return;

        bool valid =
            waypoint_list != null &&
            waypoint_list.Length > 0 &&
            waypoint_list.Length <= 256;

        if (valid)
        {
            foreach (MissionWaypoint waypoint in waypoint_list)
            {
                if (!MissionInterfaceRules.WaypointValid(waypoint))
                {
                    valid = false;
                    break;
                }
            }
        }

        if (!valid)
        {
            routeHealthy = false;
            LastError = "Waypoint list rejected.";
            autopilot.Disengage();
            return;
        }

        waypoints = (MissionWaypoint[])waypoint_list.Clone();
        routeHealthy = true;
        LastError = "";

        if (HasReceivedCommand &&
            Time.unscaledTime - lastPacketTime <= guidanceTimeout)
        {
            ProcessGuidance(LastReceived, lastPacketTime);
        }
    }

    public void ReceiveCommand(MissionCommand command)
    {
        if (!MissionRequested)
            return;

        if (!MissionInterfaceRules.CommandValid(command))
        {
            LastError = "Malformed S2U command rejected.";
            return;
        }

        if (completeLatched &&
            command.mission_state != (byte)MissionStateCode.MISSION_COMPLETE)
        {
            LastError = "Start a new mission before leaving COMPLETE.";
            return;
        }

        LastReceived = command;
        HasReceivedCommand = true;
        lastPacketTime = Time.unscaledTime;
        LastError = "";

        ProcessGuidance(command, lastPacketTime);
    }

    private void ProcessGuidance(
        MissionCommand command,
        float receivedTime)
    {
        if (command.mission_state == (byte)MissionStateCode.INITIALIZE)
        {
            hasGuidance = false;
            autopilot.Disengage();
            return;
        }

        if (command.data_status != (byte)DataStatusCode.VALID)
            return;

        if (!routeHealthy ||
            command.current_waypoint_index >= waypoints.Length)
        {
            LastError = "Waiting for a valid waypoint list/index.";
            return;
        }

        if (command.mission_state == (byte)MissionStateCode.NAVIGATE)
        {
            acceptedGuidance = command;
            hasGuidance = true;
            lastGoodGuidanceTime = receivedTime;
        }
        else if (command.mission_state ==
                 (byte)MissionStateCode.MISSION_COMPLETE)
        {
            if (hasGuidance)
            {
                completeLatched = true;
                lastGoodGuidanceTime = receivedTime;
            }
        }
    }

    public bool TryGetGuidance(
        out MissionCommand command,
        out string reason)
    {
        command = acceptedGuidance;

        if (!isActiveAndEnabled)
        {
            reason = "Mission interface is disabled.";
            return false;
        }

        if (ExternalPeerReady != null && !ExternalPeerReady())
        {
            reason = "Board disconnected.";
            return false;
        }

        if (!MissionRequested)
        {
            reason = "MISSION START required.";
            return false;
        }

        if (!HasReceivedCommand)
        {
            reason = "Waiting for S2U command.";
            return false;
        }

        if (LastReceived.mission_state ==
            (byte)MissionStateCode.INITIALIZE)
        {
            reason = "INITIALIZE: AP is not permitted yet.";
            return false;
        }

        if (!routeHealthy || waypoints.Length == 0)
        {
            reason = "Waiting for waypoint list.";
            return false;
        }

        if (!hasGuidance)
        {
            reason = "Waiting for valid NAVIGATE guidance.";
            return false;
        }

        if (Time.unscaledTime - lastGoodGuidanceTime >
            Mathf.Max(0.02f, guidanceTimeout))
        {
            reason = "Guidance timeout. AP disconnected.";
            return false;
        }

        reason = LastReceived.data_status == (byte)DataStatusCode.VALID
            ? "Guidance ready."
            : "Holding last valid guidance briefly.";

        return true;
    }

    public bool EngageAP(out string message)
    {
        if (!TryGetGuidance(out MissionCommand command, out message))
            return false;

        if (!autopilot.EngageCurrent())
        {
            message = autopilot.Status;
            return false;
        }

        if (!autopilot.TrySetTargets(
                command.target_heading,
                command.target_altitude,
                command.target_speed,
                out message))
        {
            autopilot.Disengage();
            return false;
        }

        message = "AP following mission guidance.";
        return true;
    }

    public void DisengageAP()
    {
        autopilot.Disengage();
    }

    private void OnDisable()
    {
        ClearMission();
    }
}