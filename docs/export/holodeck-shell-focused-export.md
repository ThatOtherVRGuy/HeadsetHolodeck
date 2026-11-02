# Focused Holodeck Shell Export Checklist

## Purpose

This checklist captures the exact Unity-to-WebXR export path for the focused static holodeck shell used by HeadsetHolodeckWeb Milestone 2.

The export is intentionally small. It includes the arch/frame, immediate shell or floor geometry, and anchor transforms. It does not include generated worlds, runtime UI logic, physics bodies, full scene parity, or experimental objects.

## Output Location

Copy the final export to:

`apps/web/public/assets/unity-export/holodeck-shell/`

Accepted primary asset names:

- `holodeck-shell.glb`
- `holodeck-shell.gltf`

If `.gltf` is used, keep the `.bin` and texture files in the same folder or direct child folders.

## Required Anchors

Every export must contain nodes with these exact names:

- `WorldRoot`
- `GeneratedWorldRoot`
- `MainStatusPanelAnchor`
- `RecordControlAnchor`
- `UserStartPose`

If empty GameObjects do not survive export, replace them with tiny marker meshes or exporter-compatible nodes that preserve the same names.

## Static Intent Log

Record each exported object and its intended static role.

| Unity Object | Include? | Transform Static | Render Static | Lighting Static | Notes |
| --- | --- | --- | --- | --- | --- |
| WorldRoot | yes | yes | no | no | Anchor/root marker |
| GeneratedWorldRoot | yes | yes | no | no | Runtime generated content parent |
| MainStatusPanelAnchor | yes | yes | no | no | Primary UI anchor |
| RecordControlAnchor | yes | yes | no | no | Recording controls anchor |
| UserStartPose | yes | yes | no | no | Initial player/camera pose |

## Export Steps

1. Open HeadsetHolodeckDev in Unity 6000.2.10f1.
2. Open `Assets/Scenes/Holodeck.unity`.
3. Verify or create the five required anchors.
4. Select only the focused shell geometry and required anchors.
5. Fill in the Static Intent Log above for every selected object.
6. Ensure selected geometry uses web-friendly materials where practical.
7. Export as GLB or GLTF using the available Unity exporter.
8. Open the exported file in an external GLTF viewer or importer.
9. Confirm all five required anchor names are present.
10. Confirm scale and orientation are sane: user height around 1.5-1.8m, forward direction matches the intended generated-world view.
11. Copy the export into `apps/web/public/assets/unity-export/holodeck-shell/`.
12. Run the web app and verify the runtime shell loader reports the shell loaded.

## Validation Notes

Record the actual export result here before committing the asset:

- Export format:
- Exporter/package used:
- Primary asset path:
- Companion files:
- Missing or renamed anchors:
- Scale/orientation issues:
- Material issues:
- Static metadata survived in GLTF extras/userData:
- Runtime loader result:
