using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    [Header("Target Setup")]
    public Transform target;      

    [Header("Camera Settings")]
    public float distance = 50f;   
    public float sensitivity = 3f; 

    private float rotX = 15f; 
    private float rotY = 0f;  

    void LateUpdate()
    {
        if (target == null) return;

        if (Input.GetMouseButton(1))
        {
            rotY += Input.GetAxis("Mouse X") * sensitivity;
            rotX -= Input.GetAxis("Mouse Y") * sensitivity;

            rotX = Mathf.Clamp(rotX, -5f, 80f);
        }

        Quaternion rotation = Quaternion.Euler(rotX, rotY, 0);
        Vector3 position = target.position + (rotation * new Vector3(0, 0, -distance));

        transform.position = position;
        transform.LookAt(target.position);
    }
}
