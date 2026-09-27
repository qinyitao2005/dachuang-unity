using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RoboticsLab.Editor
{
    /// <summary>Read-only checks of the original CAD assemblies placed in the teaching room.</summary>
    public static class LabOriginalEquipmentValidation
    {
        const string OriginalScene = "Assets/Scenes/SampleScene.unity";
        const string CellMeshSource = "Assets/无标题.fbx";
        const string RackMeshSource = "Assets/架子2.fbx";
        const string AvatarSource = "Assets/Jogging.fbx";
        const string AvatarSHA256 = "346C19BFC70A5BE3F1353D552F798338FD145774408EDFA47149EFBC8A2679D8";
        const float FloorTolerance = .02f;

        [Serializable] class InventoryBaseline
        {
            public string originalScene, sourceSHA256Before, sourceSHA256After;
            public bool sourceUnchanged;
        }

        [MenuItem("Robot Lab/Diagnostics/Verify Original Equipment")]
        public static void Verify()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before verifying the authored equipment.");
            var scene = SceneManager.GetActiveScene();
            var report = new List<string> { "Original equipment verification / " + DateTime.UtcNow.ToString("O"), "Scene: " + scene.path };
            int failures = 0;
            Action<bool, string> check = (pass, message) => { report.Add((pass ? "PASS: " : "FAIL: ") + message); if (!pass) failures++; };
            try
            {
                check(scene.path == LabSceneBuilder.ScenePath, "Active scene is the teaching room");
                var roots = scene.GetRootGameObjects();
                var transforms = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
                var stations = roots.SelectMany(r => r.GetComponentsInChildren<LabRobotStation>(true)).OrderBy(s => s.stationIndex).ToArray();
                check(stations.Length == 3, "Three teaching stations exist; actual=" + stations.Length);
                check(stations.Select(s => s.stationIndex).OrderBy(i => i).SequenceEqual(new[] { 0, 1, 2 }), "Station indices are exactly 0, 1, 2");
                var protectedOriginalParts = new HashSet<Transform>();
                foreach (var station in stations)
                {
                    string label = station.name + " [" + station.stationIndex + "]";
                    check(station.modelRoot != null, label + " has a complete modelRoot reference");
                    if (!station.modelRoot) continue;
                    foreach (var t in station.modelRoot.GetComponentsInChildren<Transform>(true)) protectedOriginalParts.Add(t);
                    var meshes = station.modelRoot.GetComponentsInChildren<MeshFilter>(true)
                        .Where(m => !station.ringGripTool || !m.transform.IsChildOf(station.ringGripTool)).ToArray();
                    int sourceCount = meshes.Count(m => MeshPath(m) == CellMeshSource);
                    check(meshes.Length == 1751, label + " complete original assembly has 1751 MeshFilters; actual=" + meshes.Length);
                    check(sourceCount == 1751 && sourceCount == meshes.Length, label + " every CAD mesh comes from " + CellMeshSource + "; matching=" + sourceCount);
                    if(station.ringGripTool)
                    {
                        var grip=station.ringGripTool.GetComponentsInChildren<MeshFilter>(true);
                        check(grip.Count(m=>MeshPath(m)==CellMeshSource)==30,label+" separate ring gripper retains 30 original CAD meshes");
                        check(grip.Length==34&&station.ringGripTool.Find("DistalJawShoes"),label+" adds four attached distal jaw shoe meshes");
                    }
                    Bounds bounds;
                    bool hasBounds = TryMeshBounds(station.modelRoot, out bounds);
                    check(hasBounds && Mathf.Abs(bounds.min.y) <= FloorTolerance, label + " model bottom is on floor within " + FloorTolerance.ToString("F3") + " m; minY=" + bounds.min.y.ToString("F6"));
                    report.Add("INFO: " + label + " world bounds min=" + bounds.min.ToString("F6") + " max=" + bounds.max.ToString("F6") + " size=" + bounds.size.ToString("F6"));
                    report.Add("INFO: " + label + " model root=" + Hierarchy(station.modelRoot) + " world=" + station.modelRoot.position.ToString("F6") + " rotation=" + station.modelRoot.eulerAngles.ToString("F3") + " scale=" + station.modelRoot.lossyScale.ToString("F6"));
                }

                var rackMeshes = roots.SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true)).Where(m => MeshPath(m) == RackMeshSource).ToArray();
                var racks = rackMeshes.Select(FindRackRoot).Distinct().ToArray();
                check(racks.Length == 2, "Two original rack instances from " + RackMeshSource + "; actual=" + racks.Length);
                check(rackMeshes.Length == 4, "Two original racks retain their two source meshes each; total matching MeshFilters=" + rackMeshes.Length);
                foreach (var rack in racks)
                {
                    var meshes = rack.GetComponentsInChildren<MeshFilter>(true).Where(m => MeshPath(m) == RackMeshSource).ToArray();
                    check(meshes.Length == 2, "Rack " + Hierarchy(rack) + " retains both original source meshes; actual=" + meshes.Length);
                    Bounds bounds; TryMeshBounds(rack, out bounds, RackMeshSource);
                    report.Add("INFO: rack=" + Hierarchy(rack) + " source=" + RackMeshSource + " min=" + bounds.min.ToString("F6") + " max=" + bounds.max.ToString("F6") + " scale=" + rack.lossyScale.ToString("F6"));
                }

                var leftovers = transforms.Where(t => !protectedOriginalParts.Contains(t) && IsLegacyEquipmentName(t.name)).Select(Hierarchy).ToArray();
                check(leftovers.Length == 0, "No legacy Workbench / Backboard / generated guard objects remain outside the original CAD assemblies");
                foreach (string item in leftovers) report.Add("LEGACY: " + item);
                report.Add("INFO: Legacy check matches Workbench, Backboard, Guard, Fence and 护栏 names; the imported original assembly is excluded.");

                string baselinePath = ProjectPath("Documentation/OriginalEquipmentInventory.json");
                check(File.Exists(baselinePath), "Original scene inventory baseline is present");
                if (File.Exists(baselinePath))
                {
                    var baseline = JsonUtility.FromJson<InventoryBaseline>(File.ReadAllText(baselinePath));
                    bool usable = baseline != null && baseline.originalScene == OriginalScene && baseline.sourceUnchanged
                        && !string.IsNullOrEmpty(baseline.sourceSHA256Before)
                        && string.Equals(baseline.sourceSHA256Before, baseline.sourceSHA256After, StringComparison.OrdinalIgnoreCase);
                    check(usable, "Inventory baseline contains a consistent preserved SampleScene SHA256");
                    string actual = Hash(ProjectPath(OriginalScene));
                    check(usable && string.Equals(actual, baseline.sourceSHA256Before, StringComparison.OrdinalIgnoreCase), "Original SampleScene file is unchanged from the inventory baseline");
                    report.Add("SHA256: " + OriginalScene + " current=" + actual + " baseline=" + (baseline == null ? "<null>" : baseline.sourceSHA256Before));
                }
                string avatarHash = Hash(ProjectPath(AvatarSource));
                check(string.Equals(avatarHash, AvatarSHA256, StringComparison.OrdinalIgnoreCase), "Original Jogging.fbx matches the approved SHA256");
                report.Add("SHA256: " + AvatarSource + " current=" + avatarHash + " baseline=" + AvatarSHA256);
            }
            catch (Exception e)
            {
                check(false, "Verification exception: " + e);
            }
            report.Add("RESULT: " + (failures == 0 ? "PASS" : "FAIL") + "; failed checks=" + failures);
            string destination = ProjectPath("Documentation/OriginalEquipmentValidation.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.WriteAllLines(destination, report, new UTF8Encoding(false));
            if (failures == 0) Debug.Log("ROBOT_ORIGINAL_EQUIPMENT_VALIDATION PASS " + destination);
            else Debug.LogError("ROBOT_ORIGINAL_EQUIPMENT_VALIDATION FAIL count=" + failures + " " + destination);
        }

        static Transform FindRackRoot(MeshFilter mesh)
        {
            var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(mesh.gameObject);
            if (prefabRoot && prefabRoot.GetComponentsInChildren<MeshFilter>(true).Count(m => MeshPath(m) == RackMeshSource) == 2) return prefabRoot.transform;
            // An unpacked source rack contains two mesh children; stop at their first common ancestor.
            for (Transform current = mesh.transform; current; current = current.parent)
                if (current.GetComponentsInChildren<MeshFilter>(true).Count(m => MeshPath(m) == RackMeshSource) == 2) return current;
            return mesh.transform;
        }

        static bool IsLegacyEquipmentName(string name)
        {
            return new[] { "Workbench", "Backboard", "Guard", "Fence", "护栏" }
                .Any(part => name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static string MeshPath(MeshFilter mesh) => mesh && mesh.sharedMesh ? AssetDatabase.GetAssetPath(mesh.sharedMesh) : "";

        static bool TryMeshBounds(Transform root, out Bounds result, string source = null)
        {
            result = new Bounds(root.position, Vector3.zero); bool initialized = false;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh || (source != null && MeshPath(filter) != source)) continue;
                Bounds b = filter.sharedMesh.bounds;
                Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(new Vector3(x == 0 ? b.min.x : b.max.x, y == 0 ? b.min.y : b.max.y, z == 0 ? b.min.z : b.max.z));
                    if (!initialized) { result = new Bounds(point, Vector3.zero); initialized = true; } else result.Encapsulate(point);
                }
            }
            return initialized;
        }

        [MenuItem("Robot Lab/Capture/Original Equipment Detail")]
        public static void CaptureDetail()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != LabSceneBuilder.ScenePath) throw new InvalidOperationException("Open the teaching scene before taking its equipment detail image.");
            var station = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LabRobotStation>(true)).FirstOrDefault(s => s.stationIndex == 0);
            Vector3 target = station ? station.transform.position + new Vector3(.1f, 1.1f, 0) : new Vector3(-4.4f, 1.1f, 5.5f);
            GameObject cameraObject = null; RenderTexture rt = null; Texture2D image = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                cameraObject = new GameObject("TemporaryOriginalEquipmentCapture") { hideFlags = HideFlags.HideAndDontSave };
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(-1.1f, 2.05f, 3.3f); camera.transform.LookAt(target);
                camera.fieldOfView = 45; camera.nearClipPlane = .05f; camera.farClipPlane = 90;
                camera.clearFlags = CameraClearFlags.Skybox; camera.allowHDR = false;
                rt = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
                camera.targetTexture = rt; camera.Render();
                RenderTexture.active = rt;
                image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
                string destination = ProjectPath("Documentation/Screenshots/OriginalEquipmentDetail.png");
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.WriteAllBytes(destination, image.EncodeToPNG());
                Debug.Log("ROBOT_ORIGINAL_EQUIPMENT_DETAIL_CAPTURE " + destination);
            }
            finally
            {
                RenderTexture.active = previous;
                if (cameraObject) cameraObject.GetComponent<Camera>().targetTexture = null;
                if (image) Object.DestroyImmediate(image);
                if (rt) { rt.Release(); Object.DestroyImmediate(rt); }
                if (cameraObject) Object.DestroyImmediate(cameraObject);
            }
        }

        static string ProjectPath(string relative) => Path.Combine(Path.GetDirectoryName(Application.dataPath), relative);
        static string Hierarchy(Transform transform) => transform.parent ? Hierarchy(transform.parent) + "/" + transform.name : transform.name;
        static string Hash(string path)
        {
            using (var file = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }
    }
}
