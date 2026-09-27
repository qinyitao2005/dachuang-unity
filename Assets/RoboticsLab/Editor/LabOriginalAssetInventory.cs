using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RoboticsLab.Editor
{
    /// <summary>Reads the loaded original scene or temporarily opens it additively. No source asset is saved.</summary>
    public static class LabOriginalAssetInventory
    {
        const string OriginalScene = "Assets/Scenes/SampleScene.unity";
        static readonly string[] CellNames = { "Draw", "多工位实训平台 1", "多工位实训平台 2" };
        static readonly string[] RackAssets = { "Assets/架子.fbx", "Assets/架子2.fbx" };
        static Dictionary<string, AssetRecord> assets;

        [Serializable] public class Inventory
        {
            public string utc, originalScene, sourceSHA256Before, sourceSHA256After, activeSceneBefore, activeSceneAfter;
            public bool sourceUnchanged, activeSceneUnchanged, activeSceneDirtyBefore, activeSceneDirtyAfter;
            public List<EquipmentRecord> equipment = new List<EquipmentRecord>();
            public List<EquipmentRecord> racks = new List<EquipmentRecord>();
            public List<EquipmentRecord> rackSourceModels = new List<EquipmentRecord>();
            public List<AssetRecord> assetCatalog = new List<AssetRecord>();
        }
        [Serializable] public class TransformRecord
        {
            public string name, hierarchy;
            public bool activeSelf, activeInHierarchy;
            public Vector3 worldPosition, worldEuler, localPosition, localEuler, localScale, lossyScale, worldRight, worldUp, worldForward;
            public Quaternion worldRotation, localRotation;
        }
        [Serializable] public class BoundsRecord
        {
            public bool valid;
            public Vector3 min, max, center, size;
        }
        [Serializable] public class AssetRecord
        {
            public string key, name, type, path, guid, localId, shader, mainTexture;
            public bool hasColor;
            public Color color;
        }
        [Serializable] public class RendererRecord
        {
            public string hierarchy, relativeHierarchy, rendererType, meshAssetKey;
            public bool enabled, activeInHierarchy;
            public BoundsRecord geometryWorldBounds;
            public List<string> materialAssetKeys = new List<string>();
        }
        [Serializable] public class ChildRecord
        {
            public TransformRecord transform;
            public int renderers, meshFilters;
            public BoundsRecord worldBounds, equipmentLocalBounds;
        }
        [Serializable] public class JointRecord
        {
            public TransformRecord transform;
            public Vector3 positionInEquipment;
            public string axis;
            public float minAngle, maxAngle;
        }
        [Serializable] public class SolverRecord
        {
            public string type, hierarchy;
            public bool enabled;
            public TransformRecord armRoot, baseJoint, endEffector, target, lastJoint;
            public Vector3 baseInEquipment, endInEquipment;
            public float baseHeightAboveEquipmentBoundsMin, endHeightAboveEquipmentBoundsMin;
            public List<TransformRecord> ancestors = new List<TransformRecord>();
            public List<JointRecord> joints = new List<JointRecord>();
        }
        [Serializable] public class EquipmentRecord
        {
            public string kind, correspondingPrefabAsset, consoleSideInference;
            public TransformRecord root;
            public BoundsRecord geometryWorldBounds, geometryLocalBounds;
            public int transformCount, rendererCount, meshFilterCount;
            public List<SourceCount> meshSources = new List<SourceCount>();
            public List<SourceCount> materialSources = new List<SourceCount>();
            public List<ChildRecord> directChildren = new List<ChildRecord>();
            public List<SolverRecord> solvers = new List<SolverRecord>();
            public List<RendererRecord> renderers = new List<RendererRecord>();
        }
        [Serializable] public class SourceCount { public string assetPath; public int count; }

        [MenuItem("Robot Lab/Diagnostics/Inspect Original Equipment")]
        public static void InspectOriginalEquipment()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before inspecting the preserved original scene.");
            Scene active = SceneManager.GetActiveScene();
            var report = new Inventory
            {
                utc = DateTime.UtcNow.ToString("O"), originalScene = OriginalScene,
                sourceSHA256Before = Hash(OriginalScene), activeSceneBefore = active.path, activeSceneDirtyBefore = active.isDirty
            };
            assets = new Dictionary<string, AssetRecord>();
            Scene preview = SceneManager.GetSceneByPath(OriginalScene);
            bool openedHere = !preview.IsValid() || !preview.isLoaded;
            try
            {
                if (openedHere) preview = EditorSceneManager.OpenScene(OriginalScene, OpenSceneMode.Additive);
                Transform[] transforms = preview.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
                foreach (string name in CellNames)
                {
                    Transform cell = transforms.FirstOrDefault(t => t.name == name && t.parent && t.parent.name == "多工位机器人集合");
                    if (!cell) throw new InvalidOperationException("Original complete equipment root not found: " + name);
                    report.equipment.Add(ReadEquipment(cell, "complete configured training cell", false));
                }
                var rackRoots = new HashSet<Transform>();
                foreach (var filter in preview.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true)))
                {
                    if (!filter.sharedMesh || !RackAssets.Contains(AssetDatabase.GetAssetPath(filter.sharedMesh))) continue;
                    Transform rack = filter.transform;
                    for (Transform t = filter.transform; t; t = t.parent)
                    {
                        if (t.name.StartsWith("架子", StringComparison.Ordinal) && t.parent) { rack = t; break; }
                    }
                    rackRoots.Add(rack);
                }
                foreach (var rack in rackRoots.OrderBy(t => Hierarchy(t))) report.racks.Add(ReadEquipment(rack, "original scene rack instance", false));
                foreach (string path in RackAssets)
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (model) report.rackSourceModels.Add(ReadEquipment(model.transform, "source FBX asset: " + path, true));
                }
            }
            finally
            {
                if (openedHere && preview.IsValid()) EditorSceneManager.CloseScene(preview, true);
                if (active.IsValid() && active.isLoaded && SceneManager.GetActiveScene().handle != active.handle) SceneManager.SetActiveScene(active);
            }
            Scene after = SceneManager.GetActiveScene();
            report.sourceSHA256After = Hash(OriginalScene);
            report.sourceUnchanged = report.sourceSHA256Before == report.sourceSHA256After;
            report.activeSceneAfter = after.path;
            report.activeSceneUnchanged = active.handle == after.handle;
            report.activeSceneDirtyAfter = active.isDirty;
            report.assetCatalog = assets.Values.OrderBy(a => a.path).ThenBy(a => a.name).ToList();
            Directory.CreateDirectory("Documentation");
            File.WriteAllText("Documentation/OriginalEquipmentInventory.json", JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            File.WriteAllText("Documentation/OriginalEquipmentInventory.txt", ToText(report), new UTF8Encoding(false));
            Debug.Log("ROBOT_ORIGINAL_EQUIPMENT_INVENTORY_READY cells=" + report.equipment.Count + " racks=" + report.racks.Count
                + " sourceUnchanged=" + report.sourceUnchanged + " activeSceneUnchanged=" + report.activeSceneUnchanged);
        }

        static EquipmentRecord ReadEquipment(Transform root, string kind, bool includeRendererDetails)
        {
            var data = new EquipmentRecord { kind = kind, root = ReadTransform(root) };
            var source = PrefabUtility.GetCorrespondingObjectFromSource(root.gameObject);
            data.correspondingPrefabAsset = source ? AssetDatabase.GetAssetPath(source) : "<unpacked scene hierarchy; inspect mesh/material asset keys>";
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            data.transformCount = root.GetComponentsInChildren<Transform>(true).Length;
            data.rendererCount = renderers.Length;
            data.meshFilterCount = root.GetComponentsInChildren<MeshFilter>(true).Length;
            data.meshSources = renderers.Select(r => SharedMesh(r)).Where(m => m)
                .GroupBy(m => AssetDatabase.GetAssetPath(m)).Select(g => new SourceCount { assetPath = g.Key, count = g.Count() }).ToList();
            data.materialSources = renderers.SelectMany(r => r.sharedMaterials).Where(m => m)
                .GroupBy(m => AssetDatabase.GetAssetPath(m)).Select(g => new SourceCount { assetPath = g.Key, count = g.Count() }).ToList();
            data.geometryWorldBounds = GeometryBounds(root, Matrix4x4.identity);
            data.geometryLocalBounds = GeometryBounds(root, root.worldToLocalMatrix);
            foreach (Transform child in root)
            {
                data.directChildren.Add(new ChildRecord
                {
                    transform = ReadTransform(child), renderers = child.GetComponentsInChildren<Renderer>(true).Length,
                    meshFilters = child.GetComponentsInChildren<MeshFilter>(true).Length,
                    worldBounds = GeometryBounds(child, Matrix4x4.identity), equipmentLocalBounds = GeometryBounds(child, root.worldToLocalMatrix)
                });
            }
            Transform console = root.Cast<Transform>().FirstOrDefault(t => t.name == "控制台");
            if (console)
            {
                Vector3 p = root.InverseTransformPoint(console.position);
                string side = Mathf.Abs(p.x) > Mathf.Abs(p.z) ? (p.x >= 0 ? "+X" : "-X") : (p.z >= 0 ? "+Z" : "-Z");
                data.consoleSideInference = "Console root is located on equipment-local " + side + " side at " + p.ToString("F5")
                    + ". This identifies the operating-console side from placement, not a verified front-face normal.";
            }
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var old = component as IK;
                var constrained = component as IKnew;
                var level = component as IKnew2;
                if (old) data.solvers.Add(ReadSolver(old, old.joints, old.endEffector, old.target, root, data.geometryWorldBounds));
                else if (constrained) data.solvers.Add(ReadSolver(constrained, constrained.joints, constrained.endEffector, constrained.target, root, data.geometryWorldBounds));
                else if (level) data.solvers.Add(ReadSolver(level, level.joints, level.endEffector, level.target, root, data.geometryWorldBounds));
            }
            if (includeRendererDetails) foreach (var renderer in renderers)
            {
                Mesh mesh = SharedMesh(renderer);
                var entry = new RendererRecord
                {
                    hierarchy = Hierarchy(renderer.transform), relativeHierarchy = AnimationUtility.CalculateTransformPath(renderer.transform, root),
                    rendererType = renderer.GetType().Name, enabled = renderer.enabled, activeInHierarchy = renderer.gameObject.activeInHierarchy,
                    meshAssetKey = mesh ? AssetKey(mesh) : "<no mesh>",
                    geometryWorldBounds = mesh ? TransformBounds(renderer.transform.localToWorldMatrix, mesh.bounds) : FromBounds(renderer.bounds)
                };
                foreach (Material material in renderer.sharedMaterials) entry.materialAssetKeys.Add(material ? AssetKey(material) : "<null>");
                data.renderers.Add(entry);
            }
            return data;
        }

        static SolverRecord ReadSolver(MonoBehaviour solver, JointConfig[] joints, Transform end, Transform target, Transform equipment, BoundsRecord bounds)
        {
            var list = joints == null ? new JointConfig[0] : joints.Where(j => j != null && j.joint).ToArray();
            Transform first = list.Length > 0 ? list[0].joint : null;
            Transform last = list.Length > 0 ? list[list.Length - 1].joint : null;
            Transform arm = solver.transform;
            for (Transform t = solver.transform; t && t != equipment; t = t.parent) if (t.name == "机械臂") { arm = t; break; }
            var result = new SolverRecord
            {
                type = solver.GetType().Name, hierarchy = Hierarchy(solver.transform), enabled = solver.enabled,
                armRoot = ReadTransform(arm), baseJoint = ReadTransform(first), endEffector = ReadTransform(end), target = ReadTransform(target), lastJoint = ReadTransform(last),
                baseInEquipment = first ? equipment.InverseTransformPoint(first.position) : Vector3.zero,
                endInEquipment = end ? equipment.InverseTransformPoint(end.position) : Vector3.zero,
                baseHeightAboveEquipmentBoundsMin = first && bounds.valid ? first.position.y - bounds.min.y : 0,
                endHeightAboveEquipmentBoundsMin = end && bounds.valid ? end.position.y - bounds.min.y : 0
            };
            for (Transform t = solver.transform; t; t = t.parent) result.ancestors.Add(ReadTransform(t));
            foreach (var joint in list) result.joints.Add(new JointRecord
            {
                transform = ReadTransform(joint.joint), positionInEquipment = equipment.InverseTransformPoint(joint.joint.position),
                axis = joint.axis.ToString(), minAngle = joint.minAngle, maxAngle = joint.maxAngle
            });
            return result;
        }

        static BoundsRecord GeometryBounds(Transform root, Matrix4x4 worldToOutput)
        {
            Bounds all = new Bounds(); bool found = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = SharedMesh(renderer);
                BoundsRecord one = mesh ? TransformBounds(worldToOutput * renderer.transform.localToWorldMatrix, mesh.bounds)
                    : TransformBounds(worldToOutput, renderer.bounds);
                Bounds box = new Bounds(one.center, one.size);
                if (!found) { all = box; found = true; } else all.Encapsulate(box);
            }
            return found ? FromBounds(all) : new BoundsRecord();
        }
        static Mesh SharedMesh(Renderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter) return filter.sharedMesh;
            var skinned = renderer as SkinnedMeshRenderer;
            return skinned ? skinned.sharedMesh : null;
        }
        static BoundsRecord TransformBounds(Matrix4x4 matrix, Bounds bounds)
        {
            Bounds output = new Bounds(matrix.MultiplyPoint3x4(bounds.min), Vector3.zero);
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                output.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                    y == 0 ? bounds.min.y : bounds.max.y, z == 0 ? bounds.min.z : bounds.max.z)));
            return FromBounds(output);
        }
        static BoundsRecord FromBounds(Bounds bounds) => new BoundsRecord { valid = true, center = bounds.center, min = bounds.min, max = bounds.max, size = bounds.size };
        static TransformRecord ReadTransform(Transform t) => !t ? null : new TransformRecord
        {
            name = t.name, hierarchy = Hierarchy(t), activeSelf = t.gameObject.activeSelf, activeInHierarchy = t.gameObject.activeInHierarchy,
            worldPosition = t.position, worldRotation = t.rotation, worldEuler = t.eulerAngles, localPosition = t.localPosition,
            localRotation = t.localRotation, localEuler = t.localEulerAngles, localScale = t.localScale, lossyScale = t.lossyScale,
            worldRight = t.right, worldUp = t.up, worldForward = t.forward
        };
        static string AssetKey(Object asset)
        {
            string guid; long local;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out guid, out local);
            string key = (string.IsNullOrEmpty(guid) ? "builtin:" + asset.GetType().Name : guid) + ":" + local;
            if (!assets.ContainsKey(key))
            {
                var data = new AssetRecord { key = key, name = asset.name, type = asset.GetType().Name, path = AssetDatabase.GetAssetPath(asset), guid = guid, localId = local.ToString() };
                var material = asset as Material;
                if (material)
                {
                    data.shader = material.shader ? material.shader.name : "<null>";
                    data.hasColor = material.HasProperty("_Color"); if (data.hasColor) data.color = material.GetColor("_Color");
                    data.mainTexture = material.mainTexture ? AssetDatabase.GetAssetPath(material.mainTexture) : "<none>";
                }
                assets.Add(key, data);
            }
            return key;
        }
        static string Hierarchy(Transform t) => t.parent ? Hierarchy(t.parent) + "/" + t.name : t.name;
        static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static string ToText(Inventory report)
        {
            var text = new StringBuilder();
            text.AppendLine("Original equipment inventory / " + report.utc);
            text.AppendLine("Source: " + report.originalScene);
            text.AppendLine("Source SHA256 unchanged=" + report.sourceUnchanged + " active scene unchanged=" + report.activeSceneUnchanged);
            text.AppendLine("Bounds include original static mesh geometry and all children, including inactive ones. Mesh and material source counts are summarized without dumping every CAD part.");
            foreach (var item in report.equipment.Concat(report.racks).Concat(report.rackSourceModels))
            {
                text.AppendLine(); text.AppendLine("=== " + item.kind + " / " + item.root.hierarchy + " ===");
                text.AppendLine("ROOT world=" + item.root.worldPosition.ToString("F6") + " euler=" + item.root.worldEuler.ToString("F5")
                    + " localScale=" + item.root.localScale.ToString("F6") + " lossyScale=" + item.root.lossyScale.ToString("F6"));
                text.AppendLine("AXES right=" + item.root.worldRight + " up=" + item.root.worldUp + " forward=" + item.root.worldForward);
                text.AppendLine("BOUNDS world min=" + item.geometryWorldBounds.min.ToString("F6") + " max=" + item.geometryWorldBounds.max.ToString("F6") + " size=" + item.geometryWorldBounds.size.ToString("F6"));
                text.AppendLine("BOUNDS local min=" + item.geometryLocalBounds.min.ToString("F6") + " max=" + item.geometryLocalBounds.max.ToString("F6"));
                text.AppendLine("Transforms=" + item.transformCount + " meshFilters=" + item.meshFilterCount + " renderers=" + item.rendererCount);
                foreach (var source in item.meshSources) text.AppendLine("MESH SOURCE " + source.assetPath + " count=" + source.count);
                foreach (var source in item.materialSources) text.AppendLine("MATERIAL SOURCE " + source.assetPath + " slots=" + source.count);
                if (!string.IsNullOrEmpty(item.consoleSideInference)) text.AppendLine(item.consoleSideInference);
                foreach (var child in item.directChildren)
                    text.AppendLine("CHILD " + child.transform.name + " local=" + child.transform.localPosition.ToString("F5") + " scale=" + child.transform.localScale.ToString("F5")
                        + " meshes=" + child.meshFilters + " local bounds=" + child.equipmentLocalBounds.min.ToString("F5") + " -> " + child.equipmentLocalBounds.max.ToString("F5"));
                foreach (var solver in item.solvers)
                {
                    text.AppendLine("SOLVER " + solver.type + " enabled=" + solver.enabled + " at " + solver.hierarchy);
                    text.AppendLine("  armRoot=" + (solver.armRoot == null ? "<null>" : solver.armRoot.hierarchy));
                    text.AppendLine("  base=" + (solver.baseJoint == null ? "<null>" : solver.baseJoint.hierarchy) + " equipment-local=" + solver.baseInEquipment.ToString("F6")
                        + " height above complete bounds.minY=" + solver.baseHeightAboveEquipmentBoundsMin.ToString("F6"));
                    text.AppendLine("  end=" + (solver.endEffector == null ? "<null>" : solver.endEffector.hierarchy) + " equipment-local=" + solver.endInEquipment.ToString("F6"));
                    foreach (var joint in solver.joints) text.AppendLine("    JOINT " + joint.transform.name + " axis=" + joint.axis + " limits=" + joint.minAngle + ".." + joint.maxAngle + " local-to-equipment=" + joint.positionInEquipment.ToString("F5"));
                }
                foreach (var group in item.renderers.GroupBy(r => r.meshAssetKey).Select(g => new { Asset = assets.ContainsKey(g.Key) ? assets[g.Key].path : g.Key, Count = g.Count() }).GroupBy(g => g.Asset))
                    text.AppendLine("MESH SOURCE " + group.Key + " renderer count=" + group.Sum(x => x.Count));
                foreach (var key in item.renderers.SelectMany(r => r.materialAssetKeys).Distinct())
                    if (assets.ContainsKey(key)) { var mat = assets[key]; text.AppendLine("MATERIAL " + mat.name + " path=" + mat.path + " localFileId=" + mat.localId + " shader=" + mat.shader + " color=" + mat.color); }
            }
            return text.ToString();
        }
    }
}
