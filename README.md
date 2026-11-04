# After Effects to Unity

Cut-out character animation from After Effects to Unity: body parts become sprites,
layer parenting becomes the hierarchy, each comp becomes an AnimationClip.

## Install (Unity)

Package Manager > **+** > **Add package from git URL…**

```
https://github.com/gonzamoiguer/AE-Anim-to-Unity
```

Pin a release with `#v1.0.0` at the end of the URL.

## Install (After Effects)

1. In Unity: **Tools > After Effects to Unity > Show After Effects Script**
2. In After Effects open **File > Scripts > Install ScriptUI Panel...** select `ExportForUnity.jsx` restart AE.
3. Open **Window > ExportForUnity.jsx** and dock it.
4. Enable *Preferences > Scripting & Expressions > Allow Scripts to Write Files and Access Network*.

## Use

- One comp per animation, all duplicates of the same rig (same layer names and parenting).
- Import Illustrator files as *Composition – Retain Layer Sizes*. Anchor points = pivots.
- Select the comps in the Project panel and export into your Unity `Assets`:
  - **Export character**: `.aeanim` (prefab + clips) and `_aeparts/` PNGs.
  - **Export animation only**: clips only, for a character already in Unity.
- Clips that should not loop: add their names to **Play Once** in the `.aeanim` inspector.

## Recomendation

Layers will be exported as individual PNGs. It is recommended to create a Sprite Atlas in Unity to save batches of draw calls.

## Limitations

2D layers only. Masks, mattes, effects and time remapping are ignored.
Hidden layers are skipped unless they parent a visible layer.
