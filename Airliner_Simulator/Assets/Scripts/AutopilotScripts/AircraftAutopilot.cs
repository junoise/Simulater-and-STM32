using System;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine;

[DefaultExecutionOrder(-150)]
[RequireComponent(typeof(AirlinerPhysics))]
public class AircraftAutopilot : MonoBehaviour
{
    [Header("References")]
    public CesiumGeoreference georeference;
    public LandingSystem landing;

    [Header("Engagement Conditions")]
    public float minimumAirborneTime = 2f;
    public float minimumEngageClearance = 100f;
    public float disengageClearance = 50f;
    public float minimumTargetSpeed = 80f;
    public float maximumTargetSpeed = 140f;

    [Header("Heading Control")]
    public float headingToBankGain = 0.6f;
    public float maximumBank = 15f;
    public float bankCommandRate = 4f;

    [Header("Altitude Control")]
    public float altitudeToVerticalSpeedGain = 0.05f;
    public float maximumClimbRate = 5f;
    public float maximumDescentRate = 3f;
    public float verticalSpeedCommandRate = 1f;

    [Tooltip("Pitch correction in degrees per m/s of vertical speed error.")]
    public float verticalSpeedGain = 0.6f;

    public float verticalSpeedIntegralGain = 0.08f;
    public float maximumPitchIntegral = 4f;
    public float minimumPitch = -8f;
    public float maximumPitch = 12f;
    public float pitchCommandRate = 3f;

    [Header("Attitude Response")]
    public float pitchResponse = 1.2f;
    public float rollResponse = 1.5f;
    public float attitudeDampingRatio = 1f;
    public float maximumControlInput = 0.7f;

    [Header("Attitude Error Correction")]
    [Tooltip("Integral gain for persistent pitch/bank tracking errors.")]
    public float attitudeIntegralGain = 0.4f;

    [Tooltip("Maximum integral correction in degrees per second squared.")]
    public float maximumAttitudeIntegral = 5f;

    [Header("Speed Control")]
    public float speedGain = 0.15f;
    public float speedIntegralGain = 0.015f;
    public float speedCommandRate = 1.5f;
    public float throttleChangeRate = 0.2f;

    [Header("Recovery - Simulator Test Settings")]
    public float recoverySinkRate = 8f;
    public float recoveryBankAngle = 35f;
    public float recoveryClimbRate = 2f;
    public float recoveryBankCommandRate = 12f;
    public float recoveryPitchCommandRate = 6f;
    public float recoveryStableTime = 2f;

    public bool IsEngaged { get; private set; }
    public bool IsRecovering { get; private set; }
    public string Status { get; private set; } = "MANUAL";

    public float Heading { get; private set; }
    public double Altitude { get; private set; }
    public float GroundSpeed { get; private set; }
    public float VerticalSpeed { get; private set; }
    public float Pitch { get; private set; }
    public float Bank { get; private set; }

    public float TargetHeading { get; private set; }
    public double TargetAltitude { get; private set; }
    public float TargetSpeed { get; private set; }

    public bool ThrottleLimited { get; private set; }
    public bool AttitudeLimited { get; private set; }

    private Rigidbody rb;
    private AirlinerPhysics flight;

    private Vector3 up;
    private Vector3 bodyForward;

    private float airborneTime;
    private float recoveryStableTimer;

    // 기존 진단 스크립트가 읽는 이름을 유지합니다.
    private float commandedBank;
    private float commandedPitch;
    private float commandedVerticalSpeed;
    private float commandedSpeed;

    private float pitchIntegral;
    private float speedIntegral;
    private float automaticThrottle;

    // 자세 제어용 적분값입니다. 고도 제어의 pitchIntegral과 별개입니다.
    // 내부 단위는 rad/s²입니다.
    private float pitchAttitudeIntegral;
    private float bankAttitudeIntegral;

    private Vector3 lastRequestedAxes;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        flight = GetComponent<AirlinerPhysics>();

        if (georeference == null)
            georeference = GetComponentInParent<CesiumGeoreference>();

        if (landing == null)
            landing = GetComponent<LandingSystem>();
    }

    private static bool Finite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool FiniteVector(Vector3 value)
    {
        return Finite(value.x) &&
               Finite(value.y) &&
               Finite(value.z);
    }

    private bool ReadState()
    {
        if (rb == null || georeference == null)
            return false;

        up = Physics.gravity.sqrMagnitude > 0.001f
            ? -Physics.gravity.normalized
            : Vector3.up;

        Quaternion rotation = rb.rotation;

        bodyForward = rotation * Vector3.forward;
        Vector3 bodyRight = rotation * Vector3.right;
        Vector3 bodyUp = rotation * Vector3.up;

        Vector3 localPosition =
            georeference.transform.InverseTransformPoint(rb.position);

        double3 ecef =
            georeference.TransformUnityPositionToEarthCenteredEarthFixed(
                new double3(
                    localPosition.x,
                    localPosition.y,
                    localPosition.z
                )
            );

        double3 llh =
            georeference.ellipsoid.CenteredFixedToLongitudeLatitudeHeight(ecef);

        Altitude = llh.z;

        double latitude = llh.y * Math.PI / 180.0;
        double longitude = llh.x * Math.PI / 180.0;

        double3 northEcef = new double3(
            -Math.Sin(latitude) * Math.Cos(longitude),
            -Math.Sin(latitude) * Math.Sin(longitude),
            Math.Cos(latitude)
        );

        double3 northLocal =
            georeference.TransformEarthCenteredEarthFixedDirectionToUnity(
                northEcef
            );

        Vector3 northWorld =
            georeference.transform.TransformDirection(
                new Vector3(
                    (float)northLocal.x,
                    (float)northLocal.y,
                    (float)northLocal.z
                )
            );

        Vector3 north =
            Vector3.ProjectOnPlane(northWorld, up).normalized;

        if (north.sqrMagnitude < 0.5f)
            return false;

        Vector3 east = Vector3.Cross(up, north).normalized;

        Heading = Mathf.Repeat(
            Mathf.Atan2(
                Vector3.Dot(bodyForward, east),
                Vector3.Dot(bodyForward, north)
            ) * Mathf.Rad2Deg,
            360f
        );

        GroundSpeed =
            Vector3.ProjectOnPlane(rb.linearVelocity, up).magnitude;

        VerticalSpeed = Vector3.Dot(rb.linearVelocity, up);

        Pitch = Mathf.Asin(
            Mathf.Clamp(Vector3.Dot(bodyForward, up), -1f, 1f)
        ) * Mathf.Rad2Deg;

        Bank = Mathf.Atan2(
            -Vector3.Dot(bodyRight, up),
            Vector3.Dot(bodyUp, up)
        ) * Mathf.Rad2Deg;

        return Finite(Altitude) &&
               Finite(Heading) &&
               Finite(GroundSpeed) &&
               Finite(VerticalSpeed) &&
               Finite(Pitch) &&
               Finite(Bank) &&
               FiniteVector(rb.angularVelocity);
    }

    private void Update()
    {
        if (!IsEngaged)
            return;

        if (Input.GetKeyDown(KeyCode.F1))
        {
            StopAutopilot("MANUAL: F1");
        }
        else if (AirlinerPhysics.HasManualControlInput())
        {
            StopAutopilot("MANUAL: CONTROL INPUT");
        }
    }

    public bool EngageCurrent()
    {
        if (IsEngaged)
            return true;

        if (!ReadState() ||
            flight == null ||
            !flight.isActiveAndEnabled ||
            rb.isKinematic)
        {
            Status = "AP REJECTED: AIRCRAFT NOT READY";
            return false;
        }

        if (flight.AnyWheelGrounded ||
            airborneTime < minimumAirborneTime)
        {
            Status = "AP REJECTED: AIRBORNE FLIGHT REQUIRED";
            return false;
        }

        if (landing == null || !landing.isActiveAndEnabled)
        {
            Status = "AP REJECTED: LANDING SENSOR MISSING / DISABLED";
            return false;
        }

        if (!landing.TryGetAutopilotHeight(
                out float clearance,
                out bool held))
        {
            Status = "AP REJECTED: GROUND SENSOR UNAVAILABLE";
            return false;
        }

        if (clearance < minimumEngageClearance)
        {
            Status =
                $"AP REJECTED: CLEARANCE {clearance:F0} < " +
                $"{minimumEngageClearance:F0} m";
            return false;
        }

        if (Mathf.Abs(Pitch) > 15f ||
            Mathf.Abs(Bank) > 30f ||
            Mathf.Abs(VerticalSpeed) > 5f)
        {
            Status = "AP REJECTED: STABILIZE ATTITUDE / VERTICAL SPEED";
            return false;
        }

        if (GroundSpeed < minimumTargetSpeed ||
            GroundSpeed > maximumTargetSpeed)
        {
            Status = "AP REJECTED: SPEED OUTSIDE TARGET RANGE";
            return false;
        }

        if (flight.fuelSystem != null &&
            flight.fuelSystem.isEngineStarved)
        {
            Status = "AP REJECTED: NO FUEL";
            return false;
        }

        if (AirlinerPhysics.HasManualControlInput())
        {
            Status = "AP REJECTED: RELEASE CONTROL KEYS";
            return false;
        }

        if (!ValidInertia())
        {
            Status = "AP REJECTED: INVALID INERTIA / ROTATION CONSTRAINTS";
            return false;
        }

        TargetHeading = Heading;
        TargetAltitude = Altitude;
        TargetSpeed = GroundSpeed;

        commandedBank = Bank;
        commandedPitch = Pitch;
        commandedVerticalSpeed = VerticalSpeed;
        commandedSpeed = GroundSpeed;

        pitchIntegral = 0f;
        speedIntegral = 0f;
        ResetAttitudeIntegrals();

        automaticThrottle = flight.throttle;
        lastRequestedAxes = flight.ControlInput;

        IsRecovering = false;
        recoveryStableTimer = 0f;
        ThrottleLimited = false;
        AttitudeLimited = false;
        IsEngaged = true;

        Status = held
            ? "AP ACTIVE: HEIGHT HELD BRIEFLY"
            : "AP ACTIVE";

        flight.SetAutomaticInput(lastRequestedAxes, automaticThrottle);
        return true;
    }

    public void Disengage()
    {
        StopAutopilot("MANUAL");
    }

    private void ResetAttitudeIntegrals()
    {
        pitchAttitudeIntegral = 0f;
        bankAttitudeIntegral = 0f;
    }

    private void StopAutopilot(string reason)
    {
        IsEngaged = false;
        IsRecovering = false;
        Status = reason;

        pitchIntegral = 0f;
        speedIntegral = 0f;
        ResetAttitudeIntegrals();

        recoveryStableTimer = 0f;
        ThrottleLimited = false;
        AttitudeLimited = false;
        lastRequestedAxes = Vector3.zero;

        if (flight != null)
            flight.ReleaseAutomaticControl();
    }

    private void OnDisable()
    {
        StopAutopilot("MANUAL: AP DISABLED");
    }

    public bool TrySetTargets(
        float heading,
        double altitude,
        float speed,
        out string message)
    {
        if (!IsEngaged)
        {
            message = "Turn AP ON before applying targets.";
            return false;
        }

        if (!Finite(heading) || !Finite(altitude) || !Finite(speed))
        {
            message = "Targets must be finite numbers.";
            return false;
        }

        if (heading < 0f || heading > 360f)
        {
            message = "Heading must be between 0 and 360.";
            return false;
        }

        if (altitude < -500.0 || altitude > 6000.0)
        {
            message = "Altitude must be between -500 and 6000 m.";
            return false;
        }

        if (speed < minimumTargetSpeed || speed > maximumTargetSpeed)
        {
            message =
                $"Speed must be {minimumTargetSpeed:F0}-" +
                $"{maximumTargetSpeed:F0} m/s.";
            return false;
        }

        if (landing == null ||
            !landing.isActiveAndEnabled ||
            !landing.TryGetAutopilotHeight(
                out float clearance,
                out bool held))
        {
            message = "Ground sensor unavailable.";
            return false;
        }

        double minimumLocalAltitude =
            Altitude - clearance + minimumEngageClearance;

        if (altitude < minimumLocalAltitude)
        {
            message = "Target altitude is too close to local terrain.";
            return false;
        }

        TargetHeading = Mathf.Repeat(heading, 360f);
        TargetAltitude = altitude;
        TargetSpeed = speed;

        message = "Targets applied.";
        return true;
    }

    private void FixedUpdate()
    {
        bool stateValid = ReadState();

        if (rb == null || flight == null)
        {
            if (IsEngaged)
                StopAutopilot("AP OFF: MISSING AIRCRAFT COMPONENT");

            return;
        }

        if (!flight.isActiveAndEnabled ||
            rb.isKinematic ||
            flight.AnyWheelGrounded)
        {
            airborneTime = 0f;
        }
        else
        {
            airborneTime += Time.fixedDeltaTime;
        }

        if (!IsEngaged)
            return;

        if (!stateValid ||
            !flight.isActiveAndEnabled ||
            rb.isKinematic ||
            flight.AnyWheelGrounded)
        {
            StopAutopilot("AP OFF: AIRCRAFT STATE");
            return;
        }

        if (AirlinerPhysics.HasManualControlInput())
        {
            StopAutopilot("MANUAL: CONTROL INPUT");
            return;
        }

        if (GroundSpeed < 65f ||
            Mathf.Abs(Pitch) > 35f ||
            Mathf.Abs(Bank) > 60f)
        {
            StopAutopilot("AP OFF: FLIGHT ENVELOPE - MANUAL REQUIRED");
            return;
        }

        if (flight.fuelSystem != null &&
            flight.fuelSystem.isEngineStarved)
        {
            StopAutopilot("AP OFF: NO FUEL");
            return;
        }

        if (landing == null || !landing.isActiveAndEnabled)
        {
            StopAutopilot("AP OFF: LANDING SENSOR MISSING / DISABLED");
            return;
        }

        if (!landing.TryGetAutopilotHeight(
                out float clearance,
                out bool held))
        {
            StopAutopilot("AP OFF: GROUND SENSOR LOST");
            return;
        }

        if (clearance < disengageClearance)
        {
            StopAutopilot("AP OFF: LOW HEIGHT - MANUAL REQUIRED");
            return;
        }

        float dt = Time.fixedDeltaTime;

        UpdateRecovery(dt);
        UpdateControl(dt);

        if (!IsEngaged)
            return;

        Status = IsRecovering
            ? $"AP RECOVERY: WINGS LEVEL / VS {VerticalSpeed:F1}"
            : $"AP ACTIVE / VS CMD {commandedVerticalSpeed:F1}";

        if (held)
            Status += " / HEIGHT HELD";

        if (ThrottleLimited)
            Status += " / THR LIMIT";

        if (AttitudeLimited)
            Status += " / ATT LIMIT";
    }

    private void UpdateRecovery(float dt)
    {
        bool needsRecovery =
            VerticalSpeed < -Mathf.Max(4f, recoverySinkRate) ||
            Mathf.Abs(Bank) > recoveryBankAngle ||
            GroundSpeed < minimumTargetSpeed - 5f;

        if (!IsRecovering && needsRecovery)
        {
            IsRecovering = true;
            recoveryStableTimer = 0f;
            pitchIntegral = 0f;
            speedIntegral = 0f;
            ResetAttitudeIntegrals();
            commandedVerticalSpeed = 0f;
        }

        if (!IsRecovering)
            return;

        bool stable =
            VerticalSpeed >= -1f &&
            VerticalSpeed <= maximumClimbRate + 1f &&
            Mathf.Abs(Bank) < 8f &&
            Mathf.Abs(Pitch) < 15f &&
            GroundSpeed >= minimumTargetSpeed;

        recoveryStableTimer = stable
            ? recoveryStableTimer + dt
            : 0f;

        if (recoveryStableTimer >= recoveryStableTime)
        {
            IsRecovering = false;
            recoveryStableTimer = 0f;
            pitchIntegral = 0f;
            ResetAttitudeIntegrals();
        }
    }

    private bool ValidInertia()
    {
        if (rb == null)
            return false;

        Vector3 inertia = rb.inertiaTensor;

        return FiniteVector(inertia) &&
               inertia.x > 0f &&
               inertia.y > 0f &&
               inertia.z > 0f &&
               (rb.constraints & RigidbodyConstraints.FreezeRotation) ==
                   RigidbodyConstraints.None;
    }

    private Vector3 InverseInertia(Vector3 bodyTorque)
    {
        Quaternion principalRotation = rb.inertiaTensorRotation;

        Vector3 principalTorque =
            Quaternion.Inverse(principalRotation) * bodyTorque;

        Vector3 inertia = rb.inertiaTensor;

        Vector3 principalAcceleration = new Vector3(
            principalTorque.x / inertia.x,
            principalTorque.y / inertia.y,
            principalTorque.z / inertia.z
        );

        return principalRotation * principalAcceleration;
    }

    private Vector3 EstimateAerodynamicTorque()
    {
        Transform aircraftTransform = flight.transform;

        Vector3 localVelocity =
            aircraftTransform.InverseTransformDirection(rb.linearVelocity);

        float forwardSpeed = localVelocity.z;

        if (forwardSpeed <= 2f)
            return Vector3.zero;

        Vector3 localAngularVelocity =
            aircraftTransform.InverseTransformDirection(rb.angularVelocity);

        float slipAngle = Mathf.Atan2(
            localVelocity.x,
            forwardSpeed
        ) * Mathf.Rad2Deg;

        return new Vector3(
            -localAngularVelocity.x * forwardSpeed * 50f,
            slipAngle * forwardSpeed * 5000f -
                localAngularVelocity.y * forwardSpeed * 50f,
            0f
        );
    }

    private static bool SolveInputs(
        float a,
        float b,
        float c,
        float d,
        float pitchAcceleration,
        float bankAcceleration,
        out Vector2 inputs)
    {
        inputs = Vector2.zero;

        double determinant = (double)a * d - (double)b * c;

        double scale =
            Math.Sqrt((double)a * a + (double)b * b) *
            Math.Sqrt((double)c * c + (double)d * d);

        if (!Finite(determinant) ||
            !Finite(scale) ||
            scale <= 0.0 ||
            Math.Abs(determinant) <= scale * 0.000001)
        {
            return false;
        }

        double pitchInput =
            ((double)pitchAcceleration * d -
             (double)b * bankAcceleration) / determinant;

        double rollInput =
            ((double)a * bankAcceleration -
             (double)pitchAcceleration * c) / determinant;

        if (!Finite(pitchInput) || !Finite(rollInput))
            return false;

        inputs = new Vector2((float)pitchInput, (float)rollInput);

        return Finite(inputs.x) && Finite(inputs.y);
    }

    private bool CalculateAttitudeInputs(
        float dt,
        float commandedPitchRate,
        float commandedBankRate,
        out Vector3 axes)
    {
        axes = Vector3.zero;

        if (!ValidInertia() || dt <= 0f)
            return false;

        float pitchRadians = Pitch * Mathf.Deg2Rad;
        float bankRadians = Bank * Mathf.Deg2Rad;

        float sinBank = Mathf.Sin(bankRadians);
        float cosBank = Mathf.Cos(bankRadians);
        float cosPitch = Mathf.Cos(pitchRadians);

        if (cosPitch < 0.5f)
            return false;

        float tanPitch = Mathf.Tan(pitchRadians);

        Quaternion worldToBody = Quaternion.Inverse(rb.rotation);

        Vector3 bodyAngularVelocity =
            worldToBody * rb.angularVelocity;

        Vector3 pitchRateRow = new Vector3(
            -cosBank,
            -sinBank,
            0f
        );

        Vector3 bankRateRow = new Vector3(
            -sinBank * tanPitch,
            cosBank * tanPitch,
            -1f
        );

        float pitchRate =
            Vector3.Dot(pitchRateRow, bodyAngularVelocity);

        float bankRate =
            Vector3.Dot(bankRateRow, bodyAngularVelocity);

        float headingRate =
            (-sinBank * bodyAngularVelocity.x +
              cosBank * bodyAngularVelocity.y) / cosPitch;

        float pitchKinematicAcceleration =
            -bankRate * headingRate * cosPitch;

        float bankKinematicAcceleration =
            pitchRate *
            (bankRate * tanPitch + headingRate / cosPitch);

        float pitchError =
            Mathf.DeltaAngle(Pitch, commandedPitch) * Mathf.Deg2Rad;

        float bankError =
            Mathf.DeltaAngle(Bank, commandedBank) * Mathf.Deg2Rad;

        float pitchFrequency = Mathf.Max(0.1f, pitchResponse);
        float rollFrequency = Mathf.Max(0.1f, rollResponse);
        float damping = Mathf.Max(0.1f, attitudeDampingRatio);

        float desiredPitchAcceleration =
            pitchFrequency * pitchFrequency * pitchError +
            2f * damping * pitchFrequency *
            (commandedPitchRate * Mathf.Deg2Rad - pitchRate);

        float desiredBankAcceleration =
            rollFrequency * rollFrequency * bankError +
            2f * damping * rollFrequency *
            (commandedBankRate * Mathf.Deg2Rad - bankRate);

        Quaternion actuatorToBody =
            worldToBody * flight.transform.rotation;

        float actuatorSpeed = Vector3.Dot(
            rb.linearVelocity,
            flight.transform.forward
        );

        float efficiency = Mathf.Max(0f, actuatorSpeed * 0.1f);

        Vector3 pitchTorquePerInput =
            actuatorToBody *
            (Vector3.right * flight.pitchPower * efficiency);

        Vector3 rollTorquePerInput =
            actuatorToBody *
            (Vector3.forward * -flight.rollPower * efficiency);

        Vector3 pitchAccelerationPerInput =
            InverseInertia(pitchTorquePerInput);

        Vector3 rollAccelerationPerInput =
            InverseInertia(rollTorquePerInput);

        Vector3 aerodynamicTorque =
            actuatorToBody * EstimateAerodynamicTorque();

        Vector3 passiveAcceleration =
            InverseInertia(aerodynamicTorque) -
            Mathf.Max(0f, rb.angularDamping) * bodyAngularVelocity;

        float passivePitchAcceleration =
            Vector3.Dot(pitchRateRow, passiveAcceleration) +
            pitchKinematicAcceleration;

        float passiveBankAcceleration =
            Vector3.Dot(bankRateRow, passiveAcceleration) +
            bankKinematicAcceleration;

        float a =
            Vector3.Dot(pitchRateRow, pitchAccelerationPerInput);
        float b =
            Vector3.Dot(pitchRateRow, rollAccelerationPerInput);
        float c =
            Vector3.Dot(bankRateRow, pitchAccelerationPerInput);
        float d =
            Vector3.Dot(bankRateRow, rollAccelerationPerInput);

        float integralLimit =
            Mathf.Max(0f, maximumAttitudeIntegral) * Mathf.Deg2Rad;

        float integralGain = Mathf.Max(0f, attitudeIntegralGain);

        float candidatePitchIntegral = pitchAttitudeIntegral;
        float candidateBankIntegral = bankAttitudeIntegral;

        bool inputFollowing =
            Mathf.Abs(
                flight.ControlInput.x - lastRequestedAxes.x
            ) < 0.02f &&
            Mathf.Abs(
                flight.ControlInput.y - lastRequestedAxes.y
            ) < 0.02f;

        if (IsRecovering)
        {
            ResetAttitudeIntegrals();
            candidatePitchIntegral = 0f;
            candidateBankIntegral = 0f;
        }
        else if (inputFollowing)
        {
            candidatePitchIntegral = Mathf.Clamp(
                pitchAttitudeIntegral + integralGain * pitchError * dt,
                -integralLimit,
                integralLimit
            );

            candidateBankIntegral = Mathf.Clamp(
                bankAttitudeIntegral + integralGain * bankError * dt,
                -integralLimit,
                integralLimit
            );
        }

        if (!SolveInputs(
                a, b, c, d,
                desiredPitchAcceleration + candidatePitchIntegral -
                    passivePitchAcceleration,
                desiredBankAcceleration + candidateBankIntegral -
                    passiveBankAcceleration,
                out Vector2 candidateInputs))
        {
            return false;
        }

        float inputLimit = Mathf.Clamp(maximumControlInput, 0.01f, 1f);

        bool candidateInsideLimits =
            Mathf.Abs(candidateInputs.x) <= inputLimit &&
            Mathf.Abs(candidateInputs.y) <= inputLimit;

        if (candidateInsideLimits)
        {
            pitchAttitudeIntegral = candidatePitchIntegral;
            bankAttitudeIntegral = candidateBankIntegral;
        }

        if (!SolveInputs(
                a, b, c, d,
                desiredPitchAcceleration + pitchAttitudeIntegral -
                    passivePitchAcceleration,
                desiredBankAcceleration + bankAttitudeIntegral -
                    passiveBankAcceleration,
                out Vector2 requestedInputs))
        {
            return false;
        }

        AttitudeLimited =
            Mathf.Abs(requestedInputs.x) > inputLimit ||
            Mathf.Abs(requestedInputs.y) > inputLimit;

        axes = new Vector3(
            Mathf.Clamp(requestedInputs.x, -inputLimit, inputLimit),
            Mathf.Clamp(requestedInputs.y, -inputLimit, inputLimit),
            0f
        );

        return FiniteVector(axes);
    }

    private void UpdateControl(float dt)
    {
        float gravity = Physics.gravity.magnitude;

        float forwardSpeed = Mathf.Max(
            10f,
            Vector3.Dot(rb.linearVelocity, bodyForward)
        );

        float pressure = forwardSpeed * forwardSpeed;

        float headingError =
            Mathf.DeltaAngle(Heading, TargetHeading);

        float desiredBank = IsRecovering
            ? 0f
            : Mathf.Clamp(
                headingError * headingToBankGain,
                -maximumBank,
                maximumBank
            );

        float previousCommandedBank = commandedBank;

        commandedBank = Mathf.MoveTowards(
            commandedBank,
            desiredBank,
            Mathf.Max(
                0f,
                IsRecovering ? recoveryBankCommandRate : bankCommandRate
            ) * dt
        );

        float commandedBankRate =
            (commandedBank - previousCommandedBank) /
            Mathf.Max(0.0001f, dt);

        float altitudeError = (float)Math.Max(
            -2000.0,
            Math.Min(2000.0, TargetAltitude - Altitude)
        );

        float desiredVerticalSpeed = Mathf.Clamp(
            altitudeError * altitudeToVerticalSpeedGain,
            -maximumDescentRate,
            maximumClimbRate
        );

        if (IsRecovering)
        {
            desiredVerticalSpeed =
                GroundSpeed < minimumTargetSpeed
                    ? 0f
                    : Mathf.Clamp(
                        recoveryClimbRate,
                        0f,
                        maximumClimbRate
                    );
        }

        commandedVerticalSpeed = Mathf.MoveTowards(
            commandedVerticalSpeed,
            desiredVerticalSpeed,
            Mathf.Max(0f, verticalSpeedCommandRate) * dt
        );

        float cosBank = Mathf.Max(
            0.65f,
            Mathf.Cos(Bank * Mathf.Deg2Rad)
        );

        float trimPitch =
            rb.mass * gravity /
            Mathf.Max(
                1f,
                pressure * flight.liftCoefficient * 0.1f *
                cosBank * cosBank
            );

        trimPitch = Mathf.Clamp(trimPitch, 0f, 10f);

        float desiredPathAngle = Mathf.Atan2(
            commandedVerticalSpeed,
            Mathf.Max(30f, GroundSpeed)
        ) * Mathf.Rad2Deg;

        float verticalSpeedError =
            commandedVerticalSpeed - VerticalSpeed;

        if (IsRecovering)
        {
            pitchIntegral = 0f;
        }
        else
        {
            float candidateIntegral = Mathf.Clamp(
                pitchIntegral +
                    verticalSpeedError * verticalSpeedIntegralGain * dt,
                -maximumPitchIntegral,
                maximumPitchIntegral
            );

            float candidatePitch =
                trimPitch +
                desiredPathAngle +
                verticalSpeedGain * verticalSpeedError +
                candidateIntegral;

            bool insideLimits =
                candidatePitch >= minimumPitch &&
                candidatePitch <= maximumPitch;

            bool unwindingUpper =
                candidatePitch > maximumPitch &&
                verticalSpeedError < 0f;

            bool unwindingLower =
                candidatePitch < minimumPitch &&
                verticalSpeedError > 0f;

            if (insideLimits || unwindingUpper || unwindingLower)
                pitchIntegral = candidateIntegral;
        }

        float desiredPitch = Mathf.Clamp(
            trimPitch +
            desiredPathAngle +
            verticalSpeedGain * verticalSpeedError +
            pitchIntegral,
            minimumPitch,
            maximumPitch
        );

        float previousCommandedPitch = commandedPitch;

        commandedPitch = Mathf.MoveTowards(
            commandedPitch,
            desiredPitch,
            Mathf.Max(
                0f,
                IsRecovering ? recoveryPitchCommandRate : pitchCommandRate
            ) * dt
        );

        float commandedPitchRate =
            (commandedPitch - previousCommandedPitch) /
            Mathf.Max(0.0001f, dt);

        if (!CalculateAttitudeInputs(
                dt,
                commandedPitchRate,
                commandedBankRate,
                out Vector3 axes))
        {
            StopAutopilot("AP OFF: ATTITUDE CONTROL MODEL INVALID");
            return;
        }

        commandedSpeed = Mathf.MoveTowards(
            commandedSpeed,
            TargetSpeed,
            Mathf.Max(0f, speedCommandRate) * dt
        );

        float speedError = commandedSpeed - GroundSpeed;
        float drag = pressure * flight.dragCoefficient;

        float climbAcceleration =
            gravity * commandedVerticalSpeed /
            Mathf.Max(40f, GroundSpeed);

        float candidateSpeedIntegral = Mathf.Clamp(
            speedIntegral + speedError * speedIntegralGain * dt,
            -1f,
            1f
        );

        float minimumThrottle = 0f;

        if (IsRecovering &&
            rb.linearVelocity.magnitude <= TargetSpeed + 10f)
        {
            minimumThrottle = Mathf.Clamp01(
                drag / Mathf.Max(1f, flight.maxThrust)
            );
        }

        float candidateThrottle =
            (drag + rb.mass *
                (speedGain * speedError +
                 candidateSpeedIntegral +
                 climbAcceleration)) /
            Mathf.Max(1f, flight.maxThrust);

        bool throttleInside =
            candidateThrottle >= minimumThrottle &&
            candidateThrottle <= 1f;

        bool throttleUnwindingUpper =
            candidateThrottle > 1f && speedError < 0f;

        bool throttleUnwindingLower =
            candidateThrottle < minimumThrottle && speedError > 0f;

        if (throttleInside ||
            throttleUnwindingUpper ||
            throttleUnwindingLower)
        {
            speedIntegral = candidateSpeedIntegral;
        }

        float requestedThrottle =
            (drag + rb.mass *
                (speedGain * speedError +
                 speedIntegral +
                 climbAcceleration)) /
            Mathf.Max(1f, flight.maxThrust);

        ThrottleLimited =
            requestedThrottle <= minimumThrottle ||
            requestedThrottle >= 1f;

        automaticThrottle = Mathf.MoveTowards(
            automaticThrottle,
            Mathf.Clamp(requestedThrottle, minimumThrottle, 1f),
            Mathf.Max(0f, throttleChangeRate) * dt
        );

        lastRequestedAxes = axes;
        flight.SetAutomaticInput(axes, automaticThrottle);
    }
}