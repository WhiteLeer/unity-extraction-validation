using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

public static class FxLabExtractValidationSceneBuilder
{
    private const string ScenePath = "Assets/unity-sr-extraction-validation/Scenes/SR-Extract-Validation.unity";
    private const string GeneratedRoot = "Assets/unity-sr-extraction-validation/Generated/SRExtractValidation";
    private const string ControllerPath = GeneratedRoot + "/SR_ExtractValidation.controller";
    private const string TimelinePath = GeneratedRoot + "/SR_ExtractValidation.playable";
    private static readonly string[] ModelSearchFolders =
    {
        "Assets/unity-sr-extraction-validation/Imported/SR/Roles",
        "Assets/unity-sr-extraction-validation"
    };

    public static void BuildScene()
    {
        EnsureFolder(GeneratedRoot);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "SR-Extract-Validation";

        var envRoot = new GameObject("ENV_Root");
        var modelRoot = new GameObject("MODEL_Root");
        var timelineRoot = new GameObject("TIMELINE_Root");
        var qaRoot = new GameObject("QA_Root");

        var camera = CreateCamera(envRoot.transform);
        CreateDirectionalLight(envRoot.transform);
        CreateReferenceGround(envRoot.transform);
        CreateCameraPresets(qaRoot.transform);

        var modelGo = InstantiateBestModel(modelRoot.transform);
        if (modelGo == null)
        {
            modelGo = new GameObject("MODEL_Target");
            modelGo.transform.SetParent(modelRoot.transform, false);
            modelGo.transform.localPosition = Vector3.zero;
        }

        var animator = modelGo.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            animator = modelGo.AddComponent<Animator>();
        }

        var clips = CollectCandidateClips(modelGo).ToList();
        var controller = BuildController(clips);
        animator.runtimeAnimatorController = controller;

        var directorGo = new GameObject("Timeline_Director");
        directorGo.transform.SetParent(timelineRoot.transform, false);
        var director = directorGo.AddComponent<PlayableDirector>();
        var timeline = BuildTimeline(clips, animator, modelGo, camera, director);
        director.playableAsset = timeline;
        director.timeUpdateMode = DirectorUpdateMode.GameTime;

        var runtimeController = timelineRoot.AddComponent<FxLabExtractValidationController>();
        runtimeController.targetAnimator = animator;
        runtimeController.targetDirector = director;

        var animPreview = timelineRoot.AddComponent<SRRoleAnimPreviewController>();
        animPreview.targetAnimator = animator;
        animPreview.clips = clips.ToList();
        animPreview.clipIndex = 0;

        SceneView.lastActiveSceneView?.LookAtDirect(camera.transform.position, camera.transform.rotation);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SR Extract Validation] Scene created: {ScenePath}");
    }

    private static Camera CreateCamera(Transform parent)
    {
        var go = new GameObject("CAM_Main");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(0f, 1.6f, -3.4f);
        go.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
        var camera = go.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.fieldOfView = 45f;
        go.tag = "MainCamera";
        return camera;
    }

    private static void CreateDirectionalLight(Transform parent)
    {
        var go = new GameObject("LGT_Directional");
        go.transform.SetParent(parent, false);
        go.transform.rotation = Quaternion.Euler(35f, 145f, 0f);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;
    }

    private static void CreateReferenceGround(Transform parent)
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "REF_Ground";
        ground.transform.SetParent(parent, false);
        ground.transform.localPosition = new Vector3(0f, 0f, 0f);
        ground.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
        var renderer = ground.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = "MAT_REF_Ground";
            mat.color = new Color(0.22f, 0.22f, 0.22f, 1f);
            renderer.sharedMaterial = mat;
        }
    }

    private static void CreateCameraPresets(Transform parent)
    {
        CreatePreset(parent, "CAM_Preset_Front", new Vector3(0f, 1.55f, -2.7f), new Vector3(12f, 0f, 0f));
        CreatePreset(parent, "CAM_Preset_ThreeQuarter", new Vector3(1.55f, 1.5f, -2.45f), new Vector3(10f, -26f, 0f));
        CreatePreset(parent, "CAM_Preset_CloseFace", new Vector3(0.2f, 1.62f, -0.9f), new Vector3(4f, 0f, 0f));
        CreatePreset(parent, "CAM_Preset_FullBody", new Vector3(0f, 1.35f, -4.2f), new Vector3(8f, 0f, 0f));
    }

    private static void CreatePreset(Transform parent, string name, Vector3 pos, Vector3 euler)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
    }

    private static GameObject InstantiateBestModel(Transform parent)
    {
        var candidates = FindAssets<GameObject>(ModelSearchFolders);
        foreach (var prefab in candidates)
        {
            if (prefab == null)
            {
                continue;
            }

            GameObject instance;
            if (PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            }
            else
            {
                instance = Object.Instantiate(prefab);
            }

            instance.name = "MODEL_Target";
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            if (HasUsableRenderer(instance))
            {
                return instance;
            }

            Object.DestroyImmediate(instance);
        }

        return null;
    }

    private static IEnumerable<AnimationClip> CollectCandidateClips(GameObject modelGo)
    {
        var set = new Dictionary<string, AnimationClip>();

        var animator = modelGo.GetComponentInChildren<Animator>();
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                AddClip(set, clip);
            }
        }

        var modelPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(modelGo);
        if (!string.IsNullOrEmpty(modelPath))
        {
            var embedded = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>();
            foreach (var clip in embedded)
            {
                AddClip(set, clip);
            }
        }

        var clipFolders = ModelSearchFolders.Where(AssetDatabase.IsValidFolder).Distinct().ToArray();
        if (clipFolders.Length == 0)
        {
            return set.Values.Take(20);
        }

        var animGuids = AssetDatabase.FindAssets("t:AnimationClip", clipFolders);
        foreach (var guid in animGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            AddClip(set, clip);
            if (set.Count >= 40)
            {
                break;
            }
        }

        return set.Values.Take(20);
    }

    private static AnimatorController BuildController(IEnumerable<AnimationClip> clips)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        }

        var sm = controller.layers[0].stateMachine;
        foreach (var child in sm.states.ToArray())
        {
            sm.RemoveState(child.state);
        }

        AnimatorState defaultState = null;
        var index = 0;
        foreach (var clip in clips)
        {
            if (clip == null)
            {
                continue;
            }
            var state = sm.AddState("Clip_" + clip.name, new Vector3(220f, 70f + index * 55f, 0f));
            state.motion = clip;
            if (defaultState == null)
            {
                defaultState = state;
            }
            index++;
        }

        if (defaultState != null)
        {
            sm.defaultState = defaultState;
        }

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static TimelineAsset BuildTimeline(IEnumerable<AnimationClip> clips, Animator animator, GameObject modelGo, Camera camera, PlayableDirector director)
    {
        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        if (timeline == null)
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
        }

        foreach (var track in timeline.GetOutputTracks().ToArray())
        {
            timeline.DeleteTrack(track);
        }

        var animTrack = timeline.CreateTrack<AnimationTrack>(null, "Animation Track");
        var activationTrack = timeline.CreateTrack<ActivationTrack>(null, "Activation Track");

        var start = 0.0;
        var count = 0;
        foreach (var clip in clips.Take(3))
        {
            if (clip == null)
            {
                continue;
            }
            var timelineClip = animTrack.CreateDefaultClip();
            timelineClip.displayName = clip.name;
            timelineClip.start = start;
            timelineClip.duration = Mathf.Max(0.1f, clip.length);
            ((AnimationPlayableAsset)timelineClip.asset).clip = clip;
            start += timelineClip.duration + 0.05;
            count++;
        }

        if (count == 0)
        {
            var timelineClip = animTrack.CreateDefaultClip();
            timelineClip.displayName = "NoClip";
            timelineClip.start = 0;
            timelineClip.duration = 1;
        }

        var activeClip = activationTrack.CreateDefaultClip();
        activeClip.displayName = "Model Active";
        activeClip.start = 0;
        activeClip.duration = System.Math.Max(1.0, start);

        director.SetGenericBinding(animTrack, animator);
        director.SetGenericBinding(activationTrack, modelGo);
        if (camera != null)
        {
            director.extrapolationMode = DirectorWrapMode.Hold;
        }

        EditorUtility.SetDirty(timeline);
        return timeline;
    }

    private static IEnumerable<T> FindAssets<T>(IEnumerable<string> searchFolders) where T : Object
    {
        var results = new List<T>();
        var seen = new HashSet<string>();

        foreach (var folder in searchFolders)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                continue;
            }
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder });
            foreach (var guid in guids)
            {
                if (!seen.Add(guid))
                {
                    continue;
                }

                var path = AssetDatabase.GUIDToAssetPath(guid);
                var obj = AssetDatabase.LoadAssetAtPath<T>(path);
                if (obj != null)
                {
                    results.Add(obj);
                }
            }
        }
        return results;
    }

    private static bool HasUsableRenderer(GameObject go)
    {
        var skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var r in skinned)
        {
            if (r != null && r.sharedMesh != null)
            {
                return true;
            }
        }

        var meshFilters = go.GetComponentsInChildren<MeshFilter>(true);
        foreach (var mf in meshFilters)
        {
            if (mf != null && mf.sharedMesh != null)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddClip(Dictionary<string, AnimationClip> set, AnimationClip clip)
    {
        if (clip == null)
        {
            return;
        }
        if (clip.name.StartsWith("__preview__", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (!set.ContainsKey(clip.name))
        {
            set.Add(clip.name, clip);
        }
    }

    private static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        var current = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}

