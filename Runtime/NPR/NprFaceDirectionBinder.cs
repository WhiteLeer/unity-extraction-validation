using UnityEngine;

[DisallowMultipleComponent]
public class NprFaceDirectionBinder : MonoBehaviour
{
    public Transform HeadBone;
    public bool AutoFindHeadBone = true;
    public string HeadNameKeyword = "Head";

    private static readonly int HeadForwardId = Shader.PropertyToID("_HeadForwardWS");
    private static readonly int HeadRightId = Shader.PropertyToID("_HeadRightWS");

    private Renderer[] _renderers;
    private MaterialPropertyBlock _mpb;

    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        _mpb = new MaterialPropertyBlock();
        if (AutoFindHeadBone && HeadBone == null)
        {
            HeadBone = FindHeadByName(transform, HeadNameKeyword);
        }
    }

    private void LateUpdate()
    {
        Transform head = HeadBone != null ? HeadBone : transform;
        Vector3 f = head.forward;
        Vector3 r = head.right;

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer rd = _renderers[i];
            if (rd == null) continue;
            rd.GetPropertyBlock(_mpb);
            _mpb.SetVector(HeadForwardId, new Vector4(f.x, f.y, f.z, 0f));
            _mpb.SetVector(HeadRightId, new Vector4(r.x, r.y, r.z, 0f));
            rd.SetPropertyBlock(_mpb);
        }
    }

    private static Transform FindHeadByName(Transform root, string keyword)
    {
        if (root == null) return null;
        string key = string.IsNullOrEmpty(keyword) ? "Head" : keyword;
        Transform[] trs = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < trs.Length; i++)
        {
            Transform t = trs[i];
            if (t.name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return t;
            }
        }
        return null;
    }
}

