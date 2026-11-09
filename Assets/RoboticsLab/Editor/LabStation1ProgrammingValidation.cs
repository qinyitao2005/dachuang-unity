using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RoboticsLab.Editor
{
    /// <summary>Opt-in Play Mode acceptance using real button raycasts and the actual probe TCP.</summary>
    [InitializeOnLoad]
    public static class LabStation1ProgrammingValidation
    {
        const string Pending = "RoboticsLab.Station1ProgrammingValidation";
        [Serializable] sealed class Report
        {
            public bool passed;
            public string exception, unityVersion;
            public float returnErrorMm, maximumPathErrorMm, coverage, score;
            public int checks;
            public string debug;
            public List<string> results = new List<string>();
        }
        static Report report;
        static IEnumerator sequence;
        static LabTrainingApp app;
        static LabStation1Programming program;
        static LabRobotStation station;
        static float started;
        static int previousFrame;
        static bool finishing;
        static string validationProgramPath;

        static LabStation1ProgrammingValidation() { EditorApplication.update += Tick; }

        [MenuItem("Robot Lab/Diagnostics/Validate Station1 Programming")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before running this acceptance.");
            sequence = null; finishing = false; previousFrame = -1; validationProgramPath = null;
            EditorSceneManager.OpenScene("Assets/RoboticsLab/Scenes/RoboticsTrainingRoom.unity");
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Pending, false) || finishing || !EditorApplication.isPlaying) return;
            if (sequence == null)
            {
                report = new Report { unityVersion = Application.unityVersion };
                started = Time.realtimeSinceStartup;
                sequence = Flatten(Checks());
            }
            if (Time.frameCount == previousFrame) return;
            previousFrame = Time.frameCount;
            try
            {
                if (Time.realtimeSinceStartup - started > 150) throw new TimeoutException("Station1 programming acceptance exceeded 150 seconds.");
                if (!sequence.MoveNext()) Finish();
            }
            catch (Exception error) { report.exception = error.ToString(); Debug.LogException(error); Finish(); }
        }

        static IEnumerator Flatten(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Peek();
                if (!current.MoveNext()) { stack.Pop(); continue; }
                if (current.Current is IEnumerator nested) stack.Push(nested); else yield return current.Current;
            }
        }

        static IEnumerator Checks()
        {
            for (int i = 0; i < 12; i++) yield return null;
            app = UnityEngine.Object.FindObjectOfType<LabTrainingApp>();
            Require(app && app.stations.Length == 3, "Main scene has all three stations.");
            app.ClosePanel(); app.HelmetWorn = app.LessonCompleted = app.QuizPassed = true;
            Require(app.TryStartStation(0), "Ordinary entry opens Station1.");
            station = app.ActiveStation;
            var trace = station.GetComponent<LabTracingExercise>();
            trace.SelectShape(1);
            app.ToggleStation1Pendant();
            program = app.station1Pendant.Programming;
            Require(program && program.Executor.AllowTracing, "Separate tracing executor is bound.");
            Require(!app.pendantController.Executor.AllowTracing, "Stations2/3 executor keeps its original defaults.");
            for (int i = 0; i < 6; i++) yield return null;
            Require(app.Station1PendantVisible && app.TracingPaperVisible, "Pendant and paper view remain visible together.");
            Click("SELECT"); Click("F2"); Click("F1");
            validationProgramPath = LabPendantProgramStorage.PathFor(0, program.ProgramName);
            Require(!string.IsNullOrEmpty(program.ProgramName) && File.Exists(validationProgramPath),
                "SELECT CREATE creates a named, persisted Station1 program.");

            Click("F1");
            Require(program.Model.Points.Count == 0, "POINT without SHIFT opens choice instead of recording.");
            Click("PREV");
            Click("SHIFT_LEFT");
            Require(program.ShiftActive, "Mouse SHIFT latches a simulated held modifier.");
            var path = trace.GetPath(1);
            Vector3 start = station.transform.InverseTransformPoint(trace.PaperToWorld(path[0]));
            Vector3 safe = start + Vector3.up * trace.travelLift;
            yield return MoveTo(safe);
            Vector3 beforeJog = Actual();
            Pointer("J1+", true);
            for (int i = 0; i < 12; i++) yield return null;
            Pointer("J1+", false);
            Require(Actual().x > beforeJog.x + .0001f, "Held pendant +X moves the actual probe through the station adapter.");
            Require(program.ManualDirection().sqrMagnitude < .000001f, "Releasing the jog button clears its movement input.");
            yield return MoveTo(safe);
            Click("F1");
            Require(program.Model.Points.Count == 1 && Vector3.Distance(program.Model.FindPoint(1).LocalPosition, Actual()) < .0003f,
                "SHIFT+POINT records the real TCP as safe P1.");
            yield return MoveTo(start);
            Click("F1");
            Require(program.Model.Instructions.Count == 2, "L start is recorded independently of the safe point.");
            Require(!station.PenDown, "Teaching keeps the probe out of drawing/pen mode.");

            yield return MoveTo(station.transform.InverseTransformPoint(trace.PaperToWorld(path[path.Length / 4])));
            Click("SHIFT_LEFT"); Click("F1"); Click("DOWN"); Click("ENTER"); Click("SHIFT_LEFT");
            Require(program.Model.HasPendingArc, "C SHIFT+POINT records a via point, leaving an incomplete arc.");
            Click("FWD");
            Require(!program.IsBusy, "Incomplete C cannot execute.");
            yield return MoveTo(station.transform.InverseTransformPoint(trace.PaperToWorld(path[path.Length / 2])));
            Click("F5");
            Require(!program.Model.HasPendingArc && program.Model.Instructions.Count == 3, "SHIFT+TOUCHUP completes the same C instruction.");

            // Classroom sequence: safe -> half-circle -> same safe, without a separate lift point.
            Click("1"); Click("ENTER");
            Vector3 halfEnd = program.Model.FindPoint(program.Model.Instructions[2].TargetPoint).LocalPosition;
            Vector3 savedSafe = program.Model.FindPoint(1).LocalPosition;
            Require(halfEnd.y < savedSafe.y - .001f
                && new Vector2(halfEnd.x - savedSafe.x, halfEnd.z - savedSafe.z).magnitude > .001f,
                "Regression uses an endpoint lower than and horizontally separate from the safe point.");
            Require(program.Model.Instructions.Count == 4 && program.Model.Instructions[3].TargetPoint == 1,
                "Half-circle program ends by reusing P1 with no intermediate departure point.");
            for (int i = 0; i < 4; i++) Click("UP");
            Click("FWD");
            Require(program.IsBusy, "SHIFT+FWD accepts direct L return from the half-circle endpoint.");
            float halfTimeout = Time.realtimeSinceStartup + 35;
            while (program.Executor.State == LabTrajectoryState.Running && Time.realtimeSinceStartup < halfTimeout) yield return null;
            Require(program.Executor.State == LabTrajectoryState.Completed, "Half-circle and direct return complete: " + program.Executor.StatusText);
            Require(Vector3.Distance(Actual(), savedSafe) <= .0011f, "Real probe returns to the original P1 after the open half-circle.");
            Require(Vector3.Distance(program.Model.FindPoint(1).LocalPosition, savedSafe) < .000001f,
                "Return reuses the saved safe point without overwriting it.");
            Click("FCTN"); Click("DOWN"); Click("ENTER");

            yield return MoveTo(station.transform.InverseTransformPoint(trace.PaperToWorld(path[path.Length * 3 / 4])));
            Click("F1"); Click("2"); Click("ENTER");
            Require(!program.Model.HasPendingArc && program.Model.Instructions[3].TargetPoint == 2,
                "Second arc reuses the taught start P2 through point-number entry.");
            Click("1"); Click("ENTER");
            Require(program.Model.Instructions.Count == 5 && program.Model.Instructions[4].TargetPoint == 1,
                "Final L references the original safe P1.");
            Require(program.Screen.ProgramRows.text.Contains("C P[") && program.Screen.ProgramRows.text.Contains("5:L P[1]"),
                "LCD shows the executable L/C document and final return.");
            Click("SHIFT_LEFT"); Click("FWD");
            Require(!program.IsBusy, "FWD without SHIFT does not start motion.");
            for (int i = 0; i < 5; i++) Click("UP");
            Click("SHIFT_LEFT"); Click("STEP"); Click("FWD");
            yield return WaitForPause();
            report.debug = "actual=" + Actual().ToString("F6") + "; target=" + station.toolTarget.localPosition.ToString("F6")
                + "; safe=" + program.Model.FindPoint(1).LocalPosition.ToString("F6") + "; distance=" + program.Executor.CurrentPathDistance
                + "; error=" + program.Executor.MaximumPathError + "; pen=" + station.PenDown;
            Require(program.Executor.IsPaused && program.Executor.CurrentInstructionIndex == 1, "STEP executes exactly the first complete instruction. State="
                + program.Executor.State + ", row=" + program.Executor.CurrentInstructionIndex + ", status=" + program.Executor.StatusText + ", message=" + program.Message);
            Require(Vector3.Distance(Actual(), safe) < .0011f, "Real probe reaches the safe point in the first step.");
            int count = program.Model.Points.Count;
            Click("F1");
            Require(program.Model.Points.Count == count, "Paused execution cannot be edited.");
            Click("STEP"); Click("FWD");
            for (int i = 0; i < 8; i++) yield return null;
            Click("SHIFT_LEFT");
            Require(program.Executor.IsPaused, "Releasing latched SHIFT pauses the running program.");
            Click("SHIFT_LEFT"); Click("FWD");
            for (int i = 0; i < 8; i++) yield return null;
            Click("HOLD");
            Require(program.Executor.IsPaused, "HOLD pauses the active path.");
            Vector3 paused = station.toolTarget.localPosition;
            for (int i = 0; i < 8; i++) yield return null;
            Require(Vector3.Distance(paused, station.toolTarget.localPosition) < .00003f, "Paused target remains stationary.");
            Click("FWD");
            for (int i = 0; i < 8; i++) yield return null;
            Click("EMERGENCY_STOP");
            Require(station.EmergencyStopped && program.Executor.IsPaused, "Pendant emergency control pauses execution.");
            Click("EMERGENCY_STOP");
            Require(!station.EmergencyStopped && program.Executor.IsPaused, "Clearing emergency state does not auto-resume.");
            Click("RESET");
            Click("FWD");
            float timeout = Time.realtimeSinceStartup + 35;
            while (program.Executor.State == LabTrajectoryState.Running && Time.realtimeSinceStartup < timeout) yield return null;
            Require(program.Executor.State == LabTrajectoryState.Completed, "Program completes: " + program.Executor.StatusText);
            report.returnErrorMm = Vector3.Distance(Actual(), program.Model.FindPoint(1).LocalPosition) * 1000;
            report.maximumPathErrorMm = program.Executor.MaximumPathError * 1000;
            report.coverage = trace.Coverage; report.score = trace.Score;
            Require(report.returnErrorMm <= 1.1f, "Actual TCP returns to the taught safe point; error mm=" + report.returnErrorMm);
            Require(report.maximumPathErrorMm < 2.5f && report.coverage > 95, "Actual two-arc circle follows the printed course; coverage=" + report.coverage);
            int ink = 0;
            foreach (var line in station.GetComponentsInChildren<LineRenderer>(true)) if (line.name.StartsWith("DrawingStroke")) ink++;
            Require(ink == 0 && !station.PenDown, "No new drawing strokes or pen state are produced.");

            Click("FWD");
            Require(program.IsBusy, "Completed program can explicitly restart.");
            Click("RESET");
            Require(program.IsBusy, "RESET does not replace ABORT or terminate a running program.");
            Click("FCTN"); Click("ENTER");
            Require(!program.IsBusy && program.Model.Instructions.Count == 5, "ABORT terminates motion while preserving taught instructions.");
            Click("FWD");
            app.ToggleStation1Pendant();
            Require(!program.IsBusy && !app.Station1PendantVisible && app.TracingPaperVisible, "Closing the pendant stops execution and restores ordinary layout.");
            Require(program.Model.Instructions.Count == 5, "Closing keeps the taught program.");
            Require(app.TryStartStation(1), "Station2 remains enterable after Station1 programming.");
            app.ToggleTeachingPendant();
            Require(app.TeachingPendantVisible && !program.IsBusy, "Shared Station2 pendant is available after Station1.");
            Require(app.TryStartStation(2), "Station3 remains enterable after Station1 programming.");
            app.ToggleTeachingPendant();
            Require(app.TeachingPendantVisible && app.ActiveTeachingPendant.Programming.Model.Points.Count == 0,
                "Shared Station3 pendant remains independent of the Station1 program.");
            Require(app.TryStartStation(0), "Can return to Station1 for a new teaching attempt.");
            station = app.ActiveStation; app.ToggleStation1Pendant();
            for (int i = 0; i < 3; i++) yield return null;
            for (int i = 0; i < 5; i++) { Click("FCTN"); Click("DOWN"); Click("ENTER"); }
            Require(program.Model.Points.Count == 0 && program.Model.NextPointNumber == 1,
                "Deleting every instruction resets the next safe point to P1.");
            Click("SHIFT_LEFT"); yield return MoveTo(safe); Click("F1");
            Require(program.Model.FindPoint(1) != null, "New teaching after deleting all rows records a valid safe P1.");
            app.ClosePanel();
        }

        static IEnumerator MoveTo(Vector3 point)
        {
            float until = Time.realtimeSinceStartup + 15;
            while (Vector3.Distance(Actual(), point) > .0003f && Time.realtimeSinceStartup < until)
            {
                yield return null;
                Vector3 delta = point - station.toolTarget.localPosition;
                station.MoveTool(station.transform.TransformDirection(Vector3.ClampMagnitude(delta / Mathf.Max(.00001f, Time.deltaTime * station.moveSpeed), 1)));
            }
            Require(Vector3.Distance(Actual(), point) <= .0004f, "Teaching approach reaches requested probe point: " + (Vector3.Distance(Actual(), point) * 1000).ToString("F3") + "mm");
            station.MoveTool(Vector3.zero);
            yield return null;
        }

        static IEnumerator WaitForPause()
        {
            float until = Time.realtimeSinceStartup + 15;
            while (program.Executor.State == LabTrajectoryState.Running && Time.realtimeSinceStartup < until) yield return null;
        }

        static Vector3 Actual() => station.transform.InverseTransformPoint(station.ActualToolPosition);
        static void Pointer(string key, bool down)
        {
            var button = app.station1Pendant.Buttons[key];
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            if (down) ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            else ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
        }
        static void Click(string key)
        {
            var button = app.station1Pendant.Buttons[key];
            Canvas.ForceUpdateCanvases();
            var rect = button.GetComponent<RectTransform>();
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Require(hits.Count > 0 && hits[0].gameObject == button.gameObject, "Button raycast hits " + key
                + "; top=" + (hits.Count > 0 ? hits[0].gameObject.name : "none"));
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        static void Require(bool condition, string message)
        {
            report.checks++; report.results.Add((condition ? "PASS " : "FAIL ") + message);
            if (!condition) throw new InvalidOperationException(message);
        }

        static void Finish()
        {
            finishing = true; SessionState.SetBool(Pending, false);
            // Only the uniquely created test program is cleaned up. User programs are never overwritten.
            if (program) program.Deactivate();
            if (!string.IsNullOrEmpty(validationProgramPath) && File.Exists(validationProgramPath)) File.Delete(validationProgramPath);
            report.passed = string.IsNullOrEmpty(report.exception);
            Directory.CreateDirectory("Documentation");
            File.WriteAllText("Documentation/Station1ProgrammingValidation.json", JsonUtility.ToJson(report, true));
            Debug.Log("STATION1_PROGRAMMING_VALIDATION=" + report.passed);
            if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
