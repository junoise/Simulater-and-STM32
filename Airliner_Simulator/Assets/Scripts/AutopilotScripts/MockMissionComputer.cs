using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MissionInterface))]
public class MockMissionComputer : MonoBehaviour
{
    [Header("Local Test Guidance")]
    public float cruiseSpeed = 100f;
    public float arrivalRadius = 150f;
    public float arrivalAltitudeTolerance = 30f;

    [Header("Five Waypoint Demo")]
    public float middleLateralOffset = 350f;
    public float middleAltitudeOffset = 50f;
    public float middleSpeedOffset = 5f;

    [Header("Smooth Route Planning")]
    public float planningBank = 12f;

    [Tooltip("Local simulator calibration, not a real B737 constant.")]
    public float turnResponseFactor = 0.5f;

    public float pathSampleSpacing = 30f;

    [Range(0.3f, 0.9f)]
    public float maximumWaypointCutFraction = 0.65f;

    [Header("Path Tracking")]
    public float pathResponseTime = 12f;
    public float pathDampingRatio = 0.9f;
    public float curvaturePreviewSeconds = 2f;
    public float guidanceBankFilterTime = 0.5f;

    [Header("Ground Track Estimation")]
    public float trackFilterTime = 0.7f;
    public float maximumDriftCorrection = 15f;

    [Header("Fault Injection")]
    public bool pauseResponses;
    public bool sendInvalidDataStatus;

    [Header("Runtime Diagnostics - Do Not Edit During Play")]
    [SerializeField]
    private string localGuidanceStatus = "WAITING FOR MISSION";

    [SerializeField] private int currentWaypointNumber;
    [SerializeField] private int passedWaypointCount;
    [SerializeField] private float distanceToDestination;
    [SerializeField] private float crossTrackError;
    [SerializeField] private float remainingAlongTrack;
    [SerializeField] private float groundTrack;
    [SerializeField] private float headingDrift;
    [SerializeField] private float plannedTurnRadius;
    [SerializeField] private float guidanceBank;
    [SerializeField] private float routeProgress;

    public string LocalGuidanceStatus => localGuidanceStatus;
    public bool WaypointMissed => missed;
    public int PassedWaypointCount => passedWaypointCount;
    public float DistanceToWaypoint => distanceToDestination;
    public float CrossTrackError => crossTrackError;
    public float RemainingAlongTrack => remainingAlongTrack;
    public float GroundTrack => groundTrack;
    public float HeadingDrift => headingDrift;

    public bool TrackAvailable =>
        missionStarted &&
        trackValid &&
        Time.time - lastTrackTime <= MaximumTrackGap;

    private const float MaximumTrackGap = 0.75f;
    private const float MinimumTrackMovement = 3f;
    private const float MinimumTrackSpeed = 10f;
    private const float TrackWarmupTime = 0.5f;

    private struct PathPoint
    {
        public Vector2 position;
        public Vector2 tangent;
        public float curvature;
        public float station;
    }

    private MissionInterface interfaceLink;
    private AircraftAutopilot autopilot;

    private MissionWaypoint[] route =
        Array.Empty<MissionWaypoint>();

    private float[] routeSpeeds = Array.Empty<float>();
    private float[] waypointStations = Array.Empty<float>();
    private List<PathPoint> path = new List<PathPoint>();

    private double originLatitude;
    private double originLongitude;

    private int waypointIndex;
    private bool missionStarted;
    private bool routeSent;
    private bool navSentForLeg;
    private bool completed;
    private bool missed;

    private float lastUpdateTime;
    private float missedHoldHeading;

    private MissionCommand output;

    private bool hasTrackAnchor;
    private bool trackFilterSeeded;
    private bool trackValid;

    private double anchorLatitude;
    private double anchorLongitude;
    private float anchorHeading;
    private float anchorTime;

    private float trackAccumulatedTime;
    private float lastTrackTime = float.NegativeInfinity;

    private Vector2 filteredVelocity;
    private Vector2 filteredHeading;

    private void FindReferences()
    {
        if (interfaceLink == null)
            interfaceLink = GetComponent<MissionInterface>();

        if (autopilot == null)
            autopilot = GetComponent<AircraftAutopilot>();
    }

    private void Awake()
    {
        FindReferences();
    }

    private void OnEnable()
    {
        FindReferences();
        interfaceLink.MissionStartProduced += OnMissionStart;
        interfaceLink.AircraftStateProduced += OnAircraftState;
    }

    private void OnDisable()
    {
        if (interfaceLink != null)
        {
            interfaceLink.MissionStartProduced -= OnMissionStart;
            interfaceLink.AircraftStateProduced -= OnAircraftState;
        }

        missionStarted = false;
        ClearTrack();
        localGuidanceStatus = "MOCK DISABLED";
    }

    private static bool Finite(float value)
    {
        return MissionInterfaceRules.Finite(value);
    }

    private static Vector2 RightOf(Vector2 direction)
    {
        return new Vector2(direction.y, -direction.x);
    }

    private static float Bearing(Vector2 direction)
    {
        return Mathf.Repeat(
            Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg,
            360f
        );
    }

    private static Vector2 RotateClockwise(Vector2 value, float radians)
    {
        float c = Mathf.Cos(radians);
        float s = Mathf.Sin(radians);

        return new Vector2(
            value.x * c + value.y * s,
            -value.x * s + value.y * c
        );
    }
    private static Vector2 ToPlane(
        double startLatitude,
        double startLongitude,
        double latitude,
        double longitude)
    {
        double distance = MissionInterfaceRules.DistanceMeters(
            startLatitude, startLongitude, latitude, longitude
        );

        float heading = MissionInterfaceRules.BearingDegrees(
            startLatitude, startLongitude, latitude, longitude
        ) * Mathf.Deg2Rad;

        return new Vector2(
            Mathf.Sin(heading),
            Mathf.Cos(heading)
        ) * (float)distance;
    }

    private static MissionWaypoint PointAt(
        double latitude,
        double longitude,
        float heading,
        float distance,
        float altitude)
    {
        UnityAircraftState origin = new UnityAircraftState
        {
            current_latitude = (float)latitude,
            current_longitude = (float)longitude,
            current_altitude = altitude,
            current_heading = Mathf.Repeat(heading, 360f)
        };

        return MissionInterfaceRules.PointAhead(origin, distance);
    }

    private static void AddPathPoint(
        List<PathPoint> points,
        Vector2 position,
        Vector2 tangent,
        float curvature)
    {
        float station = 0f;

        if (points.Count > 0)
        {
            PathPoint previous = points[points.Count - 1];
            float distance = Vector2.Distance(previous.position, position);

            if (distance < 0.01f)
                return;

            station = previous.station + distance;
        }

        points.Add(new PathPoint
        {
            position = position,
            tangent = tangent.normalized,
            curvature = curvature,
            station = station
        });
    }

    private static void AddLine(
        List<PathPoint> points,
        Vector2 start,
        Vector2 end,
        float spacing)
    {
        Vector2 difference = end - start;
        float distance = difference.magnitude;

        if (distance < 0.01f)
            return;

        Vector2 tangent = difference / distance;
        int count = Mathf.Max(1, Mathf.CeilToInt(distance / spacing));

        if (points.Count == 0)
            AddPathPoint(points, start, tangent, 0f);

        for (int i = 1; i <= count; i++)
        {
            AddPathPoint(
                points,
                Vector2.Lerp(start, end, (float)i / count),
                tangent,
                0f
            );
        }
    }

    private static void AddArc(
        List<PathPoint> points,
        Vector2 entry,
        Vector2 entryTangent,
        float turnAngle,
        float radius,
        float spacing)
    {
        float direction = Mathf.Sign(turnAngle);

        Vector2 center =
            entry + RightOf(entryTangent) * direction * radius;

        Vector2 initialRadius = entry - center;
        float length = Mathf.Abs(turnAngle) * radius;

        int count = Mathf.Max(2, Mathf.CeilToInt(length / spacing));

        for (int i = 1; i <= count; i++)
        {
            float angle = turnAngle * i / count;

            AddPathPoint(
                points,
                center + RotateClockwise(initialRadius, angle),
                RotateClockwise(entryTangent, angle),
                direction / radius
            );
        }
    }

    private static PathPoint SamplePath(
        List<PathPoint> points,
        float station)
    {
        if (station <= points[0].station)
            return points[0];

        int last = points.Count - 1;

        if (station >= points[last].station)
            return points[last];

        int low = 0;
        int high = last;

        while (high - low > 1)
        {
            int middle = (low + high) / 2;

            if (points[middle].station <= station)
                low = middle;
            else
                high = middle;
        }

        PathPoint a = points[low];
        PathPoint b = points[high];

        float t = Mathf.InverseLerp(a.station, b.station, station);

        return new PathPoint
        {
            position = Vector2.Lerp(a.position, b.position, t),
            tangent = Vector2.Lerp(a.tangent, b.tangent, t).normalized,
            curvature = Mathf.Lerp(a.curvature, b.curvature, t),
            station = station
        };
    }

    private static void ProjectOntoPath(
        List<PathPoint> points,
        Vector2 position,
        out float station,
        out float crossTrack,
        out float distance)
    {
        float bestSquaredDistance = float.PositiveInfinity;

        station = 0f;
        crossTrack = 0f;

        for (int i = 0; i < points.Count - 1; i++)
        {
            PathPoint a = points[i];
            PathPoint b = points[i + 1];

            Vector2 segment = b.position - a.position;
            float lengthSquared = segment.sqrMagnitude;

            if (lengthSquared < 0.0001f)
                continue;

            float t = Mathf.Clamp01(
                Vector2.Dot(position - a.position, segment) /
                lengthSquared
            );

            Vector2 closest = Vector2.Lerp(a.position, b.position, t);
            Vector2 difference = position - closest;
            float squaredDistance = difference.sqrMagnitude;

            if (squaredDistance >= bestSquaredDistance)
                continue;

            bestSquaredDistance = squaredDistance;

            Vector2 tangent =
                Vector2.Lerp(a.tangent, b.tangent, t).normalized;

            station = Mathf.Lerp(a.station, b.station, t);
            crossTrack = Vector2.Dot(difference, RightOf(tangent));
        }

        distance = Mathf.Sqrt(bestSquaredDistance);
    }

    public bool ValidateDestination(
        float latitude,
        float longitude,
        float altitude,
        out string reason)
    {
        FindReferences();

        if (interfaceLink == null ||
            autopilot == null ||
            !interfaceLink.HasAircraftState)
        {
            reason = "Aircraft state is not ready.";
            return false;
        }

        return TryBuildPlan(
            interfaceLink.CurrentState,
            latitude,
            longitude,
            altitude,
            out _,
            out _,
            out _,
            out _,
            out _,
            out reason
        );
    }

    private bool TryBuildPlan(
        UnityAircraftState state,
        float latitude,
        float longitude,
        float altitude,
        out MissionWaypoint[] newRoute,
        out float[] newSpeeds,
        out List<PathPoint> newPath,
        out float[] newStations,
        out float radius,
        out string reason)
    {
        newRoute = Array.Empty<MissionWaypoint>();
        newSpeeds = Array.Empty<float>();
        newPath = new List<PathPoint>();
        newStations = Array.Empty<float>();
        radius = 0f;

        if (!MissionInterfaceRules.StateValid(state) ||
            !MissionInterfaceRules.PositionValid(latitude, longitude) ||
            !Finite(altitude))
        {
            reason = "Invalid aircraft state or destination.";
            return false;
        }

        float[] settings =
        {
            cruiseSpeed, arrivalRadius, arrivalAltitudeTolerance,
            middleLateralOffset, middleAltitudeOffset, middleSpeedOffset,
            planningBank, turnResponseFactor, pathSampleSpacing,
            maximumWaypointCutFraction, pathResponseTime,
            pathDampingRatio, curvaturePreviewSeconds,
            guidanceBankFilterTime, trackFilterTime,
            maximumDriftCorrection,
            autopilot.maximumBank, autopilot.headingToBankGain
        };

        foreach (float value in settings)
        {
            if (!Finite(value))
            {
                reason = "Guidance settings must be finite numbers.";
                return false;
            }
        }

        if (arrivalRadius < 10f || arrivalRadius > 500f ||
            arrivalAltitudeTolerance < 0f ||
            arrivalAltitudeTolerance > 200f ||
            planningBank < 3f ||
            planningBank > autopilot.maximumBank * 0.85f ||
            autopilot.maximumBank > 30f ||
            autopilot.headingToBankGain <= 0f ||
            turnResponseFactor < 0.2f || turnResponseFactor > 1.5f ||
            pathSampleSpacing < 5f || pathSampleSpacing > 100f ||
            maximumWaypointCutFraction < 0.3f ||
            maximumWaypointCutFraction > 0.9f ||
            pathResponseTime < 8f || pathResponseTime > 30f ||
            pathDampingRatio < 0.5f || pathDampingRatio > 2f ||
            curvaturePreviewSeconds < 0f ||
            curvaturePreviewSeconds > 5f ||
            guidanceBankFilterTime < 0.1f ||
            guidanceBankFilterTime > 3f ||
            trackFilterTime < 0.1f || trackFilterTime > 3f ||
            maximumDriftCorrection < 0f ||
            maximumDriftCorrection > 30f)
        {
            reason = "Check smooth route and tracking settings.";
            return false;
        }

        double totalDistance = MissionInterfaceRules.DistanceMeters(
            state.current_latitude,
            state.current_longitude,
            latitude,
            longitude
        );

        if (totalDistance < 6000.0 || totalDistance > 30000.0)
        {
            reason = "Demo destination must be 6-30 km away.";
            return false;
        }

        float middleAltitude =
            Mathf.Lerp(state.current_altitude, altitude, 0.5f) +
            middleAltitudeOffset;

        if (Mathf.Abs(altitude - state.current_altitude) > 200f ||
            Mathf.Abs(middleAltitudeOffset) > 100f ||
            Mathf.Abs(middleSpeedOffset) > 10f ||
            state.current_altitude < -500f ||
            state.current_altitude > 6000f ||
            altitude < -500f || altitude > 6000f ||
            middleAltitude < -500f || middleAltitude > 6000f)
        {
            reason = "Check waypoint altitude and speed offsets.";
            return false;
        }

        float middleSpeed = cruiseSpeed + middleSpeedOffset;

        if (cruiseSpeed < autopilot.minimumTargetSpeed ||
            cruiseSpeed > autopilot.maximumTargetSpeed ||
            middleSpeed < autopilot.minimumTargetSpeed ||
            middleSpeed > autopilot.maximumTargetSpeed)
        {
            reason = "Waypoint speeds are outside the AP target range.";
            return false;
        }

        float mainHeading = MissionInterfaceRules.BearingDegrees(
            state.current_latitude,
            state.current_longitude,
            latitude,
            longitude
        );

        MissionWaypoint first = PointAt(
            state.current_latitude,
            state.current_longitude,
            mainHeading,
            (float)(totalDistance / 3.0),
            state.current_altitude
        );

        MissionWaypoint middleBase = PointAt(
            state.current_latitude,
            state.current_longitude,
            mainHeading,
            (float)(totalDistance * 2.0 / 3.0),
            middleAltitude
        );

        float maximumOffset = Mathf.Min(
            1000f,
            (float)totalDistance * 0.08f
        );

        float offset = Mathf.Clamp(
            middleLateralOffset,
            -maximumOffset,
            maximumOffset
        );

        MissionWaypoint middle = PointAt(
            middleBase.waypoint_latitude,
            middleBase.waypoint_longitude,
            mainHeading + (offset >= 0f ? 90f : -90f),
            Mathf.Abs(offset),
            middleAltitude
        );

        MissionWaypoint last = new MissionWaypoint
        {
            waypoint_latitude = latitude,
            waypoint_longitude = longitude,
            waypoint_altitude = altitude
        };

        MissionWaypoint early = PointAt(
            state.current_latitude,
            state.current_longitude,
            mainHeading,
            (float)(totalDistance / 6.0),
            state.current_altitude
        );

        MissionWaypoint late = PointAt(
            middle.waypoint_latitude,
            middle.waypoint_longitude,
            MissionInterfaceRules.BearingDegrees(
                middle.waypoint_latitude,
                middle.waypoint_longitude,
                last.waypoint_latitude,
                last.waypoint_longitude
            ),
            (float)(MissionInterfaceRules.DistanceMeters(
                middle.waypoint_latitude,
                middle.waypoint_longitude,
                last.waypoint_latitude,
                last.waypoint_longitude
            ) * 2.0 / 3.0),
            altitude
        );

        newRoute = new MissionWaypoint[]
        {
            early, first, middle, late, last
        };

        newSpeeds = new float[]
        {
            cruiseSpeed,
            cruiseSpeed,
            middleSpeed,
            cruiseSpeed,
            cruiseSpeed
        };

        Vector2[] vertices = new Vector2[newRoute.Length + 1];
        int lastVertex = vertices.Length - 1;

        vertices[0] = Vector2.zero;

        for (int i = 0; i < newRoute.Length; i++)
        {
            vertices[i + 1] = ToPlane(
                state.current_latitude,
                state.current_longitude,
                newRoute[i].waypoint_latitude,
                newRoute[i].waypoint_longitude
            );
        }

        float planningSpeed = Mathf.Max(cruiseSpeed, middleSpeed);
        float gravity = Physics.gravity.magnitude;

        if (gravity < 0.1f)
        {
            reason = "Gravity is not configured for this demo.";
            return false;
        }

        radius =
            planningSpeed * planningSpeed /
            (gravity * turnResponseFactor *
             Mathf.Tan(planningBank * Mathf.Deg2Rad));

        Vector2[] entries = new Vector2[vertices.Length];
        Vector2[] exits = new Vector2[vertices.Length];
        float[] angles = new float[vertices.Length];
        float[] tangentDistances = new float[vertices.Length];

        for (int i = 1; i < lastVertex; i++)
        {
            Vector2 incoming =
                (vertices[i] - vertices[i - 1]).normalized;

            Vector2 outgoing =
                (vertices[i + 1] - vertices[i]).normalized;

            float angle = Mathf.DeltaAngle(
                Bearing(incoming),
                Bearing(outgoing)
            ) * Mathf.Deg2Rad;

            if (Mathf.Abs(angle) > 80f * Mathf.Deg2Rad)
            {
                reason = "Demo corner is too sharp.";
                return false;
            }

            float tangentDistance =
                radius * Mathf.Tan(Mathf.Abs(angle) * 0.5f);

            angles[i] = angle;
            tangentDistances[i] = tangentDistance;
            entries[i] = vertices[i] - incoming * tangentDistance;
            exits[i] = vertices[i] + outgoing * tangentDistance;
        }

        for (int leg = 0; leg < lastVertex; leg++)
        {
            float length = Vector2.Distance(
                vertices[leg],
                vertices[leg + 1]
            );

            float startCut =
                leg == 0 ? 0f : tangentDistances[leg];

            float endCut =
                leg == lastVertex - 1
                    ? 0f
                    : tangentDistances[leg + 1];

            if (startCut + endCut + 200f >= length)
            {
                reason =
                    "Curves overlap. Increase Ahead Distance or " +
                    "reduce Middle Lateral Offset.";
                return false;
            }
        }

        Vector2 cursor = vertices[0];

        for (int i = 1; i < lastVertex; i++)
        {
            Vector2 incoming =
                (vertices[i] - vertices[i - 1]).normalized;

            if (Mathf.Abs(angles[i]) < 0.0001f)
            {
                AddLine(newPath, cursor, vertices[i], pathSampleSpacing);
                cursor = vertices[i];
                continue;
            }

            AddLine(newPath, cursor, entries[i], pathSampleSpacing);

            AddArc(
                newPath,
                entries[i],
                incoming,
                angles[i],
                radius,
                pathSampleSpacing
            );

            cursor = exits[i];
        }

        AddLine(
            newPath,
            cursor,
            vertices[lastVertex],
            pathSampleSpacing
        );

        Vector2 finalDirection =
            (vertices[lastVertex] - vertices[lastVertex - 1]).normalized;

        AddLine(
            newPath,
            vertices[lastVertex],
            vertices[lastVertex] + finalDirection * 3000f,
            pathSampleSpacing
        );

        if (newPath.Count < 2)
        {
            reason = "Failed to construct route.";
            return false;
        }

        newStations = new float[newRoute.Length];

        for (int i = 0; i < newRoute.Length; i++)
        {
            ProjectOntoPath(
                newPath,
                vertices[i + 1],
                out float station,
                out _,
                out float closestDistance
            );

            newStations[i] = station;

            if (closestDistance >
                arrivalRadius * maximumWaypointCutFraction)
            {
                reason =
                    $"WP {i + 1}: planned clearance " +
                    $"{closestDistance:F0} m is too close to the " +
                    "arrival boundary. Reduce Middle Lateral Offset " +
                    "or increase Ahead Distance.";
                return false;
            }
        }

        reason = "Smooth route is ready.";
        return true;
    }

    private void ClearTrack()
    {
        hasTrackAnchor = false;
        trackFilterSeeded = false;
        trackValid = false;

        trackAccumulatedTime = 0f;
        lastTrackTime = float.NegativeInfinity;

        filteredVelocity = Vector2.zero;
        filteredHeading = Vector2.zero;

        groundTrack = 0f;
        headingDrift = 0f;
    }

    private void SetTrackAnchor(UnityAircraftState state)
    {
        anchorLatitude = state.current_latitude;
        anchorLongitude = state.current_longitude;
        anchorHeading = state.current_heading;
        anchorTime = Time.time;
        hasTrackAnchor = true;
    }

    private void UpdateGroundTrack(UnityAircraftState state)
    {
        if (!hasTrackAnchor)
        {
            SetTrackAnchor(state);
            return;
        }

        float sampleTime = Time.time - anchorTime;

        if (sampleTime <= 0f)
            return;

        if (sampleTime > MaximumTrackGap ||
            state.current_speed < MinimumTrackSpeed)
        {
            ClearTrack();
            SetTrackAnchor(state);
            return;
        }

        double movement = MissionInterfaceRules.DistanceMeters(
            anchorLatitude,
            anchorLongitude,
            state.current_latitude,
            state.current_longitude
        );

        if (movement < MinimumTrackMovement)
            return;

        float measuredSpeed = (float)(movement / sampleTime);

        if (!Finite(measuredSpeed) ||
            measuredSpeed > Mathf.Max(
                300f,
                state.current_speed * 2f + 30f))
        {
            ClearTrack();
            SetTrackAnchor(state);
            return;
        }

        float movementBearing = MissionInterfaceRules.BearingDegrees(
            anchorLatitude,
            anchorLongitude,
            state.current_latitude,
            state.current_longitude
        ) * Mathf.Deg2Rad;

        Vector2 measuredVelocity = new Vector2(
            Mathf.Sin(movementBearing),
            Mathf.Cos(movementBearing)
        ) * measuredSpeed;

        float midpointHeading =
            anchorHeading +
            Mathf.DeltaAngle(anchorHeading, state.current_heading) * 0.5f;

        midpointHeading *= Mathf.Deg2Rad;

        Vector2 measuredHeading = new Vector2(
            Mathf.Sin(midpointHeading),
            Mathf.Cos(midpointHeading)
        );

        float blend = 1f - Mathf.Exp(
            -sampleTime / Mathf.Max(0.1f, trackFilterTime)
        );

        if (!trackFilterSeeded)
        {
            filteredVelocity = measuredVelocity;
            filteredHeading = measuredHeading;
            trackFilterSeeded = true;
        }
        else
        {
            filteredVelocity =
                Vector2.Lerp(filteredVelocity, measuredVelocity, blend);

            filteredHeading =
                Vector2.Lerp(filteredHeading, measuredHeading, blend);
        }

        trackAccumulatedTime += sampleTime;
        lastTrackTime = Time.time;

        if (filteredVelocity.magnitude >= MinimumTrackSpeed &&
            filteredHeading.sqrMagnitude > 0.25f)
        {
            groundTrack = Bearing(filteredVelocity);

            headingDrift = Mathf.Clamp(
                Mathf.DeltaAngle(
                    groundTrack,
                    Bearing(filteredHeading)
                ),
                -maximumDriftCorrection,
                maximumDriftCorrection
            );

            trackValid = trackAccumulatedTime >= TrackWarmupTime;
        }
        else
        {
            trackValid = false;
        }

        SetTrackAnchor(state);
    }

    private void ResetRuntime()
    {
        route = Array.Empty<MissionWaypoint>();
        routeSpeeds = Array.Empty<float>();
        waypointStations = Array.Empty<float>();
        path = new List<PathPoint>();

        waypointIndex = 0;
        currentWaypointNumber = 0;
        passedWaypointCount = 0;

        missionStarted = false;
        routeSent = false;
        navSentForLeg = false;
        completed = false;
        missed = false;

        distanceToDestination = 0f;
        crossTrackError = 0f;
        remainingAlongTrack = 0f;
        routeProgress = 0f;
        plannedTurnRadius = 0f;
        guidanceBank = 0f;

        lastUpdateTime = Time.time;
        ClearTrack();
    }

    private void OnMissionStart(UnityMissionRequest request)
    {
        if (request.mission_start_command != 1)
            return;

        ResetRuntime();

        UnityAircraftState state = interfaceLink.CurrentState;

        output = new MissionCommand
        {
            target_altitude = state.current_altitude,
            target_heading = state.current_heading,
            target_speed = state.current_speed,
            current_waypoint_index = 0,
            mission_state = (byte)MissionStateCode.INITIALIZE,
            data_status = (byte)DataStatusCode.UNKNOWN
        };

        if (!TryBuildPlan(
                state,
                request.destination_latitude,
                request.destination_longitude,
                request.destination_altitude,
                out MissionWaypoint[] builtRoute,
                out float[] builtSpeeds,
                out List<PathPoint> builtPath,
                out float[] builtStations,
                out float builtRadius,
                out string reason))
        {
            localGuidanceStatus = "ROUTE REJECTED: " + reason;

            if (!pauseResponses)
                interfaceLink.ReceiveCommand(output);

            Debug.LogWarning(localGuidanceStatus, this);
            return;
        }

        route = builtRoute;
        routeSpeeds = builtSpeeds;
        path = builtPath;
        waypointStations = builtStations;
        plannedTurnRadius = builtRadius;

        originLatitude = state.current_latitude;
        originLongitude = state.current_longitude;

        guidanceBank = Mathf.Clamp(
            autopilot.Bank,
            -autopilot.maximumBank,
            autopilot.maximumBank
        );

        missionStarted = true;
        currentWaypointNumber = 1;

        SetTrackAnchor(state);

        localGuidanceStatus = "SMOOTH ROUTE READY";

        Debug.Log(
            $"Smooth route ready: waypoints={route.Length}, " +
            $"radius={plannedTurnRadius:F0} m, " +
            $"middle offset={middleLateralOffset:F0} m, " +
            $"arrival radius={arrivalRadius:F0} m.",
            this
        );

        if (!pauseResponses)
            interfaceLink.ReceiveCommand(output);
    }

    private void UpdateMeasurements(UnityAircraftState state)
    {
        Vector2 position = ToPlane(
            originLatitude,
            originLongitude,
            state.current_latitude,
            state.current_longitude
        );

        ProjectOntoPath(
            path,
            position,
            out float station,
            out float error,
            out _
        );

        routeProgress = station;
        crossTrackError = error;

        UpdateWaypointMeasurements(state);
    }

    private void UpdateWaypointMeasurements(UnityAircraftState state)
    {
        MissionWaypoint target = route[waypointIndex];

        distanceToDestination =
            (float)MissionInterfaceRules.DistanceMeters(
                state.current_latitude,
                state.current_longitude,
                target.waypoint_latitude,
                target.waypoint_longitude
            );

        remainingAlongTrack =
            waypointStations[waypointIndex] - routeProgress;
    }

    private float CalculateGuidanceHeading(
        UnityAircraftState state,
        float dt)
    {
        if (missed)
            return missedHoldHeading;

        PathPoint current = SamplePath(path, routeProgress);

        float speed = Mathf.Max(30f, state.current_speed);

        float previewDistance =
            speed * Mathf.Max(0f, curvaturePreviewSeconds);

        float curvature =
            (
                current.curvature +
                SamplePath(
                    path,
                    routeProgress + previewDistance * 0.5f
                ).curvature +
                SamplePath(
                    path,
                    routeProgress + previewDistance
                ).curvature
            ) / 3f;

        float track = TrackAvailable
            ? groundTrack
            : state.current_heading;

        float courseError =
            Mathf.DeltaAngle(track, Bearing(current.tangent)) *
            Mathf.Deg2Rad;

        float frequency = 1f / Mathf.Max(8f, pathResponseTime);

        float desiredTurnRate =
            speed * curvature +
            2f * pathDampingRatio * frequency * courseError -
            frequency * frequency * crossTrackError / speed;

        float gravity = Mathf.Max(0.1f, Physics.gravity.magnitude);

        float requestedBank = Mathf.Atan(
            desiredTurnRate * speed /
            (gravity * turnResponseFactor)
        ) * Mathf.Rad2Deg;

        requestedBank = Mathf.Clamp(
            requestedBank,
            -autopilot.maximumBank,
            autopilot.maximumBank
        );

        float blend = 1f - Mathf.Exp(
            -Mathf.Max(0f, dt) /
            Mathf.Max(0.1f, guidanceBankFilterTime)
        );

        guidanceBank =
            Mathf.Lerp(guidanceBank, requestedBank, blend);

        float headingOffset =
            guidanceBank / Mathf.Max(0.01f, autopilot.headingToBankGain);

        return Mathf.Repeat(
            state.current_heading + headingOffset,
            360f
        );
    }

    private void OnAircraftState(UnityAircraftState state)
    {
        if (!missionStarted)
            return;

        float dt = Mathf.Clamp(
            Time.time - lastUpdateTime,
            0f,
            0.5f
        );

        lastUpdateTime = Time.time;

        if (pauseResponses)
        {
            ClearTrack();
            localGuidanceStatus = "RESPONSES PAUSED";
            return;
        }

        if (!routeSent)
        {
            interfaceLink.ReceiveWaypointList(route);
            routeSent = true;
        }

        if (sendInvalidDataStatus ||
            !MissionInterfaceRules.StateValid(state))
        {
            ClearTrack();
            output.data_status = (byte)DataStatusCode.INVALID;
            localGuidanceStatus = "INVALID TELEMETRY";
            interfaceLink.ReceiveCommand(output);
            return;
        }

        UpdateGroundTrack(state);
        UpdateMeasurements(state);

        output.data_status = (byte)DataStatusCode.VALID;

        if (completed)
        {
            localGuidanceStatus =
                $"COMPLETE {route.Length}/{route.Length}";

            interfaceLink.ReceiveCommand(output);
            return;
        }

        MissionWaypoint target = route[waypointIndex];

        float altitudeError = Mathf.Abs(
            state.current_altitude - target.waypoint_altitude
        );

        bool arrived =
            navSentForLeg &&
            !missed &&
            distanceToDestination <= arrivalRadius &&
            altitudeError <= arrivalAltitudeTolerance;

        if (arrived)
        {
            passedWaypointCount++;

            if (waypointIndex == route.Length - 1)
            {
                completed = true;

                output.mission_state =
                    (byte)MissionStateCode.MISSION_COMPLETE;

                localGuidanceStatus =
                    $"COMPLETE {route.Length}/{route.Length}";

                interfaceLink.ReceiveCommand(output);
                return;
            }

            waypointIndex++;
            currentWaypointNumber = waypointIndex + 1;
            navSentForLeg = false;

            UpdateWaypointMeasurements(state);

            target = route[waypointIndex];

            altitudeError = Mathf.Abs(
                state.current_altitude - target.waypoint_altitude
            );

        }

        bool passedArrivalRegion =
            navSentForLeg &&
            routeProgress >
                waypointStations[waypointIndex] + arrivalRadius;

        if (!missed && passedArrivalRegion)
        {
            missed = true;

            missedHoldHeading = Bearing(
                SamplePath(path, routeProgress).tangent
            );

            Debug.LogWarning(
                $"MISSED WAYPOINT {waypointIndex + 1}/{route.Length}: " +
                $"distance={distanceToDestination:F1} m, " +
                $"path XTE={crossTrackError:F1} m, " +
                $"along={remainingAlongTrack:F1} m, " +
                $"altitude error={altitudeError:F1} m.",
                this
            );
        }

        output.target_heading =
            CalculateGuidanceHeading(state, dt);

        output.target_altitude = target.waypoint_altitude;
        output.target_speed = routeSpeeds[waypointIndex];
        output.current_waypoint_index = (byte)waypointIndex;
        output.mission_state = (byte)MissionStateCode.NAVIGATE;

        localGuidanceStatus = missed
            ? $"MISSED WP {waypointIndex + 1}/{route.Length} - NEW MISSION"
            : $"CURVE WP {waypointIndex + 1}/{route.Length} / " +
              $"BANK {guidanceBank:+0.0;-0.0;0.0}";

        navSentForLeg = true;
        interfaceLink.ReceiveCommand(output);
    }
}