using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoboticsLab.Editor
{
    /// <summary>增量配置三台工位；保留原设备、组员脚本及教学素材。</summary>
    public static class LabStationRolesAuthoring
    {
        const string WristPath = "OriginalAssembly/机械臂/基座/大臂/关节/小臂/手腕/小手腕";
        static readonly string[] Names = { "01 · 循迹训练", "02 · 钢丝套圈", "03 · 自由画图" };

        [MenuItem("Robot Lab/Configure Dedicated Stations")]
        public static void Configure()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != LabSceneBuilder.ScenePath)
                scene = EditorSceneManager.OpenScene(LabSceneBuilder.ScenePath);
            var stations = Object.FindObjectsOfType<LabRobotStation>(true).OrderBy(s => s.stationIndex).ToArray();
            if (stations.Length != 3) throw new InvalidOperationException("Expected the three original workcells.");
            var report = new StringBuilder("Dedicated station authoring / " + DateTime.UtcNow.ToString("O") + "\n");
            foreach (var station in stations)
            {
                station.displayName = Names[station.stationIndex];
                var group = station.GetComponent<Group0927Station>();
                if (!group)
                {
                    group = Undo.AddComponent<Group0927Station>(station.gameObject);
                    foreach (var field in typeof(Group0927Station).GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var source = typeof(LabRobotStation).GetField(field.Name);
                        if (source != null && source.FieldType == field.FieldType) field.SetValue(group, source.GetValue(station));
                    }
                }
                group.displayName = station.displayName;
                group.stationIndex = station.stationIndex;
                // The imported group adapter contains a corrupt trailing wire point.
                // The facade holds the original authored 121-point wire used by its mesh.
                if (station.wirePoints != null)
                    group.wirePoints = (Vector3[])station.wirePoints.Clone();
                if (station.stationIndex == 0)
                {
                    // The team's Part1 sequence parked this original workcell as an
                    // inactive scene root. Restore it without changing its CAD transforms.
                    var previousFlow = station.transform.Find("Part1");
                    if (previousFlow) previousFlow.gameObject.SetActive(false);
                    station.modelRoot.SetParent(station.transform, true);
                    station.modelRoot.gameObject.SetActive(true);
                    MountTracingPen(station, group);
                    var tracing = station.GetComponent<LabTracingExercise>() ?? Undo.AddComponent<LabTracingExercise>(station.gameObject);
                    tracing.station = station;
                    tracing.layoutSize = new Vector2(.19f, .27f);
                    tracing.layoutOffset = Vector2.zero;
                    foreach (var line in station.drawingBoard.GetComponentsInChildren<LineRenderer>(true))
                        if (line.name == "PaperGrid") line.gameObject.SetActive(false);
                    EditorUtility.SetDirty(tracing);
                }
                // CAD tool pieces are fixed to the wrist; only six real joints belong in IK.
                group.joints = group.joints.Where(j => j != null && j.joint && j.joint != group.DrawingToolRoot).ToArray();
                EditorUtility.SetDirty(group);
                EditorUtility.SetDirty(station);
                UpdateSign(station.stationIndex);
                report.AppendLine("PASS: " + station.displayName + "; mode=" + (station.stationIndex == 1 ? 1 : 0) + "; real joints=" + group.joints.Length);
            }
            var app = Object.FindObjectOfType<LabTrainingApp>();
            if (app)
            {
                // Preserve the old Part1 objects for the team, but detach its obsolete entry.
                // Its runtime installer sees this null binding and cannot attach competing input.
                app.part1启动桥 = null;
                app.Part1接管中 = false;
                app.stations = stations;
                EditorUtility.SetDirty(app);
            }
            var previousMenu = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "机械臂循迹菜单");
            if (previousMenu) previousMenu.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Documentation");
            File.WriteAllText("Documentation/StationRolesAuthoring.txt", report.ToString(), new UTF8Encoding(false));
            Debug.Log("DEDICATED_STATIONS_CONFIGURED\n" + report);
        }

        static void MountTracingPen(LabRobotStation station, Group0927Station group)
        {
            Transform wrist = station.modelRoot.Find(WristPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LabOriginalEquipmentAssets.CellPath(0));
            Transform prototype = prefab ? prefab.transform.Find(WristPath + "/Pointer（换）") : null;
            if (!wrist || !prototype) throw new InvalidOperationException("Original CAD tracing probe is missing.");

            // Retain the earlier gripper and pen as inactive objects, while mounting
            // the complete original probe at its authored wrist connector.
            if (group.drawingGripTool) group.drawingGripTool.gameObject.SetActive(false);
            if (group.penRoot) group.penRoot.gameObject.SetActive(false);
            Transform tool = wrist.Find("TracingProbe_OriginalCAD");
            if (!tool)
            {
                tool = Object.Instantiate(prototype.gameObject, wrist, false).transform;
                tool.name = "TracingProbe_OriginalCAD";
                Undo.RegisterCreatedObjectUndo(tool.gameObject, "Install original CAD tracing probe");
            }
            tool.localPosition = prototype.localPosition;
            tool.localRotation = prototype.localRotation;
            tool.localScale = prototype.localScale;
            foreach (var behaviour in tool.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour) behaviour.enabled = false;
            foreach (var renderer in tool.GetComponentsInChildren<Renderer>(true))
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, 0);

            var needle = tool.GetComponentsInChildren<MeshFilter>(true).Single(m => m.name == "针尖-230314");
            if (!needle.sharedMesh) throw new InvalidOperationException("Original probe tip mesh is missing.");
            // The original needle mesh ends at its minimum local Y vertex. The TCP
            // is attached there, with its +Y along the real needle axis; no CAD
            // child is rotated, scaled or shifted to imitate a straight pen.
            var vertices = needle.sharedMesh.vertices;
            if (vertices.Length == 0) throw new InvalidOperationException("Original probe tip mesh is empty.");
            Vector3 tip = vertices.OrderBy(v => v.y).First();
            Transform tcp = needle.transform.Find("TracingProbeTCP");
            if (!tcp)
            {
                tcp = new GameObject("TracingProbeTCP").transform;
                tcp.SetParent(needle.transform, false);
                Undo.RegisterCreatedObjectUndo(tcp.gameObject, "Bind actual probe tip");
            }
            tcp.localPosition = tip;
            tcp.localRotation = Quaternion.identity;
            tcp.localScale = Vector3.one;
            group.drawingGripTool = tool;
            group.drawingTcp = tcp;
            group.penRoot = station.penRoot = needle.transform;
            needle.gameObject.SetActive(true);
            tool.gameObject.SetActive(false);
        }

        static void UpdateSign(int index)
        {
            var sign = GameObject.Find("StationName" + index);
            if (!sign) return;
            var original = sign.GetComponent<MeshRenderer>();
            if (original) original.enabled = false;
            var child = sign.transform.Find("DedicatedRoleLabel");
            if (!child)
            {
                child = new GameObject("DedicatedRoleLabel").transform;
                child.SetParent(sign.transform, false);
            }
            var text = child.GetComponent<TextMesh>();
            if (!text) text = child.gameObject.AddComponent<TextMesh>();
            text.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/RoboticsLab/Fonts/NotoSansSC-Regular.ttf");
            text.fontSize = 80;
            text.characterSize = .014f;
            text.anchor = TextAnchor.MiddleLeft;
            text.color = new[] { new Color(.04f,.48f,.62f), new Color(.65f,.34f,.06f), new Color(.42f,.28f,.7f) }[index];
            text.text = new[] { "循迹训练", "钢丝套圈", "自由画图" }[index];
            text.GetComponent<Renderer>().sharedMaterial = text.font.material;
            GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
        }
    }
}
