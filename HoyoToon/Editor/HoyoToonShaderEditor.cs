using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HoyoToon
{
    public sealed class ShaderEditor : ShaderGUI
    {
        private static readonly Dictionary<string, bool> SectionStates = new Dictionary<string, bool>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> ExactLabelOverrides = CreateExactLabelOverrides();
        private static readonly Dictionary<string, string> TranslationCache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> WordReplacementMap = CreateWordReplacementMap();

        private static readonly (string From, string To)[] PhraseReplacements =
        {
            ("Rendering Options", "渲染选项"),
            ("Lighting Options", "光照选项"),
            ("Special Effects", "特效"),
            ("Additive Bloom Control", "叠加泛光控制"),
            ("Per Region Bloom Intensity", "分区泛光强度"),
            ("Built In Tonemapping", "内置色调映射"),
            ("Height Gradient Colors", "高度渐变颜色"),
            ("Shadow Levels", "阴影等级"),
            ("Material Values Pack", "材质参数包"),
            ("Material Values LUT", "材质参数 LUT"),
            ("Material LUT", "材质 LUT"),
            ("Face Expression", "面部表情"),
            ("Face Shading", "面部着色"),
            ("Rim Shadow", "边缘阴影"),
            ("Rim Light", "边缘光"),
            ("Rimlight", "边缘光"),
            ("Light Map", "光照贴图"),
            ("LightMap", "光照贴图"),
            ("Face Map", "面部贴图"),
            ("Alpha Options", "透明选项"),
            ("Color Options", "颜色选项"),
            ("Secondary Texture", "次级贴图"),
            ("Custom Colors", "自定义颜色"),
            ("Custom Skin Color", "自定义肤色"),
            ("Facing Direction", "朝向"),
            ("Show IDS", "显示 ID"),
            ("SwirlDissolve", "旋涡溶解"),
            ("Starry Sky", "星空"),
            ("Hue Shifting", "色相偏移"),
            ("Fake Reflection", "伪反射"),
            ("Enable Self Shadow", "启用自阴影"),
            ("Use Self Shadow", "使用自阴影"),
            ("Enable Material Values LUT", "启用材质参数 LUT"),
            ("Enable Caustic RGB Split", "启用焦散 RGB 分离"),
            ("Enable Override Time", "启用覆盖时间"),
            ("Enable Random Seed", "启用随机种子"),
            ("Enable FOV Scaling", "启用视场缩放"),
            ("Enable Material LUT", "启用材质 LUT"),
            ("Enable Lighting from Multiple Sources", "启用多光源照明"),
            ("Limit Spot/Point Light Intensity", "限制点光/聚光灯强度"),
            ("Enable Diffuse Hue Shift", "启用漫反射色相偏移"),
            ("Enable Emission Hue Shift", "启用发光色相偏移"),
            ("Enable Rim Hue Shift", "启用边缘光色相偏移"),
            ("Enable Outline Hue Shift", "启用描边色相偏移"),
            ("Enable Hue Mask", "启用色相遮罩"),
            ("Enable Emission", "启用发光"),
            ("Enable Shadow", "启用阴影"),
            ("Enable Specular", "启用高光"),
            ("Enable Stockings", "启用丝袜"),
            ("Enable Outlines", "启用描边"),
            ("Enable Caustics", "启用焦散"),
            ("Enable Dissolve", "启用溶解"),
            ("Enable Height Light", "启用高度光照"),
            ("Enable Swirl Dissolve", "启用旋涡溶解"),
            ("Enable Debug Mode", "启用调试模式"),
            ("Enable Transparency", "启用透明")
        };

        private static readonly (string From, string To)[] WordReplacements =
        {
            ("Alpha", "Alpha"),
            ("Blend", "混合"),
            ("Bloom", "泛光"),
            ("Back", "背面"),
            ("Base", "基础"),
            ("Channel", "通道"),
            ("Color", "颜色"),
            ("Top", "上"),
            ("Bottom", "下"),
            ("First", "第一"),
            ("Second", "第二"),
            ("Third", "第三"),
            ("Fourth", "第四"),
            ("Fifth", "第五"),
            ("Sixth", "第六"),
            ("Seventh", "第七"),
            ("Eighth", "第八"),
            ("Warm", "暖"),
            ("Cool", "冷"),
            ("Front", "正面"),
            ("Caustic", "焦散"),
            ("Cull", "剔除"),
            ("Custom", "自定义"),
            ("Debug", "调试"),
            ("Diffuse", "漫反射"),
            ("Direction", "方向"),
            ("Dissolve", "溶解"),
            ("Enable", "启用"),
            ("Emission", "发光"),
            ("Face", "面部"),
            ("FOV", "视场"),
            ("Global", "全局"),
            ("Hair", "头发"),
            ("Height", "高度"),
            ("Hue", "色相"),
            ("ID", "ID"),
            ("Intensity", "强度"),
            ("Light", "光照"),
            ("Lighting", "光照"),
            ("LUT", "LUT"),
            ("Main", "主"),
            ("Mask", "遮罩"),
            ("Matcap", "Matcap"),
            ("Material", "材质"),
            ("Mode", "模式"),
            ("Options", "选项"),
            ("Outline", "描边"),
            ("Position", "位置"),
            ("Power", "功率"),
            ("Random", "随机"),
            ("Reflection", "反射"),
            ("Rim", "边缘"),
            ("Roughness", "粗糙度"),
            ("Secondary", "次级"),
            ("Shadow", "阴影"),
            ("Shininess", "高光度"),
            ("Skin", "皮肤"),
            ("Speed", "速度"),
            ("Specular", "高光"),
            ("Stencil", "模板"),
            ("Strength", "强度"),
            ("Texture", "贴图"),
            ("Threshold", "阈值"),
            ("Toggle", "开关"),
            ("Transparency", "透明"),
            ("Use", "使用"),
            ("Vertex", "顶点"),
            ("Width", "宽度"),
            ("World", "世界"),
            ("Offset", "偏移"),
            ("Scale", "缩放"),
            ("Shape", "形状"),
            ("Softness", "柔和度"),
            ("Source", "源"),
            ("Destination", "目标"),
            ("Rendering", "渲染"),
            ("Starry", "星空"),
            ("Sky", "天空")
        };

        private static Dictionary<string, string> CreateExactLabelOverrides()
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);

            dict["HoyoToon / Honkai Star Rail / Character"] = "HoyoToon / 崩坏：星穹铁道 / 角色";
            dict["Main"] = "主设置";
            dict["Face"] = "面部";
            dict["Lighting Options"] = "光照选项";
            dict["Specular"] = "高光";
            dict["Fake Reflection"] = "伪反射";
            dict["Matcap"] = "Matcap";
            dict["Stockings"] = "丝袜";
            dict["Outlines"] = "描边";
            dict["Additive Bloom Control"] = "叠加泛光控制";
            dict["Special Effects"] = "特效";
            dict["Rendering Options"] = "渲染选项";
            dict["Secondary Texture"] = "次级贴图";
            dict["Alpha Options"] = "透明选项";
            dict["Color Options"] = "颜色选项";
            dict["Custom Colors"] = "自定义颜色";
            dict["Custom Skin Color"] = "自定义肤色";
            dict["Facing Direction"] = "朝向";
            dict["Show IDS"] = "显示 ID";
            dict["Face Expression"] = "面部表情";
            dict["Shadow"] = "阴影";
            dict["Rim Light"] = "边缘光";
            dict["Rimlight Color"] = "边缘光颜色";
            dict["Rimlight Softness"] = "边缘光柔和度";
            dict["Rimlight Type"] = "边缘光类型";
            dict["Rimlight Dark"] = "边缘光暗部";
            dict["Rim Shadow"] = "边缘阴影";
            dict["Built In Tonemapping"] = "内置色调映射";
            dict["Height Light"] = "高度光照";
            dict["SwirlDissolve"] = "旋涡溶解";
            dict["Starry Sky"] = "星空";
            dict["Hue Shifting"] = "色相偏移";
            dict["Material Type"] = "材质类型";
            dict["Main Channel"] = "主通道";
            dict["Diffuse"] = "漫反射";
            dict["Light Map"] = "光照贴图";
            dict["LightMap"] = "光照贴图";
            dict["Face Map"] = "面部贴图";
            dict["Face Expression Map"] = "面部表情贴图";
            dict["Mat Pack LUT"] = "材质参数 LUT";
            dict["Material Values LUT"] = "材质参数 LUT";
            dict["Enable Lighting from Multiple Sources"] = "启用多光源照明";
            dict["Limit Spot/Point Light Intensity"] = "限制点光/聚光灯强度";
            dict["Enable Material LUT"] = "启用材质 LUT";
            dict["Enable Secondary Diffuse"] = "启用次级漫反射";
            dict["Enable Transparency"] = "启用透明";
            dict["Enable Alpha Cutoff"] = "启用 Alpha 截断";
            dict["Alpha Cutoff value"] = "Alpha 截断阈值";
            dict["Front Face Color"] = "正面颜色";
            dict["Back Face Color"] = "背面颜色";
            dict["Enable Custom Colors"] = "启用自定义颜色";
            dict["ID Texture"] = "ID 贴图";
            dict["Enable Shadow"] = "启用阴影";
            dict["Enable Self Shadow"] = "启用自阴影";
            dict["Use Self Shadow"] = "使用自阴影";
            dict["Enable Rim Light"] = "启用边缘光";
            dict["Enable Rim Hue Shift"] = "启用边缘光色相偏移";
            dict["Enable Outline Hue Shift"] = "启用描边色相偏移";
            dict["Enable Emission Hue Shift"] = "启用发光色相偏移";
            dict["Enable Diffuse Hue Shift"] = "启用漫反射色相偏移";
            dict["Enable Hue Mask"] = "启用色相遮罩";
            dict["Enable Specular"] = "启用高光";
            dict["Enable Outlines"] = "启用描边";
            dict["Enable Stockings"] = "启用丝袜";
            dict["Enable Caustics"] = "启用焦散";
            dict["Enable Dissolve"] = "启用溶解";
            dict["Enable LUT"] = "启用 LUT";
            dict["Enable Height Light"] = "启用高度光照";
            dict["Enable Swirl Dissolve"] = "启用旋涡溶解";
            dict["Enable Debug Mode"] = "启用调试模式";
            dict["Enable Random Seed"] = "启用随机种子";
            dict["Enable Override Time"] = "启用覆盖时间";
            dict["Enable FOV Scaling"] = "启用视场缩放";
            dict["Enable Cube Map"] = "启用立方体贴图";
            dict["Use Cube Map"] = "使用立方体贴图";
            dict["Use Matcap"] = "使用 Matcap";
            dict["Use Matcap as Diffuse Color"] = "将 Matcap 作为漫反射颜色";
            dict["Use Vertex Color"] = "使用顶点色";
            dict["Use World Position"] = "使用世界坐标";
            dict["Use Direction"] = "使用方向";
            dict["Use Hue Mask"] = "使用色相遮罩";
            dict["Use Hair Side Fade"] = "使用头发侧边渐隐";
            dict["Use Stencils"] = "使用模板";
            dict["Enable Caustic RGB Split"] = "启用焦散 RGB 分离";
            dict["Enable Auto Hue Shift"] = "启用自动色相偏移";
            dict["Lock Material"] = "锁定材质";
            dict["Off"] = "关闭";
            dict["On"] = "开启";
            dict["Ingame"] = "游戏内";
            dict["Add"] = "加法";
            dict["Additive"] = "加法";
            dict["Multiply"] = "乘法";
            dict["Override"] = "覆盖";
            dict["Opaque"] = "不透明";
            dict["Factor"] = "因子";
            dict["Both"] = "两者";
            dict["Forward"] = "前";
            dict["Right"] = "右";
            dict["Left"] = "左";
            dict["Up"] = "上";
            dict["Original (Encoded)"] = "原始（编码）";
            dict["Original (Raw)"] = "原始（未编码）";
            dict["All(Color Coded)"] = "全部（颜色编码）";
            dict["Materail ID 1"] = "材质 ID 1";
            dict["Material ID 1"] = "材质 ID 1";
            dict["Material ID 2"] = "材质 ID 2";
            dict["Material ID 3"] = "材质 ID 3";
            dict["Material ID 4"] = "材质 ID 4";
            dict["Material ID 5"] = "材质 ID 5";
            dict["Material ID 6"] = "材质 ID 6";
            dict["Material ID 7"] = "材质 ID 7";
            dict["Material ID 8"] = "材质 ID 8";

            return dict;
        }

        private static Dictionary<string, string> CreateWordReplacementMap()
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < WordReplacements.Length; i++)
            {
                dict[WordReplacements[i].From] = WordReplacements[i].To;
            }

            return dict;
        }

        private static readonly HashSet<string> SkippedProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "shader_is_using_HoyoToon_editor",
            "ShaderBG",
            "ShaderLogo",
            "CharacterLeft",
            "CharacterRight",
            "shader_is_using_hoyeditor",
            "footer_github",
            "footer_discord",
        };

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            if (materialEditor == null || properties == null)
            {
                return;
            }

            var previousIndent = EditorGUI.indentLevel;
            var sectionStack = new Stack<bool>();
            var visibleDepth = 0;
            sectionStack.Push(true);

            try
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(TranslateLabel("HoyoToon / Honkai Star Rail / Character"), MessageType.None);
                EditorGUILayout.Space(4f);

                foreach (var property in properties)
                {
                    if (property == null || SkippedProperties.Contains(property.name))
                    {
                        continue;
                    }

                    if (IsSectionStart(property.name))
                    {
                        var parentVisible = sectionStack.Peek();
                        var sectionLabel = TranslateLabel(property.displayName);
                        var sectionKey = property.name;

                        var expanded = SectionStates.TryGetValue(sectionKey, out var storedExpanded)
                            ? storedExpanded
                            : ShouldOpenByDefault(sectionKey);

                        if (parentVisible)
                        {
                            EditorGUILayout.Space(IsTopLevelSection(sectionKey) ? 6f : 2f);
                            EditorGUI.indentLevel = visibleDepth;
                            expanded = EditorGUILayout.Foldout(
                                expanded,
                                string.IsNullOrEmpty(sectionLabel) ? TranslateLabel(FriendlySectionName(sectionKey)) : sectionLabel,
                                true);
                            SectionStates[sectionKey] = expanded;
                        }

                        var sectionVisible = parentVisible && expanded;
                        sectionStack.Push(sectionVisible);
                        if (sectionVisible)
                        {
                            visibleDepth++;
                            EditorGUI.indentLevel = visibleDepth;
                        }
                        continue;
                    }

                    if (IsSectionEnd(property.name))
                    {
                        if (sectionStack.Count > 1)
                        {
                            var endedVisible = sectionStack.Pop();
                            if (endedVisible)
                            {
                                visibleDepth = Math.Max(0, visibleDepth - 1);
                            }

                            EditorGUI.indentLevel = visibleDepth;
                        }

                        continue;
                    }

                    if (!sectionStack.Peek())
                    {
                        continue;
                    }

                    if (IsHidden(property))
                    {
                        continue;
                    }

                    materialEditor.ShaderProperty(property, TranslateLabel(property.displayName));
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndent;
            }
        }

        private static bool IsSectionStart(string name)
        {
            return name.StartsWith("start_", StringComparison.Ordinal);
        }

        private static bool IsSectionEnd(string name)
        {
            return name.StartsWith("end_", StringComparison.Ordinal);
        }

        private static bool IsTopLevelSection(string name)
        {
            return name == "start_main"
                || name == "start_faceshading"
                || name == "start_lighting"
                || name == "start_specular"
                || name == "start_fakereflection"
                || name == "start_matcap"
                || name == "start_stockings"
                || name == "start_outlines"
                || name == "start_bloomcontrol"
                || name == "start_specialeffects"
                || name == "start_renderingOptions";
        }

        private static bool ShouldOpenByDefault(string sectionKey)
        {
            return sectionKey == "start_main";
        }

        private static bool IsHidden(MaterialProperty property)
        {
            return (property.flags & MaterialProperty.PropFlags.HideInInspector) != 0;
        }

        private static string FriendlySectionName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            if (name.StartsWith("start_", StringComparison.Ordinal))
            {
                name = name.Substring("start_".Length);
            }
            else if (name.StartsWith("end_", StringComparison.Ordinal))
            {
                name = name.Substring("end_".Length);
            }

            return name.Replace('_', ' ').Trim();
        }

        public static string TranslateLabel(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return string.Empty;
            }

            var markerIndex = label.IndexOf("--", StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                label = label.Substring(0, markerIndex);
            }

            label = label.Trim();
            if (string.IsNullOrEmpty(label))
            {
                return string.Empty;
            }

            var suffixIndex = label.IndexOf('|');
            var suffix = string.Empty;
            if (suffixIndex >= 0)
            {
                suffix = " " + label.Substring(suffixIndex).Trim();
                label = label.Substring(0, suffixIndex).Trim();
            }

            var cacheKey = label + suffix;
            if (TranslationCache.TryGetValue(cacheKey, out var cachedTranslation))
            {
                return cachedTranslation;
            }

            if (ExactLabelOverrides.TryGetValue(label, out var exactTranslation))
            {
                var exactResult = exactTranslation + suffix;
                TranslationCache[cacheKey] = exactResult;
                return exactResult;
            }

            var translated = label;
            foreach (var replacement in PhraseReplacements)
            {
                translated = translated.Replace(replacement.From, replacement.To);
            }

            translated = ReplaceTokenWords(translated);
            translated = CollapseSpaces(translated);

            var finalTranslation = translated + suffix;
            TranslationCache[cacheKey] = finalTranslation;
            return finalTranslation;
        }

        private static string ReplaceTokenWords(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var tokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return string.Empty;
            }

            var changed = false;
            for (var i = 0; i < tokens.Length; i++)
            {
                if (WordReplacementMap.TryGetValue(tokens[i], out var replacement))
                {
                    tokens[i] = replacement;
                    changed = true;
                }
            }

            return changed ? string.Join(" ", tokens) : text;
        }

        private static string CollapseSpaces(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(text.Length);
            var inSpace = false;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (char.IsWhiteSpace(ch))
                {
                    inSpace = true;
                    continue;
                }

                if (inSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(ch);
                inSpace = false;
            }

            return builder.ToString().Trim();
        }
    }
}

public sealed class HoyoToonWideEnumDrawer : MaterialPropertyDrawer
{
    private readonly string[] translatedLabels;
    private readonly float[] values;

    public HoyoToonWideEnumDrawer(string label1, float value1)
        : this(new[] { label1 }, new[] { value1 })
    {
    }

    public HoyoToonWideEnumDrawer(string label1, float value1, string label2, float value2)
        : this(new[] { label1, label2 }, new[] { value1, value2 })
    {
    }

    public HoyoToonWideEnumDrawer(string label1, float value1, string label2, float value2, string label3, float value3)
        : this(new[] { label1, label2, label3 }, new[] { value1, value2, value3 })
    {
    }

    public HoyoToonWideEnumDrawer(string label1, float value1, string label2, float value2, string label3, float value3, string label4, float value4)
        : this(new[] { label1, label2, label3, label4 }, new[] { value1, value2, value3, value4 })
    {
    }

    public HoyoToonWideEnumDrawer(string label1, float value1, string label2, float value2, string label3, float value3, string label4, float value4, string label5, float value5)
        : this(new[] { label1, label2, label3, label4, label5 }, new[] { value1, value2, value3, value4, value5 })
    {
    }

    public HoyoToonWideEnumDrawer(string label1, float value1, string label2, float value2, string label3, float value3, string label4, float value4, string label5, float value5, string label6, float value6)
        : this(new[] { label1, label2, label3, label4, label5, label6 }, new[] { value1, value2, value3, value4, value5, value6 })
    {
    }

    public HoyoToonWideEnumDrawer(string label1, float value1, string label2, float value2, string label3, float value3, string label4, float value4, string label5, float value5, string label6, float value6, string label7, float value7)
        : this(new[] { label1, label2, label3, label4, label5, label6, label7 }, new[] { value1, value2, value3, value4, value5, value6, value7 })
    {
    }

    public HoyoToonWideEnumDrawer(string label1, float value1, string label2, float value2, string label3, float value3, string label4, float value4, string label5, float value5, string label6, float value6, string label7, float value7, string label8, float value8)
        : this(new[] { label1, label2, label3, label4, label5, label6, label7, label8 }, new[] { value1, value2, value3, value4, value5, value6, value7, value8 })
    {
    }

    public HoyoToonWideEnumDrawer(string label1, float value1, string label2, float value2, string label3, float value3, string label4, float value4, string label5, float value5, string label6, float value6, string label7, float value7, string label8, float value8, string label9, float value9)
        : this(new[] { label1, label2, label3, label4, label5, label6, label7, label8, label9 }, new[] { value1, value2, value3, value4, value5, value6, value7, value8, value9 })
    {
    }

    public HoyoToonWideEnumDrawer(string label1, float value1, string label2, float value2, string label3, float value3, string label4, float value4, string label5, float value5, string label6, float value6, string label7, float value7, string label8, float value8, string label9, float value9, string label10, float value10)
        : this(new[] { label1, label2, label3, label4, label5, label6, label7, label8, label9, label10 }, new[] { value1, value2, value3, value4, value5, value6, value7, value8, value9, value10 })
    {
    }

    private HoyoToonWideEnumDrawer(string[] names, float[] numericValues)
    {
        translatedLabels = TranslateLabels(names);
        values = numericValues ?? Array.Empty<float>();
    }

    public override void OnGUI(Rect position, MaterialProperty property, string label, MaterialEditor editor)
    {
        if (translatedLabels.Length == 0)
        {
            DrawFallbackFloatField(position, property, label);
            return;
        }

        var cleanLabel = HoyoToon.ShaderEditor.TranslateLabel(label);
        var currentIndex = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if (Math.Abs(values[i] - property.floatValue) < 0.0001f)
            {
                currentIndex = i;
                break;
            }
        }

        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = property.hasMixedValue;
        var nextIndex = EditorGUI.Popup(position, cleanLabel, currentIndex, translatedLabels);
        EditorGUI.showMixedValue = false;

        if (EditorGUI.EndChangeCheck() && nextIndex >= 0 && nextIndex < values.Length)
        {
            property.floatValue = values[nextIndex];
        }
    }

    private static void DrawFallbackFloatField(Rect position, MaterialProperty property, string label)
    {
        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = property.hasMixedValue;
        var value = EditorGUI.FloatField(position, HoyoToon.ShaderEditor.TranslateLabel(label), property.floatValue);
        EditorGUI.showMixedValue = false;

        if (EditorGUI.EndChangeCheck())
        {
            property.floatValue = value;
        }
    }

    private static string[] TranslateLabels(string[] names)
    {
        if (names == null || names.Length == 0)
        {
            return Array.Empty<string>();
        }

        var translated = new string[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            translated[i] = HoyoToon.ShaderEditor.TranslateLabel(names[i]);
        }

        return translated;
    }
}

public sealed class HoyoToonShaderOptimizerLockButtonDrawer : MaterialPropertyDrawer
{
    public override void OnGUI(Rect position, MaterialProperty property, string label, MaterialEditor editor)
    {
        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = property.hasMixedValue;
        var next = EditorGUI.ToggleLeft(position, HoyoToon.ShaderEditor.TranslateLabel(label), property.floatValue > 0.5f);
        EditorGUI.showMixedValue = false;

        if (EditorGUI.EndChangeCheck())
        {
            property.floatValue = next ? 1f : 0f;
        }
    }
}

public sealed class SmallTextureDrawer : MaterialPropertyDrawer
{
    public override void OnGUI(Rect position, MaterialProperty property, string label, MaterialEditor editor)
    {
        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = property.hasMixedValue;
        var next = EditorGUI.ObjectField(position, HoyoToon.ShaderEditor.TranslateLabel(label), property.textureValue, typeof(Texture), false) as Texture;
        EditorGUI.showMixedValue = false;

        if (EditorGUI.EndChangeCheck())
        {
            property.textureValue = next;
        }
    }
}

public sealed class HelpboxDrawer : MaterialPropertyDrawer
{
    public override float GetPropertyHeight(MaterialProperty property, string label, MaterialEditor editor)
    {
        return EditorGUIUtility.singleLineHeight * 2f + EditorGUIUtility.standardVerticalSpacing;
    }

    public override void OnGUI(Rect position, MaterialProperty property, string label, MaterialEditor editor)
    {
        EditorGUI.HelpBox(position, HoyoToon.ShaderEditor.TranslateLabel(property.displayName), MessageType.Info);
    }
}
