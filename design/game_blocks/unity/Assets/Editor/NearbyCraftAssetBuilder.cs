#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Editor only. No custom runtime components or game assemblies are bundled.
public static class NearbyCraftAssetBuilder
{
    const string Source = "Assets/NearbyCraft";
    const string Output = "Assets/Generated";
    const string Bundle = "nearbycraftblocks.unity3d";
    [Serializable] public class Manifest { public Model[] models; }
    [Serializable] public class Model
    {
        public string name, family;
        public float[] bounds_min, bounds_max;
        public int[] triangles;
        public Collision[] colliders;
    }
    [Serializable] public class Collision { public float[] center, size; }

    [MenuItem("NearbyCraft/Build Windows bundle (including Proton)")]
    public static void BuildWindows() { Build(BuildTarget.StandaloneWindows64); }

    [MenuItem("NearbyCraft/Build native Linux bundle")]
    public static void BuildLinux() { Build(BuildTarget.StandaloneLinux64); }

    [MenuItem("NearbyCraft/Create prefabs without building")]
    public static void CreatePrefabs()
    {
        if (Application.unityVersion != "2022.3.62f2")
            throw new InvalidOperationException("Use Unity 2022.3.62f2 to match the installed 7DTD V3.2 player.");
        PlayerSettings.colorSpace = ColorSpace.Linear;
        Directory.CreateDirectory(Output);
        AssetDatabase.Refresh();
        ConfigureTextures();
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Source + "/manifest.json"));
        var materials = new[] { "console", "workshop", "locker" }.ToDictionary(f => f, MakeMaterial);
        foreach (var entry in manifest.models)
        {
            string path = Source + "/Models/" + entry.name + ".fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.SaveAndReimport();
            var imported = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var root = new GameObject(entry.name) { tag = "T_Block", layer = 0 };
            try
            {
                var visual = UnityEngine.Object.Instantiate(imported, root.transform);
                visual.name = "Visual";
                foreach (var importedLod in visual.GetComponentsInChildren<LODGroup>(true))
                    UnityEngine.Object.DestroyImmediate(importedLod);
                var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length != 3) throw new Exception(entry.name + ": expected exactly three LOD meshes.");
                var lods = new LOD[3];
                var thresholds = new[] { .45f, .18f, .025f };
                for (int i = 0; i < 3; i++)
                {
                    var renderer = renderers.Single(r => r.name.EndsWith("_LOD" + i));
                    renderer.enabled = true;
                    renderer.sharedMaterial = materials[entry.family];
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    if (mesh.subMeshCount != 1 || mesh.GetIndexCount(0) / 3 != entry.triangles[i])
                        throw new Exception(entry.name + ": imported mesh differs from Blender manifest.");
                    if (i == 0) CheckBounds(entry, renderer.bounds);
                    lods[i] = new LOD(thresholds[i], new Renderer[] { renderer });
                }
                var group = root.AddComponent<LODGroup>();
                group.fadeMode = LODFadeMode.CrossFade;
                group.animateCrossFading = true;
                group.SetLODs(lods);
                group.RecalculateBounds();
                // The game reads selection bounds from the first root collider.
                // Keeping a single T_Block root also preserves upper locker activation.
                foreach (var spec in entry.colliders)
                {
                    var collider = root.AddComponent<BoxCollider>();
                    collider.center = ToUnity(spec.center);
                    collider.size = new Vector3(spec.size[0], spec.size[2], spec.size[1]);
                }
                string prefab = Output + "/" + entry.name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("NearbyCraft: six prefabs created; bounds, material slots and triangle counts checked.");
    }

    static Vector3 ToUnity(float[] a) { return new Vector3(a[0], a[2], -a[1]); }

    static void CheckBounds(Model entry, Bounds actual)
    {
        var expected = new Bounds();
        expected.SetMinMax(new Vector3(entry.bounds_min[0], entry.bounds_min[2], -entry.bounds_max[1]),
                           new Vector3(entry.bounds_max[0], entry.bounds_max[2], -entry.bounds_min[1]));
        if ((expected.size - actual.size).magnitude > .005f || (expected.center - actual.center).magnitude > .005f)
            throw new Exception(entry.name + ": FBX axis/scale mismatch. Expected " + expected + ", got " + actual);
    }

    static void ConfigureTextures()
    {
        foreach (string file in Directory.GetFiles(Source + "/Textures", "*.png"))
        {
            string path = file.Replace('\\', '/');
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool normal = path.EndsWith("_Normal.png");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = path.EndsWith("_BaseColor.png") || path.EndsWith("_Emission.png");
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.isReadable = false;
            importer.SaveAndReimport();
        }
    }

    static Material MakeMaterial(string family)
    {
        string path = Output + "/NC_" + family + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("Standard");
        if (shader == null) throw new Exception("Built-in Standard shader missing; do not use URP/HDRP.");
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetColor("_Color", Color.white);
        material.SetTexture("_MainTex", Texture(family, "BaseColor"));
        material.SetTexture("_MetallicGlossMap", Texture(family, "MetallicSmoothness"));
        material.SetFloat("_GlossMapScale", 1);
        material.SetTexture("_BumpMap", Texture(family, "Normal"));
        material.SetFloat("_BumpScale", .65f);
        material.SetTexture("_EmissionMap", Texture(family, "Emission"));
        material.SetColor("_EmissionColor", Color.white * .40f);
        material.EnableKeyword("_NORMALMAP");
        material.EnableKeyword("_METALLICGLOSSMAP");
        material.EnableKeyword("_EMISSION");
        material.enableInstancing = true;
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(material);
        return material;
    }

    static Texture2D Texture(string family, string map)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Source + "/Textures/" + family + "_" + map + ".png");
        if (texture == null) throw new FileNotFoundException(family + "_" + map);
        return texture;
    }

    static void Build(BuildTarget target)
    {
        var group = BuildTargetGroup.Standalone;
        if (!BuildPipeline.IsBuildTargetSupported(group, target))
            throw new InvalidOperationException("Install the Unity " + target + " build support module.");
        if (EditorUserBuildSettings.activeBuildTarget != target &&
            !EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
            throw new InvalidOperationException("Could not switch Unity build target.");
        CreatePrefabs();
        string output = Path.GetFullPath("../built/" + target);
        Directory.CreateDirectory(output);
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Source + "/manifest.json"));
        var build = new AssetBundleBuild
        {
            assetBundleName = Bundle,
            assetNames = manifest.models.Select(m => Output + "/" + m.name + ".prefab").ToArray(),
            addressableNames = manifest.models.Select(m => m.name).ToArray()
        };
        var result = BuildPipeline.BuildAssetBundles(output, new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
        if (result == null || !File.Exists(Path.Combine(output, Bundle)))
            throw new Exception("AssetBundle build failed; read the Unity editor log.");
        // The staging script checks this receipt and hashes the actual bundle.
        File.WriteAllText(Path.Combine(output, "build-receipt.json"),
            "{\"unity_version\":\"" + Application.unityVersion + "\",\"target\":\"" + target +
            "\",\"bundle\":\"" + Bundle + "\",\"prefab_count\":6}");
        Debug.Log("NearbyCraft bundle built: " + output);
    }
}
#endif
