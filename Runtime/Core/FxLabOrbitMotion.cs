using UnityEngine;

public class FxLabOrbitMotion : MonoBehaviour
{
    public Vector3 center = Vector3.zero;
    public Vector3 axis = Vector3.up;
    public float radius = 3.0f;
    public float angularSpeed = 35.0f;

    [Header("Optional Bob")]
    public float bobAmplitude = 0.0f;
    public float bobFrequency = 1.0f;

    [Header("Optional Self Spin")]
    public Vector3 selfSpinAxis = Vector3.up;
    public float selfSpinSpeed = 0.0f;

    private Vector3 _normalizedAxis;
    private Vector3 _planeRight;
    private Vector3 _planeForward;

    private void Awake()
    {
        _normalizedAxis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.up;

        Vector3 reference = Mathf.Abs(Vector3.Dot(_normalizedAxis, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
        _planeRight = Vector3.Cross(_normalizedAxis, reference).normalized;
        _planeForward = Vector3.Cross(_normalizedAxis, _planeRight).normalized;
    }

    private void Update()
    {
        float angle = Time.time * angularSpeed * Mathf.Deg2Rad;
        Vector3 orbitOffset = (_planeRight * Mathf.Cos(angle) + _planeForward * Mathf.Sin(angle)) * radius;

        float bob = bobAmplitude > 0.0f ? Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2.0f) * bobAmplitude : 0.0f;
        Vector3 bobOffset = _normalizedAxis * bob;

        transform.position = center + orbitOffset + bobOffset;

        if (selfSpinSpeed != 0.0f)
        {
            transform.Rotate(selfSpinAxis.normalized, selfSpinSpeed * Time.deltaTime, Space.Self);
        }
    }
}
