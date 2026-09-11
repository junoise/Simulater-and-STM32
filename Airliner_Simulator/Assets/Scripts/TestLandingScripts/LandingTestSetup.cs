using UnityEngine;

[DefaultExecutionOrder(200)]
[RequireComponent(typeof(AirlinerPhysics))]
[RequireComponent(typeof(LandingSystem))]
public class LandingTestSetup : MonoBehaviour
{
    [Header("Test Mode")]
    public bool startInApproach = true;
    public Transform approachStart;

    [Header("Initial Velocity")]
    public float forwardSpeed = 100f;
    public float verticalSpeed = -2f;

    [Header("Initial Throttle")]
    [Range(0f, 1f)]
    public float initialThrottle = 0.4f;

    private Rigidbody rb;
    private AirlinerPhysics flight;
    private LandingSystem landing;
    private FuelSystem fuel;

    private Color initialFuelTextColor = Color.white;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        flight = GetComponent<AirlinerPhysics>();
        landing = GetComponent<LandingSystem>();
        fuel = GetComponent<FuelSystem>();

        if (fuel != null && fuel.fuelUIText != null)
        {
            initialFuelTextColor = fuel.fuelUIText.color;
        }
    }

    private void Start()
    {
        if (startInApproach)
        {
            StartApproachTest();
        }
    }

    public void StartApproachTest()
    {
        if (approachStart == null ||
            !flight.enabled ||
            !landing.enabled)
        {
            Debug.LogError(
                "LandingTestSetup: Check start point and flight systems.",
                this
            );

            return;
        }

        Vector3 up = Physics.gravity.sqrMagnitude > 0.001f
            ? -Physics.gravity.normalized
            : Vector3.up;

        Vector3 horizontalForward =
            Vector3.ProjectOnPlane(approachStart.forward, up).normalized;

        if (horizontalForward.sqrMagnitude < 0.5f)
        {
            Debug.LogError(
                "LandingTestSetup: Start direction must not be vertical.",
                this
            );

            return;
        }

        flight.ResetGroundControls();

        if (fuel != null)
        {
            fuel.currentFuelMass = fuel.maxFuelMass;
            fuel.isEngineStarved = false;
            fuel.isLowFuelWarning = false;

            rb.mass = fuel.emptyWeight + fuel.currentFuelMass;

            if (fuel.fuelUIText != null)
            {
                fuel.fuelUIText.color = initialFuelTextColor;
            }
        }

        transform.SetPositionAndRotation(
            approachStart.position,
            approachStart.rotation
        );

        rb.position = approachStart.position;
        rb.rotation = approachStart.rotation;

        rb.linearVelocity =
            horizontalForward * forwardSpeed +
            up * verticalSpeed;

        rb.angularVelocity = Vector3.zero;

        flight.SetThrottle(initialThrottle);
        landing.ResetSession();

        Physics.SyncTransforms();
        rb.WakeUp();
    }
}