using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Diagnostic for the board editor (--tesl-offline-native-menu=boardmap): in a practice match, switches the live
    // PlaymatSelector through all five boards and writes, per board, exports/board-map-<type>.json (every object under
    // the playmat: path, active, local position/rotation/scale; renderers with their materials' shader, textures and
    // colours; lights; particle systems; animators and animations) and a screenshot exports/board-map-<type>.png.
    public static class OfflineBoardMap
    {
        public static int Written;

        public static IEnumerator Diagnose(string exportDirectory)
        {
            PlaymatSelector selector = UnityEngine.Object.FindObjectOfType<PlaymatSelector>();
            if (selector == null) { Debug.LogError("OFFLINE BOARD MAP: no PlaymatSelector"); yield break; }
            foreach (PlaymatType type in new PlaymatType[] { PlaymatType.Original, PlaymatType.IsleOfMadness, PlaymatType.AllianceWar, PlaymatType.Elsweyr, PlaymatType.RealmsOfOblivion })
            {
                PlaymatType current = (PlaymatType)AccessTools.Field(typeof(PlaymatSelector), "currentMatType").GetValue(selector);
                if (current != type)
                {
                    BoardState state = (BoardState)AccessTools.Field(typeof(PlaymatSelector), "_boardStateRef").GetValue(selector);
                    AccessTools.Method(typeof(PlaymatSelector), "ChangePlaymat").Invoke(selector, new object[] { type, state });
                }
                float until = Time.realtimeSinceStartup + 30f;
                GameObject instance = null;
                while (Time.realtimeSinceStartup < until)
                {
                    instance = (GameObject)AccessTools.Field(typeof(PlaymatSelector), "playmatInstance").GetValue(selector);
                    if (instance != null && PlaymatSelector.IsPlaymatReady()) break;
                    yield return null;
                }
                if (instance == null) { Debug.LogWarning("OFFLINE BOARD MAP: " + type + " did not load"); continue; }
                yield return new WaitForSeconds(2.5f);
                File.WriteAllText(Path.Combine(exportDirectory, "board-map-" + type + ".json"), Describe(instance, type));
                OfflineStoryDiagnostic.Capture(Path.Combine(exportDirectory, "board-map-" + type + ".png"));
                Written++;
                Debug.Log("OFFLINE BOARD MAP: " + type + " written");
                yield return new WaitForSeconds(1f);
            }
            Debug.Log("OFFLINE BOARD MAP: done, " + Written + " boards");
        }

        private static string Q(string s)
        {
            if (s == null) return "null";
            StringBuilder b = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c < ' ') b.Append(' ');
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }

        private static string V(Vector3 v) { return "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]"; }
        private static string C(Color c) { return "[" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + "]"; }
        private static string F(float f) { return f.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture); }

        private static string PathOf(Transform t, Transform root)
        {
            string p = t.name;
            for (Transform u = t.parent; u != null && u != root; u = u.parent) p = u.name + "/" + p;
            return t == root ? "" : p;
        }

        // Shader properties worth editing: every texture and colour the material's shader exposes (Unity 2017 has no
        // property enumeration at runtime, so the common names plus the board shaders' own are probed).
        public static string[] TexturePropertyNames { get { return TexProps; } }
        public static readonly string[] ColorTextureNames = { "_MainTex", "_BoardImage", "_Diffuse", "_DiffuseTexture" };
        private static readonly string[] TexProps = { "_MainTex", "_BoardImage", "_Diffuse", "_DiffuseTexture", "_BumpMap", "_Normal", "_NormalMap", "_Specular",
            "_SpecGlossMap", "_MetallicGlossMap", "_EmissionMap", "_Emissive", "_Emission", "_Mask", "_MaskTex", "_DetailAlbedoMap", "_OcclusionMap", "_Gloss", "_Noise", "_Ramp" };
        private static readonly string[] ColorProps = { "_Color", "_TintColor", "_EmissionColor", "_EmissiveColor", "_SpecColor", "_RimColor", "_Tint" };

        public static string Describe(GameObject root, PlaymatType type)
        {
            StringBuilder s = new StringBuilder();
            s.Append("{\"board\":").Append(Q(type.ToString())).Append(",\"root\":").Append(Q(root.name));
            s.Append(",\"ambient\":{\"mode\":").Append(Q(RenderSettings.ambientMode.ToString())).Append(",\"light\":").Append(C(RenderSettings.ambientLight))
             .Append(",\"intensity\":").Append(F(RenderSettings.ambientIntensity)).Append(",\"fog\":").Append(RenderSettings.fog ? "true" : "false")
             .Append(",\"fogColor\":").Append(C(RenderSettings.fogColor)).Append("}");
            Camera cam = Camera.main;
            if (cam != null)
            {   // for the suite's board picture: world -> screen = projection * worldToCamera, then viewport -> pixels
                Matrix4x4 vp = cam.projectionMatrix * cam.worldToCameraMatrix;
                s.Append(",\"camera\":{\"pos\":").Append(V(cam.transform.position)).Append(",\"rot\":").Append(V(cam.transform.eulerAngles))
                 .Append(",\"fov\":").Append(F(cam.fieldOfView)).Append(",\"ortho\":").Append(cam.orthographic ? "true" : "false")
                 .Append(",\"screen\":[").Append(Screen.width).Append(',').Append(Screen.height).Append("],\"viewProjection\":[");
                for (int i = 0; i < 16; i++) { if (i > 0) s.Append(','); s.Append(F(vp[i / 4, i % 4])); }
                s.Append("]}");
            }
            s.Append(",\"rootWorld\":{\"pos\":").Append(V(root.transform.position)).Append(",\"rot\":").Append(V(root.transform.eulerAngles))
             .Append(",\"scale\":").Append(V(root.transform.lossyScale)).Append("},\"nodes\":[");
            bool first = true;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!first) s.Append(','); first = false;
                s.Append("\n{\"path\":").Append(Q(PathOf(t, root.transform))).Append(",\"active\":").Append(t.gameObject.activeSelf ? "true" : "false")
                 .Append(",\"pos\":").Append(V(t.localPosition)).Append(",\"rot\":").Append(V(t.localEulerAngles)).Append(",\"scale\":").Append(V(t.localScale))
                 .Append(",\"world\":").Append(V(t.position));
                Renderer rb = t.GetComponent<Renderer>();
                if (rb != null) s.Append(",\"bounds\":[").Append(V(rb.bounds.center)).Append(',').Append(V(rb.bounds.size)).Append(']');
                Renderer r = t.GetComponent<Renderer>();
                if (r != null)
                {
                    s.Append(",\"renderer\":{\"type\":").Append(Q(r.GetType().Name)).Append(",\"enabled\":").Append(r.enabled ? "true" : "false").Append(",\"materials\":[");
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        Material m = mats[i];
                        if (i > 0) s.Append(',');
                        if (m == null) { s.Append("null"); continue; }
                        s.Append("{\"name\":").Append(Q(m.name)).Append(",\"shader\":").Append(Q(m.shader == null ? null : m.shader.name)).Append(",\"textures\":{");
                        bool f2 = true;
                        foreach (string p in TexProps)
                        {
                            if (!m.HasProperty(p)) continue;
                            Texture tex = m.GetTexture(p);
                            if (tex == null) continue;
                            if (!f2) s.Append(','); f2 = false;
                            s.Append(Q(p)).Append(":{\"name\":").Append(Q(tex.name)).Append(",\"size\":[").Append(tex.width).Append(',').Append(tex.height).Append("]}");
                        }
                        s.Append("},\"colors\":{");
                        f2 = true;
                        foreach (string p in ColorProps)
                        {
                            if (!m.HasProperty(p)) continue;
                            if (!f2) s.Append(','); f2 = false;
                            s.Append(Q(p)).Append(':').Append(C(m.GetColor(p)));
                        }
                        s.Append("}}");
                    }
                    s.Append("]}");
                }
                Light l = t.GetComponent<Light>();
                if (l != null)
                    s.Append(",\"light\":{\"type\":").Append(Q(l.type.ToString())).Append(",\"color\":").Append(C(l.color)).Append(",\"intensity\":").Append(F(l.intensity))
                     .Append(",\"range\":").Append(F(l.range)).Append(",\"spot\":").Append(F(l.spotAngle)).Append(",\"shadows\":").Append(Q(l.shadows.ToString()))
                     .Append(",\"enabled\":").Append(l.enabled ? "true" : "false").Append('}');
                ParticleSystem ps = t.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    ParticleSystem.MainModule main = ps.main;
                    s.Append(",\"particles\":{\"playing\":").Append(ps.isPlaying ? "true" : "false").Append(",\"loop\":").Append(main.loop ? "true" : "false")
                     .Append(",\"speed\":").Append(F(main.simulationSpeed)).Append(",\"color\":").Append(C(main.startColor.color))
                     .Append(",\"size\":").Append(F(main.startSizeMultiplier)).Append(",\"rate\":").Append(F(ps.emission.rateOverTimeMultiplier))
                     .Append(",\"max\":").Append(main.maxParticles).Append('}');
                }
                Animator a = t.GetComponent<Animator>();
                if (a != null)
                    s.Append(",\"animator\":{\"controller\":").Append(Q(a.runtimeAnimatorController == null ? null : a.runtimeAnimatorController.name))
                     .Append(",\"speed\":").Append(F(a.speed)).Append(",\"enabled\":").Append(a.enabled ? "true" : "false").Append('}');
                Animation an = t.GetComponent<Animation>();
                if (an != null)
                {
                    s.Append(",\"animation\":{\"clips\":[");
                    bool f3 = true;
                    foreach (AnimationState st in an) { if (!f3) s.Append(','); f3 = false; s.Append(Q(st.name)); }
                    s.Append("]}");
                }
                Collider col = t.GetComponent<Collider>();
                if (col != null) s.Append(",\"clickable\":true");
                List<string> others = new List<string>();
                foreach (Component c in t.GetComponents<Component>())
                {
                    if (c == null) continue;
                    string n = c.GetType().Name;
                    if (n != "Transform" && n != "MeshFilter" && !(c is Renderer) && !(c is Light) && !(c is ParticleSystem) && !(c is Animator) && !(c is Animation) && !(c is Collider)) others.Add(n);
                }
                if (others.Count > 0) { s.Append(",\"components\":["); for (int i = 0; i < others.Count; i++) { if (i > 0) s.Append(','); s.Append(Q(others[i])); } s.Append(']'); }
                s.Append('}');
            }
            s.Append("\n],\"sceneLights\":[");
            bool fl = true;
            foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (l.transform.IsChildOf(root.transform)) continue;
                if (!fl) s.Append(','); fl = false;
                s.Append("{\"path\":").Append(Q(PathOf(l.transform, null))).Append(",\"type\":").Append(Q(l.type.ToString())).Append(",\"color\":").Append(C(l.color))
                 .Append(",\"intensity\":").Append(F(l.intensity)).Append(",\"rot\":").Append(V(l.transform.eulerAngles)).Append('}');
            }
            s.Append("]}");
            return s.ToString();
        }
    }
}
