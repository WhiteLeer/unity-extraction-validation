using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class SrCerydraPreviewBuilder
{
    private const string CharacterModelPath =
        "Assets/unity-extraction-validation/SR/characters/cerydra/Model/Avatar_Cerydra_00_Model_Chara.fbx";
    private const string WeaponRigPath =
        "Assets/unity-extraction-validation/SR/characters/cerydra/Model/Weapon/Avatar_Cerydra_00_Model_Others.fbx";
    private const string StandByAnimationPath =
        "Assets/unity-extraction-validation/SR/characters/cerydra/Animations/Avatar_Cerydra_00_Model_Chara/Idle/Avatar_Cerydra_00_Ani_StandBy.anim";
    private const string AnimationFolderPath =
        "Assets/unity-extraction-validation/SR/characters/cerydra/Animations/Avatar_Cerydra_00_Model_Chara";
    private const string PreviewRoot =
        "Assets/unity-extraction-validation/SR/characters/cerydra/Preview";

    [MenuItem("Tools/SR/Build Cerydra Preview")]
    public static void BuildPreview()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var character = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterModelPath);
        var weapon = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponRigPath);
        var animation = AssetDatabase.LoadAssetAtPath<AnimationClip>(StandByAnimationPath);
        if (character == null || weapon == null || animation == null)
            throw new InvalidOperationException("Cerydra model, weapon or StandBy animation has not finished importing.");

        var animations = AssetDatabase.FindAssets("t:AnimationClip", new[] { AnimationFolderPath })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => AssetDatabase.LoadAssetAtPath<AnimationClip>(path))
            .Where(clip => clip != null)
            .OrderBy(clip => clip.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        animations.Remove(animation);
        animations.Insert(0, animation);

        LogModelRenderers(character);
        SrCharacterPreviewBuilder.Build(
            character,
            weapon,
            animations,
            PreviewRoot,
            "Cerydra_00_CharacterPreview");
    }

    [MenuItem("Tools/SR/Inspect Cerydra Model")]
    public static void InspectModel()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var character = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterModelPath);
        if (character == null)
            throw new InvalidOperationException("Cerydra model has not finished importing.");
        LogModelRenderers(character);
        Selection.activeObject = character;
    }

    private static void LogModelRenderers(GameObject character)
    {
        foreach (var renderer in character.GetComponentsInChildren<Renderer>(true))
        {
            var skinned = renderer as SkinnedMeshRenderer;
            var mesh = skinned != null
                ? skinned.sharedMesh
                : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            var materials = string.Join(",", renderer.sharedMaterials
                .Where(material => material != null)
                .Select(material => material.name));
            Debug.Log(
                $"Cerydra renderer: {renderer.name} type={renderer.GetType().Name} " +
                $"mesh={(mesh == null ? "<none>" : mesh.name)} " +
                $"bones={(skinned == null || skinned.bones == null ? 0 : skinned.bones.Length)} " +
                $"rootBone={(skinned == null || skinned.rootBone == null ? "<none>" : skinned.rootBone.name)} " +
                $"materials={materials}",
                character);
        }
    }
}
