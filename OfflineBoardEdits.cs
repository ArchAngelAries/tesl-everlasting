using System;
using System.Collections.Generic;
using System.IO;
using FullSerializer;
using UnityEngine;

namespace OfflineEngine
{
    // A mod board's edits (boards.json "edits"), applied to the live playmat instance after it is created and undone when
    // another board takes its place on the same instance. Paths are as in the suite's board maps (under the playmat root;
    // a name repeated under one parent matches every copy).
    //   objects:   {"<path>": {hide, pos, rot, scale (local), offset (world), tint [r,g,b,a], textures {"_MainTex": "file.png"}, glow [r,g,b]}}
    //   textures:  {"<original texture name>": "file.png"}  every material on the board that uses it
    //   lights:    {"<path>": {enabled, color, intensity, range}}
    //   particles: {"<path>": {enabled, color, speed, rate, size}}
    //   animators: {"<path>": {enabled, speed}}
    //   lighting:  {brightness, tint}  every board light
    //   add: [{name, model | image | light | particles, texture, tint, glow, width, pos (world), rot, scale, parent, motion {type, speed, amount, axis}}]
    //        model: OBJ/GLB/glTF file; image: a flat PNG (decal, banner); light: {type point|spot, color, intensity, range, angle};
    //        particles: {preset embers|mist|fireflies|sparks|snow|magic|smoke|bubbles, color, rate, size, speed, area};
    //        motion: spin | bob | sway | pulse | float.
    public static class OfflineBoardEdits
    {
        public static int Applied, Added;
        public static readonly List<string> Problems = new List<string>();
        private static readonly List<Action> undo = new List<Action>();
        private static readonly List<GameObject> added = new List<GameObject>();
        private static Texture2D dot;

        private static float Fl(fsData d) { return d.IsInt64 ? d.AsInt64 : d.IsDouble ? (float)d.AsDouble : 0f; }
        private static Vector3 V3(fsData d) { List<fsData> l = d.AsList; return new Vector3(Fl(l[0]), Fl(l[1]), Fl(l[2])); }
        private static Color Col(fsData d) { List<fsData> l = d.AsList; return new Color(Fl(l[0]), Fl(l[1]), Fl(l[2]), l.Count > 3 ? Fl(l[3]) : 1f); }
        private static Dictionary<string, fsData> D(Dictionary<string, fsData> d, string k) { return d != null && d.ContainsKey(k) && d[k].IsDictionary ? d[k].AsDictionary : null; }
        private static bool Has(Dictionary<string, fsData> d, string k) { return d != null && d.ContainsKey(k) && !d[k].IsNull; }

        public static List<Transform> FindAll(Transform root, string path)
        {
            List<Transform> found = new List<Transform>();
            if (string.IsNullOrEmpty(path)) { found.Add(root); return found; }
            Walk(root, path.Split('/'), 0, found);
            return found;
        }

        private static void Walk(Transform t, string[] parts, int i, List<Transform> found)
        {
            if (i == parts.Length) { found.Add(t); return; }
            foreach (Transform c in t) if (c.name == parts[i]) Walk(c, parts, i + 1, found);
        }

        public static void Undo()
        {
            for (int i = undo.Count - 1; i >= 0; i--) { try { undo[i](); } catch (Exception) { } }
            undo.Clear();
            foreach (GameObject g in added) if (g != null) UnityEngine.Object.Destroy(g);
            added.Clear();
        }

        public static int Apply(GameObject instance, OfflineBoards.Board board)
        {
            Undo();
            if (board == null || board.Edits == null) return 0;
            int n = 0;
            Dictionary<string, fsData> e = board.Edits;
            Transform root = instance.transform;
            Dictionary<string, fsData> objects = D(e, "objects");
            if (objects != null)
                foreach (KeyValuePair<string, fsData> kv in objects)
                {
                    List<Transform> ts = FindAll(root, kv.Key);
                    if (ts.Count == 0) { Problem(board, "no object " + kv.Key); continue; }
                    foreach (Transform t in ts) n += EditObject(t, kv.Value.AsDictionary, board);
                }
            Dictionary<string, fsData> texs = D(e, "textures");
            if (texs != null)
                foreach (KeyValuePair<string, fsData> kv in texs)
                {
                    Texture2D tex = LoadTex(board, kv.Value.AsString);
                    if (tex == null) continue;
                    int hits = 0;
                    foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
                    {
                        Material[] mats = r.sharedMaterials; bool changed = false;
                        Material[] mine = null;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            if (mats[i] == null) continue;
                            foreach (string prop in OfflineBoardMap.TexturePropertyNames)
                            {
                                if (!mats[i].HasProperty(prop)) continue;
                                Texture cur = mats[i].GetTexture(prop);
                                if (cur == null || cur.name != kv.Key) continue;
                                if (mine == null) mine = Own(r);
                                mine[i].SetTexture(prop, tex); changed = true; hits++;
                            }
                        }
                        if (changed) n++;
                    }
                    if (hits == 0) Problem(board, "no texture " + kv.Key + " on this board");
                }
            Dictionary<string, fsData> lights = D(e, "lights");
            if (lights != null)
                foreach (KeyValuePair<string, fsData> kv in lights)
                    foreach (Transform t in FindAll(root, kv.Key))
                    {
                        Light l = t.GetComponent<Light>(); if (l == null) continue;
                        n += EditLight(l, kv.Value.AsDictionary);
                    }
            Dictionary<string, fsData> lighting = D(e, "lighting");
            if (lighting != null)
            {
                float bright = Has(lighting, "brightness") ? Fl(lighting["brightness"]) : 1f;
                Color tint = Has(lighting, "tint") ? Col(lighting["tint"]) : Color.white;
                foreach (Light l in instance.GetComponentsInChildren<Light>(true))
                {
                    Light ll = l; float i0 = l.intensity; Color c0 = l.color;
                    l.intensity = i0 * bright; l.color = new Color(c0.r * tint.r, c0.g * tint.g, c0.b * tint.b, c0.a);
                    undo.Add(delegate { if (ll != null) { ll.intensity = i0; ll.color = c0; } });
                    n++;
                }
            }
            Dictionary<string, fsData> parts = D(e, "particles");
            if (parts != null)
                foreach (KeyValuePair<string, fsData> kv in parts)
                    foreach (Transform t in FindAll(root, kv.Key))
                    {
                        ParticleSystem ps = t.GetComponent<ParticleSystem>(); if (ps == null) continue;
                        n += EditParticles(ps, kv.Value.AsDictionary);
                    }
            Dictionary<string, fsData> anims = D(e, "animators");
            if (anims != null)
                foreach (KeyValuePair<string, fsData> kv in anims)
                    foreach (Transform t in FindAll(root, kv.Key))
                    {
                        Animator a = t.GetComponent<Animator>(); if (a == null) continue;
                        Dictionary<string, fsData> d = kv.Value.AsDictionary;
                        Animator aa = a; float s0 = a.speed; bool e0 = a.enabled;
                        if (Has(d, "speed")) a.speed = Fl(d["speed"]);
                        if (Has(d, "enabled")) a.enabled = d["enabled"].AsBool;
                        undo.Add(delegate { if (aa != null) { aa.speed = s0; aa.enabled = e0; } });
                        n++;
                    }
            if (e.ContainsKey("add") && e["add"].IsList)
                foreach (fsData a in e["add"].AsList)
                {
                    try { if (AddObject(root, a.AsDictionary, board)) { n++; Added++; } }
                    catch (Exception ex) { Problem(board, "add failed: " + ex.Message); }
                }
            Applied += n;
            Debug.Log("OFFLINE BOARDS: " + board.Key + " edits applied: " + n + " changes, " + added.Count + " objects added");
            return n;
        }

        private static void Problem(OfflineBoards.Board board, string what)
        {
            string p = board.Key + ": " + what;
            if (!Problems.Contains(p)) { Problems.Add(p); Debug.LogWarning("OFFLINE BOARDS: " + p); }
        }

        private static Texture2D LoadTex(OfflineBoards.Board board, string rel)
        {
            string file = Path.Combine(board.Dir, rel);
            if (!File.Exists(file)) { Problem(board, "missing file " + rel); return null; }
            try { return OfflineModels.Texture(file, false); } catch (Exception ex) { Problem(board, ex.Message); return null; }
        }

        // The renderer's own material copies (the instance's), restored on undo.
        private static Material[] Own(Renderer r)
        {
            Material[] before = r.sharedMaterials;
            Material[] mine = r.materials;
            Renderer rr = r;
            undo.Add(delegate { if (rr != null) rr.sharedMaterials = before; });
            return mine;
        }

        private static int EditObject(Transform t, Dictionary<string, fsData> d, OfflineBoards.Board board)
        {
            Transform tt = t;
            Vector3 p0 = t.localPosition, s0 = t.localScale; Quaternion r0 = t.localRotation; bool a0 = t.gameObject.activeSelf;
            undo.Add(delegate { if (tt != null) { tt.localPosition = p0; tt.localRotation = r0; tt.localScale = s0; tt.gameObject.SetActive(a0); } });
            if (Has(d, "hide")) t.gameObject.SetActive(!d["hide"].AsBool);
            if (Has(d, "pos")) t.localPosition = V3(d["pos"]);
            if (Has(d, "rot")) t.localRotation = Quaternion.Euler(V3(d["rot"]));
            if (Has(d, "scale")) t.localScale = V3(d["scale"]);
            if (Has(d, "offset")) t.position += V3(d["offset"]);
            Renderer r = t.GetComponent<Renderer>();
            Dictionary<string, fsData> texs = D(d, "textures");
            if (r != null && (Has(d, "tint") || Has(d, "glow") || texs != null))
            {
                Material[] mine = Own(r);
                foreach (Material m in mine)
                {
                    if (m == null) continue;
                    if (Has(d, "tint")) foreach (string c in new string[] { "_Color", "_TintColor", "_Tint" }) if (m.HasProperty(c)) m.SetColor(c, Col(d["tint"]));
                    if (Has(d, "glow")) foreach (string c in new string[] { "_EmissionColor", "_EmissiveColor" }) if (m.HasProperty(c)) { m.SetColor(c, Col(d["glow"])); m.EnableKeyword("_EMISSION"); }
                    if (texs != null)
                        foreach (KeyValuePair<string, fsData> kv in texs)
                        {
                            Texture2D tex = LoadTex(board, kv.Value.AsString);
                            if (tex == null) continue;
                            if (m.HasProperty(kv.Key)) m.SetTexture(kv.Key, tex);
                            else if (kv.Key == "color") foreach (string p in OfflineBoardMap.ColorTextureNames) if (m.HasProperty(p) && m.GetTexture(p) != null) m.SetTexture(p, tex);
                        }
                }
            }
            return 1;
        }

        private static int EditLight(Light l, Dictionary<string, fsData> d)
        {
            Light ll = l; Color c0 = l.color; float i0 = l.intensity, rg0 = l.range; bool e0 = l.enabled;
            undo.Add(delegate { if (ll != null) { ll.color = c0; ll.intensity = i0; ll.range = rg0; ll.enabled = e0; } });
            if (Has(d, "enabled")) l.enabled = d["enabled"].AsBool;
            if (Has(d, "color")) l.color = Col(d["color"]);
            if (Has(d, "intensity")) l.intensity = Fl(d["intensity"]);
            if (Has(d, "range")) l.range = Fl(d["range"]);
            return 1;
        }

        private static int EditParticles(ParticleSystem ps, Dictionary<string, fsData> d)
        {
            ParticleSystem.MainModule main = ps.main;
            ParticleSystem.EmissionModule em = ps.emission;
            ParticleSystem pp = ps;
            ParticleSystem.MinMaxGradient col0 = main.startColor; float sp0 = main.simulationSpeed, rate0 = em.rateOverTimeMultiplier, size0 = main.startSizeMultiplier;
            bool on0 = ps.gameObject.activeSelf;
            undo.Add(delegate
            {
                if (pp == null) return;
                ParticleSystem.MainModule m = pp.main; ParticleSystem.EmissionModule e = pp.emission;
                m.startColor = col0; m.simulationSpeed = sp0; e.rateOverTimeMultiplier = rate0; m.startSizeMultiplier = size0; pp.gameObject.SetActive(on0);
            });
            if (Has(d, "enabled")) ps.gameObject.SetActive(d["enabled"].AsBool);
            if (Has(d, "color")) main.startColor = new ParticleSystem.MinMaxGradient(Col(d["color"]));
            if (Has(d, "speed")) main.simulationSpeed = Fl(d["speed"]);
            if (Has(d, "rate")) em.rateOverTimeMultiplier = rate0 * Fl(d["rate"]);
            if (Has(d, "size")) main.startSizeMultiplier = size0 * Fl(d["size"]);
            return 1;
        }

        private static bool AddObject(Transform root, Dictionary<string, fsData> d, OfflineBoards.Board board)
        {
            GameObject go;
            string name = Has(d, "name") ? d["name"].AsString : "mod object";
            Texture2D tex = Has(d, "texture") ? LoadTex(board, d["texture"].AsString) : null;
            if (Has(d, "model"))
            {
                string file = Path.Combine(board.Dir, d["model"].AsString);
                if (!File.Exists(file)) { Problem(board, "missing model " + d["model"].AsString); return false; }
                go = OfflineModels.Load(file, tex);
            }
            else if (Has(d, "image"))
            {
                string file = Path.Combine(board.Dir, d["image"].AsString);
                if (!File.Exists(file)) { Problem(board, "missing image " + d["image"].AsString); return false; }
                go = OfflineModels.ImagePlane(file, Has(d, "width") ? Fl(d["width"]) : 10f);
            }
            else if (D(d, "light") != null) go = NewLight(D(d, "light"));
            else if (D(d, "particles") != null) go = NewParticles(D(d, "particles"));
            else { Problem(board, "add entry " + name + " has no model, image, light or particles"); return false; }
            go.name = "mod:" + name;
            Transform parent = root;
            if (Has(d, "parent")) { List<Transform> ps = FindAll(root, d["parent"].AsString); if (ps.Count > 0) parent = ps[0]; }
            go.transform.SetParent(parent, false);
            if (Has(d, "pos")) go.transform.position = V3(d["pos"]);               // world position (the suite places by clicking the board picture)
            if (Has(d, "rot")) go.transform.rotation = Quaternion.Euler(V3(d["rot"]));
            if (Has(d, "scale")) { fsData s = d["scale"]; go.transform.localScale = s.IsList ? V3(s) : Vector3.one * Fl(s); }
            if (Has(d, "fit"))
            {   // uniform scale so the object's largest side is this many board units; its base sits at pos
                Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
                if (rs.Length > 0)
                {
                    Bounds b = rs[0].bounds;
                    foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                    float widest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                    if (widest > 1e-5f) go.transform.localScale *= Fl(d["fit"]) / widest;
                    b = rs[0].bounds; foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                    if (Has(d, "pos")) go.transform.position += new Vector3(V3(d["pos"]).x - b.center.x, V3(d["pos"]).y - b.min.y, V3(d["pos"]).z - b.center.z);
                }
            }
            if (Has(d, "tint") || Has(d, "glow"))
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.materials)
                    {
                        if (Has(d, "tint") && m.HasProperty("_Color")) m.SetColor("_Color", Col(d["tint"]));
                        if (Has(d, "glow") && m.HasProperty("_EmissionColor")) { m.SetColor("_EmissionColor", Col(d["glow"])); m.EnableKeyword("_EMISSION"); }
                    }
            Dictionary<string, fsData> motion = D(d, "motion");
            if (motion != null)
            {
                OfflineMotion mo = go.AddComponent<OfflineMotion>();
                mo.Kind = Has(motion, "type") ? motion["type"].AsString : "spin";
                mo.Speed = Has(motion, "speed") ? Fl(motion["speed"]) : 1f;
                mo.Amount = Has(motion, "amount") ? Fl(motion["amount"]) : 1f;
                if (Has(motion, "axis")) mo.Axis = V3(motion["axis"]);
            }
            added.Add(go);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null) Debug.Log("OFFLINE BOARDS: added " + go.name + " renderer " + r.GetType().Name + " layer " + go.layer + " bounds " + r.bounds + " material " + m.name
                        + " shader " + m.shader.name + " supported " + m.shader.isSupported + " queue " + m.renderQueue + " keywords " + string.Join(" ", m.shaderKeywords));
            return true;
        }

        private static GameObject NewLight(Dictionary<string, fsData> d)
        {
            GameObject go = new GameObject("light");
            Light l = go.AddComponent<Light>();
            l.type = Has(d, "type") && d["type"].AsString == "spot" ? LightType.Spot : LightType.Point;
            l.color = Has(d, "color") ? Col(d["color"]) : new Color(1f, 0.8f, 0.55f);
            l.intensity = Has(d, "intensity") ? Fl(d["intensity"]) : 1.5f;
            l.range = Has(d, "range") ? Fl(d["range"]) : 15f;
            if (l.type == LightType.Spot) l.spotAngle = Has(d, "angle") ? Fl(d["angle"]) : 45f;
            l.shadows = LightShadows.None;
            return go;
        }

        private static Texture2D Dot()
        {
            if (dot != null) return dot;
            dot = new Texture2D(64, 64, TextureFormat.RGBA32, true);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float r = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 31.5f);
                    float a = r * r;
                    dot.SetPixel(x, y, new Color(a, a, a, a));             // premultiplied
                }
            dot.Apply(true); dot.name = "mod:particle-dot";
            return dot;
        }

        // Particle presets: colour, speed, size, rate and area adjust each.
        private static GameObject NewParticles(Dictionary<string, fsData> d)
        {
            string preset = Has(d, "preset") ? d["preset"].AsString : "embers";
            GameObject go = new GameObject("particles-" + preset);
            go.transform.rotation = Quaternion.Euler(-90, 0, 0);                    // emit "up" from the table
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            ParticleSystem.EmissionModule em = ps.emission;
            ParticleSystem.ShapeModule shape = ps.shape;
            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            ParticleSystem.NoiseModule noise = ps.noise;
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            Color c; float life, speed, size, rate, gravity = 0f, turbulence = 0f;
            switch (preset)
            {
                case "mist": c = new Color(0.8f, 0.85f, 0.9f, 0.25f); life = 9f; speed = 0.3f; size = 6f; rate = 6f; turbulence = 0.3f; break;
                case "fireflies": c = new Color(0.8f, 1f, 0.35f, 1f); life = 6f; speed = 0.2f; size = 0.35f; rate = 4f; turbulence = 1.2f; break;
                case "sparks": c = new Color(1f, 0.75f, 0.3f, 1f); life = 0.8f; speed = 6f; size = 0.15f; rate = 25f; gravity = 1f; break;
                case "snow": c = new Color(1f, 1f, 1f, 0.9f); life = 8f; speed = 0.5f; size = 0.3f; rate = 20f; gravity = 0.05f; turbulence = 0.4f; break;
                case "magic": c = new Color(0.65f, 0.4f, 1f, 1f); life = 2.5f; speed = 1.2f; size = 0.4f; rate = 18f; turbulence = 1f; break;
                case "smoke": c = new Color(0.35f, 0.35f, 0.38f, 0.45f); life = 6f; speed = 0.8f; size = 3f; rate = 5f; turbulence = 0.4f; break;
                case "bubbles": c = new Color(0.6f, 0.9f, 1f, 0.6f); life = 4f; speed = 1f; size = 0.4f; rate = 8f; turbulence = 0.5f; break;
                default: c = new Color(1f, 0.55f, 0.15f, 1f); life = 3f; speed = 1.5f; size = 0.25f; rate = 12f; turbulence = 0.6f; break;   // embers
            }
            if (Has(d, "color")) c = Col(d["color"]);
            float k = Has(d, "speed") ? Fl(d["speed"]) : 1f;
            main.startLifetime = life; main.startSpeed = speed * k; main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size * 1.4f) ;
            if (Has(d, "size")) main.startSizeMultiplier = size * Fl(d["size"]);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a));
            main.gravityModifier = preset == "snow" ? -gravity : gravity;
            main.maxParticles = 600; main.simulationSpace = ParticleSystemSimulationSpace.World; main.loop = true;
            em.rateOverTime = rate * (Has(d, "rate") ? Fl(d["rate"]) : 1f);
            shape.shapeType = ParticleSystemShapeType.Box;
            float area = Has(d, "area") ? Fl(d["area"]) : 3f;
            shape.scale = new Vector3(area, area, preset == "snow" ? 0.1f : 0.5f);
            if (preset == "snow") { go.transform.rotation = Quaternion.Euler(90, 0, 0); }
            if (turbulence > 0f) { noise.enabled = true; noise.strength = turbulence; noise.frequency = 0.4f; }
            col.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(fade);
            ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
            Shader sh = Shader.Find("Particles/Alpha Blended Premultiply") ?? Shader.Find("Particles/Additive");
            Material m = new Material(sh); m.mainTexture = Dot(); m.name = "mod:particles-" + preset;
            pr.sharedMaterial = m;
            ps.Play(true);
            return go;
        }
    }

    // Simple movement for added objects: spin (degrees/s around axis), bob (up and down), sway (rock), pulse (scale), float (bob + slow spin).
    public class OfflineMotion : MonoBehaviour
    {
        public string Kind = "spin"; public float Speed = 1f, Amount = 1f; public Vector3 Axis = Vector3.up;
        private Vector3 p0, s0; private Quaternion r0; private bool started; private float phase;

        private void Start() { p0 = transform.localPosition; r0 = transform.localRotation; s0 = transform.localScale; started = true; phase = UnityEngine.Random.value * 6.28f; }

        private void Update()
        {
            if (!started) return;
            float t = Time.time * Speed + phase;
            switch (Kind)
            {
                case "spin": transform.Rotate(Axis, 45f * Speed * Time.deltaTime, Space.Self); break;
                case "bob": transform.localPosition = p0 + Vector3.up * Mathf.Sin(t) * 0.5f * Amount; break;
                case "sway": transform.localRotation = r0 * Quaternion.AngleAxis(Mathf.Sin(t) * 10f * Amount, Axis == Vector3.up ? Vector3.forward : Axis); break;
                case "pulse": transform.localScale = s0 * (1f + Mathf.Sin(t * 2f) * 0.08f * Amount); break;
                case "float":
                    transform.localPosition = p0 + Vector3.up * Mathf.Sin(t) * 0.35f * Amount;
                    transform.Rotate(Axis, 12f * Speed * Time.deltaTime, Space.Self); break;
            }
        }
    }
}
