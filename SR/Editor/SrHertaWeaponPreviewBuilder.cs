using System.IO;
using UnityEditor;
using UnityEngine;

internal static class SrHertaWeaponPreviewBuilder
{
    private const string CharacterModelPath = "Assets/unity-extraction-validation/SR/characters/herta/Model/Avatar_Herta_00_Model_Chara.fbx";
    private const string WeaponRigPath = "Assets/unity-extraction-validation/SR/characters/herta/Model/Weapon/Avatar_Herta_00_Model_Others.fbx";
    private const string AnimationPath = "Assets/unity-extraction-validation/SR/characters/herta/Animations/Avatar_Herta_00_Model_Chara/Skill/Avatar_Herta_00_Ani_MazeSkill01.anim";
    private const string PreviewPath = "Assets/unity-extraction-validation/SR/characters/herta/Preview";

    [MenuItem("Tools/SR/Build Herta Weapon Preview")]
    public static void Build()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var character = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterModelPath);
        var weapon = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponRigPath);
        var animation = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationPath);
        if (character == null || weapon == null || animation == null)
            throw new FileNotFoundException("Herta preview inputs are not imported yet.");
        SrCharacterPreviewBuilder.Build(character, weapon, animation, PreviewPath, "Herta_SplitWeaponPreview");
    }
}
