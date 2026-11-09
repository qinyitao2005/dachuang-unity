using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>Explicit standalone UI/rendering acceptance; inactive without --shared-pendant-smoke.</summary>
    public sealed class LabSharedPendantPlayerSmoke : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public bool passed;
            public string unityVersion, exception;
            public float wallSeconds, drawingJogMm, drawingReturnErrorMm;
            public List<string> checks = new List<string>();
            public List<string> screenshots = new List<string>();
        }

        readonly Report report = new Report();
        readonly List<string> createdPaths = new List<string>();
        LabTrainingApp app;
        LabRobotStation station;
        LabStation1PendantView view;
        LabStation1Programming program;
        string outputPath;
        float started;
        bool finishing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--shared-pendant-smoke") < 0) return;
            if (!FindObjectOfType<LabSharedPendantPlayerSmoke>())
                new GameObject("SharedPendantPlayerSmoke").AddComponent<LabSharedPendantPlayerSmoke>();
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            started = Time.realtimeSinceStartup;
            report.unityVersion = Application.unityVersion;
            outputPath = ResolveOutput();
            var sequence = Flatten(Checks());
            while (true)
            {
                bool next = false; object waiting = null;
                try { next = sequence.MoveNext(); if (next) waiting = sequence.Current; }
                catch (Exception error) { report.exception = error.ToString(); Debug.LogException(error); }
                if (!next) break;
                yield return waiting;
            }
            Finish();
        }

        void Update()
        {
            if (!finishing && started > 0 && Time.realtimeSinceStartup - started > 100)
            {
                report.exception = "Shared pendant player smoke exceeded 100 seconds.";
                StopAllCoroutines(); Finish();
            }
        }

        IEnumerator Checks()
        {
            float until = Time.realtimeSinceStartup + 15;
            while (!(app = FindObjectOfType<LabTrainingApp>()) && Time.realtimeSinceStartup < until) yield return null;
            Require(app && app.stations.Length == 3, "Normal player opens the original three-station scene.");
            while (!UnityEngine.Rendering.SplashScreen.isFinished && Time.realtimeSinceStartup - started < 20) yield return null;
            yield return new WaitForSeconds(.6f);
            app.ClosePanel(); app.HelmetWorn = app.LessonCompleted = app.QuizPassed = true;

            yield return Open(2);
            CreateProgram();
            Click("SHIFT_LEFT"); Click("F1");
            Require(program.Model.Points.Count == 1, "Station03 POINT records the actual initial tool point.");
            Vector3 first = program.Model.FindPoint(1).LocalPosition;
            Require(Vector3.Distance(first, Actual()) < .0003f, "Recorded P1 agrees with the real drawing TCP.");
            Pointer("J1+", true);
            until = Time.realtimeSinceStartup + 10;
            while (Actual().x - first.x < .006f && Time.realtimeSinceStartup < until) yield return null;
            Pointer("J1+", false);
            yield return null;
            report.drawingJogMm = (Actual().x - first.x) * 1000;
            Require(report.drawingJogMm >= 5.8f && report.drawingJogMm < 25,
                "SHIFT and held +X jog the real tool; distance mm=" + report.drawingJogMm);
            Click("F1"); Click("1"); Click("ENTER");
            Require(program.Model.Instructions.Count == 3 && program.Model.Instructions[2].TargetPoint == 1,
                "End numeric entry references the same P1 after the recorded L endpoint.");
            FirstRow(); Click("FWD"); yield return Complete(20);
            report.drawingReturnErrorMm = Vector3.Distance(first, Actual()) * 1000;
            Require(report.drawingReturnErrorMm < 1.1f, "Station03 real tool returns to P1; error mm=" + report.drawingReturnErrorMm);
            Require(!station.PenDown, "Raised drawing replay remains pen-up.");
            yield return Capture("SharedPendantPlayer_Station03.png");
            var drawing = program;

            yield return Open(1);
            Require(!drawing.IsBusy && !ReferenceEquals(drawing, program), "Switching stations stops the prior independent controller.");
            CreateProgram(); Click("SHIFT_LEFT"); Click("F1");
            Require(program.Model.Instructions.Count == 1 && !program.Model.Instructions[0].PenDown,
                "Station02 records its actual ring position with no paper-height requirement.");
            FirstRow(); Click("FWD"); yield return Complete(12);
            Require(!station.PenDown && !program.Executor.AllowTracing, "Station02 retains ring motion policy without drawing or tracing.");
            yield return Capture("SharedPendantPlayer_Station02.png");
            app.ClosePanel();
            Require(!app.ActiveStation && !app.ProgrammingVisible, "Normal default view is restored before the test player exits.");
        }

        IEnumerator Open(int index)
        {
            Require(app.TryStartStation(index), "Ordinary app entry opens Station" + (index + 1));
            station = app.ActiveStation; app.ToggleTeachingPendant();
            for (int i = 0; i < 12; i++) yield return null;
            view = app.ActiveTeachingPendant; program = view ? view.Programming : null;
            Require(view && view.Visible && program && app.TeachingPendantVisible && EventSystem.current,
                "Shared teaching LCD and real EventSystem are visible.");
        }

        void CreateProgram()
        {
            Click("SELECT"); Click("F2");
            Require(program.TextEntryFocused, "CREATE owns keyboard input.");
            var input = program.Screen.Header.transform.parent.GetComponentInChildren<InputField>(true);
            Require(input, "Visible CREATE name field exists.");
            string name = "QA_PLAYER_" + Guid.NewGuid().ToString("N").Substring(0, 10).ToUpperInvariant();
            string path = LabPendantProgramStorage.PathFor(station.stationIndex, name);
            Require(!File.Exists(path), "Smoke uses its own unique program filename.");
            input.text = name; Click("F1");
            if (File.Exists(path)) createdPaths.Add(path);
            Require(program.ProgramName == name && File.Exists(path), "F1 creates a persisted station-specific program.");
        }

        void FirstRow() { for (int i = 0; i <= program.Model.Instructions.Count; i++) Click("UP"); }
        Vector3 Actual() => station.transform.InverseTransformPoint(station.ActualToolPosition);
        IEnumerator Complete(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (program.Executor.State == LabTrajectoryState.Running && Time.realtimeSinceStartup < until) yield return null;
            Require(program.Executor.State == LabTrajectoryState.Completed, "FWD completes the real recorded program: " + program.Executor.StatusText);
        }

        PointerEventData Hit(Button button, string key)
        {
            Canvas.ForceUpdateCanvases();
            var rect = button.GetComponent<RectTransform>();
            var pointer = new PointerEventData(EventSystem.current) {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))
            };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Require(hits.Count > 0 && (hits[0].gameObject == button.gameObject || hits[0].gameObject.transform.IsChildOf(button.transform)),
                "Rendered button raycast hits " + key + "; top=" + (hits.Count > 0 ? hits[0].gameObject.name : "none"));
            return pointer;
        }
        void Click(string key)
        {
            Require(view.Buttons.TryGetValue(key, out var button) && button, "Button exists: " + key);
            Require(ExecuteEvents.Execute(button.gameObject, Hit(button, key), ExecuteEvents.pointerClickHandler), "Pointer click dispatches " + key);
        }
        void Pointer(string key, bool down)
        {
            var button = view.Buttons[key];
            if (down) ExecuteEvents.Execute(button.gameObject, Hit(button, key), ExecuteEvents.pointerDownHandler);
            else ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerUpHandler);
        }

        IEnumerator Capture(string name)
        {
            yield return new WaitForSeconds(.5f);
            string path = Path.Combine(Path.GetDirectoryName(outputPath), "Screenshots", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            float until = Time.realtimeSinceStartup + 8;
            while ((!File.Exists(path) || new FileInfo(path).Length < 2048) && Time.realtimeSinceStartup < until) yield return null;
            Require(File.Exists(path) && new FileInfo(path).Length >= 2048, "Screenshot saved: " + path);
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            bool decoded = texture.LoadImage(File.ReadAllBytes(path)); float brightest = 0;
            if (decoded) for (int y = 0; y < 9; y++) for (int x = 0; x < 16; x++)
                brightest = Mathf.Max(brightest, texture.GetPixel(x * (texture.width - 1) / 15, y * (texture.height - 1) / 8).grayscale);
            Destroy(texture);
            Require(decoded && brightest > .05f, "Screenshot contains rendered nonblack content.");
            report.screenshots.Add(path);
        }

        void Require(bool passed, string message)
        {
            report.checks.Add((passed ? "PASS " : "FAIL ") + message);
            if (!passed) throw new InvalidOperationException(message);
        }
        void Finish()
        {
            if (finishing) return;
            finishing = true;
            try
            {
                if (app) app.ClosePanel();
                // These exact GUID paths were created by this run after verifying nonexistence.
                foreach (string path in createdPaths) if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception error) { report.exception += "\nCleanup: " + error; }
            report.passed = string.IsNullOrEmpty(report.exception) && report.checks.Count > 0;
            report.wallSeconds = Time.realtimeSinceStartup - started;
            try { Directory.CreateDirectory(Path.GetDirectoryName(outputPath)); File.WriteAllText(outputPath, JsonUtility.ToJson(report, true)); }
            catch (Exception error) { report.passed = false; Debug.LogException(error); }
            Debug.Log("SHARED_PENDANT_PLAYER_SMOKE=" + report.passed + "; report=" + outputPath);
            Application.Quit(report.passed ? 0 : 1);
        }

        static string ResolveOutput()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-shared-pendant-output")
                {
                    string path = Path.GetFullPath(args[i + 1]);
                    return Path.HasExtension(path) ? path : Path.Combine(path, "SharedPendantPlayerSmoke.json");
                }
            var directory = new DirectoryInfo(Application.dataPath);
            for (int i = 0; directory != null && i < 7; i++, directory = directory.Parent)
                if (Directory.Exists(Path.Combine(directory.FullName, "Assets")) && Directory.Exists(Path.Combine(directory.FullName, "ProjectSettings")))
                    return Path.Combine(directory.FullName, "Documentation", "SharedPendantPlayerSmoke.json");
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), "SharedPendantPlayerSmoke.json");
        }
        static IEnumerator Flatten(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Peek();
                if (!current.MoveNext()) { (current as IDisposable)?.Dispose(); stack.Pop(); continue; }
                if (current.Current is IEnumerator nested) stack.Push(nested); else yield return current.Current;
            }
        }
    }
}
