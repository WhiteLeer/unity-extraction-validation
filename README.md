# Unity Extraction Validation

Unity-side import and validation tools. Place this repository at
`Assets/unity-extraction-validation` inside a compatible Unity project; this is
an Assets subtree, not a standalone Unity project.

## Current Scope

- `_CheckTool`: extraction-run checks and incremental synchronization to Clean.
  See [`_CheckTool/README.md`](_CheckTool/README.md).
- `HSR-4.4/Profiles`: historical HSR 4.4 extraction contract. It is not a
  validated HSR 4.6 parser or profile.
- `SR/Editor` and `ZZZ/Editor`: legacy version-specific import and preview
  helpers. The historical `SR` directory and source identifiers have not been
  mechanically renamed because serialized asset and script references must be
  migrated together.

The current HSR client target is 4.6. Version numbers identify source evidence,
not a promise that every importer supports that version. Never relabel old
TypeTrees, extraction runs or captures as new-version data.

Current static Shader reconstruction helper sources live in
[`my-hook-tool/unity`](https://github.com/WhiteLeer/my-hook-tool/tree/HSR-4.6-MUMU/unity).
The local `Assets/RenderDocShaderSamples` experiments are outside this Git
subtree; generated game assets are not included here.

## Source Control

Commit tools, profiles and documentation. Keep raw game packages, extraction
runs, Clean outputs and captures outside source control. Import HoyoToon or
other optional third-party render integrations separately when a legacy
workflow requires them; their source is no longer bundled in this repository.
