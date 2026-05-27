using UnityEngine;

public class FxLabCameraOrbit : MonoBehaviour
{
    public Transform target;
    public Vector3 targetOffset = new Vector3(0.0f, 1.3f, 0.0f);
    public float distance = 10.0f;
    public float minDistance = 4.0f;
    public float maxDistance = 18.0f;
    public float autoOrbitSpeed = 15.0f;

    [Header("Manual Control")]
    public bool autoOrbit = true;
    public float mouseSensitivity = 2.2f;
    public float pitchMin = -20.0f;
    public float pitchMax = 60.0f;

    private float _yaw = -20.0f;
    private float _pitch = 20.0f;

    private void LateUpdate()
    {
        if (target == null)
            return;

        if (Input.GetMouseButton(1))
        {
            _yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            _pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        }
        else if (autoOrbit)
        {
            _yaw += autoOrbitSpeed * Time.deltaTime;
        }

        _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);

        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(wheel) > 0.0001f)
        {
            distance = Mathf.Clamp(distance - wheel * 8.0f, minDistance, maxDistance);
        }

        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0.0f);
        Vector3 focus = target.position + targetOffset;
        Vector3 cameraPos = focus + rot * new Vector3(0.0f, 0.0f, -distance);

        transform.position = cameraPos;
        transform.rotation = rot;
    }
}
