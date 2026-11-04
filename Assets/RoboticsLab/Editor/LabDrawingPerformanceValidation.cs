using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace RoboticsLab.Editor
{
    /// <summary>Repeatable scene benchmark and broad-phase regression for the drawing station.</summary>
    public static class LabDrawingPerformanceValidation
    {
        const float FrameTime = 1f / 30;
        const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        [Serializable] sealed class Phase
        {
            public string name;
            public int frames, blockedFrames;
            public double medianMs, p95Ms, maximumMs, averageSweepIntervals;
            public Vector3 finalToolPosition;
        }

        [Serializable] sealed class Report
        {
            public string unityVersion;
            public int movingParts, staticParts, strokes, vertices;
            public float inkMetres, fastFrameTravelMm, fastFrameEndErrorMm;
            public Phase[] phases;
        }

        // Run only in an isolated batch editor: the benchmark opens the competition scene
        // and exercises the live CAD rig without saving a scene or changing the game clock.
        public static void Run()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Run this benchmark with Unity -batchmode -quit.");
            CheckObstacles();
            EditorSceneManager.OpenScene("Assets/RoboticsLab/Scenes/RoboticsTrainingRoom.unity");
            var station = Object.FindObjectsOfType<Group0927Station>(true).Single(s => s.stationIndex == 2);
            station.BeginOperation();
            Require(station.IsOperating && !station.EmergencyStopped, "Drawing station must open");
            var type = typeof(Group0927Station);
            var motion = type.GetField("drawingMotion", PrivateInstance).GetValue(station);
            var tick = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), motion, "Tick");
            var ink = (LabDrawingExercise)type.GetField("drawing", PrivateInstance).GetValue(station);
            var collision = (LabDrawingCollision)type.GetField("drawingCollision", PrivateInstance).GetValue(station);
            var report = new Report { unityVersion = Application.unityVersion, movingParts = collision.ProxyCount,
                staticParts = collision.StaticProxyCount, phases = new Phase[5] };
            Action frame = () => { tick(FrameTime); ink.Tick(station.PenDown); };
            report.phases[0] = Measure("idle_air", 60, station, collision, frame, null);
            station.SetPen(true);
            report.phases[1] = Measure("lower", 30, station, collision, frame, null);
            Vector3 center = station.toolTarget.localPosition;
            report.phases[2] = Measure("contact_idle", 60, station, collision, frame, null);
            report.phases[3] = Measure("line", 120, station, collision, frame, i =>
                station.toolTarget.localPosition = center + Vector3.forward * Mathf.PingPong(i * .0012f, .04f));
            report.phases[4] = Measure("curve", 120, station, collision, frame, i =>
            {
                float a = i * Mathf.PI / 60;
                station.toolTarget.localPosition = center + new Vector3(.024f * Mathf.Sin(a), 0, .024f * (1 - Mathf.Cos(a)));
            });

            foreach (var line in station.GetComponentsInChildren<LineRenderer>().Where(l => l.name == "DrawingStroke"))
            {
                report.strokes++;
                report.vertices += line.positionCount;
                for (int i = 1; i < line.positionCount; i++)
                    report.inkMetres += Vector3.Distance(line.GetPosition(i - 1), line.GetPosition(i));
            }
            Require(report.phases.All(p => p.blockedFrames == 0) && report.strokes == 1 && report.vertices > 100,
                "Known drawing path must remain clear and produce a single continuous stroke");
            // With a 1/600 s tick the allowed translation is smaller than the authored IK
            // tolerance. The physical TCP must still traverse the requested 12 mm safely.
            station.SetPen(false);
            Vector3 fastStart = station.ActualToolPosition;
            station.toolTarget.position = fastStart + station.transform.forward * .012f;
            for (int i = 0; i < 150; i++) tick(1f / 600);
            report.fastFrameTravelMm = Vector3.Distance(fastStart, station.ActualToolPosition) * 1000;
            report.fastFrameEndErrorMm = station.PositionError * 1000;
            string path = Environment.GetEnvironmentVariable("LAB_DRAWING_BENCHMARK");
            if (string.IsNullOrEmpty(path)) path = Path.GetFullPath("Logs/DrawingBenchmark.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("Drawing benchmark saved to " + path);
            Require(!station.DrawingMovementBlocked && report.fastFrameTravelMm > 11.5f && report.fastFrameEndErrorMm < .5f,
                "Small high-rate increments must move the real TCP to its goal");
        }

        static Phase Measure(string name, int count, Group0927Station station, LabDrawingCollision collision,
            Action frame, Action<int> input)
        {
            var result = new Phase { name = name, frames = count };
            var clock = new Stopwatch();
            var samples = new double[count];
            for (int i = 0; i < count; i++)
            {
                input?.Invoke(i);
                clock.Restart();
                frame();
                clock.Stop();
                samples[i] = clock.Elapsed.TotalMilliseconds;
                if (station.DrawingMovementBlocked) result.blockedFrames++;
                result.averageSweepIntervals += collision.LastSweepIntervals;
            }
            Array.Sort(samples);
            result.medianMs = samples[(count - 1) / 2];
            result.p95Ms = samples[(int)((count - 1) * .95)];
            result.maximumMs = samples[count - 1];
            result.averageSweepIntervals /= count;
            result.finalToolPosition = station.ActualToolPosition;
            return result;
        }

        [MenuItem("Robot Lab/Diagnostics/Validate Drawing Obstacle Sweep")]
        public static void CheckObstacles()
        {
            var root = new GameObject("DrawingSweepFixture") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var station = root.AddComponent<Group0927Station>();
                station.stationIndex = 2;
                station.tableHeight = -2;
                station.modelRoot = new GameObject("Model").transform;
                station.modelRoot.SetParent(root.transform, false);
                var joint = new GameObject("Joint").transform;
                joint.SetParent(station.modelRoot, false);
                Cube(joint, "Moving", Vector3.right * .1f, .003f);
                var obstacle = Cube(station.modelRoot, "Obstacle", Vector3.right * .1f, .003f);
                for (int i = 0; i < 42; i++)
                    Cube(station.modelRoot, "Remote", new Vector3(i - 20, i + 1, -3), .03f);
                station.joints = new[] { new JointConfig { joint = joint, axis = JointAxis.Y, minAngle = -180, maxAngle = 180 } };
                var collision = new LabDrawingCollision(station);
                collision.RebuildCache();
                var start = new[] { Quaternion.Euler(0, -20, 0) };
                var end = Quaternion.Euler(0, 20, 0);

                joint.localRotation = start[0];
                Require(collision.Check(out _, out _), "Before endpoint is clear");
                joint.localRotation = end;
                Require(collision.Check(out _, out _), "After endpoint is clear");
                Require(!collision.CheckSweep(start, out string reason, out _) && reason.StartsWith("运动路径")
                    && collision.LastObstacleTransform == obstacle.transform, "Thin obstacle between clear endpoints is detected");
                Require(Quaternion.Angle(joint.localRotation, end) < .001f, "Rejected sweep restores the candidate pose");
                obstacle.GetComponent<Renderer>().enabled = false;
                Require(collision.CheckSweep(start, out _, out _), "Disabled renderer stops blocking");
                obstacle.GetComponent<Renderer>().enabled = true;
                Require(!collision.CheckSweep(start, out _, out _), "Re-enabled renderer blocks again");
                obstacle.transform.localPosition = Vector3.up;
                Require(collision.CheckSweep(start, out _, out _), "Moving an obstacle outside the sweep updates the index");
                obstacle.transform.localPosition = joint.rotation * (Vector3.right * .1f);
                Require(!collision.Check(out _, out _) && collision.LastObstacleTransform == obstacle.transform,
                    "Moving an obstacle onto the idle tool is detected");
                obstacle.SetActive(false);
                Require(collision.Check(out _, out _), "Hidden obstacle stops blocking");
                Debug.Log("Drawing obstacle sweep 9/9 passed");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static GameObject Cube(Transform parent, string name, Vector3 position, float size)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = Vector3.one * size;
            return obj;
        }

        static void Require(bool passed, string label)
        {
            if (!passed) throw new InvalidOperationException(label);
        }
    }
}
