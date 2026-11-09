using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoboticsLab.Editor
{
    /// <summary>Opt-in UI and real-motion acceptance for the shared teaching workflow.</summary>
    [InitializeOnLoad]
    public static class LabAllStationPendantValidation
    {
        const string Pending = "RoboticsLab.AllStationPendantValidation";
        [Serializable] sealed class Report
        {
            public bool passed;
            public string exception, unityVersion;
            public int checks, drawingInkVertices;
            public float drawingReturnErrorMm, arcViaErrorMm, ringArrivalErrorMm, ringMaximumDeviationMm;
            public List<string> results = new List<string>();
            public List<string> screenshots = new List<string>();
        }
        static Report report;
        static IEnumerator sequence;
        static LabTrainingApp app;
        static LabStation1PendantView view;
        static LabStation1Programming program;
        static LabRobotStation station;
        static float started;
        static int previousFrame;
        static bool finishing;
        static readonly List<string> createdPaths = new List<string>();
        static readonly List<LabStation1Programming> programs = new List<LabStation1Programming>();

        static LabAllStationPendantValidation() { EditorApplication.update += Tick; }

        [MenuItem("Robot Lab/Diagnostics/Validate All Station Pendant Integration")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before acceptance.");
            sequence = null; finishing = false; previousFrame = -1; createdPaths.Clear(); programs.Clear();
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
                started = Time.realtimeSinceStartup; sequence = Flatten(Checks());
            }
            if (previousFrame == Time.frameCount) return;
            previousFrame = Time.frameCount;
            try
            {
                if (Time.realtimeSinceStartup - started > 300) throw new TimeoutException("Shared pendant acceptance exceeded 300 seconds.");
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
            for (int i = 0; i < 15; i++) yield return null;
            app = UnityEngine.Object.FindObjectOfType<LabTrainingApp>();
            Require(app && app.stations.Length == 3, "The training scene contains all three original workcells.");
            app.ClosePanel(); app.HelmetWorn = app.LessonCompleted = app.QuizPassed = true;
            yield return Open(2);
            CreateProgram();
            string drawingName = program.ProgramName;
            var drawingProgram = program;
            Require(!program.Executor.AllowTracing, "Drawing executor does not enable tracing-only rules.");

            Vector3 origin = Actual();
            Pointer("J1+", true);
            for (int i = 0; i < 8; i++) yield return null;
            Pointer("J1+", false);
            Require(Vector3.Distance(origin, Actual()) < .0003f, "Jog without SHIFT cannot move the tool.");
            Click("SHIFT_LEFT"); Pointer("J1+", true);
            for (int i = 0; i < 12; i++) yield return null;
            Pointer("J1+", false);
            Require(Actual().x > origin.x + .00005f, "SHIFT with held +X moves the actual drawing tool.");
            Require(program.ManualDirection().sqrMagnitude < .000001f, "Pointer release clears manual jog.");
            Click("F1");
            Require(program.Model.Points.Count == 1 && Vector3.Distance(program.Model.FindPoint(1).LocalPosition, Actual()) < .0003f,
                "SHIFT POINT records the actual drawing TCP.");
            Vector3 first = program.Model.FindPoint(1).LocalPosition;
            Vector3 lineEnd = first + Vector3.forward * .008f;
            yield return MoveTo(lineEnd); Click("F1");
            Vector3 via = lineEnd + new Vector3(.008f, 0, .008f);
            Vector3 end = lineEnd + Vector3.right * .016f;
            yield return MoveTo(via);
            Click("SHIFT_LEFT"); Click("F1"); Click("DOWN"); Click("ENTER"); Click("SHIFT_LEFT");
            Require(program.Model.HasPendingArc, "POINT C records a via point and awaits its endpoint.");
            Click("FWD"); Require(!program.IsBusy, "An unfinished C is rejected before motion.");
            yield return MoveTo(end); Click("F5");
            Require(!program.Model.HasPendingArc && program.Model.Instructions.Count == 3, "TOUCHUP completes the same C instruction.");
            Click("UP"); Click("LEFT");
            Vector3 oldEnd = program.Model.FindPoint(program.Model.Instructions[2].TargetPoint).LocalPosition;
            via += Vector3.forward * .0004f;
            yield return MoveTo(via); Click("F5");
            Require(Vector3.Distance(program.Model.FindPoint(program.Model.Instructions[2].ViaPoint).LocalPosition, Actual()) < .0003f
                && Vector3.Distance(program.Model.FindPoint(program.Model.Instructions[2].TargetPoint).LocalPosition, oldEnd) < .000001f,
                "LEFT plus TOUCHUP updates only the selected C via point.");
            Click("DOWN"); Click("1"); Click("ENTER");
            Require(program.Model.Instructions.Count == 4 && program.Model.Instructions[3].TargetPoint == 1,
                "End number entry appends L referencing the original P1.");
            foreach (var row in program.Model.Instructions) Require(Mathf.Abs(row.SpeedMm - 20) < .0001f && !row.PenDown,
                "Raised teaching retains fixed 20 mm/sec and pen-up state.");
            Click("OVERRIDE_PLUS"); Click("OVERRIDE_MINUS");
            Require(program.Model.Instructions[0].SpeedMm == 20, "Feed override keys cannot change the laboratory speed.");
            Click("SHIFT_LEFT"); FirstRow(); Click("FWD");
            Require(!program.IsBusy, "FWD without SHIFT cannot start execution.");
            Click("SHIFT_LEFT"); Click("STEP"); Click("FWD");
            yield return UntilStopped(18);
            Require(program.Executor.IsPaused && program.Executor.CurrentInstructionIndex == 1, "STEP executes one complete instruction then pauses.");
            int beforeEdit = program.Model.Points.Count;
            Click("F1"); Pointer("J1+", true);
            Require(program.Model.Points.Count == beforeEdit && program.ManualDirection().sqrMagnitude < .000001f,
                "Paused execution rejects recording and manual jog.");
            Pointer("J1+", false);
            Click("RESET"); Require(program.Executor.IsPaused, "RESET retains a normal HOLD instead of aborting.");
            Click("STEP"); Click("FWD");
            for (int i = 0; i < 4; i++) yield return null;
            Click("HOLD"); Require(program.Executor.IsPaused, "HOLD pauses an in-flight instruction.");
            Vector3 stopped = station.toolTarget.localPosition;
            for (int i = 0; i < 6; i++) yield return null;
            Require(Vector3.Distance(stopped, station.toolTarget.localPosition) < .00002f, "Paused target remains stationary.");
            Click("FWD"); Click("EMERGENCY_STOP");
            Require(station.EmergencyStopped && program.Executor.IsPaused, "Emergency control stops execution.");
            Click("EMERGENCY_STOP"); Click("FWD");
            Require(!station.EmergencyStopped && program.Executor.IsPaused, "Cleared emergency still requires RESET.");
            Click("RESET"); Click("FWD");
            report.arcViaErrorMm = float.MaxValue;
            float until = Time.realtimeSinceStartup + 25;
            Vector3 taughtVia = program.Model.FindPoint(program.Model.Instructions[2].ViaPoint).LocalPosition;
            while (program.Executor.State == LabTrajectoryState.Running && Time.realtimeSinceStartup < until)
            {
                yield return null;
                if (program.Executor.CurrentInstructionIndex == 2)
                    report.arcViaErrorMm = Mathf.Min(report.arcViaErrorMm, Vector3.Distance(Actual(), taughtVia) * 1000);
            }
            Require(program.Executor.State == LabTrajectoryState.Completed, "Raised L/C program completes: " + program.Executor.StatusText);
            report.drawingReturnErrorMm = Vector3.Distance(Actual(), first) * 1000;
            Require(report.drawingReturnErrorMm <= 1.1f && report.arcViaErrorMm <= 1.2f,
                "Real TCP passes the C via point and returns to saved P1; errors mm=" + report.arcViaErrorMm + "/" + report.drawingReturnErrorMm);
            Require(InkVertices() == 0, "Pen-up L/C execution leaves no drawing strokes.");
            string raisedContent = ProgramContent(program.Model);
            Click("SELECT"); Click("ENTER");
            Require(program.ProgramName == drawingName && ProgramContent(program.Model) == raisedContent,
                "SELECT opens the automatically saved program unchanged.");
            string savedFile = File.ReadAllText(LabPendantProgramStorage.PathFor(2, drawingName));
            Click("SELECT"); Click("F2");
            var duplicateInput = NameInput(); duplicateInput.text = drawingName;
            Require(program.TextEntryFocused, "Duplicate-name check is on the CREATE input page.");
            if (!program.ShiftActive) Click("SHIFT_LEFT");
            Vector3 beforeTyping = station.toolTarget.localPosition;
            Pointer("J1+", true);
            for (int i = 0; i < 6; i++) yield return null;
            Require(program.ManualDirection().sqrMagnitude < .000001f
                && Vector3.Distance(beforeTyping, station.toolTarget.localPosition) < .00002f,
                "CREATE owns input: even latched SHIFT and a held jog cannot move the station.");
            Pointer("J1+", false); Click("F1");
            Require(program.TextEntryFocused && program.ProgramName == drawingName && ProgramContent(program.Model) == raisedContent
                && File.ReadAllText(LabPendantProgramStorage.PathFor(2, drawingName)) == savedFile,
                "CREATE rejects a duplicate name without changing the document or existing file.");
            Click("PREV");
            FirstRow(); Click("FWD"); Click("RESET");
            Require(program.IsBusy, "RESET does not abort normal execution.");
            Abort(); Require(!program.IsBusy && program.Model.Instructions.Count == 4, "FCTN ABORT releases motion and preserves rows.");
            yield return Capture("SharedPendant_Station03Raised.png");

            // Verify row conversion on a separate temporary document, preserving the executed example.
            CreateProgram(); Click("F1");
            Vector3 convertVia = Actual() + Vector3.right * .003f;
            yield return MoveTo(convertVia); Click("F1"); Click("UP"); Click("F4"); Click("DOWN"); Click("ENTER");
            Require(program.Model.HasPendingArc && program.Model.Instructions.Count == 2
                && program.Model.Instructions[1].ViaPoint == 2, "F4 CHOICE converts the last L endpoint into a C via point.");
            yield return MoveTo(convertVia + Vector3.forward * .003f); Click("F5");
            int convertedEnd = program.Model.Instructions[1].TargetPoint;
            Click("UP"); Click("F4"); Click("UP"); Click("ENTER");
            Require(program.Model.Instructions.Count == 3 && program.Model.Instructions[1].Type == LabPendantInstructionType.Linear
                && program.Model.Instructions[1].TargetPoint == 2 && program.Model.Instructions[2].Type == LabPendantInstructionType.Linear
                && program.Model.Instructions[2].TargetPoint == convertedEnd,
                "F4 converts C back to two L rows while retaining both point references.");

            // Teach paper-contact ink with the same button workflow; no transforms are teleported.
            station.ResetExercise();
            CreateProgram();
            Vector3 paperA = station.exerciseCenter; paperA.y = station.drawingHeight;
            Vector3 paperB = paperA + Vector3.right * .02f;
            Vector3 paperVia = paperB + new Vector3(.01f, 0, .01f);
            Vector3 paperEnd = paperB + Vector3.right * .02f;
            yield return MoveTo(paperA);
            if (!program.ShiftActive) Click("SHIFT_LEFT");
            Click("F1"); Click("IO");
            Require(station.PenDown, "Drawing IO enables pen-down teaching.");
            yield return MoveTo(paperB); Click("F1");
            yield return MoveTo(paperVia);
            Click("SHIFT_LEFT"); Click("F1"); Click("DOWN"); Click("ENTER"); Click("SHIFT_LEFT");
            yield return MoveTo(paperEnd); Click("F5"); Click("IO"); Click("1"); Click("ENTER");
            Require(program.Model.Instructions.Count == 4 && !program.Model.Instructions[0].PenDown
                && program.Model.Instructions[1].PenDown && program.Model.Instructions[2].PenDown && !program.Model.Instructions[3].PenDown,
                "Recorded L and C preserve pen-down while approach and return remain pen-up.");
            station.ClearDrawing(); yield return null;
            FirstRow(); Click("FWD");
            int returnInk = -1;
            until = Time.realtimeSinceStartup + 25;
            while (program.Executor.State == LabTrajectoryState.Running && Time.realtimeSinceStartup < until)
            {
                yield return null;
                if (program.Executor.CurrentInstructionIndex == 3 && returnInk < 0) returnInk = InkVertices();
                else if (returnInk >= 0) Require(InkVertices() == returnInk, "Pen-up return does not add ink vertices.");
            }
            Require(program.Executor.State == LabTrajectoryState.Completed, "Paper L/C run completes: " + program.Executor.StatusText);
            report.drawingInkVertices = InkVertices();
            Require(report.drawingInkVertices >= 8 && returnInk >= 8 && !station.PenDown, "Paper line and arc create visible ink; return lifts the pen.");
            string paperName = program.ProgramName, paperContent = ProgramContent(program.Model);
            yield return Capture("SharedPendant_Station03Paper.png");

            // The ring uses its own storage and TCP, and never inherits paper or P1-return restrictions.
            yield return Open(1); CreateProgram();
            Require(!ReferenceEquals(program, drawingProgram) && program.Model.Points.Count == 0, "Station02 owns a separate controller document.");
            Require(!File.Exists(LabPendantProgramStorage.PathFor(1, paperName)), "Drawing program does not leak into Station02 storage.");
            Require(!program.Executor.AllowTracing, "Ring executor keeps steel-guide motion checks.");
            Require(station.wirePoints != null && station.wirePoints.Length > 1, "Ring has its authored wire path.");
            Vector3 wireStart = station.wirePoints[0], wireEnd = station.wirePoints[1];
            Vector3 dir = (wireEnd - wireStart).normalized;
            Vector3 legalEnd = WirePointAt(.0025f);
            Require(Vector3.Distance(wireStart, legalEnd) > .0024f, "First 2.5 mm of authored wire permits a short legal motion.");
            for (int i = 0; i <= 10; i++) Require(WireDistance(Vector3.Lerp(wireStart, legalEnd, i / 10f)) < .0001f,
                "The short linear test stays on the authored wire polyline.");
            Click("SHIFT_LEFT"); Click("F1");
            Require(program.Model.Points.Count == 1, "Ring records its real initial position without a paper-safe-height rule.");
            yield return MoveTo(legalEnd, .0003f); Click("F1");
            Require(program.Model.Instructions.Count == 2, "Ring teaching creates two L rows without forcing a return to P1.");
            Vector3 taughtEnd = program.Model.FindPoint(2).LocalPosition;
            FirstRow(); Click("FWD");
            until = Time.realtimeSinceStartup + 12;
            bool blocked = false;
            while (program.Executor.State == LabTrajectoryState.Running && Time.realtimeSinceStartup < until)
            {
                yield return null;
                report.ringMaximumDeviationMm = Mathf.Max(report.ringMaximumDeviationMm, WireDistance(Actual()) * 1000);
                blocked |= station.RingMovementBlocked || station.RobotWireBlocked;
            }
            report.ringArrivalErrorMm = Vector3.Distance(Actual(), taughtEnd) * 1000;
            Require(program.Executor.State == LabTrajectoryState.Completed && report.ringArrivalErrorMm <= .4f
                && report.ringMaximumDeviationMm <= .4f && !blocked,
                "Ring UI replay reaches the taught endpoint through collision checks; error mm=" + report.ringArrivalErrorMm);
            Click("IO"); Require(!station.PenDown, "Ring ignores drawing-only IO pen actions.");
            yield return Capture("SharedPendant_Station02.png");

            // External loaded files may ask for an illegal position; FWD must still reject it.
            Abort(); program.Model.Clear();
            Vector3 beyond = wireStart - dir * .01f;
            Require(program.Model.AddLinear(beyond, 20), "Prepare an unsafe imported L outside the wire endpoint.");
            FirstRow(); Click("FWD"); yield return UntilStopped(12);
            Require(program.Executor.State == LabTrajectoryState.Faulted && Vector3.Distance(Actual(), beyond) > .005f,
                "Shared pendant cannot bypass wire endpoint/collision protection: " + program.Executor.LastError);
            Click("RESET"); Require(!program.IsBusy, "RESET clears a fault after the obstacle condition is acknowledged.");

            yield return Open(2);
            Require(ReferenceEquals(program, drawingProgram) && program.ProgramName == paperName && ProgramContent(program.Model) == paperContent,
                "Returning to Station03 restores its own pen-state document unchanged.");
            FirstRow(); Click("SHIFT_LEFT"); Click("FWD");
            for (int i = 0; i < 3; i++) yield return null;
            var activeExecutor = program.Executor;
            yield return Open(0);
            Require(!activeExecutor.IsExecuting && program.Executor.AllowTracing, "Switching stations stops the former owner and keeps Station01 tracing policy.");
            CreateProgram(); Click("IO");
            Require(!station.PenDown && InkVertices() == 0, "Station01 remains a non-marking tracing probe.");
            Require(!File.Exists(LabPendantProgramStorage.PathFor(0, paperName)), "Station01 storage is isolated from the drawing program.");
            yield return Capture("SharedPendant_Station01.png");
            app.ClosePanel();
        }

        static IEnumerator Open(int index)
        {
            Require(app.TryStartStation(index), "Enter Station" + (index + 1) + " through ordinary app entry.");
            station = app.ActiveStation;
            app.ToggleTeachingPendant();
            for (int i = 0; i < 8; i++) yield return null;
            view = app.ActiveTeachingPendant; program = view ? view.Programming : null;
            Require(view && view.Visible && program && app.TeachingPendantVisible, "Shared pendant is visible for Station" + (index + 1));
            Require(program.Executor.Station == station && ReferenceEquals(program.Executor.Model, program.Model), "LCD document binds the current station executor.");
            if (!programs.Contains(program)) programs.Add(program);
        }
        static void CreateProgram()
        {
            if (program.IsBusy) Abort();
            Click("SELECT"); Click("F2");
            Require(program.TextEntryFocused, "CREATE owns keyboard focus.");
            var input = NameInput();
            Require(input, "CREATE exposes its name input.");
            string name = "QA_" + Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant();
            input.text = name; Click("F1");
            string path = LabPendantProgramStorage.PathFor(station.stationIndex, name);
            createdPaths.Add(path);
            Require(program.ProgramName == name && File.Exists(path) && !program.TextEntryFocused, "F1 creates a named program in its station directory.");
        }
        static InputField NameInput() => program.Screen.Header.transform.parent.GetComponentInChildren<InputField>(true);
        static string ProgramContent(LabPendantProgramModel model)
        {
            // Runtime status text legitimately changes to "loaded"; only persisted teaching data is compared.
            var result = new StringBuilder(); result.Append(model.NextPointNumber).Append('|');
            foreach (var point in model.Points)
                result.Append(point.Number).Append(':').Append(point.LocalPosition.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(point.LocalPosition.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(point.LocalPosition.z.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            result.Append('|');
            foreach (var row in model.Instructions)
                result.Append((int)row.Type).Append(',').Append(row.ViaPoint).Append(',').Append(row.TargetPoint).Append(',')
                    .Append(row.SpeedMm.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(row.PenDown).Append(';');
            return result.ToString();
        }
        static void FirstRow() { for (int i = 0; i <= program.Model.Instructions.Count; i++) Click("UP"); }
        static void Abort() { Click("FCTN"); Click("1"); Click("ENTER"); }
        static IEnumerator MoveTo(Vector3 target, float tolerance = .0004f)
        {
            float until = Time.realtimeSinceStartup + 18;
            while (Vector3.Distance(Actual(), target) > tolerance * .7f && Time.realtimeSinceStartup < until)
            {
                yield return null;
                Vector3 delta = target - station.toolTarget.localPosition;
                station.MoveTool(station.transform.TransformDirection(Vector3.ClampMagnitude(delta / Mathf.Max(.00001f, Time.deltaTime * station.moveSpeed), 1)));
            }
            station.MoveTool(Vector3.zero); yield return null;
            Require(Vector3.Distance(Actual(), target) <= tolerance,
                "Teaching motion reaches requested real TCP; error mm=" + (Vector3.Distance(Actual(), target) * 1000).ToString("F3") + "; " + station.DrawingBlockReason);
        }
        static IEnumerator UntilStopped(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (program.Executor.State == LabTrajectoryState.Running && Time.realtimeSinceStartup < until) yield return null;
            Require(program.Executor.State != LabTrajectoryState.Running, "Execution finishes or pauses within the bounded timeout.");
        }
        static IEnumerator Capture(string name)
        {
            string directory = "Documentation/Screenshots"; Directory.CreateDirectory(directory);
            string path = directory + "/" + name;
            ScreenCapture.CaptureScreenshot(path);
            for (int i = 0; i < 6; i++) yield return null;
            if (File.Exists(path)) report.screenshots.Add(path);
        }
        static int InkVertices()
        {
            int count = 0;
            foreach (var line in station.GetComponentsInChildren<LineRenderer>(true))
                if (line.name.StartsWith("DrawingStroke", StringComparison.Ordinal)) count += line.positionCount;
            return count;
        }
        static Vector3 Actual() => station.transform.InverseTransformPoint(station.ActualToolPosition);
        static float SegmentDistance(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 d = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, d) / Mathf.Max(.0000000001f, d.sqrMagnitude));
            return Vector3.Distance(p, a + t * d);
        }
        static float WireDistance(Vector3 point)
        {
            float best = float.PositiveInfinity;
            for (int i = 1; i < station.wirePoints.Length; i++) best = Mathf.Min(best,
                SegmentDistance(station.wirePoints[i - 1], station.wirePoints[i], point));
            return best;
        }
        static Vector3 WirePointAt(float distance)
        {
            for (int i = 1; i < station.wirePoints.Length; i++)
            {
                float length = Vector3.Distance(station.wirePoints[i - 1], station.wirePoints[i]);
                if (distance <= length) return Vector3.Lerp(station.wirePoints[i - 1], station.wirePoints[i], distance / Mathf.Max(length, 1e-8f));
                distance -= length;
            }
            throw new InvalidOperationException("The wire is shorter than the requested test path.");
        }
        static void Pointer(string key, bool down)
        {
            var button = view.Buttons[key];
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            if (down) ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            else ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
        }
        static void Click(string key)
        {
            var button = view.Buttons[key]; Canvas.ForceUpdateCanvases();
            var rect = button.GetComponent<RectTransform>();
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Require(hits.Count > 0 && hits[0].gameObject == button.gameObject,
                "Visible button raycast hits " + key + "; top=" + (hits.Count > 0 ? hits[0].gameObject.name : "none"));
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
            foreach (var item in programs) if (item) item.Deactivate();
            // These GUID-named files were created by this run, never existing student programs.
            foreach (string path in createdPaths) if (File.Exists(path)) File.Delete(path);
            report.passed = string.IsNullOrEmpty(report.exception);
            Directory.CreateDirectory("Documentation");
            File.WriteAllText("Documentation/AllStationPendantValidation.json", JsonUtility.ToJson(report, true));
            Debug.Log("ALL_STATION_PENDANT_VALIDATION=" + report.passed);
            if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
