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

    [Header("Input Transition")]
    public float controlInputRate = 3f;

    public float BrakeInput { get; private set; }

    public Vector3 ControlInput { get; private set; }

    public bool AutomaticControl { get; private set; }

    public bool AnyWheelGrounded =>
        (noseWheel != null && noseWheel.isGrounded) ||
        (leftWheel != null && leftWheel.isGrounded) ||
        (rightWheel != null && rightWheel.isGrounded);

    private Vector3 automaticInput;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = centerOfMass;

        if (fuelSystem == null)
        {
            fuelSystem = GetComponent<FuelSystem>();
        }

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
        {
            return false;
        }

        GameObject selected =
            EventSystem.current.currentSelectedGameObject;

        if (selected == null)
        {
            return false;
        }

        TMP_InputField field =
            selected.GetComponentInParent<TMP_InputField>();

        return field != null && field.isFocused;
    }

    public static bool HasManualControlInput()
    {
        if (IsEditingText())
        {
            return false;
        }

        return Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.2f ||
               Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.2f ||
               Input.GetKey(KeyCode.Q) ||
               Input.GetKey(KeyCode.E) ||
               Input.GetKey(KeyCode.Space);
    }

    private void WriteThrottle(float value)
    {
        throttle = Mathf.Clamp01(value);

        if (throttleSlider != null)
        {
            throttleSlider.SetValueWithoutNotify(throttle);
        }
    }
    public void SetThrottle(float value)
    {
        if (!AutomaticControl)
        {
            WriteThrottle(value);
        }
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
        {
            throttleSlider.interactable = true;
        }
    }

    private void OnDisable()
    {
        ReleaseAutomaticControl();
    }

    public void ResetGroundControls()
    {
        ReleaseAutomaticControl();
        ControlInput = Vector3.zero;
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

    private Vector3 ReadManualInput()
    {
        if (IsEditingText())
        {
            return Vector3.zero;
        }

        float yaw = 0f;

        if (Input.GetKey(KeyCode.E)) yaw += 1f;
        if (Input.GetKey(KeyCode.Q)) yaw -= 1f;

        return new Vector3(
            Input.GetAxis("Vertical"),
            Input.GetAxis("Horizontal"),
            yaw
        );
    }

    private void FixedUpdate()
    {
        if (rb == null || rb.isKinematic)
        {
            return;
        }

        Vector3 requested = AutomaticControl
            ? automaticInput
            : ReadManualInput();

        float step = Mathf.Max(0f, controlInputRate) * Time.fixedDeltaTime;

        ControlInput = new Vector3(
            Mathf.MoveTowards(ControlInput.x, requested.x, step),
            Mathf.MoveTowards(ControlInput.y, requested.y, step),
            Mathf.MoveTowards(ControlInput.z, requested.z, step)
        );

        ApplyThrust();
        ApplyAerodynamics();
        ApplyControlSurfaces();
        ApplyWheelControls();
    }

    private void ApplyThrust()
    {
        float currentThrust = maxThrust * throttle;

        if (fuelSystem != null && fuelSystem.isEngineStarved)
        {
            currentThrust = 0f;
        }

        rb.AddForce(transform.forward * currentThrust);
    }

    private void ApplyAerodynamics()
    {
        Vector3 localVelocity =
            transform.InverseTransformDirection(rb.linearVelocity);

        float forwardSpeed = localVelocity.z;

        if (forwardSpeed <= 2f)
        {
            return;
        }

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

        float slipAngle = Mathf.Atan2(
            localVelocity.x,
            forwardSpeed
        ) * Mathf.Rad2Deg;

        float weathervaneTorque = slipAngle * forwardSpeed * 5000f;
        rb.AddRelativeTorque(Vector3.up * weathervaneTorque);

        Vector3 localAngularVelocity =
            transform.InverseTransformDirection(rb.angularVelocity);

        rb.AddRelativeTorque(
            Vector3.right *
            (-localAngularVelocity.x * forwardSpeed * 50f)
        );

        rb.AddRelativeTorque(
            Vector3.up *
            (-localAngularVelocity.y * forwardSpeed * 50f)
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

        noseWheel.steerAngle = ControlInput.z * availableSteering;

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