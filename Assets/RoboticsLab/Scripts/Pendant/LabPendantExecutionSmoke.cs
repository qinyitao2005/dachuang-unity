using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>Opt-in standalone acceptance against the actual UI and committed robot TCP.</summary>
    public sealed class LabPendantExecutionSmoke : MonoBehaviour
    {
        [Serializable] public sealed class CheckResult { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed;
            public string unityVersion, scene, exception, screenshot, fileScreenshot;
            public float wallSeconds, linearDeviationMm, arcRadialErrorMm, arcViaErrorMm, arcEndErrorMm;
            public float ringMotionDistanceMm, ringArrivalErrorMm, ringMaximumDeviationMm;
            public int linearSamples, arcSamples, drawingInkVertices;
            public List<CheckResult> checks = new List<CheckResult>();
            public string[] limitations = {
                "The real scene's simulated robot and original CAD assembly are exercised; no physical robot is connected.",
                "Pointer events are dispatched after the actual EventSystem raycast; physical OS mouse hardware is not exercised.",
                "Deterministic test points are populated through the program model; actual TCP recording has its separate --pendant-smoke acceptance.",
                "Motion evidence is sampled from the actual tool TCP after committed IK frames, not merely the ideal target or execution state."
            };
        }

        readonly Report report = new Report();
        LabTrainingApp app;
        LabPendantController controller;
        LabPendantExecutor executor;
        LabRobotStation station;
        string folder, screenshotPath;
        string createdProgramPath;
        float started;
        bool finishing;
        readonly List<Vector3> samples = new List<Vector3>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--pendant-execution-smoke") < 0) return;
            if (!FindObjectOfType<LabPendantExecutionSmoke>())
                new GameObject("PendantExecutionSmoke").AddComponent<LabPendantExecutionSmoke>();
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            started = Time.realtimeSinceStartup;
            folder = Path.Combine(FindProjectRoot(), "Documentation");
            screenshotPath = Path.Combine(folder, "Screenshots", "PendantExecution.png");
            report.unityVersion = Application.unityVersion;
            report.scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            report.screenshot = screenshotPath;
            var sequence = Flatten(Checks());
            while (true)
            {
                bool next = false; object waiting = null;
                try { next = sequence.MoveNext(); if (next) waiting = sequence.Current; }
                catch (Exception error)
                {
                    report.exception = error.ToString(); Check(false, "exception", error.Message); Debug.LogException(error);
                }
                if (!next) break;
                yield return waiting;
            }
            Finish();
        }

        // Flatten nested checks so exceptions from a yielded helper are reported and quit nonzero.
        static IEnumerator Flatten(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Peek();
                if (!current.MoveNext()) { (current as IDisposable)?.Dispose(); stack.Pop(); continue; }
                if (current.Current is IEnumerator nested) stack.Push(nested);
                else yield return current.Current;
            }
        }

        void Update()
        {
            if (!finishing && started > 0 && Time.realtimeSinceStartup - started > 180)
            {
                report.exception = "Execution smoke exceeded 180 seconds.";
                Check(false, "watchdog", report.exception); StopAllCoroutines(); Finish();
            }
        }

        IEnumerator Checks()
        {
            float until = Time.realtimeSinceStartup + 12;
            while (!(app = FindObjectOfType<LabTrainingApp>()) && Time.realtimeSinceStartup < until) yield return null;
            Require(app && app.stations != null && app.stations.Length == 3, "real_scene", "Main scene contains three original workcells.");
            while (!UnityEngine.Rendering.SplashScreen.isFinished && Time.realtimeSinceStartup - started < 20) yield return null;
            yield return new WaitForSeconds(.6f);
            app.ClosePanel(); app.HelmetWorn = app.LessonCompleted = app.QuizPassed = true;
            Require(app.TryStartStation(2), "drawing_entry", "Ordinary app entry selects dedicated drawing station 03.");
            station = app.ActiveStation; controller = app.pendantController;
            Require(controller, "controller_exists", "Scene has the imported teaching pendant controller.");
            controller.Open(station); yield return null;
            executor = controller.GetComponent<LabPendantExecutor>();
            Require(executor && controller.Visible && EventSystem.current, "executor_and_ui", "Pendant has a live executor and actual EventSystem.");
            Require(executor.Station == station && ReferenceEquals(executor.Model, controller.Model), "same_document", "FWD executes the exact document visible in the LCD.");
            executor.Stop(); controller.Clear();
            for (int frame = 0; frame < 8; frame++) yield return null;

            // A small raised contour remains close to the known-safe reset pose. No transform is assigned by this test.
            Vector3 origin = ActualLocal();
            Vector3 a = origin + new Vector3(0, 0, .012f);
            Vector3 b = a + new Vector3(.025f, 0, 0);
            Vector3 via = b + new Vector3(.015f, 0, .015f);
            Vector3 end = b + new Vector3(.030f, 0, 0);
            var model = controller.Model;
            Require(model.AddLinear(a, 40) && model.AddLinear(b, 40)
                && model.RecordCircularPoint(via, 40) && model.RecordCircularPoint(end, 40)
                && controller.ReturnToFirstPoint(), "program_built", "L approach, L line, C semicircle through via, L return: four executable rows.");
            Check(model.Points.Count == 4 && model.Instructions[3].TargetPoint == 1, "point_reference_reused", "Return L references P[1], without duplicating its point number or coordinates.");
            yield return null;
            Require(controller.ScreenView.ProgramRows.text.Contains("3:C"), "lcd_has_c", controller.ScreenView.ProgramRows.text);
            Require(app.trajectoryProgram && app.trajectoryProgram.Compile("WAIT 1"), "legacy_valid_program", "A valid legacy WAIT program is prepared before exclusive pendant execution.");

            Click("STEP");
            Require(executor.State == LabTrajectoryState.Running, "step_starts", "Actual STEP button starts the first complete instruction.");
            yield return WaitForPause("step_first", 12);
            Require(executor.IsPaused && executor.CurrentInstructionIndex == 1, "step_exactly_one_line", executor.StatusText);
            Check(Vector3.Distance(ActualLocal(), a) <= .0011f, "first_fine_arrival", "Actual TCP distance mm=" + Mm(ActualLocal(), a));
            Vector3 pausedTarget = station.toolTarget.localPosition, pausedActual = station.ActualToolPosition;
            int count = model.Instructions.Count, points = model.Points.Count;
            Click("F1"); Click("F2"); controller.SetJog(Vector3.right);
            Check(model.Instructions.Count == count && model.Points.Count == points && controller.HeldDirection.sqrMagnitude < .00001f,
                "pause_reserves_control", "Recording and held manual jogging cannot steal control from a paused program.");
            Check(!app.trajectoryProgram.Run() && !app.trajectoryProgram.IsExecuting && executor.IsPaused,
                "paused_legacy_run_rejected", "A valid legacy program cannot steal the pendant's paused motion reservation.");
            yield return new WaitForSeconds(.3f);
            Check(Vector3.Distance(pausedTarget, station.toolTarget.localPosition) < .00002f && Vector3.Distance(pausedActual, station.ActualToolPosition) < .0015f,
                "paused_position_held", "Paused target is stationary; actual TCP settling stays within 1.5 mm.");

            samples.Clear(); Click("STEP");
            yield return SampleUntilNotRunning(12);
            Require(executor.IsPaused && executor.CurrentInstructionIndex == 2, "linear_step_fine", executor.StatusText);
            report.linearSamples = samples.Count;
            foreach (var p in samples) report.linearDeviationMm = Mathf.Max(report.linearDeviationMm, SegmentDistance(a, b, p) * 1000);
            Check(samples.Count >= 4 && report.linearDeviationMm <= 2.5f && Vector3.Distance(ActualLocal(), b) <= .0011f,
                "linear_real_tcp", "Samples=" + samples.Count + ", largest line deviation mm=" + report.linearDeviationMm + ", endpoint mm=" + Mm(ActualLocal(), b));

            samples.Clear(); Click("STEP");
            yield return SampleUntilNotRunning(18);
            Require(executor.IsPaused && executor.CurrentInstructionIndex == 3, "c_is_one_complete_step", executor.StatusText);
            Vector3 centre = b + new Vector3(.015f, 0, 0);
            report.arcSamples = samples.Count; report.arcViaErrorMm = float.PositiveInfinity;
            foreach (var p in samples)
            {
                float radial = Mathf.Abs(Vector3.Distance(p, centre) - .015f) * 1000;
                report.arcRadialErrorMm = Mathf.Max(report.arcRadialErrorMm, radial);
                report.arcViaErrorMm = Mathf.Min(report.arcViaErrorMm, Mm(p, via));
            }
            report.arcEndErrorMm = Mm(ActualLocal(), end);
            Check(samples.Count >= 8 && report.arcRadialErrorMm <= 2.5f && report.arcViaErrorMm <= 1.1f && report.arcEndErrorMm <= 1.1f,
                "circular_real_tcp", "Samples=" + samples.Count + ", radial mm=" + report.arcRadialErrorMm + ", via mm=" + report.arcViaErrorMm + ", end mm=" + report.arcEndErrorMm);
            Check(report.arcViaErrorMm < .015f * 1000 * .25f, "arc_not_two_chords", "Real TCP reaches the bulged semicircle via point; a direct endpoint chord misses it by 15 mm.");

            Click("FWD");
            Require(executor.State == LabTrajectoryState.Running, "fwd_resumes", "Actual FWD resumes from the next row after STEP.");
            yield return SampleUntilNotRunning(15);
            Require(executor.State == LabTrajectoryState.Completed && executor.CurrentInstructionIndex == 4, "all_rows_completed", executor.StatusText);
            Check(Vector3.Distance(ActualLocal(), a) < .0011f && !station.PenDown, "complete_arrives_and_lifts", "Return point FINE arrived and pen is lifted.");

            // HOLD must interrupt an in-flight path rather than only stop between rows.
            executor.Stop(); controller.Clear();
            Vector3 holdStart = ActualLocal(), holdEnd = holdStart + new Vector3(.05f, 0, 0);
            Require(model.AddLinear(holdEnd, 40), "hold_program", "A raised 50 mm L path is prepared.");
            Click("FWD"); yield return new WaitForSeconds(.18f); Click("HOLD");
            Require(executor.IsPaused && executor.CurrentInstructionIndex == 0, "hold_mid_instruction", executor.StatusText);
            pausedTarget = station.toolTarget.localPosition;
            Check(Mm(ActualLocal(), holdEnd) > 5, "hold_before_endpoint", "HOLD happened before reaching the endpoint.");
            yield return new WaitForSeconds(.25f);
            Check(Vector3.Distance(pausedTarget, station.toolTarget.localPosition) < .00002f, "hold_no_target_motion", "HOLD prevents any new motion targets.");
            Click("FWD"); yield return SampleUntilNotRunning(18);
            Require(executor.State == LabTrajectoryState.Completed && Mm(ActualLocal(), holdEnd) <= 1.1f, "hold_resume_completes", executor.StatusText);

            // A real station emergency stop pauses and never resumes merely because the stop is cleared.
            executor.Stop(); controller.Clear(); Require(model.AddLinear(holdStart, 40), "estop_program", "Return L path prepared.");
            Click("FWD"); yield return new WaitForSeconds(.15f); Click("EmergencyStop");
            for (int frame = 0; frame < 3; frame++) yield return null;
            Require(executor.IsPaused && !station.PenDown, "emergency_pauses", "Existing station emergency stop pauses execution and lifts pen.");
            pausedTarget = station.toolTarget.localPosition;
            Click("FWD"); Check(executor.IsPaused, "emergency_rejects_fwd", executor.StatusText);
            Click("EmergencyStop"); yield return new WaitForSeconds(.2f);
            Check(executor.IsPaused && Vector3.Distance(pausedTarget, station.toolTarget.localPosition) < .00002f, "clear_estop_not_auto_resume", "Clearing emergency stop retains paused state and target.");
            Click("FWD"); yield return SampleUntilNotRunning(18);
            Require(executor.State == LabTrajectoryState.Completed, "explicit_estop_resume", executor.StatusText);

            // Reject malformed C before the first move, without replacing it by an L.
            executor.Stop(); controller.Clear();
            Vector3 start = ActualLocal();
            Require(model.RecordCircularPoint(start + Vector3.right * .01f, 40), "pending_c_setup", "Record only the C via.");
            Vector3 targetBefore = station.toolTarget.localPosition;
            Click("FWD");
            Check(!executor.IsExecuting && executor.State == LabTrajectoryState.Faulted && !string.IsNullOrEmpty(executor.LastError)
                && Vector3.Distance(targetBefore, station.toolTarget.localPosition) < .000001f,
                "pending_c_rejected_without_motion", executor.LastError);
            Require(model.RecordCircularPoint(start + Vector3.right * .02f, 40), "collinear_c_setup", "Complete C with three collinear points.");
            Click("FWD");
            Check(!executor.IsExecuting && executor.State == LabTrajectoryState.Faulted && executor.LastError.Contains("共线")
                && Vector3.Distance(targetBefore, station.toolTarget.localPosition) < .000001f,
                "collinear_c_rejected_without_motion", executor.LastError);
            executor.Stop(); controller.Clear();
            Require(model.AddLinear(station.exerciseCenter + Vector3.right * (station.workspaceHalfSize.x + .05f), 40), "outside_setup", "Create a point beyond the allowed workspace.");
            Click("FWD");
            Check(!executor.IsExecuting && executor.State == LabTrajectoryState.Faulted && Vector3.Distance(targetBefore, station.toolTarget.localPosition) < .000001f,
                "outside_rejected_without_clamping", executor.LastError);

            // Nominal speed remains compatible with the 100 mm/sec video, but physical simulation is capped.
            executor.Stop(); controller.Clear();
            Require(model.AddLinear(ActualLocal() + Vector3.forward * .018f, 100), "nominal_speed_setup", "Record the reference video's 100 mm/sec nominal speed.");
            Click("FWD"); yield return null;
            Check(executor.State == LabTrajectoryState.Running && executor.CurrentEffectiveSpeedMm <= station.moveSpeed * 1000 + .01f,
                "nominal_speed_bounded", "Nominal 100 mm/sec runs at no more than the configured station cap; effective=" + executor.CurrentEffectiveSpeedMm);
            yield return SampleUntilNotRunning(12);
            Require(executor.State == LabTrajectoryState.Completed, "nominal_speed_completes", executor.StatusText);

            // Exercise ink through the same L/C runner on the actual drawing plane.
            executor.Stop(); controller.Clear(); station.ResetExercise(); yield return null;
            Vector3 paperA = station.exerciseCenter; paperA.y = station.drawingHeight;
            Vector3 paperB = paperA + Vector3.right * .02f;
            Vector3 paperVia = paperB + new Vector3(.01f, 0, .01f);
            Vector3 paperEnd = paperB + Vector3.right * .02f;
            Require(model.AddLinear(paperA, 40, false) && model.AddLinear(paperB, 40, true)
                && model.RecordCircularPoint(paperVia, 40, true) && model.RecordCircularPoint(paperEnd, 40, true)
                && model.AddLinear(paperA, 40, false), "drawing_program", "Raised approach, pen-down L and C, pen-up return.");
            Check(!model.Instructions[0].PenDown && model.Instructions[1].PenDown && model.Instructions[2].PenDown
                && !model.Instructions[3].PenDown, "pen_metadata", "Each L/C stores its own pen state.");
            Click("FWD"); yield return SampleUntilNotRunning(25);
            Require(executor.State == LabTrajectoryState.Completed, "drawing_lc_completes", executor.StatusText);
            foreach (var line in station.GetComponentsInChildren<LineRenderer>(true))
                if (line.name == "DrawingStroke") report.drawingInkVertices += line.positionCount;
            Check(report.drawingInkVertices >= 8 && !station.PenDown, "actual_lc_ink", "Actual paper-contact drawing generated " + report.drawingInkVertices + " ink vertices, then lifted pen.");

            string savedName = "ExecutionSmoke_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            string savedText = model.ToProgramText();
            var savedPositions = new List<Vector3>(); foreach (var point in model.Points) savedPositions.Add(point.LocalPosition);
            Click("DATA"); yield return null;
            var files = controller.GetComponentInChildren<LabPendantFilePanel>(true);
            Require(files && files.IsOpen, "data_opens_lcd_files", "Actual DATA opens the file view inside the original LCD.");
            InputField filename = null;
            foreach (var entry in controller.GetComponentsInChildren<InputField>(true)) if (entry.name == "ProgramName") { filename = entry; break; }
            Require(filename && filename.characterLimit == 32, "filename_input", "The real LCD has a bounded 32-character program-name input.");
            filename.Select(); filename.ActivateInputField(); yield return null;
            filename.text = savedName;
            controller.SetJog(Vector3.right);
            Check(files.TextEntryFocused && controller.HeldDirection.sqrMagnitude < .00001f,
                "filename_focus_blocks_jog", "Editing a filename retains focus and cannot issue held robot movement.");
            ClickUiButton("SaveProgram"); yield return null;
            createdProgramPath = LabPendantProgramStorage.PathFor(station.stationIndex, savedName);
            Require(File.Exists(createdProgramPath), "save_program_button", "Real Save button wrote the chosen unique program file.");
            Check(Array.IndexOf(controller.ProgramNames(), savedName) >= 0, "saved_name_listed", "Saved program appears in the station's file list.");
            bool visibleSavedRow = false;
            foreach (var label in controller.GetComponentsInChildren<Text>(true))
                if (label.gameObject.activeInHierarchy && label.name == "Caption" && label.text.Contains("ExecutionSmoke_")) visibleSavedRow = true;
            Check(visibleSavedRow, "saved_list_refreshed", "The saved filename is visibly listed on the LCD immediately after Save.");
            report.fileScreenshot = Path.Combine(folder, "Screenshots", "PendantFiles.png");
            yield return Capture(report.fileScreenshot, "files_screenshot");
            ClickUiButton("CloseProgramFiles"); yield return null;
            Check(!files.IsOpen && !files.TextEntryFocused, "file_close_releases_focus", "Closing the LCD file overlay releases text-entry focus.");
            controller.Clear();
            Require(model.Instructions.Count == 0, "clear_before_load", "Program is genuinely empty before loading.");
            Click("DATA"); yield return null; filename.text = savedName;
            ClickUiButton("LoadProgram"); yield return null;
            model = controller.Model;
            bool pointsRestored = model.Points.Count == savedPositions.Count;
            if (pointsRestored) for (int i = 0; i < savedPositions.Count; i++) pointsRestored &= Vector3.Distance(savedPositions[i], model.Points[i].LocalPosition) < .0000001f;
            Check(model.ToProgramText() == savedText && pointsRestored && model.Instructions[1].PenDown && model.Instructions[2].PenDown,
                "saved_lc_roundtrip", "L/C types, stable point numbers, 3D positions, speed and per-row pen state survive a saved roundtrip.");
            if (files.IsOpen) ClickUiButton("CloseProgramFiles");
            Check(!controller.SaveProgram("../escape") && !controller.LoadProgram("../escape") && model.ToProgramText() == savedText,
                "file_path_rejected_transactionally", "A path outside the program directory cannot save/load or replace the valid document.");

            Click("FWD"); yield return new WaitForSeconds(.1f); Click("HOLD");
            Check(!controller.LoadProgram(savedName) && executor.IsPaused && model.ToProgramText() == savedText,
                "paused_load_rejected", "Loading cannot replace an exclusively controlled paused program.");
            Click("RESET");
            Check(!executor.IsExecuting && model.ToProgramText() == savedText, "reset_stops_retains_program", "Actual RESET stops execution and preserves recorded rows.");
            Require(ReferenceEquals(executor.Model, model), "loaded_document_bound_to_executor", "The executor references the newly loaded document, not a stale pre-save instance.");
            Click("FWD"); yield return SampleUntilNotRunning(25);
            Require(executor.State == LabTrajectoryState.Completed && executor.CurrentInstructionIndex == model.Instructions.Count,
                "loaded_program_replays", "The program loaded through the LCD executes all four L/C rows to FINE completion: " + executor.StatusText);
            Check(Mm(ActualLocal(), paperA) <= 1.1f && !station.PenDown, "loaded_replay_actual_arrival", "The loaded program's real TCP returns to the saved paper point and lifts the pen; endpoint mm=" + Mm(ActualLocal(), paperA));

            // Closing and changing stations release every motion owner.
            executor.Stop(); controller.Clear(); Require(model.AddLinear(ActualLocal() - Vector3.forward * .04f, 40), "close_program", "Raised close/exit test path.");
            Click("FWD"); yield return new WaitForSeconds(.12f); controller.Close();
            targetBefore = station.toolTarget.localPosition; yield return new WaitForSeconds(.25f);
            Check(!controller.Visible && !executor.IsExecuting && controller.HeldDirection == Vector3.zero
                && Vector3.Distance(targetBefore, station.toolTarget.localPosition) < .00002f,
                "close_stops_execution", "Closing the pendant releases motion and retains the taught row.");
            controller.Open(station); yield return null; Click("FWD"); yield return new WaitForSeconds(.12f);
            Require(app.TryStartStation(1), "switch_ring", "Switch through the ordinary app API to station 02.");
            for (int frame = 0; frame < 4; frame++) yield return null;
            Check(!executor.IsExecuting, "station_switch_stops", "Previous drawing program cannot continue after changing station.");
            controller.Open(app.ActiveStation); yield return null;
            Check(!ReferenceEquals(controller.Model, model), "station_documents_separate", "Ring station has a distinct pendant program document.");
            var ring = app.ActiveStation;
            Require(ring.wirePoints != null && ring.wirePoints.Length > 1, "ring_wire_exists", "Station 02 has its authored steel guide.");
            controller.Clear();
            Vector3 wireDirection = (ring.wirePoints[1] - ring.wirePoints[0]).normalized;
            Require(Vector3.Distance(ring.wirePoints[0], ring.wirePoints[1]) > .0025f,
                "ring_safe_segment", "The first straight guide segment contains a full 2.5 mm legal move.");
            Vector3 ringBefore = ring.transform.InverseTransformPoint(ring.ActualToolPosition);
            Vector3 legalRingEnd = ring.wirePoints[0] + wireDirection * .0025f;
            Require(controller.Model.AddLinear(legalRingEnd, 40), "ring_legal_program", "Prepare one L that advances 2.5 mm along the authored guide's first straight segment.");
            Click("FWD");
            bool legalMoveBlocked = false; int ringSamples = 0;
            until = Time.realtimeSinceStartup + 10;
            while (executor.State == LabTrajectoryState.Running && Time.realtimeSinceStartup < until)
            {
                yield return null;
                Vector3 actualRing = ring.transform.InverseTransformPoint(ring.ActualToolPosition);
                report.ringMaximumDeviationMm = Mathf.Max(report.ringMaximumDeviationMm,
                    SegmentDistance(ring.wirePoints[0], ring.wirePoints[1], actualRing) * 1000);
                legalMoveBlocked |= ring.RingMovementBlocked || ring.RobotWireBlocked; ringSamples++;
            }
            Vector3 ringAfter = ring.transform.InverseTransformPoint(ring.ActualToolPosition);
            report.ringMotionDistanceMm = Mm(ringBefore, ringAfter); report.ringArrivalErrorMm = Mm(ringAfter, legalRingEnd);
            Require(executor.State == LabTrajectoryState.Completed && executor.CurrentInstructionIndex == 1,
                "ring_legal_l_completes", "Station 02 FWD completes a legal L through the existing steel-guide motion adapter: " + executor.StatusText);
            Check(ringSamples > 0 && report.ringMotionDistanceMm > 1.8f && report.ringArrivalErrorMm <= .4f
                && report.ringMaximumDeviationMm <= .4f && !legalMoveBlocked,
                "ring_legal_actual_tcp", "Actual motion mm=" + report.ringMotionDistanceMm + ", endpoint mm=" + report.ringArrivalErrorMm
                    + ", maximum centreline deviation mm=" + report.ringMaximumDeviationMm + ", blocked=" + legalMoveBlocked + ", samples=" + ringSamples);
            executor.Stop(); controller.Clear();
            Vector3 beyondStart = ring.wirePoints[0] - wireDirection * .01f;
            Require(controller.Model.AddLinear(beyondStart, 40), "ring_boundary_program", "Request a 10 mm motion beyond the steel guide's first endpoint.");
            Click("FWD");
            until = Time.realtimeSinceStartup + 10;
            while (executor.State == LabTrajectoryState.Running && Time.realtimeSinceStartup < until) yield return null;
            Check(executor.State == LabTrajectoryState.Faulted
                && Mm(ring.transform.InverseTransformPoint(ring.ActualToolPosition), beyondStart) > 5
                && (executor.LastError.Contains("钢丝") || executor.LastError.Contains("工作区") || executor.LastError.Contains("边界")),
                "ring_endpoint_guard_preserved", "New L/C execution cannot bypass the existing steel-guide boundary: " + executor.LastError);
            Require(app.TryStartStation(2), "return_drawing", "Return to drawing station 03.");
            station = app.ActiveStation; controller.Open(station); yield return null;
            Check(ReferenceEquals(controller.Model, model) && model.Instructions.Count == 1, "drawing_document_retained", "Station 03 retains its own program after exit and re-entry.");

            // Evidence contains the actual LCD and robot following a prepared mixed L/C document.
            executor.Stop(); controller.Clear(); origin = ActualLocal();
            model.AddLinear(origin + Vector3.forward * .012f, 40);
            model.RecordCircularPoint(origin + new Vector3(.015f, 0, .027f), 40);
            model.RecordCircularPoint(origin + new Vector3(.030f, 0, .012f), 40);
            Click("FWD"); yield return new WaitForSeconds(.2f); Click("HOLD");
            yield return Capture(screenshotPath, "execution_screenshot");
        }

        IEnumerator WaitForPause(string name, float seconds)
        {
            yield return SampleUntilNotRunning(seconds);
            Require(executor.IsPaused || executor.State == LabTrajectoryState.Completed, name, executor.StatusText);
        }

        IEnumerator SampleUntilNotRunning(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (executor.State == LabTrajectoryState.Running && Time.realtimeSinceStartup < until)
            {
                // On the next coroutine tick the preceding frame's LateUpdate has committed its IK pose.
                // Yield null also works in a batch Player, where WaitForEndOfFrame never resumes.
                yield return null; samples.Add(ActualLocal());
            }
            Require(executor.State != LabTrajectoryState.Running, "bounded_execution", executor.StatusText);
        }

        Vector3 ActualLocal() { return station.transform.InverseTransformPoint(station.ActualToolPosition); }
        static float Mm(Vector3 a, Vector3 b) { return Vector3.Distance(a, b) * 1000; }
        static float SegmentDistance(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 delta = b - a; float t = delta.sqrMagnitude < 1e-14f ? 0 : Mathf.Clamp01(Vector3.Dot(p - a, delta) / delta.sqrMagnitude);
            return Vector3.Distance(p, a + delta * t);
        }

        void Click(string key)
        {
            Button button;
            Require(controller.Buttons.TryGetValue(key, out button) && button, "button_" + key, "Authored pendant button " + key + " exists.");
            ClickButton(button, key);
        }
        void ClickUiButton(string name)
        {
            Button found = null;
            foreach (var button in controller.GetComponentsInChildren<Button>(true))
                if (button.name == name && button.gameObject.activeInHierarchy) { found = button; break; }
            Require(found, "file_button_" + name, "LCD file-view button exists and is active.");
            ClickButton(found, name);
        }
        void ClickButton(Button button, string key)
        {
            Canvas.ForceUpdateCanvases();
            var rect = button.GetComponent<RectTransform>(); var canvas = button.GetComponentInParent<Canvas>();
            Camera camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(EventSystem.current) { position = position, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            var top = hits.Count > 0 ? hits[0].gameObject : null;
            Require(top && (top == button.gameObject || top.transform.IsChildOf(button.transform)), "raycast_" + key,
                "At " + position + ", top=" + (top ? top.name : "<none>") + ", expected=" + key);
            Require(ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler), "click_" + key, "Actual pointer click handled.");
        }

        IEnumerator Capture(string path, string name)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            float until = Time.realtimeSinceStartup + 8;
            while ((!File.Exists(path) || new FileInfo(path).Length < 2048) && Time.realtimeSinceStartup < until) yield return null;
            bool saved = File.Exists(path) && new FileInfo(path).Length >= 2048;
            Check(saved, name + "_saved", path);
            if (!saved) yield break;
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            bool decoded = texture.LoadImage(File.ReadAllBytes(path)); float brightest = 0;
            if (decoded) for (int y = 0; y < 9; y++) for (int x = 0; x < 16; x++)
                brightest = Mathf.Max(brightest, texture.GetPixel(x * (texture.width - 1) / 15, y * (texture.height - 1) / 8).grayscale);
            Check(decoded && brightest > .05f, name + "_rendered", "PNG decodes with nonblack rendered content; maximum luminance=" + brightest);
            Destroy(texture);
        }

        bool Check(bool passed, string name, string detail)
        {
            report.checks.Add(new CheckResult { name = name, detail = detail, passed = passed });
            Debug.Log("PENDANT_EXECUTION " + (passed ? "PASS " : "FAIL ") + name + ": " + detail); return passed;
        }
        void Require(bool passed, string name, string detail) { if (!Check(passed, name, detail)) throw new InvalidOperationException(name + ": " + detail); }

        void Finish()
        {
            if (finishing) return; finishing = true;
            try
            {
                if (executor) executor.Stop(); if (controller) controller.Close(); if (app) app.ClosePanel();
                // This exact unique path was obtained only after this isolated process saved its own fixture.
                if (!string.IsNullOrEmpty(createdProgramPath) && File.Exists(createdProgramPath)) File.Delete(createdProgramPath);
            }
            catch (Exception error) { Check(false, "cleanup", error.Message); }
            report.passed = string.IsNullOrEmpty(report.exception) && report.checks.Count > 0;
            foreach (var check in report.checks) report.passed &= check.passed;
            report.wallSeconds = Time.realtimeSinceStartup - started;
            try { Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "PendantExecutionSmoke.json"), JsonUtility.ToJson(report, true)); }
            catch (Exception error) { report.passed = false; Debug.LogError(error); }
            Debug.Log("PENDANT_EXECUTION_SMOKE " + JsonUtility.ToJson(report));
            Application.Quit(report.passed ? 0 : 1);
        }

        static string FindProjectRoot()
        {
            var directory = new DirectoryInfo(Application.dataPath);
            for (int depth = 0; directory != null && depth < 7; depth++, directory = directory.Parent)
                if (Directory.Exists(Path.Combine(directory.FullName, "Assets")) && Directory.Exists(Path.Combine(directory.FullName, "ProjectSettings"))) return directory.FullName;
            return Path.GetDirectoryName(Application.dataPath);
        }
    }
}
