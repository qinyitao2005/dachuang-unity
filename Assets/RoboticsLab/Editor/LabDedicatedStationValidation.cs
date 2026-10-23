using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoboticsLab.Editor
{
    /// <summary>Live runtime regression: never writes a joint, TCP, or requested target transform.</summary>
    public static class LabDedicatedStationValidation
    {
        const string ReportPath = "Documentation/DedicatedStationValidation.txt";
        static IEnumerator<int> routine;
        static readonly List<string> lines = new List<string>();
        static int lastFrame, passed, failed;
        static double started;
        public static bool Running => routine != null;
        public static string Phase { get; private set; } = "Idle";
        public static string Report => string.Join("\n", lines);

        [MenuItem("Robot Lab/Diagnostics/Validate Dedicated Stations")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode required.");
            if (Running) throw new InvalidOperationException("Dedicated station validation is already running.");
            lines.Clear(); passed = failed = 0; lastFrame = -1;
            started = EditorApplication.timeSinceStartup;
            lines.Add("Dedicated stations / actual runtime motion / " + DateTime.UtcNow.ToString("O"));
            lines.Add("Coverage and accuracy are measured from actual TCP samples; targets and joints are never teleported.");
            routine = Checks();
            EditorApplication.update += Tick;
        }

        public static void Cancel()
        {
            if (!Running) return;
            Check(false, "Validation cancelled before completion");
            Finish();
        }

        static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode ended during validation.");
                if (EditorApplication.timeSinceStartup - started > 480) throw new TimeoutException("Global validation wall timeout.");
                // Editor update may run repeatedly between player frames. Issue at most one input per frame.
                if (lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                if (routine != null && routine.MoveNext()) return;
            }
            catch (Exception ex) { Check(false, "Exception: " + ex); }
            Finish();
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            var finishing = routine; routine = null;
            try { finishing?.Dispose(); }
            catch (Exception ex) { Check(false, "Cleanup: " + ex.Message); }
            Phase = "Finished";
            lines.Add("RESULT passed=" + passed + " failed=" + failed + " wallSeconds=" + (EditorApplication.timeSinceStartup - started).ToString("F1"));
            Directory.CreateDirectory("Documentation");
            File.WriteAllText(ReportPath, Report, new System.Text.UTF8Encoding(false));
            Debug.Log("DEDICATED_STATION_VALIDATION\n" + Report);
        }

        static void Check(bool ok, string text)
        {
            if (ok) passed++; else failed++;
            lines.Add((ok ? "PASS " : "FAIL ") + text);
        }

        static IEnumerator<int> Checks()
        {
            var stations = Object.FindObjectsOfType<LabRobotStation>(true).OrderBy(s => s.stationIndex).ToArray();
            var app = Object.FindObjectOfType<LabTrainingApp>();
            bool appEnabled = app && app.enabled;
            if (app) app.enabled = false;
            LabTracingExercise tracing = null;
            try
            {
                Phase = "Fixed station roles";
                Check(stations.Length == 3, "Exactly three station facades");
                if (stations.Length != 3) yield break;
                foreach (var station in stations)
                {
                    station.EndOperation();
                    var group = station.GetComponent<Group0927Station>();
                    Check(group != null, "Station " + station.stationIndex + " uses the separated robot modules");
                    if (!group) yield break;
                    for (int requested = 0; requested <= 1; requested++)
                    {
                        station.SetMode(requested);
                        Check(station.TrainingMode == station.RequiredMode, "Station " + station.stationIndex + " facade rejects cross-role mode " + requested);
                        group.SetMode(requested);
                        Check(group.TrainingMode == station.RequiredMode, "Station " + station.stationIndex + " group rejects cross-role mode " + requested);
                    }
                }
                var traceStation = stations[0];
                var traceRig = traceStation.GetComponent<Group0927Station>();
                tracing = traceStation.GetComponent<LabTracingExercise>();
                Check(tracing != null && traceStation.IsTracing && stations[1].IsRing && stations[2].IsDrawing,
                    "Station identities are tracing / ring / free drawing");
                if (!tracing) yield break;
                var rigid = new RigidToolSnapshot(traceRig.DrawingToolRoot);
                int cadMeshes = traceRig.DrawingToolRoot ? traceRig.DrawingToolRoot.GetComponentsInChildren<MeshFilter>(true)
                    .Count(m => m.sharedMesh) : 0;
                Check(cadMeshes == 28, "Tracing uses the complete original Pointer CAD probe including its needle; meshes=" + cadMeshes);
                Check(ProbeBindingValid(traceStation, traceRig),
                    "TracingProbeTCP is parented to the original needle mesh and both station bindings use that needle");
                Phase = "Tracing initial pose";
                traceStation.BeginOperation();
                yield return 0;
                Check(traceStation.IsOperating && !traceStation.EmergencyStopped, "Tracing BeginOperation succeeds: " + traceStation.DrawingBlockReason);
                if (!traceStation.IsOperating) yield break;
                Check(traceRig.DrawingTiltDegrees <= 1.05f && traceRig.DrawingGripTiltDegrees <= 1.05f && ProbeTilt(traceRig) <= 1.05f,
                    "Initial probe TCP and original needle axis are vertical: " + Tilts(traceRig) + " needle=" + ProbeTilt(traceRig).ToString("F3"));
                Check(rigid.Unchanged(), "All original probe CAD local transforms remain unchanged after initial IK");
                tracing.SelectShape(0);
                tracing.StartAutomatic();
                Check(tracing.IsManual && !tracing.IsAutomatic, "J course remains a manual exercise");

                for (int shape = 1; shape <= 4; shape++)
                {
                    tracing.SelectShape(shape);
                    tracing.StartAutomatic();
                    Phase = "Automatic " + shape + ": " + tracing.CurrentShapeName;
                    Check(tracing.IsAutomatic, "Automatic start " + tracing.CurrentShapeName);
                    float simStart = Time.time, maximumPenTilt = 0, maximumGripTilt = 0, maximumNeedleTilt = 0, maxTargetError = 0;
                    float maximumPaperHeightError = 0;
                    double wallStart = EditorApplication.timeSinceStartup;
                    bool allRigid = true, noInk = NoInk(traceStation);
                    int sampledFrames = 0, contactFrames = 0;
                    while (tracing.IsAutomatic && Time.time - simStart < 60 && EditorApplication.timeSinceStartup - wallStart < 90)
                    {
                        yield return 0;
                        sampledFrames++;
                        maximumPenTilt = Mathf.Max(maximumPenTilt, traceRig.DrawingTiltDegrees);
                        maximumGripTilt = Mathf.Max(maximumGripTilt, traceRig.DrawingGripTiltDegrees);
                        maximumNeedleTilt = Mathf.Max(maximumNeedleTilt, ProbeTilt(traceRig));
                        maxTargetError = Mathf.Max(maxTargetError, traceStation.PositionError);
                        allRigid &= rigid.Unchanged();
                        noInk &= NoInk(traceStation);
                        if (traceStation.PenDown && tracing.Coverage > 0)
                        {
                            contactFrames++;
                            float y = traceStation.transform.InverseTransformPoint(traceStation.ActualToolPosition).y;
                            maximumPaperHeightError = Mathf.Max(maximumPaperHeightError, Mathf.Abs(y - traceStation.drawingHeight));
                        }
                    }
                    bool timedOut = tracing.IsAutomatic;
                    if (timedOut) tracing.StopAutomatic();
                    string measured = tracing.CurrentShapeName + " coverage=" + tracing.Coverage.ToString("F2") + "% accuracy=" + tracing.Accuracy.ToString("F2")
                        + "% score=" + tracing.Score.ToString("F2") + " meanErrorMm=" + tracing.MeanErrorMm.ToString("F3")
                        + " frames=" + sampledFrames + " elapsed=" + (Time.time - simStart).ToString("F1") + "s status=" + tracing.Status;
                    Check(!timedOut && tracing.Completed && tracing.Coverage >= 99, "Actual course completion " + measured);
                    Check(tracing.Accuracy >= 80 && tracing.Score >= 80, "Actual course quality " + measured);
                    Check(maximumPenTilt <= 1.05f && maximumGripTilt <= 1.05f && maximumNeedleTilt <= 1.05f,
                        "Original needle axis and probe TCP remain vertical throughout " + tracing.CurrentShapeName + " tcp=" + maximumPenTilt.ToString("F3") + " probe=" + maximumGripTilt.ToString("F3") + " needle=" + maximumNeedleTilt.ToString("F3"));
                    Check(allRigid, "No CAD deformation during " + tracing.CurrentShapeName);
                    Check(contactFrames > 10 && maximumPaperHeightError <= tracing.contactTolerance,
                        "Real paper contact " + tracing.CurrentShapeName + " frames=" + contactFrames + " maximumHeightMm=" + (maximumPaperHeightError * 1000).ToString("F3"));
                    Check(noInk && NoInk(traceStation), "Automatic tracing creates no DrawingStroke objects or ink: " + tracing.CurrentShapeName + " vertices=" + InkVertices(traceStation));
                    lines.Add("INFO " + tracing.CurrentShapeName + " max requested-target lagMm=" + (maxTargetError * 1000).ToString("F3"));
                }

                Phase = "Manual J / real input path";
                tracing.SelectShape(0);
                Vector2[] manualPath = tracing.GetPath(0);
                traceStation.SetPen(false);
                Vector3 currentPaper = traceStation.transform.InverseTransformPoint(traceStation.ActualToolPosition);
                Vector3 lifted = traceStation.transform.TransformPoint(new Vector3(currentPaper.x,
                    traceStation.drawingHeight + tracing.travelLift, currentPaper.z));
                foreach (int step in MoveTo(traceStation, lifted, 8)) yield return step;
                Vector3 manualStartAbove = tracing.PaperToWorld(manualPath[0], tracing.travelLift);
                foreach (int step in MoveTo(traceStation, manualStartAbove, 15)) yield return step;
                Check(Vector3.Distance(traceStation.ActualToolPosition, manualStartAbove) < .001f,
                    "Manual J reaches its actual start while pen is lifted");
                tracing.BeginManual();
                Vector3 manualStart = tracing.PaperToWorld(manualPath[0]);
                foreach (int step in MoveTo(traceStation, manualStart, 8)) yield return step;
                float manualStarted = Time.time, manualPenTilt = 0, manualGripTilt = 0, manualNeedleTilt = 0;
                double manualDeadline = EditorApplication.timeSinceStartup + 140;
                bool manualRigid = true, manualNoInk = NoInk(traceStation);
                int manualFrames = 0;
                for (int point = 0; point < manualPath.Length && tracing.AttemptActive &&
                    Time.time - manualStarted < 100 && EditorApplication.timeSinceStartup < manualDeadline; point++)
                {
                    Vector3 destination = tracing.PaperToWorld(manualPath[point]);
                    while (tracing.AttemptActive && Vector3.Distance(traceStation.ActualToolPosition, destination) > .0007f &&
                        Time.time - manualStarted < 100 && EditorApplication.timeSinceStartup < manualDeadline)
                    {
                        // Model the pendant's manual Cartesian input. The test has no access to scorer internals.
                        Vector3 requested = traceStation.toolTarget ? traceStation.toolTarget.position : traceStation.ActualToolPosition;
                        Vector3 velocity = Vector3.ClampMagnitude((destination - requested) / Mathf.Max(.0001f, Time.deltaTime), .05f);
                        traceStation.MoveTool(velocity / Mathf.Max(.0001f, traceStation.moveSpeed));
                        yield return 0;
                        manualFrames++;
                        manualPenTilt = Mathf.Max(manualPenTilt, traceRig.DrawingTiltDegrees);
                        manualGripTilt = Mathf.Max(manualGripTilt, traceRig.DrawingGripTiltDegrees);
                        manualNeedleTilt = Mathf.Max(manualNeedleTilt, ProbeTilt(traceRig));
                        manualRigid &= rigid.Unchanged();
                        manualNoInk &= NoInk(traceStation);
                    }
                }
                // Let LateUpdate score the final accepted tip pose before manual submission.
                yield return 0;
                if (tracing.AttemptActive) tracing.FinishAttempt();
                string manualMeasured = "J coverage=" + tracing.Coverage.ToString("F2") + "% accuracy=" + tracing.Accuracy.ToString("F2")
                    + "% score=" + tracing.Score.ToString("F2") + " meanErrorMm=" + tracing.MeanErrorMm.ToString("F3")
                    + " frames=" + manualFrames + " status=" + tracing.Status;
                Check(tracing.Completed && tracing.Coverage >= 99, "Actual manual course completion " + manualMeasured);
                Check(tracing.Accuracy >= 80 && tracing.Score >= 80, "Actual manual course quality " + manualMeasured);
                Check(manualRigid && manualPenTilt <= 1.05f && manualGripTilt <= 1.05f && manualNeedleTilt <= 1.05f,
                    "Manual J preserves rigid original probe CAD and vertical needle; tcp=" + manualPenTilt.ToString("F3") + " probe=" + manualGripTilt.ToString("F3") + " needle=" + manualNeedleTilt.ToString("F3"));
                Check(manualNoInk && NoInk(traceStation), "Manual J creates no DrawingStroke objects or ink; vertices=" + InkVertices(traceStation));

                Phase = "Emergency stop";
                tracing.SelectShape(1); tracing.StartAutomatic();
                foreach (int step in Wait(.25f)) yield return step;
                traceStation.SetEmergencyStop(true);
                var freeze = CaptureJoints(traceRig);
                Vector3 frozenTip = traceStation.ActualToolPosition;
                foreach (int step in Wait(.4f))
                {
                    traceStation.MoveTool(Vector3.one);
                    yield return step;
                }
                Check(!tracing.IsAutomatic && !tracing.AttemptActive && !traceStation.PenDown, "Emergency stop ends automatic attempt and lifts pen state");
                Check(JointsUnchanged(traceRig, freeze) && Vector3.Distance(frozenTip, traceStation.ActualToolPosition) < .00001f,
                    "Emergency stop rejects movement and holds actual joints/TCP");
                traceStation.SetEmergencyStop(false);
                yield return 0;
                Check(!tracing.IsAutomatic, "Emergency release never restarts a stopped program");
                tracing.StartAutomatic(); yield return 0;
                traceStation.EndOperation(); yield return 0;
                Check(!tracing.IsAutomatic && !tracing.AttemptActive && !traceStation.IsOperating, "Exiting the station stops automatic motion");

                Phase = "Ring station smoke test";
                var ring = stations[1];
                ring.BeginOperation(); yield return 0;
                Check(ring.IsOperating && ring.TrainingMode == 1 && ring.ringRoot && ring.ringRoot.gameObject.activeInHierarchy,
                    "Second station opens ring exercise");
                Check(!ring.drawingBoard || !ring.drawingBoard.gameObject.activeInHierarchy, "Second station has no drawing exercise visible");
                if (ring.IsOperating && ring.wirePoints != null && ring.wirePoints.Length > 1)
                {
                    Vector3 before = ring.ActualToolPosition;
                    Vector3 direction = ring.transform.TransformDirection((ring.wirePoints[1] - ring.wirePoints[0]).normalized);
                    foreach (int step in Wait(.25f)) { ring.MoveTool(direction * .2f); yield return step; }
                    Check(Vector3.Distance(before, ring.ActualToolPosition) > .0001f, "Ring accepts input through real IK; displacementMm=" + (Vector3.Distance(before, ring.ActualToolPosition) * 1000).ToString("F3"));
                }
                ring.EndOperation();

                Phase = "Free drawing smoke test";
                var draw = stations[2];
                draw.BeginOperation(); yield return 0;
                Check(draw.IsOperating && draw.TrainingMode == 0, "Third station opens free drawing: " + draw.DrawingBlockReason);
                Check(!draw.GetComponent<LabTracingExercise>(), "Third station remains free drawing without tracing score ownership");
                if (draw.IsOperating)
                {
                    draw.SetPen(true);
                    Vector3 start = draw.transform.TransformPoint(new Vector3(draw.exerciseCenter.x, draw.drawingHeight, draw.exerciseCenter.z));
                    foreach (int step in MoveTo(draw, start, 6)) yield return step;
                    Vector3 finish = start + draw.transform.TransformDirection(new Vector3(.025f, 0, 0));
                    foreach (int step in MoveTo(draw, finish, 6)) yield return step;
                    draw.SetPen(false); yield return 0;
                    var drawRig = draw.GetComponent<Group0927Station>();
                    float inkLength = InkLengthWorld(draw);
                    float remaining = Vector3.Distance(draw.ActualToolPosition, finish);
                    Check(InkVertices(draw) >= 2 && inkLength >= .020f && remaining <= .001f,
                        "Third station draws the requested 25 mm stroke via MoveTool; vertices=" + InkVertices(draw)
                        + " inkLengthMm=" + (inkLength * 1000).ToString("F3") + " endpointErrorMm=" + (remaining * 1000).ToString("F3"));
                    Check(drawRig.DrawingTiltDegrees <= 1.05f && drawRig.DrawingGripTiltDegrees <= 1.05f, "Third station preserves tool orientation: " + Tilts(drawRig));
                    draw.EndOperation();
                }
            }
            finally
            {
                if (tracing) tracing.StopAutomatic();
                foreach (var station in stations) if (station) station.EndOperation();
                if (app) app.enabled = appEnabled;
            }
        }

        static IEnumerable<int> Wait(float seconds)
        {
            float until = Time.time + seconds;
            double deadline = EditorApplication.timeSinceStartup + seconds + 5;
            while (Time.time < until && EditorApplication.timeSinceStartup < deadline) yield return 0;
        }

        static IEnumerable<int> MoveTo(LabRobotStation station, Vector3 destination, float limit)
        {
            float until = Time.time + limit;
            double deadline = EditorApplication.timeSinceStartup + limit + 5;
            while (station.IsOperating && Vector3.Distance(station.ActualToolPosition, destination) > .0007f
                && Time.time < until && EditorApplication.timeSinceStartup < deadline)
            {
                Vector3 requested = station.toolTarget ? station.toolTarget.position : station.ActualToolPosition;
                Vector3 velocity = Vector3.ClampMagnitude((destination - requested) / Mathf.Max(.0001f, Time.deltaTime), .02f);
                station.MoveTool(velocity / Mathf.Max(.0001f, station.moveSpeed));
                yield return 0;
            }
        }

        static bool ProbeBindingValid(LabRobotStation station, Group0927Station rig)
        {
            return rig && rig.DrawingToolRoot && rig.DrawingToolRoot.name == "TracingProbe_OriginalCAD"
                && rig.drawingGripTool == rig.DrawingToolRoot && rig.penRoot && rig.penRoot.name == "针尖-230314"
                && rig.penRoot.GetComponent<MeshFilter>() && rig.penRoot.IsChildOf(rig.DrawingToolRoot)
                && rig.drawingTcp && rig.drawingTcp.name == "TracingProbeTCP" && rig.drawingTcp.parent == rig.penRoot
                && station.penRoot == rig.penRoot;
        }
        static float ProbeTilt(Group0927Station rig) => rig.penRoot ? Vector3.Angle(rig.penRoot.up, Vector3.up) : 180;
        // Check inactive children too: hiding a stroke is not the same as generating no ink.
        static bool NoInk(LabRobotStation station) => !station.GetComponentsInChildren<Transform>(true)
            .Any(node => node.name == "DrawingStroke") && InkVertices(station) == 0;
        static int InkVertices(LabRobotStation station) => station.GetComponentsInChildren<LineRenderer>(true)
            .Where(line => line.name == "DrawingStroke").Sum(line => line.positionCount);
        static float InkLengthWorld(LabRobotStation station)
        {
            float length = 0;
            foreach (var line in station.GetComponentsInChildren<LineRenderer>(true))
            {
                if (line.name != "DrawingStroke") continue;
                for (int point = 1; point < line.positionCount; point++)
                {
                    Vector3 a = line.GetPosition(point - 1), b = line.GetPosition(point);
                    if (!line.useWorldSpace) { a = line.transform.TransformPoint(a); b = line.transform.TransformPoint(b); }
                    length += Vector3.Distance(a, b);
                }
            }
            return length;
        }
        static string Tilts(Group0927Station rig) => "pen=" + rig.DrawingTiltDegrees.ToString("F3") + " grip=" + rig.DrawingGripTiltDegrees.ToString("F3");
        static Quaternion[] CaptureJoints(Group0927Station rig) => rig.joints.Select(j => j != null && j.joint ? j.joint.localRotation : Quaternion.identity).ToArray();
        static bool JointsUnchanged(Group0927Station rig, Quaternion[] snapshot)
        {
            for (int i = 0; i < snapshot.Length; i++)
                if (rig.joints[i] != null && rig.joints[i].joint && !SameRotation(snapshot[i], rig.joints[i].joint.localRotation)) return false;
            return true;
        }
        static bool SameRotation(Quaternion a, Quaternion b)
        {
            float difference = Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z) + Mathf.Abs(a.w - b.w);
            float opposite = Mathf.Abs(a.x + b.x) + Mathf.Abs(a.y + b.y) + Mathf.Abs(a.z + b.z) + Mathf.Abs(a.w + b.w);
            return Mathf.Min(difference, opposite) < .00001f;
        }

        sealed class RigidToolSnapshot
        {
            readonly Transform[] nodes;
            readonly Vector3[] positions, scales;
            readonly Quaternion[] rotations;
            public RigidToolSnapshot(Transform root)
            {
                nodes = root ? root.GetComponentsInChildren<Transform>(true) : new Transform[0];
                positions = nodes.Select(t => t.localPosition).ToArray();
                scales = nodes.Select(t => t.localScale).ToArray();
                rotations = nodes.Select(t => t.localRotation).ToArray();
            }
            public bool Unchanged()
            {
                if (nodes.Length == 0) return false;
                for (int i = 0; i < nodes.Length; i++)
                    if (!nodes[i] || (nodes[i].localPosition - positions[i]).sqrMagnitude > 1e-12f
                        || (nodes[i].localScale - scales[i]).sqrMagnitude > 1e-12f || !SameRotation(nodes[i].localRotation, rotations[i])) return false;
                return true;
            }
        }
    }
}
