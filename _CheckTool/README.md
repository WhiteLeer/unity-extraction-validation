# Extraction Check Tool

菜单：`Tools > Extraction > Check and Build Clean`

1. 在“当前类型”下拉栏选择 `HSR-4.4` 或 `ZZZ-3.3`。
2. 将单个提取 Run 目录拖入窗口，例如 `ExtractionRuns/HSR-4.4/Runs/20260824_Herta_003`。
   这里可以直接使用外部目录，不需要先把 FBX、动画或贴图导入 Unity。
3. 工具会从当前 Unity 项目的 `Assets/unity-extraction-validation/<Profile>/Profiles/` 读取通用 Profile，
   因此外部 Run 不需要复制 Profile；Run 目录本身仍应包含 `request.json` 和 `run-config.json`。
4. 点击“检查格式”，再点击“重建 Clean”。只重建当前角色目录，其他角色、raw、日志、配置和报告不会被删除。

当前只实现 `HSR-4.4` 的检查和 Clean 逻辑；`ZZZ-3.3` 先作为明确的 Profile 入口，
选择它时不会误用 HSR 规则，待补充 ZZZ 的源目录和命名规范后再启用。

生成的角色目录固定为：

```text
Clean/<角色显示名>/
  _Base_Anim/
  _Base_Model/
  _Base_Texture/
  _Extra_0_Material/
  _Extra_0_Shader/
  _Extra_1_Effect/
  _Extra_1_Prefab/
  _Extra_2_TimeLine/
  _Preview/
```

当前阶段只复制角色模型 `.fbx`、动画 `.anim` 和贴图文件。Run 可以是分阶段提取的；
未提供的阶段目录会给出警告，但不会阻止其他阶段检查或生成 Clean。
`.srprefab`、材质、Shader、JSON、TXT、日志和 `.meta` 只作为 raw 或诊断证据保留，
不复制到 `Clean`。所有 `_Extra_*` 和 `_Preview` 只创建目录，暂不填充。

工具记住当前 Windows 用户最近一次使用的提取 Run 目录。
