// Put in an Editor folder. Imports <Name>.aeanim (+ <Name>_aeparts/*.png)
// Full export:      prefab (main asset, with an Animator) + one AnimationClip per comp.
// Animation only:   just the clips; they work on any character from the same AE rig,
//                   because clips target body parts by layer name.
// Re-export from AE -> automatic reimport.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

[ScriptedImporter(6, "aeanim")]
public class AEAnimImporter : ScriptedImporter
{
    public float pixelsPerUnit = 100;
    [Tooltip("Clip names that play once. All other clips loop.")]
    public string[] playOnce = new string[0];

    [System.Serializable] class Data { public string name; public bool animOnly; public float width, height; public Layer[] layers; public Clip[] clips; }
    [System.Serializable] class Layer { public string name, parent, image; public float w, h; public int order; }
    [System.Serializable] class Clip { public string name; public float fps; public int frames; public Track[] tracks; }
    [System.Serializable] class Track { public string layer; public float[] keys; }

    // per frame: posX posY anchorX anchorY rotation scaleX scaleY opacity visible
    const int K = 9;

    static string PartsDir(string path) =>
        Path.GetDirectoryName(path).Replace('\\', '/') + "/" + Path.GetFileNameWithoutExtension(path) + "_aeparts/";

    // Makes sure the PNGs are imported as sprites before this asset.
    static string[] GatherDependenciesFromSourceFile(string path)
    {
        var data = JsonUtility.FromJson<Data>(File.ReadAllText(path));
        var deps = new List<string>();
        if (!data.animOnly)
            foreach (var l in data.layers)
                if (!string.IsNullOrEmpty(l.image)) deps.Add(PartsDir(path) + l.image);
        return deps.ToArray();
    }

    public override void OnImportAsset(AssetImportContext ctx)
    {
        var data = JsonUtility.FromJson<Data>(File.ReadAllText(ctx.assetPath));
        float ppu = pixelsPerUnit;

        // --- rig (also needed in animation-only mode, to compute the clip paths) ---
        var root = new GameObject(data.name);
        root.AddComponent<Animator>();
        var layers = new Dictionary<string, Layer>();
        var nodes = new Dictionary<string, Transform>(); // node origin = AE anchor point

        foreach (var l in data.layers)
        {
            var node = new GameObject(l.name.Replace('/', '_')).transform;
            layers[l.name] = l;
            nodes[l.name] = node;

            if (data.animOnly || string.IsNullOrEmpty(l.image)) continue;
            string pngPath = PartsDir(ctx.assetPath) + l.image;
            ctx.DependsOnArtifact(pngPath);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
            if (sprite == null) { ctx.LogImportWarning("Missing sprite: " + pngPath); continue; }

            var sr = new GameObject("_sprite").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(node, false);
            sr.sprite = sprite;
            sr.sortingOrder = -l.order; // AE layer 1 is on top
            // Sprite pivot is top-left (see postprocessor): just stretch it to the layer size.
            sr.transform.localScale = new Vector3(l.w / ppu / sprite.bounds.size.x, l.h / ppu / sprite.bounds.size.y, 1);
        }

        foreach (var l in data.layers)
            nodes[l.name].SetParent(nodes.TryGetValue(l.parent, out var p) ? p : root.transform, false);

        // --- clips ---
        var T = typeof(Transform);
        var R = typeof(SpriteRenderer);
        AnimationClip first = null;

        foreach (var c in data.clips)
        {
            var clip = new AnimationClip { name = c.name, frameRate = c.fps };
            var tracks = new Dictionary<string, Track>();
            foreach (var t in c.tracks) tracks[t.layer] = t;

            foreach (var t in c.tracks)
            {
                if (!nodes.TryGetValue(t.layer, out var node)) { ctx.LogImportWarning($"{c.name}: unknown layer {t.layer}"); continue; }
                bool top = node.parent == root.transform;
                tracks.TryGetValue(layers[t.layer].parent, out var pt);

                int n = c.frames;
                float[] px = new float[n], py = new float[n], ax = new float[n], ay = new float[n],
                        rot = new float[n], sx = new float[n], sy = new float[n], op = new float[n], vis = new float[n];

                for (int f = 0; f < n; f++)
                {
                    int i = f * K;
                    // AE positions are measured from the parent's top-left corner (or the comp's);
                    // Unity's from the parent's origin, which is the parent's anchor (or the comp center).
                    float pax = top ? data.width / 2 : pt != null ? pt.keys[i + 2] : 0;
                    float pay = top ? data.height / 2 : pt != null ? pt.keys[i + 3] : 0;
                    px[f] =  (t.keys[i]     - pax) / ppu;
                    py[f] = -(t.keys[i + 1] - pay) / ppu; // AE is y-down
                    ax[f] = -t.keys[i + 2] / ppu;         // art shifted by -anchor
                    ay[f] =  t.keys[i + 3] / ppu;
                    rot[f] = -t.keys[i + 4];              // AE rotates clockwise
                    sx[f] = t.keys[i + 5] / 100;
                    sy[f] = t.keys[i + 6] / 100;
                    op[f] = t.keys[i + 7] / 100;
                    vis[f] = t.keys[i + 8];
                }

                string path = AnimationUtility.CalculateTransformPath(node, root.transform);
                Add(clip, path, T, "m_LocalPosition.x", px, c.fps);
                Add(clip, path, T, "m_LocalPosition.y", py, c.fps);
                Add(clip, path, T, "localEulerAnglesRaw.x", new float[1], c.fps);
                Add(clip, path, T, "localEulerAnglesRaw.y", new float[1], c.fps);
                Add(clip, path, T, "localEulerAnglesRaw.z", rot, c.fps);
                Add(clip, path, T, "m_LocalScale.x", sx, c.fps);
                Add(clip, path, T, "m_LocalScale.y", sy, c.fps);

                if (string.IsNullOrEmpty(layers[t.layer].image)) continue;
                string sp = path + "/_sprite";
                Add(clip, sp, T, "m_LocalPosition.x", ax, c.fps);
                Add(clip, sp, T, "m_LocalPosition.y", ay, c.fps);
                Add(clip, sp, R, "m_Color.a", op, c.fps);
                Add(clip, sp, R, "m_Enabled", vis, c.fps, step: true);
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = System.Array.IndexOf(playOnce, c.name) < 0;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            ctx.AddObjectToAsset("clip_" + c.name, clip);
            if (first == null) first = clip;
        }

        if (data.animOnly)
        {
            DestroyImmediate(root);
            if (first != null) ctx.SetMainObject(first);
            return;
        }

        if (first != null) first.SampleAnimation(root, 0); // initial pose
        ctx.AddObjectToAsset("root", root);
        ctx.SetMainObject(root);
    }

    // Constant values become a single key; otherwise one key per frame with linear tangents.
    static void Add(AnimationClip clip, string path, System.Type type, string prop, float[] v, float fps, bool step = false)
    {
        bool constant = true;
        for (int i = 1; i < v.Length && constant; i++) constant = Mathf.Approximately(v[i], v[0]);

        var keys = new Keyframe[constant ? 1 : v.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            float inT  = i > 0 ? (v[i] - v[i - 1]) * fps : 0;
            float outT = i < keys.Length - 1 ? (v[i + 1] - v[i]) * fps : 0;
            if (step) inT = outT = float.PositiveInfinity;
            keys[i] = new Keyframe(i / fps, v[i], inT, outT);
        }
        clip.SetCurve(path, type, prop, new AnimationCurve(keys));
    }
}

// Part PNGs are imported as sprites with a top-left pivot: the importer relies on that pivot.
class AEPartsPostprocessor : AssetPostprocessor
{
    public override uint GetVersion() => 1;

    void OnPreprocessTexture()
    {
        if (!assetPath.Contains("_aeparts/")) return;
        var ti = (TextureImporter)assetImporter;
        if (ti.importSettingsMissing) ti.alphaIsTransparency = true;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;

        var st = new TextureImporterSettings();
        ti.ReadTextureSettings(st);
        st.spriteAlignment = (int)SpriteAlignment.TopLeft;
        ti.SetTextureSettings(st);
    }
}

// Tools menu: opens the folder with the After Effects panel script, ready to copy into AE.
static class AEAnimMenu
{
    [MenuItem("Tools/After Effects to Unity/Show After Effects Script")]
    static void RevealJsx()
    {
        var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AEAnimMenu).Assembly);
        if (pkg == null) { Debug.LogWarning("Install this as a package to use this menu."); return; }
        EditorUtility.RevealInFinder(Path.Combine(pkg.resolvedPath, "AfterEffects~", "ExportForUnity.jsx"));
    }
}
