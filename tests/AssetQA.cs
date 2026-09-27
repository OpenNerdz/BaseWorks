// Opt-in only: compiled with -p:AssetQA=true, never included in a release.
// Main-menu smoke test; no world or save is created or modified.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace NearbyCraft
{
    internal static class NearbyCraftAssetQa
    {
        private static bool started;
        internal static void Update(ref ModEvents.SUnityUpdateData data)
        {
            if (started || Time.unscaledTime < 15 || !Environment.GetCommandLineArgs().Contains("-NearbyCraftAssetQA")) return;
            started = true;
            var host = new GameObject("NearbyCraft_AssetQA");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<NearbyCraftAssetQaRunner>();
        }
    }

    public sealed class NearbyCraftAssetQaRunner : MonoBehaviour
    {
        private int checks;
        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
            Log.Out("[NearbyCraft AssetQA] PASS " + message);
        }

        private IEnumerator Start()
        {
            bool failed = false;
            try
            {
                Check(GameManager.Instance.World == null, "main-menu test has no loaded world");
                Check(GameIO.GetSaveGameRootDir().Replace('\\', '/').Contains("/NearbyCraft/qa-userdata/Saves"),
                    "isolated QA user-data root");
                string[] names = { "NC_Storage_T1", "NC_Storage_T2", "NC_Storage_T3", "NC_Storage_T4",
                    "NC_Workshop", "NC_LoadoutLocker" };
                foreach (string name in names) CheckModel(name);
                Log.Out("[NearbyCraft AssetQA] COMPLETE " + checks + " native asset/render assertions, no world loaded.");
            }
            catch (Exception error)
            {
                failed = true;
                Log.Error("[NearbyCraft AssetQA] FAILED " + error);
            }
            yield return null;
            Application.Quit(failed ? 1 : 0);
        }

        private void CheckModel(string name)
        {
            Transform prefab = NearbyCraftBlockModels.GetPrefab(name);
            var shape = new BlockShapeModelEntity { modelName = name };
            var resolved = (Transform)AccessTools.Method(typeof(BlockShapeModelEntity), "getPrefab").Invoke(shape, null);
            Check(resolved == prefab, name + " resolves through the native ModelEntity hook");
            Check(prefab != null && prefab.GetComponent<BoxCollider>() != null, name + " loads with root collider");
            Check(prefab.GetComponentsInChildren<MeshRenderer>(true).Length == 3, name + " has three native LOD renderers");
            var sourceMaterial = prefab.GetComponentsInChildren<MeshRenderer>(true)[0].sharedMaterial;
            Check(sourceMaterial != null && sourceMaterial.shader != null && sourceMaterial.shader.isSupported,
                name + " Standard shader is supported");
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Check(instance.GetComponentsInChildren<MeshRenderer>(true)[0].sharedMaterial != sourceMaterial,
                    name + " clone owns an independent material");
                Check(instance.GetComponent<LODGroup>() != null, name + " has a native LOD group");
                Check(instance.GetComponentsInChildren<Light>(true).Length == 0, name + " has no runtime lights");
                CheckRendered(instance, name);
            }
            finally { UnityEngine.Object.Destroy(instance.gameObject); }
        }

        private void CheckRendered(Transform model, string name)
        {
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true)) renderer.gameObject.layer = 31;
            model.position = Vector3.zero;
            var cameraObject = new GameObject("NC_AssetQA_Camera");
            var lightObject = new GameObject("NC_AssetQA_Light");
            int resolution = name == "NC_Storage_T1" ? 1024 : 320;
            var target = new RenderTexture(resolution, resolution, 24);
            var image = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.transform.position = new Vector3(2.4f, name == "NC_LoadoutLocker" ? 2.4f : 1.8f, 3.2f);
                camera.transform.LookAt(new Vector3(0, name == "NC_LoadoutLocker" ? 1f : .5f, 0));
                camera.fieldOfView = 35;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 20;
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.5f;
                light.cullingMask = 1 << 31;
                light.transform.rotation = Quaternion.Euler(40, -30, 0);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                image.Apply();
                if (name == "NC_Storage_T1")
                    File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(GameIO.GetSaveGameRootDir()),
                        "nearbycraft-model-front-qa.png"), image.EncodeToPNG());
                int visible = 0, magenta = 0;
                foreach (Color32 pixel in image.GetPixels32())
                {
                    if (pixel.r > 18 || pixel.g > 18 || pixel.b > 18) visible++;
                    if (pixel.r > 145 && pixel.g < 105 && pixel.b > 145) magenta++;
                }
                Check(visible > 1500 && magenta == 0,
                    name + " GPU render has visible geometry and no error-magenta pixels (" + visible + " visible)");
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.Destroy(image);
                UnityEngine.Object.Destroy(target);
                UnityEngine.Object.Destroy(cameraObject);
                UnityEngine.Object.Destroy(lightObject);
            }
        }
    }
}
