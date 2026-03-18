using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public static class SRTimelineAuditTool
{
    private const string TimelineRoot = "Assets/unity-sr-extraction-validation/Generated/SRTimelineRecovered";
    private const string ReportPath = "Assets/unity-sr-extraction-validation/Generated/SRTimelineRecovered/Timeline_Audit_Report.txt";
    private const string ControlLookupReportPath = "Assets/unity-sr-extraction-validation/Generated/SRTimelineRecovered/Timeline_ControlLookup_Report.txt";
    private const string CoverageReportPath = "Assets/unity-sr-extraction-validation/Generated/SRTimelineRecovered/Timeline_RebuildCoverage_Report.txt";
    private const string SummaryFolderPrefKey = "SRExtractionValidation.SR.DefaultTimelineSummaryFolder";
    private const string LegacySummaryFolderPrefKey = "AllEffectsLab.SR.DefaultTimelineSummaryFolder";

    public static void AuditRecoveredTimelines()
    {
        var guids = AssetDatabase.FindAssets("t:TimelineAsset", new[] { TimelineRoot });
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("[Timeline审计] 未找到可审计的 TimelineAsset。");
            return;
        }

        var lines = new List<string>();
        lines.Add("Timeline 审计报告");
        lines.Add("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        lines.Add("模式: 静态轨道+片段审计");
        lines.Add(string.Empty);

        foreach (var guid in guids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            if (timeline == null) continue;

            lines.Add("==================================================");
            lines.Add("Timeline: " + timeline.name);
            lines.Add("Path: " + path);
            lines.Add("TrackCount: " + timeline.GetOutputTracks().Count());

            int trackIndex = 0;
            foreach (var track in timeline.GetOutputTracks())
            {
                trackIndex++;
                var trackType = track.GetType();
                var isCustomTrack = IsCustomType(trackType);
                lines.Add($"  [Track {trackIndex}] {track.name}");
                lines.Add($"    Type: {trackType.FullName} ({trackType.Assembly.GetName().Name})");
                lines.Add($"    CustomTrack: {isCustomTrack}");

                int clipIndex = 0;
                foreach (var clip in track.GetClips())
                {
                    clipIndex++;
                    var asset = clip.asset;
                    var clipType = asset != null ? asset.GetType() : null;
                    var isCustomClip = clipType != null && IsCustomType(clipType);
                    lines.Add($"      - Clip {clipIndex}: {clip.displayName} [{clip.start:F3}~{(clip.start + clip.duration):F3}]");
                    if (clipType != null)
                    {
                        lines.Add($"        ClipType: {clipType.FullName} ({clipType.Assembly.GetName().Name})");
                        lines.Add($"        CustomClip: {isCustomClip}");
                    }
                    else
                    {
                        lines.Add("        ClipType: <null>");
                    }
                }
            }
            lines.Add(string.Empty);
        }

        var abs = Path.Combine(Directory.GetCurrentDirectory(), ReportPath);
        var dir = Path.GetDirectoryName(abs);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllLines(abs, lines, Encoding.UTF8);
        AssetDatabase.ImportAsset(ReportPath);
        AssetDatabase.Refresh();
        Debug.Log("[Timeline审计] 已输出: " + ReportPath);
    }

    public static void AuditRecoveredTimelinesWithBindings()
    {
        var guids = AssetDatabase.FindAssets("t:TimelineAsset", new[] { TimelineRoot });
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("[Timeline审计] 未找到可审计的 TimelineAsset。");
            return;
        }

        var lines = new List<string>();
        lines.Add("Timeline 引用审计报告");
        lines.Add("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        lines.Add("模式: 轨道+片段+绑定可用性审计");
        lines.Add(string.Empty);

        var director = UnityEngine.Object.FindObjectOfType<PlayableDirector>();
        var animator = UnityEngine.Object.FindObjectOfType<Animator>();
        var modelTarget = ResolveModelTargetForBinding();
        var mainCamera = Camera.main;
        lines.Add($"场景上下文: director={(director != null)}, animator={(animator != null)}, modelTarget={(modelTarget != null)}, camera={(mainCamera != null)}");
        lines.Add(string.Empty);

        var missingBindingCount = 0;
        var nullClipAssetCount = 0;
        var nullAnimationClipCount = 0;
        var controlClipWithoutSourceCount = 0;

        foreach (var guid in guids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            if (timeline == null) continue;

            lines.Add("==================================================");
            lines.Add("Timeline: " + timeline.name);
            lines.Add("Path: " + path);
            lines.Add("TrackCount: " + timeline.GetOutputTracks().Count());

            int trackIndex = 0;
            foreach (var track in timeline.GetOutputTracks())
            {
                trackIndex++;
                var trackType = track.GetType();
                var isCustomTrack = IsCustomType(trackType);
                lines.Add($"  [Track {trackIndex}] {track.name}");
                lines.Add($"    Type: {trackType.FullName} ({trackType.Assembly.GetName().Name})");
                lines.Add($"    CustomTrack: {isCustomTrack}");

                var bindingStatus = EvaluateTrackBinding(track, director, animator, modelTarget, mainCamera);
                lines.Add($"    Binding: {bindingStatus}");
                if (bindingStatus.StartsWith("MISSING", StringComparison.OrdinalIgnoreCase))
                {
                    missingBindingCount++;
                }

                int clipIndex = 0;
                foreach (var clip in track.GetClips())
                {
                    clipIndex++;
                    var asset = clip.asset;
                    var clipType = asset != null ? asset.GetType() : null;
                    var isCustomClip = clipType != null && IsCustomType(clipType);
                    lines.Add($"      - Clip {clipIndex}: {clip.displayName} [{clip.start:F3}~{(clip.start + clip.duration):F3}]");
                    if (clipType != null)
                    {
                        lines.Add($"        ClipType: {clipType.FullName} ({clipType.Assembly.GetName().Name})");
                        lines.Add($"        CustomClip: {isCustomClip}");
                    }
                    else
                    {
                        lines.Add("        ClipType: <null>");
                        nullClipAssetCount++;
                    }

                    if (asset is AnimationPlayableAsset animationPlayable)
                    {
                        if (animationPlayable.clip == null)
                        {
                            lines.Add("        AnimationClip: <null> (缺失)");
                            nullAnimationClipCount++;
                        }
                        else
                        {
                            lines.Add("        AnimationClip: " + animationPlayable.clip.name);
                        }
                    }

                    if (asset is ControlPlayableAsset controlPlayable)
                    {
                        if (controlPlayable.sourceGameObject.exposedName == default)
                        {
                            lines.Add("        ControlRef: exposedName=<empty> (可能缺失控制对象)");
                            controlClipWithoutSourceCount++;
                        }
                        else
                        {
                            lines.Add("        ControlRef: exposedName=" + controlPlayable.sourceGameObject.exposedName);
                        }
                    }
                }
            }
            lines.Add(string.Empty);
        }

        lines.Add("==================================================");
        lines.Add("统计");
        lines.Add("missingBindingCount=" + missingBindingCount);
        lines.Add("nullClipAssetCount=" + nullClipAssetCount);
        lines.Add("nullAnimationClipCount=" + nullAnimationClipCount);
        lines.Add("controlClipWithoutSourceCount=" + controlClipWithoutSourceCount);

        var abs = Path.Combine(Directory.GetCurrentDirectory(), ReportPath);
        var dir = Path.GetDirectoryName(abs);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllLines(abs, lines, Encoding.UTF8);
        AssetDatabase.ImportAsset(ReportPath);
        AssetDatabase.Refresh();
        Debug.Log("[Timeline审计] 已输出(含绑定检查): " + ReportPath);
    }

    public static void AuditControlTrackResourceLookup(string roleFolder)
    {
        var guids = AssetDatabase.FindAssets("t:TimelineAsset", new[] { TimelineRoot });
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("[控制轨反查] 未找到可审计的 TimelineAsset。");
            return;
        }

        var searchFolders = new List<string>();
        if (!string.IsNullOrEmpty(roleFolder) && AssetDatabase.IsValidFolder(roleFolder))
        {
            searchFolders.Add(roleFolder);
        }
        if (AssetDatabase.IsValidFolder("Assets/unity-sr-extraction-validation/Imported/SR"))
        {
            searchFolders.Add("Assets/unity-sr-extraction-validation/Imported/SR");
        }
        if (searchFolders.Count == 0)
        {
            searchFolders.Add("Assets");
        }

        var lines = new List<string>
        {
            "Timeline 控制轨资源反查报告",
            "时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            "搜索目录: " + string.Join(" | ", searchFolders),
            string.Empty
        };

        int controlClipTotal = 0;
        int controlClipMatched = 0;

        foreach (var guid in guids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            if (timeline == null) continue;

            var timelineLines = new List<string>();
            foreach (var track in timeline.GetOutputTracks())
            {
                if (!(track is ControlTrack)) continue;
                foreach (var clip in track.GetClips())
                {
                    if (!(clip.asset is ControlPlayableAsset)) continue;
                    controlClipTotal++;

                    var key = BuildLookupKey(track.name, clip.displayName);
                    var hits = FindAssetHitsByKey(key, searchFolders).Take(6).ToList();
                    if (hits.Count > 0)
                    {
                        controlClipMatched++;
                    }

                    timelineLines.Add($"  Track={track.name} | Clip={clip.displayName}");
                    timelineLines.Add($"    Key={key}");
                    if (hits.Count == 0)
                    {
                        timelineLines.Add("    Match=<NONE>");
                    }
                    else
                    {
                        foreach (var hit in hits)
                        {
                            timelineLines.Add("    Match=" + hit);
                        }
                    }
                }
            }

            if (timelineLines.Count > 0)
            {
                lines.Add("==================================================");
                lines.Add("Timeline: " + timeline.name);
                lines.Add("Path: " + path);
                lines.AddRange(timelineLines);
                lines.Add(string.Empty);
            }
        }

        lines.Add("==================================================");
        lines.Add("统计");
        lines.Add("controlClipTotal=" + controlClipTotal);
        lines.Add("controlClipMatched=" + controlClipMatched);
        lines.Add("controlClipMissing=" + (controlClipTotal - controlClipMatched));

        var abs = Path.Combine(Directory.GetCurrentDirectory(), ControlLookupReportPath);
        var dir = Path.GetDirectoryName(abs);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllLines(abs, lines, Encoding.UTF8);
        AssetDatabase.ImportAsset(ControlLookupReportPath);
        AssetDatabase.Refresh();
        Debug.Log("[控制轨反查] 已输出: " + ControlLookupReportPath);
    }

    public static void AuditTimelineRebuildCoverage(string roleKey)
    {
        var lines = new List<string>
        {
            "Timeline 重建覆盖率自检",
            "时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            "角色关键字: " + (string.IsNullOrWhiteSpace(roleKey) ? "<empty>" : roleKey),
            string.Empty
        };

        var summaryRoot = ResolveSummaryRootFromEditorPrefs();
        if (string.IsNullOrEmpty(summaryRoot) || !Directory.Exists(summaryRoot))
        {
            lines.Add("summaryRoot: <missing>");
            WriteReport(CoverageReportPath, lines);
            Debug.LogWarning("[重建覆盖率] 未找到 summary 根目录，已输出空报告。");
            return;
        }

        var roleAliases = BuildRoleAliases(roleKey);
        bool MatchRoleText(string text)
        {
            if (roleAliases.Count == 0)
            {
                return true;
            }
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            return roleAliases.Any(alias => text.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        var allSummary = Directory.GetFiles(summaryRoot, "*__timeline_summary.json", SearchOption.AllDirectories);
        var matchedSummary = allSummary
            .Where(MatchRoleText)
            .ToList();

        var timelineGuids = AssetDatabase.FindAssets("t:TimelineAsset", new[] { TimelineRoot });
        var allTimelines = timelineGuids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(path => AssetDatabase.LoadAssetAtPath<TimelineAsset>(path))
            .Where(t => t != null)
            .ToList();

        var matchedTimelines = allTimelines
            .Where(t => MatchRoleText(t.name))
            .ToList();

        var trackful = matchedTimelines.Where(t => t.GetOutputTracks().Any()).ToList();
        var clipful = matchedTimelines.Where(t => t.GetOutputTracks().SelectMany(x => x.GetClips()).Any()).ToList();

        lines.Add($"summaryTotal={allSummary.Length}");
        lines.Add($"summaryRoleMatched={matchedSummary.Count}");
        lines.Add($"timelineTotal={allTimelines.Count}");
        lines.Add($"timelineRoleMatched={matchedTimelines.Count}");
        lines.Add($"timelineRoleMatchedWithTracks={trackful.Count}");
        lines.Add($"timelineRoleMatchedWithClips={clipful.Count}");
        lines.Add(string.Empty);
        lines.Add("示例（前20条）:");
        foreach (var t in matchedTimelines.Take(20))
        {
            var trackCount = t.GetOutputTracks().Count();
            var clipCount = t.GetOutputTracks().SelectMany(x => x.GetClips()).Count();
            lines.Add($"- {t.name} | tracks={trackCount} clips={clipCount}");
        }

        WriteReport(CoverageReportPath, lines);
        Debug.Log("[重建覆盖率] 已输出: " + CoverageReportPath);
    }

    private static List<string> BuildRoleAliases(string roleKey)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(roleKey))
        {
            aliases.Add(roleKey);
        }

        if (!string.IsNullOrWhiteSpace(roleKey) &&
            (roleKey.Equals("Sparxie", StringComparison.OrdinalIgnoreCase) ||
             roleKey.Equals("Sparkle", StringComparison.OrdinalIgnoreCase) ||
             roleKey.Equals("Hanabi", StringComparison.OrdinalIgnoreCase)))
        {
            aliases.Add("Sparxie");
            aliases.Add("Sparkle");
            aliases.Add("Hanabi");
        }

        return aliases.ToList();
    }

    private static string EvaluateTrackBinding(TrackAsset track, PlayableDirector director, Animator animator, GameObject modelTarget, Camera mainCamera)
    {
        if (track is AnimationTrack)
        {
            if (animator == null)
            {
                return "MISSING: Animator";
            }

            if (director != null)
            {
                var binding = director.GetGenericBinding(track);
                if (binding == null)
                {
                    return "MISSING: Director binding -> Animator";
                }
            }
            return "OK: Animator";
        }

        if (track is ActivationTrack || track is ControlTrack)
        {
            if (modelTarget == null)
            {
                return "MISSING: MODEL_Target";
            }

            if (director != null)
            {
                var binding = director.GetGenericBinding(track);
                if (binding == null)
                {
                    return "MISSING: Director binding -> MODEL_Target";
                }
            }
            return "OK: MODEL_Target";
        }

        if (director != null)
        {
            var binding = director.GetGenericBinding(track);
            if (binding != null)
            {
                return "OK: Director binding exists";
            }
        }

        if (mainCamera != null)
        {
            return "WARN: no explicit binding (camera fallback only)";
        }

        return "MISSING: no binding";
    }

    private static bool IsCustomType(Type t)
    {
        if (t == null) return false;
        var ns = t.Namespace ?? string.Empty;
        var asm = t.Assembly.GetName().Name ?? string.Empty;
        if (ns.StartsWith("UnityEngine.Timeline", StringComparison.Ordinal)) return false;
        if (ns.StartsWith("UnityEngine", StringComparison.Ordinal)) return false;
        if (asm.StartsWith("Unity.", StringComparison.Ordinal) || asm.Equals("UnityEngine", StringComparison.Ordinal)) return false;
        return true;
    }

    private static string BuildLookupKey(string trackName, string clipName)
    {
        var raw = (trackName ?? string.Empty) + "_" + (clipName ?? string.Empty);
        raw = raw.Replace("(marker-placeholder)", string.Empty)
                 .Replace("(placeholder)", string.Empty)
                 .Replace("(Clone)", string.Empty)
                 .Replace("Recovered", string.Empty)
                 .Replace("Track", string.Empty)
                 .Trim();
        var tokens = raw.Split(new[] { '_', '-', ' ', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 4)
            .ToArray();
        if (tokens.Length == 0)
        {
            return raw;
        }
        return string.Join(" ", tokens.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> FindAssetHitsByKey(string key, List<string> folders)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            yield break;
        }

        var tokens = key.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            yield break;
        }

        var query = string.Join(" ", tokens.Take(3));
        var guids = AssetDatabase.FindAssets(query, folders.ToArray());
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            if (ext != ".prefab" && ext != ".fbx")
            {
                continue;
            }
            yield return path;
        }
    }

    private static string ResolveSummaryRootFromEditorPrefs()
    {
        var configured = EditorPrefs.GetString(SummaryFolderPrefKey, string.Empty);
        if (!string.IsNullOrEmpty(configured))
        {
            return configured;
        }

        var legacy = EditorPrefs.GetString(LegacySummaryFolderPrefKey, string.Empty);
        if (!string.IsNullOrEmpty(legacy))
        {
            EditorPrefs.SetString(SummaryFolderPrefKey, legacy);
        }
        return legacy;
    }

    private static void WriteReport(string assetPath, List<string> lines)
    {
        var abs = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
        var dir = Path.GetDirectoryName(abs);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllLines(abs, lines, Encoding.UTF8);
        AssetDatabase.ImportAsset(assetPath);
        AssetDatabase.Refresh();
    }

    private static GameObject ResolveModelTargetForBinding()
    {
        var direct = GameObject.Find("MODEL_Target");
        if (direct != null)
        {
            return direct;
        }

        var modelRoot = GameObject.Find("MODEL_Root");
        if (modelRoot == null || modelRoot.transform.childCount == 0)
        {
            return null;
        }

        for (var i = 0; i < modelRoot.transform.childCount; i++)
        {
            var child = modelRoot.transform.GetChild(i);
            if (child != null && child.name.StartsWith("MODEL_Target", StringComparison.OrdinalIgnoreCase))
            {
                return child.gameObject;
            }
        }

        return modelRoot.transform.GetChild(0).gameObject;
    }
}


