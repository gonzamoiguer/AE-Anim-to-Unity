// AE -> Unity exporter, as a dockable panel.
// Install: copy to <After Effects folder>/Scripts/ScriptUI Panels/, restart AE,
//          then Window > ExportForUnity.jsx and dock it anywhere.
//          (Running it from File > Scripts also works: it opens as a floating window.)
// Needs: Preferences > Scripting & Expressions > "Allow Scripts to Write Files and Access Network".
//
// Select one or more compositions in the Project panel (one per animation: idle, walk...), then:
//   Export character       -> <Name>.aeanim (rig + clips) + <Name>_aeparts/*.png
//   Export animation only  -> <Name>.aeanim with just clips, for a character already in Unity
//
// Rules: comps should be duplicates of the same rig (same layer names, parenting, comp size).
//        Layer names must be unique inside a comp.
//        Hidden layers are skipped, unless they parent a visible layer (exported without image).
(function (thisObj) {
    var SCALE = 2; // PNG resolution multiplier (vector art is re-rasterized at this size)

    var ui = thisObj instanceof Panel ? thisObj : new Window("palette", "Export for Unity", undefined, { resizeable: true });
    ui.orientation = "column";
    ui.alignChildren = ["fill", "top"];
    ui.add("button", undefined, "Export character").onClick = function () { run(false); };
    ui.add("button", undefined, "Export animation only").onClick = function () { run(true); };
    ui.layout.layout(true);
    if (ui instanceof Window) ui.show();

    function run(animOnly) {
        try { exportComps(animOnly); }
        catch (e) { alert("Export failed: " + e.toString() + " (line " + e.line + ")"); }
    }

    function exportComps(animOnly) {
        var comps = [], sel = app.project.selection;
        for (var i = 0; i < sel.length; i++) if (sel[i] instanceof CompItem) comps.push(sel[i]);
        if (!comps.length && app.project.activeItem instanceof CompItem) comps.push(app.project.activeItem);
        if (!comps.length) { alert("Select one or more compositions in the Project panel."); return; }

        var name = prompt(animOnly ? "Animation file name" : "Character name", comps[0].name);
        if (!name) return;
        name = clean(name);

        var last = app.settings.haveSetting("ExportForUnity", "folder") ? app.settings.getSetting("ExportForUnity", "folder") : "~";
        var outDir = new Folder(last).selectDlg("Export folder (inside your Unity Assets)");
        if (!outDir) return;
        app.settings.saveSetting("ExportForUnity", "folder", outDir.fsName);

        var partsDir = new Folder(outDir.fsName + "/" + name + "_aeparts");
        if (!animOnly) partsDir.create();

        // Pass 1: keep layers visible in any comp, plus their parents (hidden parents become transform-only).
        var keep = {}, visible = {};
        for (var c = 0; c < comps.length; c++) {
            for (var j = 1; j <= comps[c].numLayers; j++) {
                var L = comps[c].layer(j);
                if (!usable(L) || !L.enabled) continue;
                visible[L.name] = true;
                for (var p = L; p; p = p.parent) keep[p.name] = true;
            }
        }

        // Pass 2: build rig and sample clips.
        var rig = [], inRig = {}, clips = [], images = {}, dupes = [];

        for (var c = 0; c < comps.length; c++) {
            var comp = comps[c], fps = comp.frameRate;
            var frames = Math.round(comp.workAreaDuration * fps);
            var tracks = [], seen = {};

            for (var j = 1; j <= comp.numLayers; j++) {
                var L = comp.layer(j);
                if (!usable(L) || !keep[L.name]) continue;
                if (seen[L.name]) { dupes.push(comp.name + " / " + L.name); continue; }
                seen[L.name] = true;

                // Rig = union of layers across comps, first appearance wins.
                if (!inRig[L.name]) {
                    inRig[L.name] = true;
                    rig.push({ name: L.name, parent: L.parent ? L.parent.name : "", image: visible[L.name] ? imageFor(L) : "", w: L.width, h: L.height, order: j });
                }
                tracks.push({ layer: L.name, keys: sample(L, comp.workAreaStart, fps, frames) });
            }
            clips.push({ name: clean(comp.name), fps: fps, frames: frames, tracks: tracks });
        }

        var out = new File(outDir.fsName + "/" + name + ".aeanim");
        out.encoding = "UTF-8";
        out.open("w");
        out.write(toJSON({ name: name, animOnly: animOnly, width: comps[0].width, height: comps[0].height, layers: rig, clips: clips }));
        out.close();

        alert("Exported " + clips.length + " clip(s), " + rig.length + " layers." +
            (dupes.length ? "\n\nSkipped duplicate layer names:\n" + dupes.join("\n") : ""));

        function imageFor(L) {
            if (!L.source || L.nullLayer || !L.source.hasVideo) return "";
            if (!images[L.source.id]) {
                images[L.source.id] = clean(L.source.name) + "_" + L.source.id + ".png";
                if (!animOnly) renderSource(L.source, new File(partsDir.fsName + "/" + images[L.source.id]));
            }
            return images[L.source.id];
        }
    }

    // per frame: posX posY anchorX anchorY rotation scaleX scaleY opacity visible
    function sample(L, start, fps, frames) {
        var tr = L.transform, keys = [];
        for (var f = 0; f < frames; f++) {
            var t = start + f / fps;
            var p = tr.position.dimensionsSeparated
                ? [tr.property("ADBE Position_0").valueAtTime(t, false), tr.property("ADBE Position_1").valueAtTime(t, false)]
                : tr.position.valueAtTime(t, false);
            var a = tr.anchorPoint.valueAtTime(t, false);
            var s = tr.scale.valueAtTime(t, false);
            var vis = (L.enabled && t >= L.inPoint && t < L.outPoint) ? 1 : 0;
            keys.push(p[0], p[1], a[0], a[1], tr.rotation.valueAtTime(t, false), s[0], s[1], tr.opacity.valueAtTime(t, false), vis);
        }
        return keys;
    }

    function renderSource(src, file) {
        var w = Math.max(4, Math.ceil(src.width * SCALE)), h = Math.max(4, Math.ceil(src.height * SCALE));
        var tmp = app.project.items.addComp("__unity_tmp", w, h, 1, 1, 24);
        var l = tmp.layers.add(src);
        l.transform.scale.setValue([100 * SCALE, 100 * SCALE]);
        l.transform.position.setValue([w / 2, h / 2]);
        if (l.canSetCollapseTransformation) l.collapseTransformation = true; // continuous rasterization for vectors
        tmp.saveFrameToPng(0, file);
        for (var n = 0; n < 50 && !file.exists; n++) $.sleep(100); // saveFrameToPng can return before the file exists
        tmp.remove();
    }

    function usable(L) { return L instanceof AVLayer && !L.threeDLayer && !L.guideLayer; }

    function clean(s) { return s.replace(/[^A-Za-z0-9_\-]/g, "_"); }

    function toJSON(v) {
        var i, parts = [];
        if (v instanceof Array) {
            for (i = 0; i < v.length; i++) parts.push(toJSON(v[i]));
            return "[" + parts.join(",") + "]";
        }
        if (typeof v === "object") {
            for (i in v) parts.push('"' + i + '":' + toJSON(v[i]));
            return "{" + parts.join(",") + "}";
        }
        if (typeof v === "string") return '"' + v.replace(/\\/g, "\\\\").replace(/"/g, '\\"') + '"';
        if (typeof v === "boolean") return v ? "true" : "false";
        return String(Math.round(v * 1000) / 1000);
    }
})(this);
