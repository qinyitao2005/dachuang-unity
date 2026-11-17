using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>Photographs only the original arm meshes; no scene or asset changes are persisted.</summary>
    public static class LabRobotOnlyPhoto
    {
        public static void Capture(LabRobotStation station, string output, List<string> results)
        {
            Transform arm = null;
            foreach (var child in station.modelRoot.GetComponentsInChildren<Transform>(true))
                if (child.name == "机械臂") { arm = child; break; }
            if (!arm) arm = station.modelRoot;
            var layers = new Dictionary<GameObject, int>();
            var meshes = new List<Renderer>();
            foreach (var r in arm.GetComponentsInChildren<Renderer>())
                if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)) meshes.Add(r);
            if (meshes.Count == 0) throw new InvalidOperationException("Original robot arm has no visible meshes.");
            Bounds bounds = meshes[0].bounds;
            foreach (var r in meshes)
            {
                bounds.Encapsulate(r.bounds);
                if (!layers.ContainsKey(r.gameObject)) layers.Add(r.gameObject, r.gameObject.layer);
                r.gameObject.layer = 30;
            }
            var cameraObject = new GameObject("RobotOnlyPhotoCamera"); var camera = cameraObject.AddComponent<Camera>();
            var lightMasks = new Dictionary<Light, int>();
            foreach (var sceneLight in UnityEngine.Object.FindObjectsOfType<Light>())
            { lightMasks.Add(sceneLight, sceneLight.cullingMask); sceneLight.cullingMask &= ~(1 << 30); }
            var oldAmbientMode = RenderSettings.ambientMode; var oldAmbient = RenderSettings.ambientLight;
            float oldAmbientIntensity = RenderSettings.ambientIntensity, oldReflection = RenderSettings.reflectionIntensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.28f, .30f, .34f);
            RenderSettings.ambientIntensity = .6f; RenderSettings.reflectionIntensity = .25f;
            var lightObject = new GameObject("RobotOnlyPhotoKeyLight"); var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = .7f; light.cullingMask = 1 << 30;
            lightObject.transform.rotation = Quaternion.Euler(35, -35, 0);
            var fillObject = new GameObject("RobotOnlyPhotoFillLight"); var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = .22f; fill.cullingMask = 1 << 30;
            fillObject.transform.rotation = Quaternion.Euler(25, 130, 0);
            var rt = new RenderTexture(2400, 2400, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(2400, 2400, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            try
            {
                camera.enabled = false; camera.cullingMask = 1 << 30;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.88f, .91f, .94f);
                camera.orthographic = true; camera.nearClipPlane = .01f; camera.farClipPlane = 50;
                camera.orthographicSize = bounds.extents.magnitude * 1.04f;
                Vector3 direction = station.transform.TransformDirection(new Vector3(1.8f, 1.1f, -2.5f).normalized);
                camera.transform.position = bounds.center + direction * Mathf.Max(2, bounds.size.magnitude * 3);
                camera.transform.LookAt(bounds.center); camera.targetTexture = rt;
                camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 2400, 2400), 0, 0); image.Apply();
                string path = Path.Combine(output, "机械臂单独展示.png"); Directory.CreateDirectory(output);
                File.WriteAllBytes(path, image.EncodeToPNG()); results.Add(path);
                File.WriteAllText(Path.Combine(output, "RobotPhotoMetadata.txt"), "Source=" + arm.name + "\nMeshes=" + meshes.Count + "\nBounds=" + bounds + "\nOriginal CAD meshes; camera-only isolated rendering.\n");
            }
            finally
            {
                foreach (var pair in layers) if (pair.Key) pair.Key.layer = pair.Value;
                foreach (var pair in lightMasks) if (pair.Key) pair.Key.cullingMask = pair.Value;
                RenderSettings.ambientMode = oldAmbientMode; RenderSettings.ambientLight = oldAmbient;
                RenderSettings.ambientIntensity = oldAmbientIntensity; RenderSettings.reflectionIntensity = oldReflection;
                RenderTexture.active = active;
                UnityEngine.Object.Destroy(image); UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(cameraObject); UnityEngine.Object.Destroy(lightObject); UnityEngine.Object.Destroy(fillObject);
            }
        }
    }
}
