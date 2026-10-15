# HSR 4.4 提取验收区

这里保存 HSR 4.4 的提取配置、日志和报告。原始资源放在项目外的独立
`ExtractionRuns` 目录，不是最终资源库，也不是 Unity 资源清理区。当前阶段
只负责：请求解析、预检查、原样提取和证据记录。

清理、标准化、Unity 导入、结构验证、视觉验证和复制到 canonical `SR`
目录，全部属于下一阶段工具的职责。

## 可复现前提

本规范不要求在 AS 源码外再打临时补丁。提取器必须使用
`my-assets-studio` 的 `SR-4.4` 分支，并使用 `.NET 8` 构建：

```powershell
git clone --branch SR-4.4 https://github.com/WhiteLeer/my-assets-studio.git
dotnet restore .\AnimeStudio.CLI\AnimeStudio.CLI.csproj
dotnet build .\AnimeStudio.CLI\AnimeStudio.CLI.csproj -c Release
```

构建结果是 `AnimeStudio.CLI/bin/Release/net8.0-windows/AnimeStudio.CLI.exe`。
仓库不分发游戏安装文件、Block、索引、CABMap 或 TypeTree；这些数据必须由使用者
针对同一 HSR 版本自行准备。缺少其中任意一项时，不能用扩大名称筛选或全量扫描
代替，必须在 Run 日志中记录为外部输入缺失。

HSR 4.4 的默认外部输入文件名记录在 `Profiles/HSR-4.4.json`：

- 游戏源目录：`StarRail_Data/StreamingAssets/Asb/Windows`
- 角色 GameObject AssetMap：`sr44_character_gameobjects.map`
- 角色 Material AssetMap：`sr44_character_materials.map`
- 全局动画 AssetMap：`sr44_animation_global.map`
- CABMap：`sr44_cab_map.bin`
- 外部 TypeTree：与该版本运行时匹配的 `structs.dump`

其中 AssetMap、CABMap 和 TypeTree 必须来自对应游戏版本，不能跨版本复用而不做
验证。`AS_CLI`、`SOURCE_ROOT`、`RAW_ROOT`、各索引路径和 Run 筛选器都应由执行脚本
或 `run-config.json` 提供，不能写死在 AS 源码中。

## 标准顺序

1. 在 `Runs/<运行编号>/request.json` 中记录用户请求。
2. 根据 `Profiles/HSR-4.4.json` 生成参数化配置。
3. 预检查游戏目录、索引、CABMap、TypeTree 和名称/类型筛选。
4. 分阶段提取角色模型、武器模型、动画和贴图；特效 Prefab 单独执行。
5. 记录命中对象、源 block、依赖闭包、导出数、跳过数和警告。
6. 将不可修改的 run 交给下一阶段验证工具。

如果预检查失败，不应直接扩大筛选范围或进行全量扫描，必须先记录失败
上下文。

## 通用角色闭包脚本

`my-assets-studio/SR-4.4` 分支提供了参数化脚本
`Tools/HSR44/Extract-HsrCharacter.ps1`，用于自动完成角色模型、武器/其他模型、
动画和贴图的分阶段提取。脚本不把 Herta、Sparkle 等角色写进源码；角色名、资源
前缀和本次输出目录都由参数提供。

每个 HSR 4.4 数据集只需首次建立两个版本级索引。该步骤会扫描全部 Block，可能
持续较长时间并占用较多内存；完成后同一版本的其他角色会复用索引，不再重复建立
全量索引：

```powershell
$script = 'Tools/HSR44/Extract-HsrCharacter.ps1'

& $script `
  -AsCli $AS_CLI `
  -SourceRoot $SOURCE_ROOT `
  -MapRoot $MAP_ROOT `
  -CabMap $CAB_MAP `
  -TypeTree $TYPETREE `
  -AnimationMap $ANIMATION_MAP `
  -CharacterName Herta `
  -AssetPrefix Avatar_Herta_00 `
  -RawRoot "$RAW_ROOT/Runs/20260824_Herta_003/raw" `
  -BuildIndexes
```

后续角色不再传 `-BuildIndexes`：

```powershell
& $script `
  -AsCli $AS_CLI -SourceRoot $SOURCE_ROOT -MapRoot $MAP_ROOT `
  -CabMap $CAB_MAP -TypeTree $TYPETREE -AnimationMap $ANIMATION_MAP `
  -CharacterName Sparkle -RawRoot "$RAW_ROOT/Runs/<run-id>/raw"
```

模型阶段可以用 `-MergeWeaponModel` 额外生成一个角色+武器合并 FBX。普通模型
仍会保留；合并文件位于 `raw/Character/Models/_Merge/<资源前缀>_Model_Merge.fbx`，
单独导出的武器也会按同批次角色的原始模型前缀命名，不再输出无上下文的
`Weapon/Weapon.fbx`：

合并不是把两个 FBX 的根节点简单并列。脚本优先使用
`<资源前缀>_Model_Others` 作为武器 rig，只附加其中的 Renderer，复用角色骨架，
并保留武器 Renderer 的局部位置、旋转和缩放；这样可以避免重复骨骼和比例尺漂移。
如果只命中了 standalone `Weapon` 根，日志会明确警告，不能把该结果当作最终合并
模型验收结果。

```powershell
& $script `
  -AsCli $AS_CLI -SourceRoot $SOURCE_ROOT -MapRoot $MAP_ROOT `
  -CabMap $CAB_MAP -TypeTree $TYPETREE -AnimationMap $ANIMATION_MAP `
  -CharacterName Herta -AssetPrefix Avatar_Herta_00 `
  -RawRoot "$RAW_ROOT/Runs/<run-id>/raw" `
  -Stages Models -MergeWeaponModel
```

测试时不要默认重跑所有阶段。`-Stages Models`、`-Stages Animations` 和
`-Stages Textures` 可以单独执行；`-CleanStageOutput` 只清理本次选中的阶段目录，
不会删除其他阶段、日志、索引或 provenance。例如只回归模型：

```powershell
& $script ... -Stages Models -CleanStageOutput -MergeWeaponModel
```

索引复用只能省掉全量 Block 建索引；选定阶段仍会读取该角色依赖闭包。对小范围
测试，建议使用单独 Run 目录并只选择一个阶段，避免覆盖已验证结果。

如果角色的显示名与游戏内资源 token 一致，脚本会自动匹配
`Avatar_<角色>_<变体>`；不一致时必须显式传 `-AssetPrefix`，例如资源前缀为
`Avatar_Cerydra_00`。这不是硬编码，而是本次请求的资源身份参数。

脚本的闭包策略是：先从角色根 GameObject 找源 Block，再通过 CAB 依赖加载模型、
网格、材质和动画；贴图先走材质引用，再用角色前缀做受限补充。每个 Run 会在
`logs/` 保存阶段命令和输出，在 `provenance/hsr44-resource-closure.json` 保存
实际正则、索引和阶段信息。运行时脚本动态加载的资源、缺少 TypeTree 的字段以及
未出现在静态依赖中的资源仍可能需要单独记录和补提，不能把“未导出”直接解释为
“游戏没有该资源”。

## 目录职责

```text
HSR-4.4/
  Profiles/                         # HSR 4.4 通用规则
  Runs/<运行编号>/
    request.json                    # 本次用户请求
    run-config.json                 # 参数化配置和执行结果
    normalized/                     # 下一阶段生成的 Unity 资源候选
    validation/                     # 下一阶段生成的验证报告
    logs/                           # 每个提取阶段的日志
    provenance/                     # 工具、索引和 TypeTree 来源
```

原始资源路径由 `${HSR_4_4_RAW_ROOT}` 指定，例如：

```text
${HSR_4_4_RAW_ROOT}/Runs/<运行编号>/raw/
```

AnimeStudio CLI 的第二个位置参数就是 raw 输出目录。不要把 raw 指向 Unity
项目的 `Assets`，否则 Unity 会为数 GB 的原始文件生成大量 `.meta` 并触发扫描。

例如，渲染本配置后，角色提取命令的前两个位置参数应类似：

```powershell
& $AS_CLI $sourceRoot "$HSR_4_4_RAW_ROOT\Runs\$RUN_ID\raw\Character" ...
```

其中 `$HSR_4_4_RAW_ROOT` 由本机运行配置指定，可以改为任意有足够空间的目录；
不要把绝对路径写死在 Profile 或 AnimeStudio 源码中。`run-config.json` 中所有
角色、特效和探测命令都使用同一个 raw 根目录。

## AnimeStudio 命令约定

所有命令都必须使用本次 Run 的变量和对应索引，不要把角色名写进工具源码。
`--types` 中的标记含义是：`Parse` 允许读取，`Export` 允许输出，`Both` 两者都启用。
需要转换的 Unity 对象不能只写 `:Export`；例如贴图必须使用
`Texture2D:Both`，动画必须使用 `AnimationClip:Both`。

角色模型和武器模型：

```powershell
& $AS_CLI $SOURCE_ROOT "$RAW_ROOT\Character" `
  --game SR --map_op AllMaps --map_type MessagePack `
  --asset_map $CHARACTER_GAMEOBJECT_MAP --cab_map $CAB_MAP `
  --type_tree_dump $TYPETREE --batch_load `
  --export_type FBX --group_assets ByModel `
  --types GameObject:Both --names $MODEL_NAME_REGEX
```

角色动画：

```powershell
& $AS_CLI $SOURCE_ROOT "$RAW_ROOT\Character" `
  --game SR --map_op AllMaps --map_type MessagePack `
  --asset_map $CHARACTER_GAMEOBJECT_MAP --animation_map $ANIMATION_MAP `
  --cab_map $CAB_MAP --type_tree_dump $TYPETREE --batch_load `
  --export_type Convert --group_assets ByModel `
  --types GameObject:Both AnimationClip:Both --names $ANIMATION_NAME_REGEX
```

贴图优先按材质依赖提取：

```powershell
& $AS_CLI $SOURCE_ROOT "$RAW_ROOT\Character\Textures" `
  --game SR --map_op AllMaps --map_type MessagePack `
  --asset_map $CHARACTER_MATERIAL_MAP --cab_map $CAB_MAP `
  --type_tree_dump $TYPETREE --batch_load `
  --export_type Convert --group_assets None `
  --types Material:Both Texture2D:Both `
  --map_names $MATERIAL_SOURCE_REGEX `
  --material_dependencies --material_roots $MATERIAL_NAME_REGEX
```

如果材质索引不完整，再执行一次以角色请求中的贴图命名规则为范围的补充提取，
仍然使用 `Texture2D:Both`，并将补充结果与依赖结果合并，同时记录两次命中数。
不能用全量 `Texture2D` 扫描替代缺失诊断。

角色名等具体信息只应出现在 `request.json` 和本次 run 报告中。配置使用
`${SR_4_4_ROOT}`、`${AS_CLI}`、`${RUN_ID}` 等变量，执行前必须完成渲染。

## 当前文件格式

- `.fbx`：AnimeStudio 导出的 Kaydara FBX，Unity 可直接识别。
- `.anim`：Unity YAML AnimationClip，Unity 可识别，但仍需验证 Avatar 绑定。
- `.srprefab`：AnimeStudio 自定义 ZIP 中间包，不是 Unity `.prefab`；内部
  包含 manifest、组件参数和依赖描述。

`.srprefab` 只能保留在外部 raw 的 `Effects` 目录。材质、贴图、网格和 Shader 必须由
下一阶段根据依赖信息生成或导入为 Unity 资源。

## 缺失结果说明

报告必须用普通语言区分以下情况：

- **索引没有命中**：当前索引和筛选没有找到对象，可能是索引或名称变体问题。
- **没有源 block**：索引找到对象，但没有解析出它所在的源文件。
- **源 block 不存在**：索引记录了文件，但当前游戏目录中没有该文件。
- **依赖无法解析**：根 block 存在，但 CAB 依赖没有闭合。
- **被筛选排除**：对象存在，但名称或类型筛选主动排除了它。
- **TypeTree/导出失败**：对象加载了，但字段解码或目标格式导出失败。

不能把“没有输出”直接解释为“游戏没有该资源”。

## 当前 Herta Run

`Runs/20260824_Herta_002` 保留此前仅含角色模型/动画的基线批次。
`Runs/20260824_Herta_003` 是包含角色模型、武器/其他模型、动画和贴图的扩展批次。
每个 Run 的结果、缺失项、重复审计、日志和输入文件哈希均保存在该 Run 内；raw
目录不应被清理或覆盖。
