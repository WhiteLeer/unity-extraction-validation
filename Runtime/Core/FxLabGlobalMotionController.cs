using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[ExecuteAlways]
public class FxLabGlobalMotionController : MonoBehaviour
{
    [Header("Global")]
    [Min(0f)]
    public float globalSpeed = 1.0f;

    [Tooltip("同步到 Time.timeScale（仅 Play 模式）")]
    public bool syncTimeScale = false;

    [Header("Category Multipliers")]
    [Min(0f)] public float spinMultiplier = 1.0f;
    [Min(0f)] public float orbitMultiplier = 1.0f;
    [Min(0f)] public float cameraOrbitMultiplier = 1.0f;
    [Min(0f)] public float lightCycleMultiplier = 1.0f;

    [Header("Scan")]
    public bool includeInactive = true;

    private readonly List<FxLabSpin> _spins = new List<FxLabSpin>();
    private readonly List<float> _spinBase = new List<float>();

    private readonly List<FxLabOrbitMotion> _orbits = new List<FxLabOrbitMotion>();
    private readonly List<float> _orbitAngularBase = new List<float>();
    private readonly List<float> _orbitSelfSpinBase = new List<float>();

    private readonly List<FxLabCameraOrbit> _cameraOrbits = new List<FxLabCameraOrbit>();
    private readonly List<float> _cameraOrbitBase = new List<float>();

    private readonly List<FxLabDirectionalLightAnimator> _lights = new List<FxLabDirectionalLightAnimator>();
    private readonly List<float> _lightCycleBase = new List<float>();

    private float _lastAppliedGlobal = -1f;
    private float _lastAppliedSpin = -1f;
    private float _lastAppliedOrbit = -1f;
    private float _lastAppliedCam = -1f;
    private float _lastAppliedLight = -1f;
    private bool _scanned;

    private void OnEnable()
    {
        ScanTargets();
        ApplySpeed(force: true);
    }

    private void OnValidate()
    {
        globalSpeed = Mathf.Max(0f, globalSpeed);
        spinMultiplier = Mathf.Max(0f, spinMultiplier);
        orbitMultiplier = Mathf.Max(0f, orbitMultiplier);
        cameraOrbitMultiplier = Mathf.Max(0f, cameraOrbitMultiplier);
        lightCycleMultiplier = Mathf.Max(0f, lightCycleMultiplier);

        if (!_scanned)
            ScanTargets();

        ApplySpeed(force: true);
    }

    private void Update()
    {
        if (!_scanned)
            ScanTargets();

        ApplySpeed(force: false);
    }

    [ContextMenu("Rescan Targets")]
    public void RescanTargets()
    {
        ScanTargets();
        ApplySpeed(force: true);
    }

    [ContextMenu("Reset Demo Default Speeds")]
    public void ResetDemoDefaultSpeeds()
    {
        // Match defaults created by DynamicFxLabBuilder.
        for (int i = 0; i < _spins.Count; i++)
        {
            FxLabSpin s = _spins[i];
            if (s == null) continue;
            if (s.gameObject.name == "OBJ_DoF_Near") s.speed = 22.0f;
            else if (s.gameObject.name == "OBJ_DoF_Far") s.speed = -25.0f;
        }

        for (int i = 0; i < _orbits.Count; i++)
        {
            FxLabOrbitMotion o = _orbits[i];
            if (o == null) continue;

            if (o.gameObject.name == "OBJ_DoF_Mid")
            {
                o.angularSpeed = 30.0f;
                o.selfSpinSpeed = 28.0f;
            }
            else if (o.gameObject.name == "FX_Emitter_Bloom")
            {
                o.angularSpeed = 52.0f;
                o.selfSpinSpeed = 0.0f;
            }
            else if (o.gameObject.name == "FX_Runner_Reflection")
            {
                o.angularSpeed = -68.0f;
                o.selfSpinSpeed = 0.0f;
            }
            else if (o.gameObject.name == "LGT_Point_BloomOrbit")
            {
                o.angularSpeed = -54.0f;
                o.selfSpinSpeed = 0.0f;
            }
        }

        for (int i = 0; i < _cameraOrbits.Count; i++)
        {
            FxLabCameraOrbit c = _cameraOrbits[i];
            if (c == null) continue;
            if (c.gameObject.name == "CAM_Main") c.autoOrbitSpeed = 14.0f;
        }

        for (int i = 0; i < _lights.Count; i++)
        {
            FxLabDirectionalLightAnimator l = _lights[i];
            if (l == null) continue;
            if (l.gameObject.name == "LGT_Directional") l.cycleSeconds = 18.0f;
        }

        globalSpeed = 1.0f;
        spinMultiplier = 1.0f;
        orbitMultiplier = 1.0f;
        cameraOrbitMultiplier = 1.0f;
        lightCycleMultiplier = 1.0f;

        ScanTargets();
        ApplySpeed(force: true);
    }

    private void ScanTargets()
    {
        _spins.Clear();
        _spinBase.Clear();
        _orbits.Clear();
        _orbitAngularBase.Clear();
        _orbitSelfSpinBase.Clear();
        _cameraOrbits.Clear();
        _cameraOrbitBase.Clear();
        _lights.Clear();
        _lightCycleBase.Clear();

        FxLabSpin[] spins = GetComponentsInChildren<FxLabSpin>(includeInactive);
        for (int i = 0; i < spins.Length; i++)
        {
            if (spins[i] == null) continue;
            _spins.Add(spins[i]);
            _spinBase.Add(spins[i].speed);
        }

        FxLabOrbitMotion[] orbits = GetComponentsInChildren<FxLabOrbitMotion>(includeInactive);
        for (int i = 0; i < orbits.Length; i++)
        {
            if (orbits[i] == null) continue;
            _orbits.Add(orbits[i]);
            _orbitAngularBase.Add(orbits[i].angularSpeed);
            _orbitSelfSpinBase.Add(orbits[i].selfSpinSpeed);
        }

        FxLabCameraOrbit[] cameraOrbits = GetComponentsInChildren<FxLabCameraOrbit>(includeInactive);
        for (int i = 0; i < cameraOrbits.Length; i++)
        {
            if (cameraOrbits[i] == null) continue;
            _cameraOrbits.Add(cameraOrbits[i]);
            _cameraOrbitBase.Add(cameraOrbits[i].autoOrbitSpeed);
        }

        FxLabDirectionalLightAnimator[] lights = GetComponentsInChildren<FxLabDirectionalLightAnimator>(includeInactive);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] == null) continue;
            _lights.Add(lights[i]);
            _lightCycleBase.Add(lights[i].cycleSeconds);
        }

        _scanned = true;
    }

    private void ApplySpeed(bool force)
    {
        if (!force &&
            Mathf.Approximately(_lastAppliedGlobal, globalSpeed) &&
            Mathf.Approximately(_lastAppliedSpin, spinMultiplier) &&
            Mathf.Approximately(_lastAppliedOrbit, orbitMultiplier) &&
            Mathf.Approximately(_lastAppliedCam, cameraOrbitMultiplier) &&
            Mathf.Approximately(_lastAppliedLight, lightCycleMultiplier))
        {
            return;
        }

        float spinScale = globalSpeed * spinMultiplier;
        float orbitScale = globalSpeed * orbitMultiplier;
        float camScale = globalSpeed * cameraOrbitMultiplier;
        float lightScale = Mathf.Max(0.0001f, globalSpeed * lightCycleMultiplier);

        for (int i = 0; i < _spins.Count; i++)
        {
            if (_spins[i] == null) continue;
            _spins[i].speed = _spinBase[i] * spinScale;
        }

        for (int i = 0; i < _orbits.Count; i++)
        {
            if (_orbits[i] == null) continue;
            _orbits[i].angularSpeed = _orbitAngularBase[i] * orbitScale;
            _orbits[i].selfSpinSpeed = _orbitSelfSpinBase[i] * orbitScale;
        }

        for (int i = 0; i < _cameraOrbits.Count; i++)
        {
            if (_cameraOrbits[i] == null) continue;
            _cameraOrbits[i].autoOrbitSpeed = _cameraOrbitBase[i] * camScale;
        }

        for (int i = 0; i < _lights.Count; i++)
        {
            if (_lights[i] == null) continue;
            // cycleSeconds 越小运动越快，因此这里用除法。
            _lights[i].cycleSeconds = _lightCycleBase[i] / lightScale;
        }

        if (syncTimeScale && Application.isPlaying)
            Time.timeScale = Mathf.Max(0f, globalSpeed);

        _lastAppliedGlobal = globalSpeed;
        _lastAppliedSpin = spinMultiplier;
        _lastAppliedOrbit = orbitMultiplier;
        _lastAppliedCam = cameraOrbitMultiplier;
        _lastAppliedLight = lightCycleMultiplier;
    }
}
