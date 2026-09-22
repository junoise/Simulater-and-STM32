using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Rigidbody))]
public class AirlinerPhysics : MonoBehaviour
{
    private Rigidbody rb;

    [Header("Systems")]
    public FuelSystem fuelSystem;

    [Header("Engine & Thrust")]
    public float maxThrust = 500000f;
    [Range(0f, 1f)] public float throttle = 0f;
    public Slider throttleSlider;

    [Header("Aerodynamics")]
    public float liftCoefficient = 150f;
    public float dragCoefficient = 10f;

    [Header("Flight Controls")]
    public float pitchPower = 5000f;
    public float rollPower = 5000f;
    public float yawPower = 2000f;

    [Header("Landing Gear")]
    public WheelCollider noseWheel;
    public WheelCollider leftWheel;
    public WheelCollider rightWheel;
    public float steerAngle = 30f;
    public float brakeForce = 200000f;

    [Header("Ground Control")]
    public float highSpeedSteerAngle = 2f;
    public float steeringLimitStartSpeed = 10f;
    public float steeringLimitFullSpeed = 60f;
    public float brakeApplyRate = 1.5f;
    public float brakeReleaseRate = 3f;

    [Header("Mass Distribution")]
    public Vector3 centerOfMass = new Vector3(0f, 1f, -1f);

    [Header("Automatic Input Transition")]
    public float controlInputRate = 3f;

    [Header("Manual Input Smoothing")]
    public float manualCommandRate = 1.5f;
    public float manualSurfaceInputRate = 1f;

    [Header("Manual Rate Assist - Simulator Settings")]
    public float manualPitchRate = 2f;
    public float manualRollRate = 5f;
    public float manualRateResponse = 2f;

    [Range(0.01f, 1f)]
    public float manualMaximumControlInput = 0.3f;

    [Header("Manual Rudder")]
    [Range(0f, 1f)]
    public float manualRudderInput = 0.025f;

    public float manualRudderReferenceSpeed = 100f;
    public float manualMaximumYawRate = 2f;

    [Header("Manual Ground Inputs")]
    [Range(0f, 1f)]
    public float manualGroundPitchScale = 0.12f;

    [Range(0f, 1f)]
    public float manualGroundRollScale = 0.05f;

    [Header("Manual Envelope Assist - Not Real B737 Limits")]
    public bool manualEnvelopeAssist = true;
    public float manualBankLimit = 35f;
    public float manualPitchUpLimit = 20f;
    public float manualPitchDownLimit = 10f;
    public float manualEnvelopeSoftZone = 8f;

    public float BrakeInput { get; private set; }
    public Vector3 ControlInput { get; private set; }
    public bool AutomaticControl { get; private set; }

    public bool AnyWheelGrounded =>
        (noseWheel != null && noseWheel.isGrounded) ||
        (leftWheel != null && leftWheel.isGrounded) ||
        (rightWheel != null && rightWheel.isGrounded);

    private Vector3 automaticInput;

    private Vector3 manualCommand;
    private float manualSteeringInput;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = centerOfMass;

        if (fuelSystem == null)
            fuelSystem = GetComponent<FuelSystem>();

        if (noseWheel == null || leftWheel == null || rightWheel == null)
        {
            Debug.LogError(
                "AirlinerPhysics: Assign all three WheelColliders.",
                this
            );

            enabled = false;
            return;
        }

        WriteThrottle(throttle);
    }

    public static bool IsEditingText()
    {
        if (EventSystem.current == null)
            return false;

        GameObject selected =
            EventSystem.current.currentSelectedGameObject;

        if (selected == null)
            return false;

        TMP_InputField field =
            selected.GetComponentInParent<TMP_InputField>();

        return field != null && field.isFocused;
    }

    public static bool HasManualControlInput()
    {
        if (IsEditingText())
            return false;

        return Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.2f ||
               Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.2f ||
               Input.GetKey(KeyCode.Q) ||
               Input.GetKey(KeyCode.E) ||
               Input.GetKey(KeyCode.Space);
    }

    private static bool Finite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool FiniteVector(Vector3 value)
    {
        return Finite(value.x) &&
               Finite(value.y) &&
               Finite(value.z);
    }

    private void WriteThrottle(float value)
    {
        throttle = Mathf.Clamp01(value);

        if (throttleSlider != null)
            throttleSlider.SetValueWithoutNotify(throttle);
    }

    public void SetThrottle(float value)
    {
        if (!AutomaticControl)
            WriteThrottle(value);
    }

    public void SetAutomaticInput(Vector3 axes, float throttleValue)
    {
        AutomaticControl = true;

        automaticInput = new Vector3(
            Mathf.Clamp(axes.x, -1f, 1f),
            Mathf.Clamp(axes.y, -1f, 1f),
            Mathf.Clamp(axes.z, -1f, 1f)
        );

        WriteThrottle(throttleValue);
    }

    public void ReleaseAutomaticControl()
    {
        AutomaticControl = false;
        automaticInput = Vector3.zero;

        if (throttleSlider != null)
            throttleSlider.interactable = true;
    }

    private void OnDisable()
    {
        ReleaseAutomaticControl();
        manualCommand = Vector3.zero;
        manualSteeringInput = 0f;
    }

    public void ResetGroundControls()
    {
        ReleaseAutomaticControl();

        ControlInput = Vector3.zero;
        manualCommand = Vector3.zero;
        manualSteeringInput = 0f;
        BrakeInput = 0f;

        if (noseWheel != null)
        {
            noseWheel.steerAngle = 0f;
            noseWheel.brakeTorque = 0f;
            noseWheel.motorTorque = 0f;
        }

        if (leftWheel != null)
        {
            leftWheel.brakeTorque = 0f;
            leftWheel.motorTorque = 0f;
        }

        if (rightWheel != null)
        {
            rightWheel.brakeTorque = 0f;
            rightWheel.motorTorque = 0f;
        }
    }

    private void LateUpdate()
    {
        throttle = Mathf.Clamp01(throttle);

        if (throttleSlider != null)
        {
            throttleSlider.SetValueWithoutNotify(throttle);
            throttleSlider.interactable = !AutomaticControl;
        }
    }

    private Vector3 ReadManualKeys()
    {
        if (IsEditingText())
            return Vector3.zero;

        float yaw = 0f;

        if (Input.GetKey(KeyCode.E)) yaw += 1f;
        if (Input.GetKey(KeyCode.Q)) yaw -= 1f;

        return new Vector3(
            Input.GetAxisRaw("Vertical"),
            Input.GetAxisRaw("Horizontal"),
            yaw
        );
    }

    private static Vector3 MoveAxes(
        Vector3 current,
        Vector3 target,
        float step)
    {
        return new Vector3(
            Mathf.MoveTowards(current.x, target.x, step),
            Mathf.MoveTowards(current.y, target.y, step),
            Mathf.MoveTowards(current.z, target.z, step)
        );
    }

    private void FixedUpdate()
    {
        if (rb == null || rb.isKinematic)
            return;

        float dt = Time.fixedDeltaTime;
        Vector3 requested;
        float inputRate;

        if (AutomaticControl)
        {
            requested = automaticInput;
            inputRate = controlInputRate;

            manualCommand = Vector3.zero;
            manualSteeringInput = 0f;
        }
        else
        {
            manualCommand = MoveAxes(
                manualCommand,
                ReadManualKeys(),
                Mathf.Max(0f, manualCommandRate) * dt
            );

            manualSteeringInput = manualCommand.z;

            requested = CalculateManualInput(dt);
            inputRate = manualSurfaceInputRate;
        }

        ControlInput = MoveAxes(
            ControlInput,
            requested,
            Mathf.Max(0f, inputRate) * dt
        );

        ApplyThrust();
        ApplyAerodynamics();
        ApplyControlSurfaces();
        ApplyWheelControls();
    }

    private Vector3 CalculateManualInput(float dt)
    {
        float forwardSpeed = Mathf.Max(
            0f,
            Vector3.Dot(rb.linearVelocity, transform.forward)
        );

        float rudderSpeedScale = Mathf.Clamp01(
            Mathf.Max(1f, manualRudderReferenceSpeed) /
            Mathf.Max(1f, forwardSpeed)
        );

        float yawInput =
            manualCommand.z *
            Mathf.Clamp01(manualRudderInput) *
            rudderSpeedScale;

        Vector3 up = Physics.gravity.sqrMagnitude > 0.001f
            ? -Physics.gravity.normalized
            : Vector3.up;

        Quaternion rotation = rb.rotation;
        Vector3 forward = rotation * Vector3.forward;
        Vector3 right = rotation * Vector3.right;
        Vector3 bodyUp = rotation * Vector3.up;

        float pitch = Mathf.Asin(
            Mathf.Clamp(Vector3.Dot(forward, up), -1f, 1f)
        ) * Mathf.Rad2Deg;

        float bank = Mathf.Atan2(
            -Vector3.Dot(right, up),
            Vector3.Dot(bodyUp, up)
        ) * Mathf.Rad2Deg;

        Vector3 bodyOmega =
            Quaternion.Inverse(rotation) * rb.angularVelocity;

        float bankRadians = bank * Mathf.Deg2Rad;
        float pitchRadians = pitch * Mathf.Deg2Rad;

        float sinBank = Mathf.Sin(bankRadians);
        float cosBank = Mathf.Cos(bankRadians);
        float cosPitch = Mathf.Cos(pitchRadians);

        if (Mathf.Abs(cosPitch) > 0.2f)
        {
            float yawRateDegrees =
                (-sinBank * bodyOmega.x + cosBank * bodyOmega.y) /
                cosPitch * Mathf.Rad2Deg;

            float yawRateLimit = Mathf.Max(0.1f, manualMaximumYawRate);

            if (yawInput * yawRateDegrees > 0f)
            {
                float factor = 1f - Mathf.InverseLerp(
                    yawRateLimit * 0.5f,
                    yawRateLimit,
                    Mathf.Abs(yawRateDegrees)
                );

                yawInput *= factor;
            }
        }

        Vector3 simpleInput = new Vector3(
            manualCommand.x * Mathf.Clamp01(manualGroundPitchScale),
            manualCommand.y * Mathf.Clamp01(manualGroundRollScale),
            yawInput
        );

        if (AnyWheelGrounded || forwardSpeed < 10f)
            return simpleInput;

        Vector3 inertia = rb.inertiaTensor;

        if (!FiniteVector(inertia) ||
            inertia.x <= 0f || inertia.y <= 0f || inertia.z <= 0f ||
            !FiniteVector(bodyOmega) ||
            cosPitch < 0.35f ||
            (rb.constraints & RigidbodyConstraints.FreezeRotation) !=
                RigidbodyConstraints.None)
        {
            return simpleInput;
        }

        float desiredPitchRate =
            -manualCommand.x * Mathf.Max(0f, manualPitchRate);

        float desiredBankRate =
            manualCommand.y * Mathf.Max(0f, manualRollRate);

        if (manualEnvelopeAssist)
        {
            desiredPitchRate = LimitManualRate(
                desiredPitchRate,
                pitch,
                -Mathf.Max(1f, manualPitchDownLimit),
                Mathf.Max(1f, manualPitchUpLimit),
                Mathf.Max(1f, manualEnvelopeSoftZone),
                Mathf.Max(0.1f, manualPitchRate)
            );

            desiredBankRate = LimitManualRate(
                desiredBankRate,
                bank,
                -Mathf.Max(1f, manualBankLimit),
                Mathf.Max(1f, manualBankLimit),
                Mathf.Max(1f, manualEnvelopeSoftZone),
                Mathf.Max(0.1f, manualRollRate)
            );
        }

        if (!TryCalculateManualRateInputs(
                desiredPitchRate,
                desiredBankRate,
                yawInput,
                pitchRadians,
                sinBank,
                cosBank,
                cosPitch,
                bodyOmega,
                dt,
                out Vector3 assistedInput))
        {
            return simpleInput;
        }

        return assistedInput;
    }

    private static float LimitManualRate(
        float requestedRate,
        float angle,
        float minimumAngle,
        float maximumAngle,
        float softZone,
        float maximumRecoveryRate)
    {
        if (angle > maximumAngle)
        {
            return -Mathf.Min(
                maximumRecoveryRate,
                (angle - maximumAngle) * 0.8f
            );
        }

        if (angle < minimumAngle)
        {
            return Mathf.Min(
                maximumRecoveryRate,
                (minimumAngle - angle) * 0.8f
            );
        }

        if (requestedRate > 0f)
        {
            return requestedRate * Mathf.Clamp01(
                (maximumAngle - angle) / softZone
            );
        }

        if (requestedRate < 0f)
        {
            return requestedRate * Mathf.Clamp01(
                (angle - minimumAngle) / softZone
            );
        }

        return 0f;
    }

    private Vector3 InverseInertia(Vector3 torque)
    {
        Quaternion principalRotation = rb.inertiaTensorRotation;

        Vector3 principalTorque =
            Quaternion.Inverse(principalRotation) * torque;

        Vector3 inertia = rb.inertiaTensor;

        return principalRotation * new Vector3(
            principalTorque.x / inertia.x,
            principalTorque.y / inertia.y,
            principalTorque.z / inertia.z
        );
    }
    private Vector3 CalculateAerodynamicTorque(
        Vector3 localVelocity,
        Vector3 localAngularVelocity)
    {
        float forwardSpeed = localVelocity.z;

        if (forwardSpeed <= 2f)
            return Vector3.zero;

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
    private bool TryCalculateManualRateInputs(
        float desiredPitchRateDegrees,
        float desiredBankRateDegrees,
        float requestedYaw,
        float pitchRadians,
        float sinBank,
        float cosBank,
        float cosPitch,
        Vector3 bodyOmega,
        float dt,
        out Vector3 axes)
    {
        axes = Vector3.zero;

        float tanPitch = Mathf.Tan(pitchRadians);

        Vector3 pitchRow = new Vector3(-cosBank, -sinBank, 0f);

        Vector3 bankRow = new Vector3(
            -sinBank * tanPitch,
            cosBank * tanPitch,
            -1f
        );

        float pitchRate = Vector3.Dot(pitchRow, bodyOmega);
        float bankRate = Vector3.Dot(bankRow, bodyOmega);

        float headingRate =
            (-sinBank * bodyOmega.x + cosBank * bodyOmega.y) /
            cosPitch;

        float pitchKinematicAcceleration =
            -bankRate * headingRate * cosPitch;

        float bankKinematicAcceleration =
            pitchRate *
            (bankRate * tanPitch + headingRate / cosPitch);

        float response = Mathf.Max(0.1f, manualRateResponse);

        float desiredPitchAcceleration =
            response *
            (desiredPitchRateDegrees * Mathf.Deg2Rad - pitchRate);

        float desiredBankAcceleration =
            response *
            (desiredBankRateDegrees * Mathf.Deg2Rad - bankRate);

        Quaternion actuatorToBody =
            Quaternion.Inverse(rb.rotation) * transform.rotation;

        float forwardSpeed = Vector3.Dot(
            rb.linearVelocity,
            transform.forward
        );

        float efficiency = Mathf.Max(0f, forwardSpeed * 0.1f);

        Vector3 pitchEffect = InverseInertia(
            actuatorToBody *
            (Vector3.right * pitchPower * efficiency)
        );

        Vector3 rollEffect = InverseInertia(
            actuatorToBody *
            (Vector3.forward * -rollPower * efficiency)
        );

        Vector3 localVelocity =
            transform.InverseTransformDirection(rb.linearVelocity);

        Vector3 localOmega =
            transform.InverseTransformDirection(rb.angularVelocity);

        float nextYaw = Mathf.MoveTowards(
            ControlInput.z,
            requestedYaw,
            Mathf.Max(0f, manualSurfaceInputRate) * dt
        );

        Vector3 knownTorque =
            CalculateAerodynamicTorque(localVelocity, localOmega) +
            Vector3.up * nextYaw * yawPower * efficiency;

        Vector3 passiveAcceleration =
            InverseInertia(actuatorToBody * knownTorque) -
            Mathf.Max(0f, rb.angularDamping) * bodyOmega;

        float neededPitch =
            desiredPitchAcceleration -
            Vector3.Dot(pitchRow, passiveAcceleration) -
            pitchKinematicAcceleration;

        float neededBank =
            desiredBankAcceleration -
            Vector3.Dot(bankRow, passiveAcceleration) -
            bankKinematicAcceleration;

        float a = Vector3.Dot(pitchRow, pitchEffect);
        float b = Vector3.Dot(pitchRow, rollEffect);
        float c = Vector3.Dot(bankRow, pitchEffect);
        float d = Vector3.Dot(bankRow, rollEffect);

        double determinant = (double)a * d - (double)b * c;

        double scale =
            System.Math.Sqrt((double)a * a + (double)b * b) *
            System.Math.Sqrt((double)c * c + (double)d * d);

        if (double.IsNaN(determinant) ||
            double.IsInfinity(determinant) ||
            double.IsNaN(scale) ||
            double.IsInfinity(scale) ||
            scale <= 0.0 ||
            System.Math.Abs(determinant) <= scale * 0.000001)
        {
            return false;
        }

        float pitchInput = (float)(
            ((double)neededPitch * d - (double)b * neededBank) /
            determinant
        );

        float rollInput = (float)(
            ((double)a * neededBank - (double)neededPitch * c) /
            determinant
        );

        if (!Finite(pitchInput) || !Finite(rollInput))
            return false;

        float limit = Mathf.Clamp(
            manualMaximumControlInput,
            0.01f,
            1f
        );

        axes = new Vector3(
            Mathf.Clamp(pitchInput, -limit, limit),
            Mathf.Clamp(rollInput, -limit, limit),
            requestedYaw
        );

        return FiniteVector(axes);
    }

    private void ApplyThrust()
    {
        float currentThrust = maxThrust * throttle;

        if (fuelSystem != null && fuelSystem.isEngineStarved)
            currentThrust = 0f;

        rb.AddForce(transform.forward * currentThrust);
    }

    private void ApplyAerodynamics()
    {
        Vector3 localVelocity =
            transform.InverseTransformDirection(rb.linearVelocity);

        float forwardSpeed = localVelocity.z;

        if (forwardSpeed <= 2f)
            return;

        float dynamicPressure = forwardSpeed * forwardSpeed;

        float aoa = Mathf.Atan2(
            -localVelocity.y,
            forwardSpeed
        ) * Mathf.Rad2Deg;

        aoa = Mathf.Clamp(aoa, -15f, 15f);

        float liftForce =
            dynamicPressure * liftCoefficient * (aoa * 0.1f);

        rb.AddForce(transform.up * liftForce);

        float dragForce = dynamicPressure * dragCoefficient;
        rb.AddForce(-transform.forward * dragForce);

        float sideDrag = -localVelocity.x * forwardSpeed * 200f;
        rb.AddRelativeForce(Vector3.right * sideDrag);

        Vector3 localAngularVelocity =
            transform.InverseTransformDirection(rb.angularVelocity);

        rb.AddRelativeTorque(
            CalculateAerodynamicTorque(
                localVelocity,
                localAngularVelocity
            )
        );
    }

    private void ApplyControlSurfaces()
    {
        float forwardSpeed =
            Vector3.Dot(rb.linearVelocity, transform.forward);

        float efficiency = Mathf.Max(0f, forwardSpeed * 0.1f);

        rb.AddRelativeTorque(
            Vector3.right * ControlInput.x * pitchPower * efficiency
        );

        rb.AddRelativeTorque(
            Vector3.forward * -ControlInput.y * rollPower * efficiency
        );

        rb.AddRelativeTorque(
            Vector3.up * ControlInput.z * yawPower * efficiency
        );
    }

    private void ApplyWheelControls()
    {
        Vector3 up = Physics.gravity.sqrMagnitude > 0.001f
            ? -Physics.gravity.normalized
            : Vector3.up;

        float groundSpeed =
            Vector3.ProjectOnPlane(rb.linearVelocity, up).magnitude;

        float blend = Mathf.InverseLerp(
            steeringLimitStartSpeed,
            steeringLimitFullSpeed,
            groundSpeed
        );

        float availableSteering =
            Mathf.Lerp(steerAngle, highSpeedSteerAngle, blend);

        float steeringInput = AutomaticControl
            ? ControlInput.z
            : manualSteeringInput;

        noseWheel.steerAngle = steeringInput * availableSteering;

        bool braking =
            !AutomaticControl &&
            !IsEditingText() &&
            Input.GetKey(KeyCode.Space) &&
            (leftWheel.isGrounded || rightWheel.isGrounded);

        float targetBrake = braking ? 1f : 0f;

        float changeRate = targetBrake > BrakeInput
            ? brakeApplyRate
            : brakeReleaseRate;

        BrakeInput = Mathf.MoveTowards(
            BrakeInput,
            targetBrake,
            changeRate * Time.fixedDeltaTime
        );

        noseWheel.brakeTorque = 0f;
        leftWheel.brakeTorque = BrakeInput * brakeForce;
        rightWheel.brakeTorque = BrakeInput * brakeForce;

        noseWheel.motorTorque = 0.001f;
        leftWheel.motorTorque = BrakeInput > 0f ? 0f : 0.001f;
        rightWheel.motorTorque = BrakeInput > 0f ? 0f : 0.001f;
    }
}