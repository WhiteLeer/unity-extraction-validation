using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SrExtraction
{
    /// <summary>
    /// Replaces the embedded FBX materials with the decoded SR material assets.
    /// The FBX material names are retained by AnimeStudio, so matching by name
    /// is safer than assigning slots by order.
    /// </summary>
    public sealed class SrSparkleCharacterMaterialPostprocessor : AssetPostprocessor
    {
        private const string CharacterMarker = "/SR/characters/";
        private const string ModelMarker = "/Model/";

        [MenuItem("SR/Refresh Character Model Materials")]
        private static void RefreshCharacterModelMaterials()
        {
            const string searchRoot = "Assets/unity-extraction-validation/SR/characters";
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { searchRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }

            Debug.Log("[SR] Refreshed and reimported all character model assets.");
        }

        private void OnPostprocessModel(GameObject root)
        {
            var normalizedPath = assetPath.Replace('\\', '/');
            if (!TryGetCharacterRoot(normalizedPath, out var characterRoot))
                return;

            var characterName = characterRoot[(characterRoot.LastIndexOf('/') + 1)..];
            var materialFolder = characterRoot + "/Material";
            var materials = BuildMaterialLookup(materialFolder);
            foreach (var fallbackFolder in GetFallbackMaterialFolders(characterRoot, characterName))
            {
                foreach (var fallback in BuildMaterialLookup(fallbackFolder))
                    AddAlias(materials, fallback.Key, fallback.Value);
            }

            var replaced = 0;
            var unresolved = 0;
            var unresolvedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (var i = 0; i < slots.Length; i++)
                {
                    var embedded = slots[i];
                    if (!TryFindMaterial(materials, embedded, renderer.gameObject.name, out var decoded))
                    {
                        unresolved++;
                        if (embedded != null)
                            unresolvedNames.Add(StripCloneSuffix(embedded.name));
                        continue;
                    }

                    slots[i] = decoded;
                    replaced++;
                }

                renderer.sharedMaterials = slots;
            }

            if (replaced == 0)
                Debug.LogWarning($"[SR] No {characterName} FBX material slots were remapped from {materialFolder}; unresolved={unresolved}; names={FormatNames(unresolvedNames)}.");
            else
                Debug.Log($"[SR] Remapped {replaced} {characterName} FBX material slot(s); unresolved={unresolved}; names={FormatNames(unresolvedNames)}.");
        }

        private static bool TryGetCharacterRoot(string path, out string characterRoot)
        {
            characterRoot = null;
            if (!path.Contains(CharacterMarker, StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                return false;

            var modelIndex = path.LastIndexOf(ModelMarker, StringComparison.OrdinalIgnoreCase);
            if (modelIndex < 0)
                return false;

            characterRoot = path[..modelIndex];
            return true;
        }

        private static Dictionary<string, Material> BuildMaterialLookup(string folder)
        {
            var lookup = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            var loaded = new List<Material>();
            foreach (var material in LoadMaterials(folder))
            {
                loaded.Add(material);
                lookup[material.name] = material;
            }

            // AnimeStudio writes short FBX slot names such as Body and Face,
            // while decoded Unity materials usually use <character>_Mat_<slot>.
            foreach (var material in loaded)
            {
                var markerIndex = material.name.IndexOf("_Mat_", StringComparison.OrdinalIgnoreCase);
                if (markerIndex >= 0)
                {
                    var alias = material.name[(markerIndex + "_Mat_".Length)..];
                    AddAlias(lookup, alias, material);

                    var suffixIndex = alias.LastIndexOf('_');
                    if (suffixIndex > 0 && (alias.EndsWith("_D", StringComparison.OrdinalIgnoreCase) ||
                                            alias.EndsWith("_S", StringComparison.OrdinalIgnoreCase)))
                    {
                        var baseAlias = alias[..suffixIndex];
                        while (baseAlias.Length > 0 && char.IsDigit(baseAlias[^1]))
                            baseAlias = baseAlias[..^1];
                        AddAlias(lookup, baseAlias, material);
                    }
                    else
                    {
                        var baseAlias = alias;
                        while (baseAlias.Length > 0 && char.IsDigit(baseAlias[^1]))
                            baseAlias = baseAlias[..^1];
                        AddAlias(lookup, baseAlias, material);
                    }
                }
            }

            // Some exports use a separate Face_Mask slot without a separate
            // decoded material; fall back to Face only when necessary.
            if (lookup.TryGetValue("Face", out var face))
                AddAlias(lookup, "Face_Mask", face);

            return lookup;
        }

        private static IEnumerable<string> GetFallbackMaterialFolders(string characterRoot, string characterName)
        {
            var fallbackNames = new List<string>();
            if (characterName.StartsWith("playerboy", StringComparison.OrdinalIgnoreCase))
                fallbackNames.Add("playerboy");
            else if (characterName.StartsWith("playergirl", StringComparison.OrdinalIgnoreCase))
                fallbackNames.Add("playergirl");
            else if (characterName.StartsWith("tribbie_", StringComparison.OrdinalIgnoreCase))
                fallbackNames.Add("tribbie");
            else if (characterName.Equals("dr_ratio_01", StringComparison.OrdinalIgnoreCase))
                fallbackNames.Add("drratio");
            else if (characterName.Equals("firefly_01", StringComparison.OrdinalIgnoreCase))
                fallbackNames.Add("firefly_00");
            else if (characterName.Equals("kafka_01", StringComparison.OrdinalIgnoreCase))
                fallbackNames.Add("kafka");
            else if (characterName.Equals("mar7th2", StringComparison.OrdinalIgnoreCase) ||
                     characterName.Equals("mar_7th_01", StringComparison.OrdinalIgnoreCase))
                fallbackNames.Add("mar7th");
            else if (characterName.Equals("sparxie_01", StringComparison.OrdinalIgnoreCase))
                fallbackNames.Add("sparxie");
            else if (characterName.EndsWith("_lod1", StringComparison.OrdinalIgnoreCase) ||
                     characterName.EndsWith("_lod2", StringComparison.OrdinalIgnoreCase))
            {
                var baseName = characterName[..characterName.LastIndexOf("_lod", StringComparison.OrdinalIgnoreCase)];
                fallbackNames.Add(baseName);
                if (baseName.EndsWith("_00", StringComparison.OrdinalIgnoreCase))
                    fallbackNames.Add(baseName[..^3]);
                if (baseName.Equals("silverwolf999_00", StringComparison.OrdinalIgnoreCase))
                    fallbackNames.Add("silverwolflv999");
            }

            var parent = characterRoot[..characterRoot.LastIndexOf('/')];
            foreach (var name in fallbackNames)
            {
                var folder = parent + "/" + name + "/Material";
                if (!folder.Equals(characterRoot + "/Material", StringComparison.OrdinalIgnoreCase))
                    yield return folder;
            }
        }

        private static bool TryFindMaterial(
            Dictionary<string, Material> lookup,
            Material embedded,
            string rendererName,
            out Material material)
        {
            if (embedded != null && lookup.TryGetValue(StripCloneSuffix(embedded.name), out material))
                return true;

            // Unity imports the FBX slots as Lit, so use the renderer node name
            // when the original material name was collapsed by the importer.
            var normalizedRendererName = StripRendererVariant(StripCloneSuffix(rendererName));
            if (lookup.TryGetValue(normalizedRendererName, out material))
                return true;

            // Some character variants expose only the generic node name while
            // their decoded materials carry a suffix such as _D or _S.
            if (lookup.TryGetValue(normalizedRendererName + "_D", out material))
                return true;

            foreach (var candidate in lookup)
            {
                if (candidate.Key.StartsWith(normalizedRendererName + "_", StringComparison.OrdinalIgnoreCase))
                {
                    material = candidate.Value;
                    return true;
                }
            }

            material = null;
            return false;
        }

        private static void AddAlias(Dictionary<string, Material> lookup, string alias, Material material)
        {
            if (!string.IsNullOrWhiteSpace(alias) && !lookup.ContainsKey(alias))
                lookup.Add(alias, material);
        }

        private static string FormatNames(HashSet<string> names)
        {
            return names.Count == 0 ? "<none>" : string.Join(", ", names);
        }

        private static IEnumerable<Material> LoadMaterials(string folder)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null)
                    yield return material;
            }
        }

        private static string StripCloneSuffix(string name)
        {
            const string suffix = " (Instance)";
            return name.EndsWith(suffix, StringComparison.Ordinal) ? name[..^suffix.Length] : name;
        }

        private static string StripRendererVariant(string name)
        {
            var separator = name.LastIndexOf('_');
            if (separator <= 0)
                return name;

            var suffix = name[(separator + 1)..];
            if (int.TryParse(suffix, out _) || suffix.Equals("Ultra", StringComparison.OrdinalIgnoreCase) ||
                suffix.StartsWith("LOD", StringComparison.OrdinalIgnoreCase))
                return name[..separator];

            return name;
        }
    }

}
