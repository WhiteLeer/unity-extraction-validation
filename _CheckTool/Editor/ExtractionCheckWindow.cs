using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace UnityExtractionValidation.CheckTool
{
    public sealed class ExtractionCheckWindow : EditorWindow
    {
        private const string MenuPath = "Tools/Extraction/Check and Build Clean";
        private const string InputFolderPreference = "UnityExtractionValidation.CheckTool.InputFolder";
        private const string ProfilePreference = "UnityExtractionValidation.CheckTool.Profile";
        private static readonly string[] SupportedProfiles = { "HSR-4.4", "ZZZ-3.3" };

        private string inputFolder = string.Empty;
        private int profileIndex;
        private CheckResult checkResult;
        private Vector2 messagesScroll;
        private bool showDetails = true;

        [MenuItem(MenuPath)]
        private static void Open()
        {
            var window = GetWindow<ExtractionCheckWindow>("Extraction Check");
            window.minSize = new Vector2(680f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            inputFolder = EditorPrefs.GetString(InputFolderPreference, string.Empty);
            profileIndex = Mathf.Clamp(EditorPrefs.GetInt(ProfilePreference, 0), 0, SupportedProfiles.Length - 1);
            if (Directory.Exists(inputFolder))
                CheckInput();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("资源提取整理检查", EditorStyles.boldLabel);
            var selectedProfile = EditorGUILayout.Popup("类型", profileIndex, SupportedProfiles);
            if (selectedProfile != profileIndex)
            {
                profileIndex = selectedProfile;
                EditorPrefs.SetInt(ProfilePreference, profileIndex);
                checkResult = null;
            }

            DrawFolderInput();
            HandleFolderDrop(new Rect(0f, 0f, position.width, position.height));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("检查格式", GUILayout.Height(28f)))
                    CheckInput();

                using (new EditorGUI.DisabledScope(checkResult == null || checkResult.HasErrors))
                {
                    if (GUILayout.Button("同步 Clean", GUILayout.Height(28f)))
                        RebuildClean();
                }
            }

            if (checkResult == null)
                return;

            EditorGUILayout.Space(6f);
            DrawSummary();

            showDetails = EditorGUILayout.Foldout(showDetails, "检查详情", true);
            if (!showDetails)
                return;

            messagesScroll = EditorGUILayout.BeginScrollView(messagesScroll);
            foreach (var message in checkResult.Messages)
            {
                var type = message.Level == CheckLevel.Error
                    ? MessageType.Error
                    : message.Level == CheckLevel.Warning ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(message.Text, type);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawFolderInput()
        {
            EditorGUILayout.LabelField("Run 路径", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                var edited = EditorGUILayout.TextField(inputFolder);
                if (!string.Equals(edited, inputFolder, StringComparison.Ordinal))
                    SetInputFolder(edited);

                if (GUILayout.Button("浏览...", GUILayout.Width(76f)))
                {
                    var selected = EditorUtility.OpenFolderPanel("选择提取 Run 目录", inputFolder, string.Empty);
                    if (!string.IsNullOrEmpty(selected))
                        SetInputFolder(selected);
                }
            }

        }

        private void HandleFolderDrop(Rect dropRect)
        {
            var current = Event.current;
            if (!dropRect.Contains(current.mousePosition))
                return;

            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
                return;

            var folder = DragAndDrop.paths
                .FirstOrDefault(path => !string.IsNullOrEmpty(path) && Directory.Exists(path));
            if (string.IsNullOrEmpty(folder))
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                SetInputFolder(folder);
                CheckInput();
            }

            current.Use();
        }

        private void SetInputFolder(string path)
        {
            inputFolder = string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : Path.GetFullPath(path.Trim().Trim('"'));
            EditorPrefs.SetString(InputFolderPreference, inputFolder);
            checkResult = null;
        }

        private void CheckInput()
        {
            checkResult = ExtractionChecker.Check(inputFolder, SupportedProfiles[profileIndex]);
            Repaint();
        }

        private void DrawSummary()
        {
            var status = checkResult.HasErrors ? "检查失败" : "结构可整理";
            var statusType = checkResult.HasErrors ? MessageType.Error :
                checkResult.WarningCount > 0 ? MessageType.Warning : MessageType.Info;
            EditorGUILayout.HelpBox(
                string.Format(
                    "{0}\nProfile: {1}\nRun: {2}\n角色: {3}\n资源: FBX {4}，动画 {5}，贴图 {6}，特效 {7}\nClean: {8}\nraw: {9}\n错误: {10}，警告: {11}",
                    status,
                    checkResult.SelectedProfileId + " / " + (checkResult.ProfileId ?? "<未识别>"),
                    checkResult.RunId ?? "<未识别>",
                    checkResult.CharacterName ?? "<未识别>",
                    checkResult.ModelCount,
                    checkResult.AnimationCount,
                    checkResult.TextureCount,
                    checkResult.EffectCount,
                    checkResult.CleanCharacterFolder ?? "<未识别>",
                    checkResult.RawFolder ?? "<未识别>",
                    checkResult.ErrorCount,
                    checkResult.WarningCount),
                statusType);
        }

        private void RebuildClean()
        {
            if (checkResult == null || checkResult.HasErrors)
                return;

            var characterFolder = checkResult.CleanCharacterFolder;
            if (Directory.Exists(characterFolder) &&
                !EditorUtility.DisplayDialog(
                    "确认同步 Clean",
                    "当前角色的 Clean 文件夹将按文件内容增量同步，其他角色、raw 和 Runs 不会被修改。继续吗？",
                    "重建",
                    "取消"))
            {
                return;
            }

            try
            {
                CreateCleanLayout(characterFolder);

                var copied = new List<string>();
                CopyFolderFiles(Path.Combine(checkResult.RawFolder, "Character", "Models"),
                    Path.Combine(characterFolder, "_Base_Model"),
                    new[] { ".fbx" }, copied);
                CopyFolderFiles(Path.Combine(checkResult.RawFolder, "Character", "Animations"),
                    Path.Combine(characterFolder, "_Base_Anim"),
                    new[] { ".anim" }, copied);
                CopyFolderFiles(Path.Combine(checkResult.RawFolder, "Character", "Textures"),
                    Path.Combine(characterFolder, "_Base_Texture"),
                    new[] { ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".dds" }, copied);

                var changedFbxFiles = copied
                    .Where(path => string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (copied.Count > 0)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    ApplyFbxImportSettings(changedFbxFiles);
                }
                checkResult.Messages.Insert(0, new CheckMessage(
                    CheckLevel.Info,
                    string.Format("已同步 {0}，变更 {1} 个资源；内容未变化的文件未重新导入。_Extra_* 和 _Preview 保持为空。",
                        characterFolder, copied.Count)));
                Repaint();
            }
            catch (Exception exception)
            {
                checkResult.Messages.Insert(0, new CheckMessage(CheckLevel.Error,
                    "重建 Clean 失败: " + exception.Message));
                Repaint();
            }
        }

    private static void ApplyFbxImportSettings(IEnumerable<string> fbxFiles)
    {
        var projectRoot = Directory.GetParent(Application.dataPath).FullName;
        foreach (var file in fbxFiles.Where(path =>
                         string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase)))
            {
                var relative = GetRelativePath(projectRoot, file).Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(relative) as ModelImporter;
                if (importer == null)
                    continue;

                if (importer.useFileScale)
                {
                    // Unity 2022.3 exposes the FBX "Convert Units" option as useFileScale.
                    importer.useFileScale = false;
                    importer.SaveAndReimport();
                }
            }
        }

        private static void CreateCleanLayout(string characterFolder)
        {
            foreach (var name in new[]
                     {
                         "_Base_Anim", "_Base_Model",
                         "_Base_Texture",
                         "_Extra_0_Material", "_Extra_0_Shader",
                         "_Extra_1_Effect", "_Extra_1_Prefab",
                         "_Extra_2_TimeLine", "_Preview"
                     })
            {
                Directory.CreateDirectory(Path.Combine(characterFolder, name));
            }
        }

        private static void CopyFolderFiles(
            string source,
            string destination,
            IEnumerable<string> extensions,
            ICollection<string> copied)
        {
            if (!Directory.Exists(source))
                return;

            var allowed = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                if (!allowed.Contains(Path.GetExtension(file)) || file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                var relative = GetRelativePath(source, file);
                var target = Path.Combine(destination, relative);
                if (File.Exists(target) && FilesHaveSameHash(file, target))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
                copied.Add(target);
            }
        }

        private static bool FilesHaveSameHash(string source, string target)
        {
            var sourceInfo = new FileInfo(source);
            var targetInfo = new FileInfo(target);
            if (sourceInfo.Length != targetInfo.Length)
                return false;

            // AS outputs preserve stable timestamps. Avoid reading large animation
            // and texture files when the source signature is already unchanged.
            if (sourceInfo.LastWriteTimeUtc == targetInfo.LastWriteTimeUtc)
                return true;

            using var sha256 = SHA256.Create();
            using var sourceStream = File.OpenRead(source);
            using var targetStream = File.OpenRead(target);
            var sourceHash = sha256.ComputeHash(sourceStream);
            var targetHash = sha256.ComputeHash(targetStream);
            return sourceHash.SequenceEqual(targetHash);
        }

        private static string SanitizeFolderName(string value)
        {
            var name = string.IsNullOrWhiteSpace(value) ? "UnknownCharacter" : value.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid.ToString(), string.Empty);
            return string.IsNullOrWhiteSpace(name) ? "UnknownCharacter" : name;
        }

        private static string GetRelativePath(string root, string path)
        {
            return Path.GetRelativePath(root, path);
        }
    }

    internal static class ExtractionChecker
    {
        public static CheckResult Check(string inputFolder, string selectedProfileId)
        {
            var result = new CheckResult
            {
                InputFolder = inputFolder,
                SelectedProfileId = selectedProfileId
            };
            if (!string.Equals(selectedProfileId, "HSR-4.4", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(CheckLevel.Error,
                    "ZZZ-3.3 已加入 Profile 选择器，但本轮只实现 HSR-4.4 的检查和 Clean 逻辑。请切换到 HSR-4.4。 ");
                return result;
            }

            if (string.IsNullOrWhiteSpace(inputFolder) || !Directory.Exists(inputFolder))
            {
                result.Add(CheckLevel.Error, "Run 目录不存在，请拖入类似 20260824_Herta_003 的文件夹。");
                return result;
            }

            result.RunId = Path.GetFileName(Path.GetFullPath(inputFolder).TrimEnd(Path.DirectorySeparatorChar));
            result.ValidationRoot = FindValidationRoot(inputFolder, selectedProfileId);
            if (string.IsNullOrEmpty(result.ValidationRoot))
            {
                result.Add(CheckLevel.Error,
                    "找不到对应的 HSR-4.4 Profile。请确认 Run 位于 HSR-4.4/Runs/<run-id>/ 下，" +
                    "并且 Profiles/HSR-4.4.json 存在。");
                return result;
            }

            var profilesFolder = Path.Combine(result.ValidationRoot, "Profiles");

            var profileFile = Path.Combine(profilesFolder, selectedProfileId + ".json");
            if (!File.Exists(profileFile))
                profileFile = Directory.GetFiles(profilesFolder, "*.json", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            if (profileFile == null)
            {
                result.Add(CheckLevel.Error, "Profiles/ 下没有 JSON Profile。");
                return result;
            }

            var profile = ReadJson<ProfileDocument>(profileFile, result, "Profile");
            result.ProfileId = string.IsNullOrWhiteSpace(profile.id)
                ? Path.GetFileNameWithoutExtension(profileFile)
                : profile.id;
            if (!string.Equals(result.ProfileId, selectedProfileId, StringComparison.OrdinalIgnoreCase))
                result.Add(CheckLevel.Error,
                    string.Format("当前选择为 {0}，但目录中的 Profile 是 {1}。请切换选择或拖入对应目录。",
                        selectedProfileId, result.ProfileId));

            var request = ReadJson<RequestDocument>(Path.Combine(inputFolder, "request.json"), result, "request");
            var runConfig = ReadJson<RunConfigDocument>(Path.Combine(inputFolder, "run-config.json"), result, "run-config");
            result.CharacterName = request.subject == null ? string.Empty : request.subject.displayName;
            if (string.IsNullOrWhiteSpace(result.CharacterName))
                result.Add(CheckLevel.Error, "request.json 缺少 subject.displayName，无法生成 Clean/<角色名>。");
            if (!string.IsNullOrWhiteSpace(request.profile) &&
                !string.Equals(request.profile, result.ProfileId, StringComparison.OrdinalIgnoreCase))
                result.Add(CheckLevel.Warning, "request.profile 与 Profile 文件不一致。");
            if (!string.IsNullOrWhiteSpace(runConfig.runId) &&
                !string.Equals(runConfig.runId, result.RunId, StringComparison.OrdinalIgnoreCase))
                result.Add(CheckLevel.Warning, "run-config.runId 与目录名不一致。");

            result.CleanCharacterFolder = Path.Combine(
                result.ValidationRoot,
                "Clean",
                SanitizeFolderName(result.CharacterName));
            result.RawFolder = ResolveRawFolder(result.ValidationRoot, result.ProfileId, result.RunId);
            if (string.IsNullOrEmpty(result.RawFolder))
            {
                result.Add(CheckLevel.Error,
                    "找不到外部 raw。期望路径为项目根/ExtractionRuns/<Profile>/Runs/<Run>/raw，" +
                    "或通过同名 Profile 的 *_RAW_ROOT 环境变量指定。");
                return result;
            }

            var characterFolder = Path.Combine(result.RawFolder, "Character");
            var effectsFolder = Path.Combine(result.RawFolder, "Effects");
            RequireDirectory(characterFolder, result, "raw/Character");
            WarnIfMissingDirectory(effectsFolder, result, "raw/Effects");
            WarnIfMissingDirectory(Path.Combine(characterFolder, "Models"), result, "raw/Character/Models");
            WarnIfMissingDirectory(Path.Combine(characterFolder, "Animations"), result, "raw/Character/Animations");

            result.ModelCount = CountFiles(Path.Combine(characterFolder, "Models"), ".fbx");
            result.AnimationCount = CountFiles(Path.Combine(characterFolder, "Animations"), ".anim");
            result.TextureCount = CountTextureFiles(Path.Combine(characterFolder, "Textures"));
            result.EffectCount = CountFiles(effectsFolder, ".srprefab");
            if (result.ModelCount == 0)
                result.Add(CheckLevel.Warning, "没有找到 .fbx；Clean/角色名/_Base_Model 将创建为空目录。");
            if (result.AnimationCount == 0)
                result.Add(CheckLevel.Warning, "没有找到 .anim；Clean/角色名/_Base_Anim 将创建为空目录。");
            if (result.TextureCount == 0)
                result.Add(CheckLevel.Warning, "没有找到贴图；Clean/角色名/_Base_Texture 将创建为空目录。");
            if (result.EffectCount > 0)
                result.Add(CheckLevel.Info,
                    string.Format("检测到 {0} 个 .srprefab；按当前规则暂不复制到 _Extra_*。", result.EffectCount));

            var metaCount = CountFiles(result.RawFolder, ".meta");
            if (metaCount > 0)
                result.Add(CheckLevel.Warning,
                    string.Format("raw 中有 {0} 个 .meta，它们是迁移前 Unity 产生的元数据，不会复制到 Clean。", metaCount));

            result.Add(CheckLevel.Info, "检查完成：Clean 将只接收角色 FBX、AnimationClip 和贴图；其他目录暂不填充。");
            return result;
        }

        private static string FindValidationRoot(string runFolder, string selectedProfileId)
        {
            for (var current = new DirectoryInfo(Path.GetFullPath(runFolder)); current != null; current = current.Parent)
            {
                var profilePath = Path.Combine(current.FullName, "Profiles", selectedProfileId + ".json");
                if (File.Exists(profilePath))
                    return current.FullName;
            }

            // External ExtractionRuns live beside the Unity project, so their parent
            // chain cannot reach the profile stored under the project's Assets tree.
            var projectProfileRoot = Path.Combine(
                Application.dataPath,
                "unity-extraction-validation",
                selectedProfileId);
            if (File.Exists(Path.Combine(projectProfileRoot, "Profiles", selectedProfileId + ".json")))
                return projectProfileRoot;

            return string.Empty;
        }

        private static string ResolveRawFolder(string validationRoot, string profileId, string runId)
        {
            var environmentName = profileId.Replace('-', '_').ToUpperInvariant() + "_RAW_ROOT";
            var configuredRoot = Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(configuredRoot))
            {
                var configured = Path.Combine(configuredRoot, "Runs", runId, "raw");
                if (Directory.Exists(configured))
                    return configured;
            }

            for (var current = new DirectoryInfo(validationRoot); current != null; current = current.Parent)
            {
                var candidate = Path.Combine(current.FullName, "ExtractionRuns", profileId, "Runs", runId, "raw");
                if (Directory.Exists(candidate))
                    return candidate;
            }

            var legacy = Path.Combine(validationRoot, "Runs", runId, "raw");
            return Directory.Exists(legacy) ? legacy : string.Empty;
        }

        private static string SanitizeFolderName(string value)
        {
            var name = string.IsNullOrWhiteSpace(value) ? "UnknownCharacter" : value.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid.ToString(), string.Empty);
            return string.IsNullOrWhiteSpace(name) ? "UnknownCharacter" : name;
        }

        private static T ReadJson<T>(string path, CheckResult result, string label) where T : class
        {
            try
            {
                var value = JsonUtility.FromJson<T>(File.ReadAllText(path));
                if (value == null)
                    throw new InvalidDataException("JSON 为空。");
                return value;
            }
            catch (Exception exception)
            {
                result.Add(CheckLevel.Error, label + " 无法读取: " + exception.Message);
                return Activator.CreateInstance<T>();
            }
        }

        private static void RequireDirectory(string path, CheckResult result, string label)
        {
            if (!Directory.Exists(path))
                result.Add(CheckLevel.Error, "缺少目录: " + label);
        }

        private static void WarnIfMissingDirectory(string path, CheckResult result, string label)
        {
            if (!Directory.Exists(path))
                result.Add(CheckLevel.Warning, "当前 Run 未提供阶段目录: " + label + "，按阶段提取处理。");
        }

        private static int CountFiles(string folder, string extension)
        {
            if (!Directory.Exists(folder))
                return 0;
            return Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Count(path => string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase));
        }

        private static int CountTextureFiles(string folder)
        {
            if (!Directory.Exists(folder))
                return 0;

            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".dds"
            };
            return Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Count(path => extensions.Contains(Path.GetExtension(path)));
        }
    }

    internal sealed class CheckResult
    {
        public string InputFolder;
        public string ValidationRoot;
        public string CleanCharacterFolder;
        public string SelectedProfileId;
        public string ProfileId;
        public string RunId;
        public string CharacterName;
        public string RawFolder;
        public int ModelCount;
        public int AnimationCount;
        public int TextureCount;
        public int EffectCount;
        public readonly List<CheckMessage> Messages = new();

        public int ErrorCount => Messages.Count(message => message.Level == CheckLevel.Error);
        public int WarningCount => Messages.Count(message => message.Level == CheckLevel.Warning);
        public bool HasErrors => ErrorCount > 0;

        public void Add(CheckLevel level, string text)
        {
            Messages.Add(new CheckMessage(level, text));
        }
    }

    internal sealed class CheckMessage
    {
        public CheckMessage(CheckLevel level, string text)
        {
            Level = level;
            Text = text;
        }

        public CheckLevel Level { get; }
        public string Text { get; }
    }

    internal enum CheckLevel
    {
        Info,
        Warning,
        Error
    }

    [Serializable]
    internal sealed class ProfileDocument
    {
        public string id;
    }

    [Serializable]
    internal sealed class RequestDocument
    {
        public string profile;
        public RequestSubject subject;
    }

    [Serializable]
    internal sealed class RequestSubject
    {
        public string displayName;
    }

    [Serializable]
    internal sealed class RunConfigDocument
    {
        public string runId;
    }
}
