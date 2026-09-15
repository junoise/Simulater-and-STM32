using UnityEngine;
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

    public float BrakeInput { get; private set; }

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

        SetThrottle(throttle);
    }

    public void SetThrottle(float value)
    {
        throttle = Mathf.Clamp01(value);

        if (throttleSlider != null)
        {
            throttleSlider.SetValueWithoutNotify(throttle);
        }
    }

    public void ResetGroundControls()
    {
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
        }
    }

    private void FixedUpdate()
    {
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

        float pitchDamping =
            localAngularVelocity.x * forwardSpeed * 50f;

        float yawDamping =
            localAngularVelocity.y * forwardSpeed * 50f;

        rb.AddRelativeTorque(Vector3.right * -pitchDamping);
        rb.AddRelativeTorque(Vector3.up * -yawDamping);
    }

    private float ReadYawInput()
    {
        float input = 0f;

        if (Input.GetKey(KeyCode.E))
        {
            input += 1f;
        }

        if (Input.GetKey(KeyCode.Q))
        {
            input -= 1f;
        }

        return input;
    }

    private void ApplyControlSurfaces()
    {
        float pitchInput = Input.GetAxis("Vertical");
        float rollInput = Input.GetAxis("Horizontal");
        float yawInput = ReadYawInput();

        float forwardSpeed =
            Vector3.Dot(rb.linearVelocity, transform.forward);

        float controlEfficiency = Mathf.Max(0f, forwardSpeed * 0.1f);

        rb.AddRelativeTorque(
            Vector3.right * pitchInput * pitchPower * controlEfficiency
        );

        rb.AddRelativeTorque(
            Vector3.forward * -rollInput * rollPower * controlEfficiency
        );

        rb.AddRelativeTorque(
            Vector3.up * yawInput * yawPower * controlEfficiency
        );
    }

    private void ApplyWheelControls()
    {
        Vector3 up = Physics.gravity.sqrMagnitude > 0.001f
            ? -Physics.gravity.normalized
            : Vector3.up;

        float groundSpeed =
            Vector3.ProjectOnPlane(rb.linearVelocity, up).magnitude;

        float speedBlend = Mathf.InverseLerp(
            steeringLimitStartSpeed,
            steeringLimitFullSpeed,
            groundSpeed
        );

        float availableSteering = Mathf.Lerp(
            steerAngle,
            highSpeedSteerAngle,
            speedBlend
        );

        noseWheel.steerAngle = ReadYawInput() * availableSteering;

        bool mainGearGrounded =
            leftWheel.isGrounded || rightWheel.isGrounded;

        float targetBrake =
            Input.GetKey(KeyCode.Space) && mainGearGrounded ? 1f : 0f;

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