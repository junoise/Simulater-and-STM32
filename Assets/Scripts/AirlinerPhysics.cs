using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class AirlinerPhysics : MonoBehaviour
{
    private Rigidbody rb;

    [Header("Engine & Thrust")]
    public float maxThrust = 500000f;
    [Range(0, 1)] public float throttle = 0f;

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

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        ApplyThrust();
        ApplyAerodynamics();
        ApplyControlSurfaces();
        ApplyWheelControls();
    }

    private void ApplyThrust()
    {
        Vector3 thrustForce = transform.forward * (maxThrust * throttle);
        rb.AddForce(thrustForce);
    }

    private void ApplyAerodynamics()
    {
        float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);

        if (forwardSpeed > 2f)
        {
            float dynamicPressure = forwardSpeed * forwardSpeed;
            Vector3 velocityDir = rb.linearVelocity.normalized;

            float aoa = Vector3.SignedAngle(transform.forward, velocityDir, transform.right);
            
            if (aoa > 180f) aoa -= 360f;

            if (aoa > -1f && aoa < 1f) aoa = 0f;
            aoa = Mathf.Clamp(aoa, -15f, 15f);
            float effectiveAoA = aoa;

            float liftForce = dynamicPressure * liftCoefficient * (effectiveAoA * 0.1f);

            float pitchInput = Input.GetAxis("Vertical");
            bool isTakeOffCommand = (pitchInput < -0.1f);

            bool isGrounded = noseWheel.isGrounded || leftWheel.isGrounded || rightWheel.isGrounded;

            if (isGrounded && !isTakeOffCommand)
            {
                float maxGroundedLift = rb.mass * 9.81f * 0.9f;
                liftForce = Mathf.Min(liftForce, maxGroundedLift);
            }

            rb.AddForce(transform.up * liftForce);

            float dragForce = dynamicPressure * dragCoefficient;
            rb.AddForce(-transform.forward * dragForce);

            Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
            float sideDrag = -localVelocity.x * forwardSpeed * 200f;
            rb.AddRelativeForce(Vector3.right * sideDrag);

            float slipAngle = Vector3.SignedAngle(transform.forward, velocityDir, transform.up);
            float weathervaneTorque = slipAngle * forwardSpeed * 5000f;
            rb.AddRelativeTorque(Vector3.up * weathervaneTorque);

            Vector3 localAngularVel = transform.InverseTransformDirection(rb.angularVelocity);
            float pitchDampingForce = localAngularVel.x * forwardSpeed * 50f;
            float yawDampingForce = localAngularVel.y * forwardSpeed * 50f;

            rb.AddRelativeTorque(Vector3.right * -pitchDampingForce);
            rb.AddRelativeTorque(Vector3.up * -yawDampingForce);
        }
    }

    private void ApplyControlSurfaces()
    {
        float pitchInput = Input.GetAxis("Vertical");
        float rollInput = Input.GetAxis("Horizontal");

        float yawInput = 0f;
        if (Input.GetKey(KeyCode.E)) yawInput = 1f;
        if (Input.GetKey(KeyCode.Q)) yawInput = -1f;

        float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
        float controlEfficiency = Mathf.Max(0, forwardSpeed * 0.1f);

        rb.AddRelativeTorque(Vector3.right * pitchInput * pitchPower * controlEfficiency);
        rb.AddRelativeTorque(Vector3.forward * -rollInput * rollPower * controlEfficiency);
        rb.AddRelativeTorque(Vector3.up * yawInput * yawPower * controlEfficiency);
    }

    private void ApplyWheelControls()
    {
        float yawInput = 0f;
        if (Input.GetKey(KeyCode.E)) yawInput = 1f;
        if (Input.GetKey(KeyCode.Q)) yawInput = -1f;

        noseWheel.steerAngle = yawInput * steerAngle;

        noseWheel.motorTorque = 0.001f;
        leftWheel.motorTorque = 0.001f;
        rightWheel.motorTorque = 0.001f;

        if (Input.GetKey(KeyCode.Space))
        {
            leftWheel.brakeTorque = brakeForce;
            rightWheel.brakeTorque = brakeForce;

            leftWheel.motorTorque = 0f;
            rightWheel.motorTorque = 0f;
        }
        else
        {
            leftWheel.brakeTorque = 0f;
            rightWheel.brakeTorque = 0f;
        }
    }
}