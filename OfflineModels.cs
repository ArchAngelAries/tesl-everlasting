using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FullSerializer;
using UnityEngine;

namespace OfflineEngine
{
    // Static 3D models from mods, loaded at runtime (no Unity editor needed): Wavefront OBJ (+ MTL) and glTF 2.0 binary
    // (.glb, Blender's default export; .gltf with external or embedded buffers too). Meshes, node transforms, base colour,
    // normal and emission textures and factors. Both formats are right-handed, Unity is left-handed: X is mirrored and
    // triangle winding reversed. Skinning and animation clips are not read (OfflineMotion gives simple movement).
    public static class OfflineModels
    {
        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static Shader lit;

        public static Shader Lit()
        {
            if (lit == null) lit = Shader.Find("Standard (Specular setup)") ?? Shader.Find("Standard");
            return lit;
        }

        public static Texture2D Texture(string file, bool linear)
        {
            Texture2D t;
            string key = file + (linear ? "|linear" : "");
            if (textures.TryGetValue(key, out t) && t != null) return t;
            t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            if (!ImageConversion.LoadImage(t, File.ReadAllBytes(file), false)) throw new InvalidOperationException("unreadable image " + file);
            t.name = "mod:" + Path.GetFileNameWithoutExtension(file);
            t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 4;
            textures[key] = t;
            return t;
        }

        public static Texture2D TextureFromBytes(byte[] data, string name)
        {
            Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!ImageConversion.LoadImage(t, data, false)) return null;
            t.name = "mod:" + name; t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 4;
            return t;
        }

        // A lit material: colour map, tint, normal map, glow map/colour, smoothness. transparent = alpha-faded (images, glass).
        public static Material NewMaterial(string name, Texture2D color, Color tint, Texture2D normal, Texture2D glow, Color glowColor, bool transparent)
        { return NewMaterial(name, color, tint, normal, glow, glowColor, transparent ? 2 : 0); }

        // mode 0 opaque, 1 cutout (hard alpha edge, writes depth: decals on the table), 2 fade (soft alpha: glass).
        public static Material NewMaterial(string name, Texture2D color, Color tint, Texture2D normal, Texture2D glow, Color glowColor, int mode)
        {
            bool transparent = mode == 2;
            Material m = new Material(Lit());
            m.name = "mod:" + name;
            if (color != null) m.SetTexture("_MainTex", color);
            m.SetColor("_Color", tint);
            m.SetColor("_SpecColor", new Color(0.12f, 0.12f, 0.12f, 1f));
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.3f);
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            if (glow != null || glowColor.maxColorComponent > 0.001f)
            {
                if (glow != null) m.SetTexture("_EmissionMap", glow);
                m.SetColor("_EmissionColor", glow != null && glowColor.maxColorComponent < 0.001f ? Color.white : glowColor);
                m.EnableKeyword("_EMISSION");
            }
            if (transparent)
            {   // Standard shader "Fade" mode
                m.SetFloat("_Mode", 2f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON"); m.EnableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = 3000;
            }
            else if (mode == 1)
            {   // Standard shader "Cutout"
                m.SetFloat("_Mode", 1f); m.SetFloat("_Cutoff", 0.5f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero); m.SetInt("_ZWrite", 1);
                m.EnableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = 2450;
            }
            return m;
        }

        public static GameObject Load(string file, Texture2D overrideColor)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            GameObject go = ext == ".obj" ? LoadObj(file, overrideColor) : ext == ".glb" || ext == ".gltf" ? LoadGltf(file, overrideColor) : null;
            if (go == null) throw new InvalidOperationException("unsupported model format " + ext + " (OBJ, GLB or glTF)");
            return go;
        }

        // ---- OBJ ----------------------------------------------------------------------------------------------------
        private sealed class ObjMaterial { public string Name; public Color Kd = Color.white; public Color Ke = Color.black; public string MapKd, MapBump, MapKe; public float D = 1f; }

        private static float P(string s) { return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture); }

        private static Dictionary<string, ObjMaterial> ReadMtl(string file)
        {
            Dictionary<string, ObjMaterial> mats = new Dictionary<string, ObjMaterial>();
            if (!File.Exists(file)) return mats;
            ObjMaterial cur = null;
            string dir = Path.GetDirectoryName(file);
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                string k = p[0].ToLowerInvariant();
                if (k == "newmtl") { cur = new ObjMaterial { Name = line.Substring(6).Trim() }; mats[cur.Name] = cur; continue; }
                if (cur == null) continue;
                string rest = p.Length > 1 ? Path.Combine(dir, p[p.Length - 1]) : null;
                if (k == "kd" && p.Length >= 4) cur.Kd = new Color(P(p[1]), P(p[2]), P(p[3]), 1f);
                else if (k == "ke" && p.Length >= 4) cur.Ke = new Color(P(p[1]), P(p[2]), P(p[3]), 1f);
                else if (k == "d" && p.Length >= 2) cur.D = P(p[1]);
                else if (k == "map_kd") cur.MapKd = rest;
                else if (k == "map_bump" || k == "bump" || k == "norm") cur.MapBump = rest;
                else if (k == "map_ke") cur.MapKe = rest;
            }
            return mats;
        }

        public static GameObject LoadObj(string file, Texture2D overrideColor)
        {
            List<Vector3> v = new List<Vector3>(), vn = new List<Vector3>();
            List<Vector2> vt = new List<Vector2>();
            Dictionary<string, ObjMaterial> mtl = new Dictionary<string, ObjMaterial>();
            List<string> groupMats = new List<string>();
            List<List<int[]>> groups = new List<List<int[]>>();      // per material: corner triples (v, vt, vn) in triangles
            int current = -1;
            string dir = Path.GetDirectoryName(file);
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length < 2 || line[0] == '#') continue;
                string[] p = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (p[0])
                {
                    case "v": v.Add(new Vector3(-P(p[1]), P(p[2]), P(p[3]))); break;
                    case "vt": vt.Add(new Vector2(P(p[1]), p.Length > 2 ? P(p[2]) : 0f)); break;
                    case "vn": vn.Add(new Vector3(-P(p[1]), P(p[2]), P(p[3]))); break;
                    case "mtllib": foreach (KeyValuePair<string, ObjMaterial> kv in ReadMtl(Path.Combine(dir, line.Substring(7).Trim()))) mtl[kv.Key] = kv.Value; break;
                    case "usemtl":
                        {
                            string name = line.Substring(6).Trim();
                            current = groupMats.IndexOf(name);
                            if (current < 0) { groupMats.Add(name); groups.Add(new List<int[]>()); current = groups.Count - 1; }
                            break;
                        }
                    case "f":
                        {
                            if (current < 0) { groupMats.Add(""); groups.Add(new List<int[]>()); current = 0; }
                            List<int[]> corners = new List<int[]>();
                            for (int i = 1; i < p.Length; i++)
                            {
                                string[] q = p[i].Split('/');
                                int a = int.Parse(q[0]); int b = q.Length > 1 && q[1].Length > 0 ? int.Parse(q[1]) : 0; int c = q.Length > 2 && q[2].Length > 0 ? int.Parse(q[2]) : 0;
                                corners.Add(new int[] { a < 0 ? v.Count + a : a - 1, b < 0 ? vt.Count + b : b - 1, c < 0 ? vn.Count + c : c - 1 });
                            }
                            for (int i = 1; i + 1 < corners.Count; i++)          // fan; winding reversed for the X mirror
                            { groups[current].Add(corners[0]); groups[current].Add(corners[i + 1]); groups[current].Add(corners[i]); }
                            break;
                        }
                }
            }
            List<Vector3> pos = new List<Vector3>(), nor = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            Dictionary<string, int> unique = new Dictionary<string, int>();
            List<int[]> tris = new List<int[]>();
            foreach (List<int[]> g in groups)
            {
                List<int> idx = new List<int>();
                foreach (int[] c in g)
                {
                    string key = c[0] + "/" + c[1] + "/" + c[2];
                    int at;
                    if (!unique.TryGetValue(key, out at))
                    {
                        at = pos.Count; unique[key] = at;
                        pos.Add(v[c[0]]);
                        uv.Add(c[1] >= 0 && c[1] < vt.Count ? vt[c[1]] : Vector2.zero);
                        nor.Add(c[2] >= 0 && c[2] < vn.Count ? vn[c[2]] : Vector3.zero);
                    }
                    idx.Add(at);
                }
                tris.Add(idx.ToArray());
            }
            Mesh mesh = new Mesh { name = Path.GetFileNameWithoutExtension(file) };
            if (pos.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(pos); mesh.SetUVs(0, uv);
            mesh.subMeshCount = tris.Count;
            for (int i = 0; i < tris.Count; i++) mesh.SetTriangles(tris[i], i);
            if (vn.Count > 0) mesh.SetNormals(nor); else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (vt.Count > 0) mesh.RecalculateTangents();
            Material[] mats = new Material[tris.Count];
            for (int i = 0; i < tris.Count; i++)
            {
                ObjMaterial m;
                mtl.TryGetValue(groupMats[i], out m);
                Texture2D color = overrideColor ?? (m != null && m.MapKd != null && File.Exists(m.MapKd) ? Texture(m.MapKd, false) : null);
                Texture2D bump = m != null && m.MapBump != null && File.Exists(m.MapBump) ? Texture(m.MapBump, true) : null;
                Texture2D glow = m != null && m.MapKe != null && File.Exists(m.MapKe) ? Texture(m.MapKe, false) : null;
                Color kd = m == null ? Color.white : new Color(m.Kd.r, m.Kd.g, m.Kd.b, m.D);
                if (color != null && m != null && m.Kd.maxColorComponent < 0.01f) kd = new Color(1, 1, 1, m.D);
                mats[i] = NewMaterial(groupMats[i], color, kd, bump, glow, m == null ? Color.black : m.Ke, m != null && m.D < 0.99f);
            }
            GameObject go = new GameObject(Path.GetFileNameWithoutExtension(file));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }

        // ---- glTF 2.0 -----------------------------------------------------------------------------------------------
        private sealed class Gltf
        {
            public Dictionary<string, fsData> Root; public List<byte[]> Buffers = new List<byte[]>(); public string Dir;
            public Dictionary<int, Texture2D> Images = new Dictionary<int, Texture2D>(); public Dictionary<int, Material> Materials = new Dictionary<int, Material>();
            public Texture2D Override;
            public List<fsData> L(string k) { return Root.ContainsKey(k) && Root[k].IsList ? Root[k].AsList : new List<fsData>(); }
        }

        private static int I(Dictionary<string, fsData> d, string k, int def) { return d.ContainsKey(k) ? (int)(d[k].IsInt64 ? d[k].AsInt64 : (long)d[k].AsDouble) : def; }
        private static float Fl(fsData d) { return d.IsInt64 ? d.AsInt64 : (float)d.AsDouble; }

        public static GameObject LoadGltf(string file, Texture2D overrideColor)
        {
            Gltf g = new Gltf { Dir = Path.GetDirectoryName(file), Override = overrideColor };
            byte[] bytes = File.ReadAllBytes(file);
            string json;
            byte[] bin = null;
            if (bytes.Length > 12 && bytes[0] == 'g' && bytes[1] == 'l' && bytes[2] == 'T' && bytes[3] == 'F')
            {
                int pos = 12; json = null;
                while (pos + 8 <= bytes.Length)
                {
                    int len = BitConverter.ToInt32(bytes, pos); int type = BitConverter.ToInt32(bytes, pos + 4);
                    if (type == 0x4E4F534A) json = System.Text.Encoding.UTF8.GetString(bytes, pos + 8, len);
                    else if (type == 0x004E4942) { bin = new byte[len]; Buffer.BlockCopy(bytes, pos + 8, bin, 0, len); }
                    pos += 8 + len;
                }
                if (json == null) throw new InvalidOperationException("GLB without JSON chunk");
            }
            else json = System.Text.Encoding.UTF8.GetString(bytes);
            g.Root = fsJsonParser.Parse(json).AsDictionary;
            foreach (fsData b in g.L("buffers"))
            {
                Dictionary<string, fsData> bd = b.AsDictionary;
                if (!bd.ContainsKey("uri")) { g.Buffers.Add(bin); continue; }
                g.Buffers.Add(Uri(g, bd["uri"].AsString));
            }
            GameObject root = new GameObject(Path.GetFileNameWithoutExtension(file));
            List<fsData> scenes = g.L("scenes");
            List<fsData> top = new List<fsData>();
            if (scenes.Count > 0)
            {
                int s = I(g.Root, "scene", 0);
                Dictionary<string, fsData> scene = scenes[Math.Min(s, scenes.Count - 1)].AsDictionary;
                if (scene.ContainsKey("nodes")) top = scene["nodes"].AsList;
            }
            else for (int i = 0; i < g.L("nodes").Count; i++) top.Add(new fsData((long)i));
            foreach (fsData n in top) Node(g, (int)n.AsInt64, root.transform);
            return root;
        }

        private static byte[] Uri(Gltf g, string uri)
        {
            if (uri.StartsWith("data:", StringComparison.Ordinal)) return Convert.FromBase64String(uri.Substring(uri.IndexOf(',') + 1));
            return File.ReadAllBytes(Path.Combine(g.Dir, System.Uri.UnescapeDataString(uri)));
        }

        private static void Node(Gltf g, int index, Transform parent)
        {
            Dictionary<string, fsData> n = g.L("nodes")[index].AsDictionary;
            GameObject go = new GameObject(n.ContainsKey("name") ? n["name"].AsString : "node" + index);
            go.transform.SetParent(parent, false);
            if (n.ContainsKey("matrix"))
            {
                List<fsData> m = n["matrix"].AsList;   // column-major
                Matrix4x4 mx = new Matrix4x4();
                for (int i = 0; i < 16; i++) mx[i % 4, i / 4] = Fl(m[i]);
                Vector3 t = mx.GetColumn(3);
                Vector3 sc = new Vector3(mx.GetColumn(0).magnitude, mx.GetColumn(1).magnitude, mx.GetColumn(2).magnitude);
                Quaternion q = Quaternion.LookRotation(mx.GetColumn(2), mx.GetColumn(1));
                go.transform.localPosition = new Vector3(-t.x, t.y, t.z);
                go.transform.localRotation = new Quaternion(q.x, -q.y, -q.z, q.w);
                go.transform.localScale = sc;
            }
            else
            {
                if (n.ContainsKey("translation")) { List<fsData> t = n["translation"].AsList; go.transform.localPosition = new Vector3(-Fl(t[0]), Fl(t[1]), Fl(t[2])); }
                if (n.ContainsKey("rotation")) { List<fsData> r = n["rotation"].AsList; go.transform.localRotation = new Quaternion(Fl(r[0]), -Fl(r[1]), -Fl(r[2]), Fl(r[3])); }
                if (n.ContainsKey("scale")) { List<fsData> s = n["scale"].AsList; go.transform.localScale = new Vector3(Fl(s[0]), Fl(s[1]), Fl(s[2])); }
            }
            if (n.ContainsKey("mesh")) MeshInto(g, (int)n["mesh"].AsInt64, go);
            if (n.ContainsKey("children")) foreach (fsData c in n["children"].AsList) Node(g, (int)c.AsInt64, go.transform);
        }

        // An accessor as floats (normalized integer types scaled), with its component count.
        private static float[] Accessor(Gltf g, int index, out int comps)
        {
            Dictionary<string, fsData> a = g.L("accessors")[index].AsDictionary;
            string type = a["type"].AsString;
            comps = type == "SCALAR" ? 1 : type == "VEC2" ? 2 : type == "VEC3" ? 3 : type == "VEC4" ? 4 : type == "MAT4" ? 16 : 1;
            int count = I(a, "count", 0), ct = I(a, "componentType", 5126);
            bool normalized = a.ContainsKey("normalized") && a["normalized"].AsBool;
            float[] outv = new float[count * comps];
            if (!a.ContainsKey("bufferView")) return outv;
            Dictionary<string, fsData> bv = g.L("bufferViews")[I(a, "bufferView", 0)].AsDictionary;
            byte[] buf = g.Buffers[I(bv, "buffer", 0)];
            int size = ct == 5126 || ct == 5125 ? 4 : ct == 5123 || ct == 5122 ? 2 : 1;
            int stride = I(bv, "byteStride", 0); if (stride == 0) stride = size * comps;
            int start = I(bv, "byteOffset", 0) + I(a, "byteOffset", 0);
            for (int i = 0; i < count; i++)
                for (int c = 0; c < comps; c++)
                {
                    int at = start + i * stride + c * size;
                    float f;
                    switch (ct)
                    {
                        case 5126: f = BitConverter.ToSingle(buf, at); break;
                        case 5125: f = BitConverter.ToUInt32(buf, at); break;
                        case 5123: f = BitConverter.ToUInt16(buf, at); if (normalized) f /= 65535f; break;
                        case 5122: f = BitConverter.ToInt16(buf, at); if (normalized) f = Math.Max(f / 32767f, -1f); break;
                        case 5121: f = buf[at]; if (normalized) f /= 255f; break;
                        default: f = (sbyte)buf[at]; if (normalized) f = Math.Max(f / 127f, -1f); break;
                    }
                    outv[i * comps + c] = f;
                }
            return outv;
        }

        private static Texture2D Image(Gltf g, Dictionary<string, fsData> texInfo)
        {
            if (texInfo == null || !texInfo.ContainsKey("index")) return null;
            Dictionary<string, fsData> tex = g.L("textures")[I(texInfo, "index", 0)].AsDictionary;
            if (!tex.ContainsKey("source")) return null;
            int src = I(tex, "source", 0);
            Texture2D t;
            if (g.Images.TryGetValue(src, out t)) return t;
            Dictionary<string, fsData> img = g.L("images")[src].AsDictionary;
            byte[] data;
            if (img.ContainsKey("bufferView"))
            {
                Dictionary<string, fsData> bv = g.L("bufferViews")[I(img, "bufferView", 0)].AsDictionary;
                byte[] buf = g.Buffers[I(bv, "buffer", 0)];
                data = new byte[I(bv, "byteLength", 0)];
                Buffer.BlockCopy(buf, I(bv, "byteOffset", 0), data, 0, data.Length);
            }
            else data = Uri(g, img["uri"].AsString);
            t = TextureFromBytes(data, img.ContainsKey("name") ? img["name"].AsString : "image" + src);
            g.Images[src] = t;
            return t;
        }

        private static Dictionary<string, fsData> D(Dictionary<string, fsData> d, string k) { return d != null && d.ContainsKey(k) && d[k].IsDictionary ? d[k].AsDictionary : null; }

        private static Material GltfMaterial(Gltf g, int index)
        {
            Material m;
            if (g.Materials.TryGetValue(index, out m)) return m;
            if (index < 0) { m = NewMaterial("default", g.Override, Color.white, null, null, Color.black, false); g.Materials[index] = m; return m; }
            Dictionary<string, fsData> md = g.L("materials")[index].AsDictionary;
            Dictionary<string, fsData> pbr = D(md, "pbrMetallicRoughness");
            Color baseColor = Color.white;
            if (pbr != null && pbr.ContainsKey("baseColorFactor")) { List<fsData> c = pbr["baseColorFactor"].AsList; baseColor = new Color(Fl(c[0]), Fl(c[1]), Fl(c[2]), Fl(c[3])); }
            Color emissive = Color.black;
            if (md.ContainsKey("emissiveFactor")) { List<fsData> c = md["emissiveFactor"].AsList; emissive = new Color(Fl(c[0]), Fl(c[1]), Fl(c[2]), 1f); }
            bool blend = md.ContainsKey("alphaMode") && md["alphaMode"].AsString == "BLEND";
            m = NewMaterial(md.ContainsKey("name") ? md["name"].AsString : "material" + index, g.Override ?? Image(g, D(pbr, "baseColorTexture")), baseColor,
                Image(g, D(md, "normalTexture")), Image(g, D(md, "emissiveTexture")), emissive, blend);
            g.Materials[index] = m;
            return m;
        }

        private static void MeshInto(Gltf g, int index, GameObject go)
        {
            Dictionary<string, fsData> mesh = g.L("meshes")[index].AsDictionary;
            List<Vector3> pos = new List<Vector3>(), nor = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            List<int[]> subs = new List<int[]>();
            List<Material> mats = new List<Material>();
            bool hasNormals = true;
            foreach (fsData pd in mesh["primitives"].AsList)
            {
                Dictionary<string, fsData> p = pd.AsDictionary;
                if (I(p, "mode", 4) != 4) continue;                    // triangles only
                Dictionary<string, fsData> attr = p["attributes"].AsDictionary;
                int baseIndex = pos.Count, c;
                float[] P3 = Accessor(g, I(attr, "POSITION", 0), out c);
                int n = P3.Length / 3;
                for (int i = 0; i < n; i++) pos.Add(new Vector3(-P3[i * 3], P3[i * 3 + 1], P3[i * 3 + 2]));
                if (attr.ContainsKey("NORMAL")) { float[] N3 = Accessor(g, I(attr, "NORMAL", 0), out c); for (int i = 0; i < n; i++) nor.Add(new Vector3(-N3[i * 3], N3[i * 3 + 1], N3[i * 3 + 2])); }
                else { hasNormals = false; for (int i = 0; i < n; i++) nor.Add(Vector3.up); }
                if (attr.ContainsKey("TEXCOORD_0")) { float[] T2 = Accessor(g, I(attr, "TEXCOORD_0", 0), out c); for (int i = 0; i < n; i++) uv.Add(new Vector2(T2[i * 2], 1f - T2[i * 2 + 1])); }
                else for (int i = 0; i < n; i++) uv.Add(Vector2.zero);
                int[] tri;
                if (p.ContainsKey("indices"))
                {
                    float[] ix = Accessor(g, I(p, "indices", 0), out c);
                    tri = new int[ix.Length];
                    for (int i = 0; i + 2 < ix.Length; i += 3) { tri[i] = baseIndex + (int)ix[i]; tri[i + 1] = baseIndex + (int)ix[i + 2]; tri[i + 2] = baseIndex + (int)ix[i + 1]; }
                }
                else
                {
                    tri = new int[n - n % 3];
                    for (int i = 0; i + 2 < n; i += 3) { tri[i] = baseIndex + i; tri[i + 1] = baseIndex + i + 2; tri[i + 2] = baseIndex + i + 1; }
                }
                subs.Add(tri);
                mats.Add(GltfMaterial(g, I(p, "material", -1)));
            }
            if (subs.Count == 0) return;
            Mesh m = new Mesh { name = mesh.ContainsKey("name") ? mesh["name"].AsString : "mesh" + index };
            if (pos.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(pos); m.SetUVs(0, uv);
            m.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i);
            if (hasNormals) m.SetNormals(nor); else m.RecalculateNormals();
            m.RecalculateBounds(); m.RecalculateTangents();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
        }

        // A flat image (decal, banner, rug, sign): a quad lying on the table (or standing with rot), alpha-faded.
        // A flat image (decal, banner, rug, sign) lying on the table (or standing with rot). This build of the game has no
        // alpha-blended or cutout variants of the lit shader, so the image's shape is cut into the mesh instead: a grid of
        // up to 160 cells across, one quad per run of opaque cells (alpha >= 0.5), drawn with the ordinary lit material.
        public static GameObject ImagePlane(string file, float width)
        {
            Texture2D t = Texture(file, false);
            float h = width * t.height / Math.Max(1, t.width);
            Color32[] px = t.GetPixels32();
            int gw = Math.Min(160, t.width), gh = Math.Max(1, (int)Math.Round(gw * (double)t.height / t.width));
            List<Vector3> verts = new List<Vector3>(); List<Vector2> uvs = new List<Vector2>(); List<int> tris = new List<int>();
            for (int gy = 0; gy < gh; gy++)
            {
                int run = -1;
                for (int gx = 0; gx <= gw; gx++)
                {
                    bool solid = false;
                    if (gx < gw)
                    {
                        int x0 = gx * t.width / gw, x1 = Math.Max(x0 + 1, (gx + 1) * t.width / gw), y0 = gy * t.height / gh, y1 = Math.Max(y0 + 1, (gy + 1) * t.height / gh);
                        int sum = 0, n = 0;
                        for (int y = y0; y < y1; y += Math.Max(1, (y1 - y0) / 4)) for (int x = x0; x < x1; x += Math.Max(1, (x1 - x0) / 4)) { sum += px[y * t.width + x].a; n++; }
                        solid = n > 0 && sum / n >= 128;
                    }
                    if (solid && run < 0) run = gx;
                    if (!solid && run >= 0)
                    {
                        float u0 = (float)run / gw, u1 = (float)gx / gw, v0 = (float)gy / gh, v1 = (float)(gy + 1) / gh;
                        int b = verts.Count;
                        verts.Add(new Vector3((u0 - 0.5f) * width, 0, (v0 - 0.5f) * h)); verts.Add(new Vector3((u0 - 0.5f) * width, 0, (v1 - 0.5f) * h));
                        verts.Add(new Vector3((u1 - 0.5f) * width, 0, (v1 - 0.5f) * h)); verts.Add(new Vector3((u1 - 0.5f) * width, 0, (v0 - 0.5f) * h));
                        uvs.Add(new Vector2(u0, v0)); uvs.Add(new Vector2(u0, v1)); uvs.Add(new Vector2(u1, v1)); uvs.Add(new Vector2(u1, v0));
                        tris.AddRange(new int[] { b, b + 2, b + 1, b, b + 3, b + 2 });   // facing up (towards the camera)
                        run = -1;
                    }
                }
            }
            Mesh m = new Mesh { name = "image:" + Path.GetFileNameWithoutExtension(file) };
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
            GameObject go = new GameObject(m.name);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = NewMaterial(m.name, t, Color.white, null, null, Color.black, 0);
            return go;
        }
    }
}
