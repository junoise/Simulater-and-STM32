using UnityEngine;

public class RunwayLandingArea : MonoBehaviour
{
    [Header("Cesium Terrain")]
    public Transform terrainRoot;

    [Header("Runway Endpoints")]
    public Transform startPoint;
    public Transform endPoint;

    [Header("Landing Detection")]
    public float width = 40f;
    public float heightTolerance = 3f;

    [Range(0f, 1f)]
    public float minimumUpNormal = 0.7f;

    public Vector3 Up =>
        Physics.gravity.sqrMagnitude > 0.001f
            ? -Physics.gravity.normalized
            : Vector3.up;

    public float Length
    {
        get
        {
            if (startPoint == null || endPoint == null)
            {
                return 0f;
            }

            return Vector3.ProjectOnPlane(
                endPoint.position - startPoint.position,
                Up
            ).magnitude;
        }
    }

    public Vector3 Forward
    {
        get
        {
            if (Length < 0.01f)
            {
                return Vector3.forward;
            }

            return Vector3.ProjectOnPlane(
                endPoint.position - startPoint.position,
                Up
            ).normalized;
        }
    }

    public bool IsConfigured =>
        terrainRoot != null &&
        terrainRoot.gameObject.activeInHierarchy &&
        startPoint != null &&
        endPoint != null &&
        Length > 10f &&
        width > 0f &&
        heightTolerance > 0f;

    public bool IsTerrainCollider(Collider target)
    {
        return target != null &&
               terrainRoot != null &&
               target.transform.IsChildOf(terrainRoot);
    }

    public bool ContainsPoint(Vector3 point)
    {
        if (!IsConfigured)
        {
            return false;
        }

        Vector3 forward = Forward;
        Vector3 right = Vector3.Cross(Up, forward).normalized;
        Vector3 offset = point - startPoint.position;

        float along = Vector3.Dot(offset, forward);

        if (along < 0f || along > Length)
        {
            return false;
        }

        float sideways = Mathf.Abs(Vector3.Dot(offset, right));

        if (sideways > width * 0.5f)
        {
            return false;
        }

        Vector3 center = Vector3.Lerp(
            startPoint.position,
            endPoint.position,
            along / Length
        );

        float heightError = Mathf.Abs(Vector3.Dot(point - center, Up));

        return heightError <= heightTolerance;
    }

    public bool ContainsContact(
        Collider contactCollider,
        Vector3 point,
        Vector3 normal
    )
    {
        return IsTerrainCollider(contactCollider) &&
               Vector3.Dot(normal.normalized, Up) >= minimumUpNormal &&
               ContainsPoint(point);
    }

    public bool TryRaycastTerrain(
        Vector3 origin,
        Vector3 direction,
        float distance,
        out RaycastHit result
    )
    {
        result = default;

        if (terrainRoot == null)
        {
            return false;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore
        );

        float nearestDistance = float.PositiveInfinity;
        bool found = false;

        foreach (RaycastHit hit in hits)
        {
            if (!IsTerrainCollider(hit.collider))
            {
                continue;
            }

            if (Vector3.Dot(hit.normal.normalized, Up) < minimumUpNormal)
            {
                continue;
            }

            if (hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = hit.distance;
            result = hit;
            found = true;
        }

        return found;
    }

    public bool TryGetGroundBelow(
        Vector3 position,
        out RaycastHit result
    )
    {
        return TryRaycastTerrain(
            position + Up * 200f,
            -Up,
            400f,
            out result
        );
    }

    [ContextMenu("Snap Endpoints To Terrain")]
    private void SnapEndpointsToTerrain()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("Exit Play mode before snapping endpoints.", this);
            return;
        }

        if (!IsConfigured)
        {
            Debug.LogWarning(
                "Assign terrain and separated runway endpoints first.",
                this
            );
            return;
        }

        bool startFound = TryGetGroundBelow(
            startPoint.position,
            out RaycastHit startHit
        );

        bool endFound = TryGetGroundBelow(
            endPoint.position,
            out RaycastHit endHit
        );

        if (!startFound || !endFound)
        {
            Debug.LogWarning(
                "Terrain collider not found below both endpoints. " +
                "Load the runway terrain and check endpoint positions.",
                this
            );
            return;
        }

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObjects(
            new Object[] { startPoint, endPoint },
            "Snap Runway Endpoints"
        );
#endif

        startPoint.position = startHit.point;
        endPoint.position = endHit.point;

#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            gameObject.scene
        );
#endif

        Debug.Log("Runway endpoints snapped to terrain.", this);
    }

    private void OnDrawGizmos()
    {
        if (startPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(startPoint.position, 5f);
        }

        if (endPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(endPoint.position, 5f);
        }

        if (!IsConfigured)
        {
            return;
        }

        Vector3 side =
            Vector3.Cross(Up, Forward).normalized * width * 0.5f;

        Vector3 a = startPoint.position;
        Vector3 b = endPoint.position;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(a - side, a + side);
        Gizmos.DrawLine(b - side, b + side);
        Gizmos.DrawLine(a - side, b - side);
        Gizmos.DrawLine(a + side, b + side);
        Gizmos.DrawLine(a, b);
    }
}