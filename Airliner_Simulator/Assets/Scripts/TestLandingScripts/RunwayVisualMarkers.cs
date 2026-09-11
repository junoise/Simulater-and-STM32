using UnityEngine;

public class RunwayVisualMarkers : MonoBehaviour
{
    [Header("Materials")]
    public Material centerLineMaterial;
    public Material sideMarkerMaterial;

    [Header("Runway Dimensions (meters)")]
    public float runwayLength = 4000f;
    public float runwayWidth = 80f;

    [Header("Dashed Center Line")]
    public float dashLength = 25f;
    public float dashGap = 25f;
    public float dashWidth = 0.6f;

    [Header("Side Markers")]
    public float sideSpacing = 100f;
    public float sideOffset = 5f;
    public float sideHeight = 2f;

    [Header("Surface")]
    public float surfaceY = 0f;

    private const string GeneratedRootName = "GeneratedRunwayMarkers";

    [ContextMenu("Generate Markers")]
    private void GenerateMarkers()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning(
                "Exit Play mode before generating markers.",
                this
            );
            return;
        }

        if (transform.Find(GeneratedRootName) != null)
        {
            Debug.LogWarning(
                "Markers already exist. Delete GeneratedRunwayMarkers " +
                "before generating again.",
                this
            );
            return;
        }

        if (centerLineMaterial == null || sideMarkerMaterial == null)
        {
            Debug.LogWarning("Assign both materials first.", this);
            return;
        }

        if (runwayLength <= 0f ||
            runwayWidth <= 0f ||
            dashLength <= 0f ||
            dashGap < 0f ||
            dashWidth <= 0f ||
            sideSpacing <= 0f ||
            sideHeight <= 0f ||
            sideOffset < 0f)
        {
            Debug.LogWarning("Check marker dimensions.", this);
            return;
        }

        float dashStep = dashLength + dashGap;

        int dashCount = Mathf.CeilToInt(runwayLength / dashStep);
        int sidePairCount = Mathf.FloorToInt(
            runwayLength / sideSpacing
        ) + 1;

        if (dashCount > 1000 || sidePairCount > 500)
        {
            Debug.LogWarning(
                "Too many markers. Increase spacing or reduce length.",
                this
            );
            return;
        }

        GameObject root = new GameObject(GeneratedRootName);
        root.transform.SetParent(transform, false);

#if UNITY_EDITOR
        UnityEditor.Undo.RegisterCreatedObjectUndo(
            root,
            "Generate Runway Markers"
        );
#endif

        for (int i = 0; i < dashCount; i++)
        {
            float startZ = i * dashStep;
            float length = Mathf.Min(
                dashLength,
                runwayLength - startZ
            );

            CreateBox(
                root.transform,
                $"CenterDash_{i:000}",
                new Vector3(
                    0f,
                    surfaceY + 0.025f,
                    startZ + length * 0.5f
                ),
                new Vector3(dashWidth, 0.02f, length),
                centerLineMaterial
            );
        }

        float sideX = runwayWidth * 0.5f + sideOffset;

        float markerBottomY = surfaceY - 1.5f;
        float markerHeight = sideHeight + 1.5f;

        for (int i = 0; i < sidePairCount; i++)
        {
            float z = i * sideSpacing;
            float y = markerBottomY + markerHeight * 0.5f;

            CreateBox(
                root.transform,
                $"LeftMarker_{i:000}",
                new Vector3(-sideX, y, z),
                new Vector3(1f, markerHeight, 1f),
                sideMarkerMaterial
            );

            CreateBox(
                root.transform,
                $"RightMarker_{i:000}",
                new Vector3(sideX, y, z),
                new Vector3(1f, markerHeight, 1f),
                sideMarkerMaterial
            );
        }

#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
    gameObject.scene
);
#endif
    }

    private void CreateBox(
        Transform parent,
        string objectName,
        Vector3 localPosition,
        Vector3 localScale,
        Material material
    )
    {
        GameObject marker = GameObject.CreatePrimitive(
            PrimitiveType.Cube
        );

        marker.name = objectName;
        marker.transform.SetParent(parent, false);
        marker.transform.localPosition = localPosition;
        marker.transform.localRotation = Quaternion.identity;
        marker.transform.localScale = localScale;

        marker.GetComponent<MeshRenderer>().sharedMaterial = material;

        Collider markerCollider = marker.GetComponent<Collider>();

        if (markerCollider != null)
        {
            DestroyImmediate(markerCollider);
        }
    }
}