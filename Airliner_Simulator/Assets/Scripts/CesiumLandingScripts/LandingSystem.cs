using UnityEngine;

public enum LandingPhase
{
    Grounded,
    Airborne,
    Touchdown,
    Rollout,
    Stopped,
    Failed
}

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(AirlinerPhysics))]
public class LandingSystem : MonoBehaviour
{
    [Header("Cesium Runway Mode")]
    public RunwayLandingArea runwayArea;

    [Header("Existing Test Runway Mode")]
    public Collider runwayCollider;
    public LayerMask groundMask;

    [Header("Height Sensor")]
    public Transform heightSensor;
    public float groundRayDistance = 1000f;

    [Header("Detection Timing")]
    public float airborneArmTime = 1f;
    public float airborneConfirmTime = 0.2f;
    public float touchdownDisplayTime = 1f;

    [Header("Project Test Thresholds")]
    public float hardLandingSinkRate = 3f;
    public float hardLandingBankAngle = 8f;
    public float stopSpeed = 1f;
    public float stopAngularSpeed = 0.1f;
    public float stopHoldTime = 2f;

    public LandingPhase Phase { get; private set; }

    public bool NoseGrounded { get; private set; }
    public bool LeftGrounded { get; private set; }
    public bool RightGrounded { get; private set; }

    public bool HasGroundReading { get; private set; }
    public float SensorHeight { get; private set; }
    public float VerticalSpeed { get; private set; }
    public float GroundSpeed { get; private set; }
    public float BankAngle { get; private set; }

    public int TouchdownCount { get; private set; }
    public float WorstTouchdownSinkRate { get; private set; }
    public float WorstTouchdownBank { get; private set; }

    public bool NoseFirst { get; private set; }
    public bool BodyCollision { get; private set; }
    public bool OffRunway { get; private set; }

    public bool IsHardLanding =>
        WorstTouchdownSinkRate > hardLandingSinkRate ||
        WorstTouchdownBank > hardLandingBankAngle;

    private Rigidbody rb;
    private AirlinerPhysics flight;

    private bool armed;
    private bool pendingTouchdown;

    private float airborneTime;
    private float stoppedTime;
    private float lastAirborneVerticalSpeed;
    private float lastTouchdownTime;

    private int ignorePhysicsSteps;

    private Vector3 Up =>
        Physics.gravity.sqrMagnitude > 0.001f
            ? -Physics.gravity.normalized
            : Vector3.up;

    public bool HasValidSetup
    {
        get
        {
            if (flight == null ||
                heightSensor == null ||
                flight.noseWheel == null ||
                flight.leftWheel == null ||
                flight.rightWheel == null)
            {
                return false;
            }

            if (runwayArea != null)
            {
                return runwayArea.IsConfigured;
            }

            return runwayCollider != null && groundMask.value != 0;
        }
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        flight = GetComponent<AirlinerPhysics>();

        if (!HasValidSetup)
        {
            Debug.LogError(
                "LandingSystem: Check runway, sensor and wheel references.",
                this
            );

            enabled = false;
            return;
        }

        ResetSession();
    }

    public void ResetSession()
    {
        Phase = LandingPhase.Grounded;

        NoseGrounded = false;
        LeftGrounded = false;
        RightGrounded = false;

        HasGroundReading = false;
        SensorHeight = float.NaN;
        VerticalSpeed = 0f;
        GroundSpeed = 0f;
        BankAngle = 0f;

        TouchdownCount = 0;
        WorstTouchdownSinkRate = 0f;
        WorstTouchdownBank = 0f;

        NoseFirst = false;
        BodyCollision = false;
        OffRunway = false;

        armed = false;
        pendingTouchdown = true;

        airborneTime = 0f;
        stoppedTime = 0f;
        lastTouchdownTime = float.NegativeInfinity;

        lastAirborneVerticalSpeed = rb != null
            ? Vector3.Dot(rb.linearVelocity, Up)
            : 0f;

        ignorePhysicsSteps = 2;
    }

    private bool ReadWheel(
        WheelCollider wheel,
        out bool onRunway
    )
    {
        onRunway = false;

        if (!wheel.GetGroundHit(out WheelHit hit))
        {
            return false;
        }

        if (runwayArea != null)
        {
            onRunway = runwayArea.ContainsContact(
                hit.collider,
                hit.point,
                hit.normal
            );
        }
        else
        {
            onRunway = hit.collider == runwayCollider;
        }

        return true;
    }

    private void UpdateHeightReading(Vector3 up)
    {
        RaycastHit hit;

        if (runwayArea != null)
        {
            HasGroundReading = runwayArea.TryRaycastTerrain(
                heightSensor.position,
                -up,
                groundRayDistance,
                out hit
            );
        }
        else
        {
            HasGroundReading = Physics.Raycast(
                heightSensor.position,
                -up,
                out hit,
                groundRayDistance,
                groundMask,
                QueryTriggerInteraction.Ignore
            );
        }

        SensorHeight = HasGroundReading
            ? hit.distance
            : float.NaN;
    }

    private void FixedUpdate()
    {
        if (ignorePhysicsSteps > 0)
        {
            ignorePhysicsSteps--;
            return;
        }

        float dt = Time.fixedDeltaTime;
        Vector3 up = Up;

        VerticalSpeed = Vector3.Dot(rb.linearVelocity, up);

        GroundSpeed =
            Vector3.ProjectOnPlane(rb.linearVelocity, up).magnitude;

        BankAngle = Mathf.Abs(
            Mathf.Atan2(
                Vector3.Dot(transform.right, up),
                Vector3.Dot(transform.up, up)
            ) * Mathf.Rad2Deg
        );

        UpdateHeightReading(up);

        NoseGrounded = ReadWheel(
            flight.noseWheel,
            out bool noseOnRunway
        );

        LeftGrounded = ReadWheel(
            flight.leftWheel,
            out bool leftOnRunway
        );

        RightGrounded = ReadWheel(
            flight.rightWheel,
            out bool rightOnRunway
        );

        bool anyGrounded =
            NoseGrounded || LeftGrounded || RightGrounded;

        bool mainGrounded = LeftGrounded || RightGrounded;

        if (!anyGrounded)
        {
            airborneTime += dt;
            stoppedTime = 0f;
            lastAirborneVerticalSpeed = VerticalSpeed;

            if (airborneTime >= airborneArmTime)
            {
                armed = true;
            }

            if (airborneTime >= airborneConfirmTime)
            {
                pendingTouchdown = true;
            }
        }
        else
        {
            if (armed)
            {
                bool outsideRunway =
                    (NoseGrounded && !noseOnRunway) ||
                    (LeftGrounded && !leftOnRunway) ||
                    (RightGrounded && !rightOnRunway);

                OffRunway |= outsideRunway;

                if (pendingTouchdown && NoseGrounded && !mainGrounded)
                {
                    NoseFirst = true;
                }

                if (pendingTouchdown && mainGrounded)
                {
                    TouchdownCount++;

                    float sinkRate =
                        Mathf.Max(0f, -lastAirborneVerticalSpeed);

                    WorstTouchdownSinkRate = Mathf.Max(
                        WorstTouchdownSinkRate,
                        sinkRate
                    );

                    WorstTouchdownBank = Mathf.Max(
                        WorstTouchdownBank,
                        BankAngle
                    );

                    pendingTouchdown = false;
                    lastTouchdownTime = Time.time;
                }
            }

            airborneTime = 0f;
        }

        if (BodyCollision || OffRunway)
        {
            Phase = LandingPhase.Failed;
            stoppedTime = 0f;
            return;
        }

        if (!anyGrounded)
        {
            if (airborneTime >= airborneConfirmTime)
            {
                Phase = LandingPhase.Airborne;
            }

            return;
        }

        if (!armed)
        {
            Phase = LandingPhase.Grounded;
            stoppedTime = 0f;
            return;
        }

        bool allWheelsOnRunway =
            noseOnRunway && leftOnRunway && rightOnRunway;

        bool stableStop =
            TouchdownCount > 0 &&
            allWheelsOnRunway &&
            rb.linearVelocity.magnitude <= stopSpeed &&
            rb.angularVelocity.magnitude <= stopAngularSpeed &&
            BankAngle <= hardLandingBankAngle;

        stoppedTime = stableStop ? stoppedTime + dt : 0f;

        if (stoppedTime >= stopHoldTime)
        {
            Phase = LandingPhase.Stopped;
        }
        else if (TouchdownCount == 0 ||
                 Time.time - lastTouchdownTime < touchdownDisplayTime)
        {
            Phase = LandingPhase.Touchdown;
        }
        else
        {
            Phase = LandingPhase.Rollout;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!enabled || !armed || ignorePhysicsSteps > 0)
        {
            return;
        }

        for (int i = 0; i < collision.contactCount; i++)
        {
            Collider ownCollider = collision.GetContact(i).thisCollider;

            if (ownCollider is WheelCollider)
            {
                continue;
            }

            BodyCollision = true;
            Phase = LandingPhase.Failed;
            break;
        }
    }
}