using UnityEngine;

public class FxLabSpin : MonoBehaviour
{
    public Vector3 axis = new Vector3(0.0f, 1.0f, 0.0f);
    public float speed = 45.0f;

    private void Update()
    {
        if (speed == 0.0f || axis.sqrMagnitude < 0.0001f)
            return;

        transform.Rotate(axis.normalized, speed * Time.deltaTime, Space.Self);
    }
}
