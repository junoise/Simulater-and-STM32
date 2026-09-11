using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(-200)]
[RequireComponent(typeof(AirlinerPhysics))]
[RequireComponent(typeof(LandingSystem))]
public class CesiumGroundStart : MonoBehaviour
{
    [Header("Runway")]
    public RunwayLandingArea runwayArea;

    [Header("Ground Start")]
    public float distanceFromStart = 100f;
    public float rootHeightAboveGround = 1f;
    public float stableGroundTime = 0.5f;

    public bool IsWaiting { get; private set; } = true;
    public string Status { get; private set; } = "WAITING FOR TERRAIN";

    private Rigidbody rb;
    private AirlinerPhysics flight;
    private LandingSystem landing;
    private TurbulenceSystem turbulence;

    private bool resumeFlight;
    private bool resumeLanding;
    private bool resumeTurbulence;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        flight = GetComponent<AirlinerPhysics>();
        landing = GetComponent<LandingSystem>();
        turbulence = GetComponent<TurbulenceSystem>();

        resumeFlight = flight.enabled;
        resumeLanding = landing.enabled;
        resumeTurbulence = turbulence != null && turbulence.enabled;

        rb.isKinematic = true;
        flight.enabled = false;
        landing.enabled = false;

        if (turbulence != null)
        {
            turbulence.enabled = false;
        }
    }

    private void PlaceAircraft(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
        rb.position = position;
        rb.rotation = rotation;

        Physics.SyncTransforms();
    }

    private IEnumerator Start()
    {
        yield return null;

        if (runwayArea == null ||
            !runwayArea.IsConfigured ||
            runwayArea.Length < 50f ||
            !landing.HasValidSetup ||
            landing.runwayArea != runwayArea ||
            rootHeightAboveGround <= 0f ||
            stableGroundTime <= 0f)
        {
            Status = "SETUP ERROR: CHECK RUNWAY / SENSOR / WHEELS";
            Debug.LogError(Status, this);
            yield break;
        }

        Vector3 up = runwayArea.Up;

        float distance = Mathf.Clamp(
            distanceFromStart,
            20f,
            runwayArea.Length - 20f
        );

        Vector3 station = Vector3.Lerp(
            runwayArea.startPoint.position,
            runwayArea.endPoint.position,
            distance / runwayArea.Length
        );

        Quaternion rotation = Quaternion.LookRotation(
            runwayArea.Forward,
            up
        );

        PlaceAircraft(station + up * 10f, rotation);
        flight.SetThrottle(0f);

        WheelCollider[] wheels =
        {
            flight.noseWheel,
            flight.leftWheel,
            flight.rightWheel
        };

        float stableTime = 0f;
        float previousHeight = float.NaN;

        while (true)
        {
            bool allReady = true;
            float highestGround = float.NegativeInfinity;

            foreach (WheelCollider wheel in wheels)
            {
                Vector3 samplePoint =
                    wheel.transform.TransformPoint(wheel.center);

                bool found = runwayArea.TryGetGroundBelow(
                    samplePoint,
                    out RaycastHit hit
                );

                if (!found ||
                    !runwayArea.ContainsContact(
                        hit.collider,
                        hit.point,
                        hit.normal
                    ))
                {
                    allReady = false;
                    break;
                }

                float height = Vector3.Dot(hit.point - station, up);
                highestGround = Mathf.Max(highestGround, height);
            }

            if (allReady)
            {
                bool heightStable =
                    !float.IsNaN(previousHeight) &&
                    Mathf.Abs(highestGround - previousHeight) <= 0.1f;

                stableTime = heightStable ? stableTime + 0.1f : 0f;
                previousHeight = highestGround;

                Status = "TERRAIN FOUND: CHECKING STABILITY";

                if (stableTime >= stableGroundTime)
                {
                    Vector3 releasePosition =
                        station +
                        up * (highestGround + rootHeightAboveGround);

                    PlaceAircraft(releasePosition, rotation);
                    break;
                }
            }
            else
            {
                stableTime = 0f;
                previousHeight = float.NaN;
                Status = "WAITING: CHECK TERRAIN LOADING / RUNWAY AREA";
            }

            yield return new WaitForSeconds(0.1f);
        }

        yield return new WaitForFixedUpdate();

        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        flight.ResetGroundControls();
        flight.SetThrottle(0f);
        landing.ResetSession();

        flight.enabled = resumeFlight;
        landing.enabled = resumeLanding;

        if (turbulence != null)
        {
            turbulence.enabled = resumeTurbulence;
        }

        rb.WakeUp();

        IsWaiting = false;
        Status = "READY";
    }
}