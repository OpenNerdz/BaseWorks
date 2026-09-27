using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;

namespace NearbyCraft
{
    // Only the six NearbyCraft names are intercepted. All vanilla and other-mod models
    // continue through the game's normal model loader.
    [HarmonyPatch(typeof(BlockShapeModelEntity), "getPrefab")]
    internal static class NearbyCraftBlockModelPatch
    {
        private static bool Prefix(BlockShapeModelEntity __instance, ref Transform __result)
        {
            string model = __instance.modelName;
            if (!NearbyCraftBlockModels.IsOwnModel(model)) return true;
            try
            {
                __result = NearbyCraftBlockModels.GetPrefab(model);
            }
            catch (Exception error)
            {
                Log.Error("[NearbyCraft] Custom block model " + model + " failed; using a vanilla visual: " + error);
                string fallback = model == "NC_LoadoutLocker"
                    ? "@:Entities/LootContainers/locker_ver1_lootPrefab.prefab"
                    : "@:Entities/Industrial/controlPanelBase_01Prefab.prefab";
                __result = DataLoader.LoadAsset<Transform>(fallback);
            }
            return false;
        }
    }

    internal static class NearbyCraftBlockModels
    {
        private sealed class RuntimeAsset
        {
            public int format { get; set; }
            public string name { get; set; }
            public string family { get; set; }
            public float[] bounds_min { get; set; }
            public float[] bounds_max { get; set; }
            public ColliderSpec collider { get; set; }
            public MeshData[] lods { get; set; }
        }

        private sealed class ColliderSpec
        {
            public float[] center { get; set; }
            public float[] size { get; set; }
        }

        private sealed class MeshData
        {
            public float[] vertices { get; set; }
            public float[] normals { get; set; }
            public float[] uv { get; set; }
            public int[] triangles { get; set; }
        }

        private sealed class TextureSet
        {
            public Texture2D BaseColor;
            public Texture2D Normal;
            public Texture2D MetallicSmoothness;
            public Texture2D Emission;
        }

        private static readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal)
        {
            "NC_Storage_T1", "NC_Storage_T2", "NC_Storage_T3", "NC_Storage_T4",
            "NC_Workshop", "NC_LoadoutLocker"
        };
        private static readonly Dictionary<string, Transform> Prefabs = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private static readonly Dictionary<string, TextureSet> Textures = new Dictionary<string, TextureSet>(StringComparer.Ordinal);
        private static GameObject cache;

        internal static bool IsOwnModel(string name) { return name != null && Names.Contains(name); }

        internal static Transform GetPrefab(string name)
        {
            Transform existing;
            if (Prefabs.TryGetValue(name, out existing) && existing != null)
            {
                existing.GetComponent<NearbyCraftMaterialOwner>().EnsureMaterial();
                return existing;
            }

            string folder = Path.Combine(NearbyCraftMod.ModDirectory, "Assets", "NearbyCraftBlocks");
            RuntimeAsset asset;
            using (var file = File.OpenRead(Path.Combine(folder, name + ".mesh.json.gz")))
            using (var unzip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new StreamReader(unzip))
                asset = JsonConvert.DeserializeObject<RuntimeAsset>(reader.ReadToEnd());
            if (asset == null || asset.format != 1 || asset.name != name || asset.lods == null || asset.lods.Length != 3
                || (asset.family != "console" && asset.family != "workshop" && asset.family != "locker"))
                throw new InvalidDataException("Unrecognized NearbyCraft mesh asset " + name);
            if (asset.collider == null || !Triple(asset.collider.center) || !Triple(asset.collider.size)
                || !Triple(asset.bounds_min) || !Triple(asset.bounds_max))
                throw new InvalidDataException("Invalid bounds in " + name);

            if (cache == null)
            {
                cache = new GameObject("NearbyCraft_BlockModelCache");
                cache.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(cache);
            }
            var root = new GameObject(name);
            root.transform.SetParent(cache.transform, false);
            try
            {
                root.tag = "T_Mesh_B";
                var collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(-asset.collider.center[0], asset.collider.center[2], -asset.collider.center[1]);
                collider.size = new Vector3(asset.collider.size[0], asset.collider.size[2], asset.collider.size[1]);
                var lods = new LOD[3];
                float[] thresholds = { .45f, .18f, .025f };
                int totalTriangles = 0;
                for (int level = 0; level < 3; level++)
                {
                    MeshData part = asset.lods[level];
                    Validate(part, name, level);
                    int count = part.vertices.Length / 3;
                    var vertices = new Vector3[count];
                    var normals = new Vector3[count];
                    var uvs = new Vector2[count];
                    for (int i = 0; i < count; i++)
                    {
                        vertices[i] = new Vector3(part.vertices[i * 3], part.vertices[i * 3 + 1], part.vertices[i * 3 + 2]);
                        normals[i] = new Vector3(part.normals[i * 3], part.normals[i * 3 + 1], part.normals[i * 3 + 2]);
                        uvs[i] = new Vector2(part.uv[i * 2], part.uv[i * 2 + 1]);
                    }
                    var mesh = new Mesh { name = name + "_LOD" + level, indexFormat = IndexFormat.UInt32 };
                    mesh.vertices = vertices;
                    mesh.normals = normals;
                    mesh.uv = uvs;
                    mesh.triangles = part.triangles;
                    mesh.RecalculateBounds();
                    mesh.RecalculateTangents();
                    if (level == 0) CheckBounds(mesh.bounds, asset, name);
                    var visual = new GameObject("LOD" + level);
                    visual.transform.SetParent(root.transform, false);
                    visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = visual.AddComponent<MeshRenderer>();
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    lods[level] = new LOD(thresholds[level], new Renderer[] { renderer });
                    totalTriangles += part.triangles.Length / 3;
                }
                var group = root.AddComponent<LODGroup>();
                group.SetLODs(lods);
                group.RecalculateBounds();
                root.AddComponent<NearbyCraftMaterialOwner>().Initialize(asset.family);
                Prefabs[name] = root.transform;
                Log.Out("[NearbyCraft] Loaded custom " + name + " model: " + totalTriangles + " triangles across three LODs.");
                return root.transform;
            }
            catch
            {
                UnityEngine.Object.Destroy(root);
                throw;
            }
        }

        private static bool Triple(float[] values)
        {
            if (values == null || values.Length != 3) return false;
            foreach (float value in values) if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            return true;
        }

        private static void Validate(MeshData part, string name, int level)
        {
            if (part == null || part.vertices == null || part.vertices.Length == 0 || part.vertices.Length % 3 != 0
                || part.vertices.Length > 150000 || part.normals == null || part.normals.Length != part.vertices.Length
                || part.uv == null || part.uv.Length != part.vertices.Length / 3 * 2
                || part.triangles == null || part.triangles.Length == 0 || part.triangles.Length % 3 != 0)
                throw new InvalidDataException(name + " LOD" + level + " has malformed arrays");
            int count = part.vertices.Length / 3;
            foreach (int index in part.triangles)
                if (index < 0 || index >= count) throw new InvalidDataException(name + " LOD" + level + " has bad indices");
            foreach (float value in part.vertices)
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException(name + " has nonfinite vertices");
            foreach (float value in part.normals)
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException(name + " has nonfinite normals");
            foreach (float value in part.uv)
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException(name + " has nonfinite UVs");
        }

        private static void CheckBounds(Bounds actual, RuntimeAsset asset, string name)
        {
            var expected = new Bounds();
            expected.SetMinMax(new Vector3(-asset.bounds_max[0], asset.bounds_min[2], -asset.bounds_max[1]),
                               new Vector3(-asset.bounds_min[0], asset.bounds_max[2], -asset.bounds_min[1]));
            if ((expected.center - actual.center).magnitude > .006f || (expected.size - actual.size).magnitude > .006f)
                throw new InvalidDataException(name + " axis, unit scale or mesh bounds do not match Blender manifest");
        }

        internal static Material CreateMaterial(string family)
        {
            TextureSet textures;
            if (!Textures.TryGetValue(family, out textures))
            {
                string folder = Path.Combine(NearbyCraftMod.ModDirectory, "Assets", "NearbyCraftBlocks");
                textures = new TextureSet
                {
                    BaseColor = LoadTexture(Path.Combine(folder, family + "_BaseColor.png"), false),
                    Normal = LoadTexture(Path.Combine(folder, family + "_Normal.png"), true),
                    MetallicSmoothness = LoadTexture(Path.Combine(folder, family + "_MetallicSmoothness.png"), true),
                    Emission = LoadTexture(Path.Combine(folder, family + "_Emission.png"), false)
                };
                Textures.Add(family, textures);
            }
            Shader shader = Shader.Find("Standard");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Built-in Standard shader unavailable");
            var material = new Material(shader) { name = "NC_" + family + "_owned", enableInstancing = false };
            material.mainTexture = textures.BaseColor;
            material.SetTexture("_BumpMap", textures.Normal);
            material.SetFloat("_BumpScale", .65f);
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_MetallicGlossMap", textures.MetallicSmoothness);
            material.SetFloat("_GlossMapScale", 1f);
            material.EnableKeyword("_METALLICGLOSSMAP");
            material.SetTexture("_EmissionMap", textures.Emission);
            material.SetColor("_EmissionColor", Color.white * .40f);
            material.EnableKeyword("_EMISSION");
            return material;
        }

        private static Texture2D LoadTexture(string path, bool linear)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear)
            {
                name = Path.GetFileName(path), anisoLevel = 4, hideFlags = HideFlags.DontUnloadUnusedAsset
            };
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false))
            {
                UnityEngine.Object.Destroy(texture);
                throw new InvalidDataException("Cannot read texture " + path);
            }
            texture.Apply(true, true);
            return texture;
        }
    }

    // 7DTD may clean up runtime-created materials when previews and chunks unload.
    // Own one material per clone, while sharing the large textures across all clones.
    public sealed class NearbyCraftMaterialOwner : MonoBehaviour
    {
        [SerializeField] private string family;
        [NonSerialized] private Material owned;
        [NonSerialized] private MeshRenderer[] renderers;
        [NonSerialized] private float nextCheck;
        [NonSerialized] private bool failed;

        internal void Initialize(string value)
        {
            family = value;
            EnsureMaterial();
        }

        internal void EnsureMaterial()
        {
            if (renderers == null) renderers = GetComponentsInChildren<MeshRenderer>(true);
            if (owned == null)
            {
                owned = NearbyCraftBlockModels.CreateMaterial(family);
                foreach (var renderer in renderers)
                    if (renderer != null) renderer.sharedMaterial = owned;
                return;
            }
            foreach (var renderer in renderers)
                if (renderer != null && renderer.sharedMaterial == null) renderer.sharedMaterial = owned;
        }

        private void Check()
        {
            if (failed) return;
            try { EnsureMaterial(); }
            catch (Exception error)
            {
                failed = true;
                Log.Error("[NearbyCraft] Could not restore block material on " + name + ": " + error);
            }
        }

        private void Awake() { Check(); }
        private void OnEnable() { Check(); }
        private void LateUpdate()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f;
            Check();
        }
        private void OnDestroy()
        {
            if (owned != null) UnityEngine.Object.Destroy(owned);
            owned = null;
        }
    }
}
