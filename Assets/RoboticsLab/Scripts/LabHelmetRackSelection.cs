using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoboticsLab
{
    /// <summary>Selection and appearance transfer for the four existing rack props.</summary>
    public static class LabHelmetRackSelection
    {
        static int cachedSceneHandle = int.MinValue;
        static readonly List<Transform> racks = new List<Transform>();

        public static Transform FindNearest(Transform player, float maxDistance = 1.65f)
        {
            if (!player || maxDistance < 0 || float.IsNaN(maxDistance)) return null;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return null;
            bool refresh = cachedSceneHandle != scene.handle || racks.Count == 0;
            for (int i = 0; i < racks.Count && !refresh; i++) refresh = !racks[i];
            if (refresh)
            {
                racks.Clear();
                cachedSceneHandle = scene.handle;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                        if (candidate.name == "SafetyHelmetRack") racks.Add(candidate);
            }

            Transform nearest = null;
            float bestSquared = maxDistance * maxDistance;
            Vector3 origin = player.position;
            foreach (Transform rack in racks)
            {
                if (!rack.gameObject.activeInHierarchy) continue;
                foreach (Transform helmet in rack)
                {
                    if (!helmet.gameObject.activeInHierarchy || !IsHelmet(helmet.name)) continue;
                    Vector3 delta = helmet.position - origin;
                    float squared = delta.x * delta.x + delta.z * delta.z;
                    // Keep hierarchy order on an exact tie; only XZ distance affects selection.
                    if (squared > bestSquared || (nearest && squared == bestSquared)) continue;
                    bestSquared = squared;
                    nearest = helmet;
                }
            }
            return nearest;
        }

        public static void CopyAppearance(Transform source, Transform worn)
        {
            if (!source || !worn || source == worn) return;
            CopyNode(source, worn);
        }

        static bool IsHelmet(string name)
        {
            return name == "HelmetPickup" || name.StartsWith("SpareHelmet", StringComparison.Ordinal);
        }

        static void CopyNode(Transform source, Transform worn)
        {
            Renderer[] sourceRenderers = source.GetComponents<Renderer>();
            Renderer[] wornRenderers = worn.GetComponents<Renderer>();
            for (int i = 0; i < sourceRenderers.Length; i++)
            {
                Type type = sourceRenderers[i].GetType();
                int occurrence = 0;
                for (int j = 0; j < i; j++) if (sourceRenderers[j].GetType() == type) occurrence++;
                for (int j = 0; j < wornRenderers.Length; j++)
                {
                    if (wornRenderers[j].GetType() != type) continue;
                    if (occurrence-- != 0) continue;
                    // Assign references only: do not instantiate materials or alter any geometry.
                    wornRenderers[j].sharedMaterials = sourceRenderers[i].sharedMaterials;
                    break;
                }
            }

            for (int i = 0; i < source.childCount; i++)
            {
                Transform child = source.GetChild(i);
                int occurrence = 0;
                for (int j = 0; j < i; j++) if (source.GetChild(j).name == child.name) occurrence++;
                // Several helmet children are all named Vent. Match each same-name occurrence,
                // instead of Transform.Find returning the first Vent for every relative path.
                for (int j = 0; j < worn.childCount; j++)
                {
                    Transform target = worn.GetChild(j);
                    if (target.name != child.name) continue;
                    if (occurrence-- != 0) continue;
                    CopyNode(child, target);
                    break;
                }
            }
        }
    }
}
