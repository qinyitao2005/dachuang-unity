using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>Opt-in acceptance checks against the actual scene, pendant graphics and TCP.</summary>
    public sealed class LabPendantSmoke : MonoBehaviour
    {
        [Serializable]
        public sealed class CheckResult
        {
            public string name;
            public bool passed;
            public string detail;
        }

        [Serializable]
        public sealed class Report
        {
            public bool passed;
            public string unityVersion, scene, exception, screenshot;
            public float wallSeconds, jogDistanceMm, capturedPointErrorMm;
            public List<CheckResult> checks = new List<CheckResult>();
            public string[] limitations = {
                "Digital UI and simulated robot acceptance only; no physical robot or pendant was connected.",
                "Pointer events are dispatched by the test after EventSystem raycast validation; OS mouse hardware is not exercised.",
                "Teaching prerequisites are bypassed only inside this explicit smoke run after their rejection is verified.",
                "L/C recording is checked as program data; executing a real industrial controller program is outside this test."
            };
        }

        readonly Report report = new Report();
        LabTrainingApp app;
        LabPendantController controller;
        LabPendantJogButton heldButton;
        string folder, screenshotPath;
        float started;
        bool finishing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--pendant-smoke") < 0) return;
            if (!FindObjectOfType<LabPendantSmoke>())
                new GameObject("PendantProgrammingSmoke").AddComponent<LabPendantSmoke>();
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            started = Time.realtimeSinceStartup;
            report.unityVersion = Application.unityVersion;
            report.scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            folder = Path.Combine(FindProjectRoot(), "Documentation");
            Directory.CreateDirectory(Path.Combine(folder, "Screenshots"));
            screenshotPath = Path.Combine(folder, "Screenshots", "PendantProgramming.png");
            report.screenshot = screenshotPath;
            IEnumerator checks = Checks();
            while (true)
            {
                bool next = false;
                object waiting = null;
                try
                {
                    if (Time.realtimeSinceStartup - started > 90)
                        throw new TimeoutException("Pendant smoke exceeded 90 seconds.");
                    next = checks.MoveNext();
                    if (next) waiting = checks.Current;
                }
                catch (Exception ex)
                {
                    report.exception = ex.ToString();
                    Check(false, "exception", ex.Message);
                    Debug.LogException(ex);
                }
                if (!next) break;
                yield return waiting;
            }
            try { (checks as IDisposable)?.Dispose(); }
            catch (Exception ex) { Check(false, "cleanup_exception", ex.Message); }
            Finish();
        }

        // Independent watchdog also covers a coroutine that is unexpectedly left waiting.
        void Update()
        {
            if (!finishing && started > 0 && Time.realtimeSinceStartup - started > 90)
            {
                report.exception = "Pendant smoke watchdog exceeded 90 seconds.";
                Check(false, "timeout", report.exception);
                StopAllCoroutines();
                Finish();
            }
        }

        IEnumerator Checks()
        {
            float readyUntil = Time.realtimeSinceStartup + 12;
            while (!(app = FindObjectOfType<LabTrainingApp>()) && Time.realtimeSinceStartup < readyUntil)
                yield return null;
            Require(app && app.player && app.stations != null && app.stations.Length == 3,
                "real_scene", "Main scene contains the app, player and three workcells.");
            float splashUntil=Time.realtimeSinceStartup+15;
            while(!UnityEngine.Rendering.SplashScreen.isFinished&&Time.realtimeSinceStartup<splashUntil)yield return null;
            yield return new WaitForSeconds(.6f);
            for (int frame = 0; frame < 15; frame++) yield return null;
            app.ClosePanel();
            app.HelmetWorn = app.LessonCompleted = app.QuizPassed = false;
            Check(!app.TryStartStation(2) && !app.ActiveStation, "helmet_gate",
                "Station 03 rejects entry before helmet and learning prerequisites.");
            app.HelmetWorn = true;
            Check(!app.TryStartStation(2) && !app.ActiveStation, "learning_gate",
                "A helmet alone does not bypass the teaching/quiz gate.");
            // This explicit validation process is isolated from ordinary play.
            app.HelmetWorn = app.LessonCompleted = app.QuizPassed = true;
            Require(app.TryStartStation(2), "drawing_entry", "TryStartStation(2) enters station 03.");
            var drawing = app.ActiveStation;
            controller = app.pendantController;
            Require(controller && controller.pendantPrefab, "pendant_resource", "Configured authored controller prefab.");
            controller.Open(drawing);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Require(controller.Visible && controller.Model != null && controller.ScreenView,
                "pendant_open", "The pendant opens for the active drawing station.");
            Require(EventSystem.current, "event_system", "Scene supplies a real EventSystem.");
            controller.Clear(); // Start this dedicated process with an empty document.
            for (int frame = 0; frame < 10; frame++) yield return null;

            Vector3 beforeL = drawing.transform.InverseTransformPoint(drawing.ActualToolPosition);
            Click("F1");
            var document = controller.Model;
            Require(document.Instructions.Count == 1 && document.Points.Count == 1,
                "linear_one_click_one_row", "One physical F1 click appends exactly one instruction and point.");
            var line = document.Instructions[0];
            var firstPoint = document.FindPoint(line.TargetPoint);
            report.capturedPointErrorMm = firstPoint == null ? 999 : Vector3.Distance(beforeL, firstPoint.LocalPosition) * 1000;
            Check(line.Type == LabPendantInstructionType.Linear && line.TargetPoint == 1 && firstPoint != null
                && report.capturedPointErrorMm < .001f, "linear_actual_tcp",
                "L P[1] captures the real TCP in station-local coordinates; error mm=" + report.capturedPointErrorMm);
            Check(controller.ScreenView.ProgramRows.text.Contains("1:L P[1]")
                && controller.ScreenView.ProgramRows.text.Contains("100mm/sec FINE"),
                "linear_screen", controller.ScreenView.ProgramRows.text);

            Click("F2");
            Require(document.Instructions.Count == 2 && document.Points.Count == 2 && document.HasPendingArc,
                "circular_midpoint_pending", "First C click adds one pending arc row and its intermediate point.");
            var arc = document.Instructions[1];
            int middleNumber = arc.ViaPoint;
            Check(arc.Type == LabPendantInstructionType.Circular && middleNumber == 2 && arc.TargetPoint == 0
                && controller.ScreenView.ProgramRows.text.Contains("2:C P[2] P[?]"),
                "circular_pending_screen", controller.ScreenView.ProgramRows.text);
            // No frame is allowed to move the TCP between these two deliberate clicks.
            Click("F2");
            Check(document.Instructions.Count == 2 && document.Points.Count == 2 && document.HasPendingArc
                && arc.TargetPoint == 0, "duplicate_arc_endpoint_rejected",
                "C rejects an endpoint at the unchanged intermediate position.");

            Vector3 beforeJog = drawing.ActualToolPosition;
            var jogButtons = controller.GetComponentsInChildren<LabPendantJogButton>(true);
            // The view is on a separate child canvas below this controller.
            Require(jogButtons.Length >= 6, "real_jog_buttons", "Six Cartesian pendant jog buttons are present.");
            foreach (string key in new[] { "J3+", "J1+", "J2+", "J1-", "J2-", "J3-" })
            {
                LabPendantJogButton jog = null;
                foreach (var candidate in jogButtons) if (candidate.name == key) { jog = candidate; break; }
                if (!jog) continue;
                PointerEventData pointer;
                GameObject target = Raycast(jog.gameObject, out pointer);
                Require(target, "jog_raycast_" + key, "Jog button is the top eligible pointer target.");
                heldButton = jog;
                ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
                Check(controller.HeldDirection.sqrMagnitude > .5f, "jog_hold_" + key, "Pointer down starts pendant jogging.");
                float holdUntil = Time.realtimeSinceStartup + .35f;
                while (Time.realtimeSinceStartup < holdUntil) yield return null;
                ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
                heldButton = null;
                for (int frame = 0; frame < 4; frame++) yield return null;
                if (Vector3.Distance(beforeJog, drawing.ActualToolPosition) > .0005f) break;
            }
            report.jogDistanceMm = Vector3.Distance(beforeJog, drawing.ActualToolPosition) * 1000;
            Require(report.jogDistanceMm > .5f && controller.HeldDirection.sqrMagnitude < .00001f,
                "jog_moves_real_tcp", "UI pointer hold moves the real robot, release clears held direction; mm=" + report.jogDistanceMm);
            Vector3 endpoint = drawing.transform.InverseTransformPoint(drawing.ActualToolPosition);
            Click("F2");
            Require(document.Instructions.Count == 2 && document.Points.Count == 3 && !document.HasPendingArc
                && arc.IsComplete && arc.ViaPoint == middleNumber && arc.TargetPoint == 3 && arc.TargetPoint != arc.ViaPoint,
                "circular_completes_same_row", "Second C completes the same row with distinct midpoint P[2] and endpoint P[3].");
            var recordedEnd = document.FindPoint(arc.TargetPoint);
            Check(recordedEnd != null && Vector3.Distance(recordedEnd.LocalPosition, endpoint) < .000001f,
                "circular_actual_tcp", "Circular endpoint captures the moved TCP.");
            Check(controller.ScreenView.ProgramRows.text.Contains("2:C P[2] P[3]")
                && controller.ScreenView.ProgramRows.text.Contains("[End]"),
                "circular_screen", controller.ScreenView.ProgramRows.text);

            // Save evidence while station 03 contains both L and a completed C instruction.
            for (int frame = 0; frame < 8; frame++) yield return null;
            if (File.Exists(screenshotPath)) File.Delete(screenshotPath);
            ScreenCapture.CaptureScreenshot(screenshotPath);
            float captureUntil = Time.realtimeSinceStartup + 8;
            while ((!File.Exists(screenshotPath) || new FileInfo(screenshotPath).Length < 2048)
                && Time.realtimeSinceStartup < captureUntil) yield return null;
            Check(File.Exists(screenshotPath) && new FileInfo(screenshotPath).Length >= 2048,
                "screenshot_saved", screenshotPath);
            if(File.Exists(screenshotPath))
            {
                var pixels=new Texture2D(2,2,TextureFormat.RGB24,false);
                bool decoded=pixels.LoadImage(File.ReadAllBytes(screenshotPath));
                float brightest=0;
                if(decoded)for(int y=0;y<9;y++)for(int x=0;x<16;x++)
                    brightest=Mathf.Max(brightest,pixels.GetPixel(x*(pixels.width-1)/15,y*(pixels.height-1)/8).grayscale);
                Check(decoded&&brightest>.05f,"screenshot_rendered","PNG decodes and contains rendered, non-black image data; maximum sample luminance="+brightest);
                Destroy(pixels);
            }

            Click("F3");
            Check(document.Instructions.Count == 1 && document.Points.Count == 1 && document.FindPoint(1) != null,
                "undo_button", "F3 removes the whole completed arc and its two points while retaining P[1].");
            Click("F4");
            Click("F1");
            Check(document.Instructions.Count == 2 && Mathf.Approximately(document.Instructions[1].SpeedMm, 50),
                "speed_button", "F4 selects 50 mm/sec for the next recorded instruction.");
            Click("MENU/Enter");
            Check(document.Instructions.Count == 3 && document.Instructions[2].Type == LabPendantInstructionType.Linear,
                "enter_repeat_button", "The physical Enter key repeats the current L recording mode once.");
            Click("F5");
            Check(document.Instructions.Count == 0 && document.Points.Count == 0 && document.NextPointNumber == 1,
                "clear_button", "F5 clears instructions and resets point numbering.");
            for (int row = 0; row < 14; row++) Click("F1");
            yield return null;
            Canvas.ForceUpdateCanvases();
            var screen = controller.ScreenView;
            Check(document.Instructions.Count == 14 && screen.ProgramRows.text.Contains("14:L P[14]")
                && screen.Scroll.content.rect.height > screen.Scroll.viewport.rect.height
                && screen.Scroll.verticalNormalizedPosition < .02f,
                "multirow_auto_scroll", "Fourteen UI-recorded rows overflow the LCD and automatically scroll to the latest row.");

            var drawingDocument = document;
            Require(app.TryStartStation(1), "ring_entry", "TryStartStation(1) enters station 02.");
            controller.Open(app.ActiveStation);
            yield return null;
            Check(!ReferenceEquals(controller.Model, drawingDocument) && controller.Model.Instructions.Count == 0,
                "station02_separate_document", "Station 02 starts with its own program data.");
            Click("F1");
            var ringDocument = controller.Model;
            Check(ringDocument.Instructions.Count == 1, "station02_record", "Station 02 records one L row.");
            Require(app.TryStartStation(2), "drawing_reentry", "Station 03 reopens normally.");
            controller.Open(app.ActiveStation);
            yield return null;
            Check(ReferenceEquals(controller.Model, drawingDocument) && controller.Model.Instructions.Count == 14,
                "station03_document_retained", "Station 03 retains all fourteen of its own rows.");
            Require(app.TryStartStation(1), "ring_reentry", "Station 02 reopens normally.");
            controller.Open(app.ActiveStation);
            yield return null;
            Check(ReferenceEquals(controller.Model, ringDocument) && controller.Model.Instructions.Count == 1,
                "station02_document_retained", "Station 02 retains its independent single row.");
            Require(app.TryStartStation(0), "tracing_entry", "Station 01 remains enterable through the ordinary app API.");
            controller.Open(app.ActiveStation);
            Check(!controller.Visible, "tracing_rejects_pendant", "Station 01 tracing does not open the programming pendant.");

            Require(app.TryStartStation(2), "drawing_final_entry", "Station 03 enters for stop and closure checks.");
            drawing = app.ActiveStation;
            controller.Open(drawing);
            yield return null;
            int rowsBeforeStop = controller.Model.Instructions.Count;
            int pointsBeforeStop = controller.Model.Points.Count;
            drawing.SetEmergencyStop(true);
            Click("F1");
            Click("F2");
            Check(controller.Model.Instructions.Count == rowsBeforeStop && controller.Model.Points.Count == pointsBeforeStop,
                "emergency_stop_blocks_recording", "Real F1/F2 pointer clicks cannot append rows or points during emergency stop.");
            drawing.SetEmergencyStop(false);
            controller.SetJog(Vector3.right);
            Check(controller.HeldDirection.sqrMagnitude > .5f, "close_has_held_jog", "A held pendant direction exists before closure.");
            Vector3 targetBeforeClose = drawing.toolTarget ? drawing.toolTarget.position : drawing.ActualToolPosition;
            controller.Close();
            controller.RecordLinear();
            controller.RecordCircular();
            Check(!controller.Visible && controller.HeldDirection == Vector3.zero
                && controller.Model.Instructions.Count == rowsBeforeStop && controller.Model.Points.Count == pointsBeforeStop,
                "close_releases_and_blocks_recording", "Closing releases jog and closed-controller requests cannot record data.");
            float quietUntil = Time.realtimeSinceStartup + .4f;
            while (Time.realtimeSinceStartup < quietUntil) yield return null;
            Vector3 targetAfterClose = drawing.toolTarget ? drawing.toolTarget.position : drawing.ActualToolPosition;
            Check(Vector3.Distance(targetBeforeClose, targetAfterClose) < .0005f,
                "close_no_new_motion_target", "No new robot target movement follows closure; displacement mm="
                    + (Vector3.Distance(targetBeforeClose, targetAfterClose) * 1000));
        }

        void Click(string key)
        {
            Button button;
            Require(controller.Buttons.TryGetValue(key, out button) && button,
                "button_exists_" + key, "The actual pendant contains button " + key + ".");
            PointerEventData pointer;
            GameObject target = Raycast(button.gameObject, out pointer);
            Require(target, "button_raycast_" + key, "The actual button is the top eligible raycast target.");
            Require(ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler),
                "button_click_" + key, "A pointerClick event was handled by " + key + ".");
        }

        GameObject Raycast(GameObject expected, out PointerEventData pointer)
        {
            Canvas.ForceUpdateCanvases();
            var rect = expected.GetComponent<RectTransform>();
            var canvas = expected.GetComponentInParent<Canvas>();
            Camera camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
            pointer = new PointerEventData(EventSystem.current) { position = position, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            GameObject top = hits.Count > 0 ? hits[0].gameObject : null;
            bool eligible = top && (top == expected || top.transform.IsChildOf(expected.transform));
            Check(eligible, "raycast_top_" + expected.name,
                "At " + position + " top=" + (top ? top.name : "<none>") + ", expected=" + expected.name);
            return eligible ? expected : null;
        }

        bool Check(bool passed, string name, string detail)
        {
            report.checks.Add(new CheckResult { name = name, passed = passed, detail = detail });
            Debug.Log("PENDANT_SMOKE " + (passed ? "PASS " : "FAIL ") + name + ": " + detail);
            return passed;
        }

        void Require(bool passed, string name, string detail)
        {
            if (!Check(passed, name, detail)) throw new InvalidOperationException(name + ": " + detail);
        }

        void Finish()
        {
            if (finishing) return;
            finishing = true;
            try
            {
                if (heldButton) heldButton.OnPointerUp(new PointerEventData(EventSystem.current));
                if (controller) controller.Close();
                if (app) app.ClosePanel();
            }
            catch (Exception ex) { Check(false, "final_cleanup", ex.Message); }
            report.passed = string.IsNullOrEmpty(report.exception) && report.checks.Count > 0;
            foreach (var check in report.checks) report.passed &= check.passed;
            report.wallSeconds = Time.realtimeSinceStartup - started;
            try
            {
                Directory.CreateDirectory(folder);
                string json = JsonUtility.ToJson(report, true);
                File.WriteAllText(Path.Combine(folder, "PendantProgrammingSmoke.json"), json);
                Debug.Log("PENDANT_PROGRAMMING_SMOKE " + json);
            }
            catch (Exception ex)
            {
                report.passed = false;
                Debug.LogError("PENDANT_SMOKE report write failed: " + ex);
            }
            Application.Quit(report.passed ? 0 : 1);
        }

        static string FindProjectRoot()
        {
            var directory = new DirectoryInfo(Application.dataPath);
            for (int depth = 0; directory != null && depth < 7; depth++, directory = directory.Parent)
                if (Directory.Exists(Path.Combine(directory.FullName, "Assets"))
                    && Directory.Exists(Path.Combine(directory.FullName, "ProjectSettings"))) return directory.FullName;
            return Path.GetDirectoryName(Application.dataPath);
        }
    }
}
