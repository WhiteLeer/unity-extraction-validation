using UnityEngine;

public class FxLabDirectionalLightAnimator : MonoBehaviour
{
    public Vector3 baseEuler = new Vector3(40.0f, -35.0f, 0.0f);
    public Vector2 pitchSwing = new Vector2(-10.0f, 10.0f);
    public float cycleSeconds = 18.0f;
    public float baseIntensity = 1.1f;
    public float intensitySwing = 0.25f;

    private Light _light;

    private void Awake()
    {
        _light = GetComponent<Light>();
    }

    private void Update()
    {
        float t = Mathf.Sin(Time.time * Mathf.PI * 2.0f / Mathf.Max(0.1f, cycleSeconds));
        float pitch = baseEuler.x + Mathf.Lerp(pitchSwing.x, pitchSwing.y, 0.5f * (t + 1.0f));
        transform.rotation = Quaternion.Euler(pitch, baseEuler.y, baseEuler.z);

        if (_light != null)
            _light.intensity = baseIntensity + t * intensitySwing;
    }
}
