# SR Effect Regression

The importer and the regression audit are separate. Importing an effect does not
mean that the result is correct; the audit must pass before visual comparison.

## Standard flow

1. Produce one dependency manifest and its `Components` directory from the same
   source block set.
2. Import the manifest with `Tools/SR/Rebuild Effect Prefab From Manifest...`.
3. Run `Tools/SR/Audit Effect Prefab From Manifest...` and select the same
   `manifest.json` and generated `.prefab`.
4. Open the JSON report under `Temp/SR_EffectAudits`. A `PASS` means the source
   and prefab agree on the structural checks below. A `FAIL` report contains a
   machine-readable issue list with the node path and mismatch category.
5. Only after a pass, run the Unity preview and RenderDoc visual comparison.

## Command line

Run Unity in batch mode with:

```text
-executeMethod SrEffectPrefabTools.SrEffectPrefabAudit.RunFromCommandLine
-srManifest <absolute path to manifest.json>
-srPrefab Assets/unity-extraction-validation/SR/effects/<effect>/<effect>.prefab
-srAuditOutput <optional absolute path to report.json>
```

Exit codes are `0` for pass, `2` for audit failure, and `3` for an execution
error. This makes the audit usable from a PowerShell batch or CI job.

## Checks

- Source and prefab node, particle-system, renderer, and Light counts.
- Per-node ParticleSystem module enable bits.
- Non-empty serialized animation-curve array counts per module.
- Color module alpha-key array sizes.
- Particle renderer material slots, null material slots, and mesh slots.
- Exact source node path to prefab transform mapping.

The report deliberately distinguishes structural parity from visual parity. A
passing report does not prove that an unavailable game shader or runtime
MonoBehaviour has been reconstructed; those remain explicit visual/runtime
follow-up checks instead of being hidden by fallback success.
