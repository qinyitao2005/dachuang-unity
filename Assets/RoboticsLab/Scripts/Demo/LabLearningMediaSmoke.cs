using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>Opt-in acceptance in the real standalone scene; never runs in ordinary play.</summary>
    public sealed class LabLearningMediaSmoke : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public bool passed;
            public string unityVersion, exception;
            public float wallSeconds;
            public int questionsAnswered;
            public List<string> checks = new List<string>();
            public List<string> screenshots = new List<string>();
            public List<string> videoFiles = new List<string>();
            public List<string> raycastDiagnostics = new List<string>();
        }
        readonly Report report = new Report();
        LabTrainingApp app;
        string outputPath;
        float started;
        bool finishing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--learning-media-smoke") < 0 && Array.IndexOf(args, "--quiz-preview") < 0) return;
            if (!FindObjectOfType<LabLearningMediaSmoke>())
                new GameObject("LearningMediaSmoke").AddComponent<LabLearningMediaSmoke>();
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            // Optional manual UI review entry; ordinary launches never use it.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--quiz-preview") >= 0)
            {
                while (!(app = FindObjectOfType<LabTrainingApp>())) yield return null;
                while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
                app.HelmetWorn = app.LessonCompleted = true;
                app.QuizPassed = false;
                app.player.Teleport(app.teachingPoint.position + Vector3.up * .1f, 0);
                app.StartQuiz();
                Destroy(this);
                yield break;
            }
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
            if (!finishing && started > 0 && Time.realtimeSinceStartup - started > 120)
            {
                report.exception = "Learning media smoke exceeded 120 seconds (including video preparation).";
                StopAllCoroutines(); Finish();
            }
        }

        IEnumerator Checks()
        {
            float until = Time.realtimeSinceStartup + 15;
            while (!(app = FindObjectOfType<LabTrainingApp>()) && Time.realtimeSinceStartup < until) yield return null;
            Require(app && app.stations != null && app.stations.Length == 3, "Normal standalone opens the three-station scene.");
            while (!UnityEngine.Rendering.SplashScreen.isFinished && Time.realtimeSinceStartup - started < 20) yield return null;
            yield return new WaitForSecondsRealtime(.6f);
            app.ClosePanel(); app.HelmetWorn = app.LessonCompleted = true; app.QuizPassed = false;
            Require(app.QuestionCount == 20, "Runtime question bank contains exactly 20 questions.");
            Require(app.player && app.teachingPoint, "Player and physical teaching interaction point exist.");
            // Test setup places the player within the ordinary interaction radius;
            // OpenLesson itself still applies the production distance and safety gates.
            app.player.Teleport(app.teachingPoint.position + Vector3.up * .1f, 0);
            app.OpenLesson();
            yield return new WaitForSecondsRealtime(1.2f);
            Require(app.panel == "lesson" && app.player.learningFocus, "Normal nearby lesson entry focuses the physical wall screen.");
            Require(app.lessonVideo && Path.GetFileName(app.lessonVideo.url) == "TrainingLesson.mp4",
                "The physical wall lesson uses the combined three-station video.");
            until = Time.realtimeSinceStartup + 15;
            while (!app.lessonVideo.isPrepared && Time.realtimeSinceStartup < until) yield return null;
            Require(app.lessonVideo.isPrepared && app.lessonVideo.length > 30, "The wall lesson prepares the new combined video.");
            var wallPlayback = app.wallLearningDisplay.transform.Find("WallLearningCanvas/LearningToolbar/Playback").GetComponent<Button>();
            yield return Capture("WallScreen_Ready.png");
            Click(wallPlayback, "wall video playback");
            yield return new WaitForSecondsRealtime(1.2f);
            var wallPicture = app.wallLearningDisplay.transform.Find("WallLearningCanvas/VideoLesson/TeachingVideo").GetComponent<RawImage>();
            Require(app.lessonVideo.isPlaying && app.lessonVideo.time > .1 && wallPicture.texture == app.lessonVideo.targetTexture,
                "The physical wall screen displays decoded frames from the combined demonstration.");
            yield return Capture("WallScreen_CombinedDemonstration.png");
            Click(wallPlayback, "wall video pause");
            Require(!app.lessonVideo.isPlaying, "Wall playback button pauses the combined demonstration.");
            app.StartQuiz();
            Require(app.wallLearningDisplay, "Physical wall learning screen exists.");
            app.wallLearningDisplay.EnsureBuilt(); app.wallLearningDisplay.RefreshDisplay();
            yield return new WaitForSecondsRealtime(1.2f);
            Canvas.ForceUpdateCanvases();
            var quiz = app.wallLearningDisplay.transform.Find("WallLearningCanvas/KnowledgeQuiz");
            Require(quiz && quiz.gameObject.activeInHierarchy, "Quiz is on the actual room screen.");
            var reveal = quiz.GetComponentInChildren<LabQuizAnswerReveal>(true);
            Require(reveal && !reveal.IsRevealed && !app.Answered, "Correct answer starts covered.");
            yield return Capture("WallQuiz_Covered.png");
            var pointer = Hit((RectTransform)reveal.transform, "covered correct answer");
            int correctBefore = app.CorrectAnswers;
            Require(ExecuteEvents.Execute(reveal.gameObject, pointer, ExecuteEvents.pointerEnterHandler), "Pointer enter dispatches to answer cover.");
            Require(reveal.IsRevealed, "Pointer hover reveals the answer.");
            Require(!app.Answered && app.CorrectAnswers == correctBefore, "Revealing an answer does not answer the question or change the score.");
            yield return Capture("WallQuiz_Revealed.png");
            ExecuteEvents.Execute(reveal.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            Require(!reveal.IsRevealed && !app.Answered && app.CorrectAnswers == correctBefore, "Leaving the card covers the answer without submitting.");

            var seen = new HashSet<string>();
            for (int index = 0; index < 20; index++)
            {
                LabQuestion q = app.CurrentQuestion;
                Require(q != null && !string.IsNullOrWhiteSpace(q.question) && seen.Add(q.question)
                    && q.options != null && q.options.Length == 4 && q.correct >= 0 && q.correct < 4
                    && !string.IsNullOrWhiteSpace(q.explanation), "Question " + (index + 1) + " has a unique prompt, four choices, valid answer and explanation.");
                var options = new HashSet<string>();
                foreach (string option in q.options) Require(!string.IsNullOrWhiteSpace(option) && options.Add(option), "Question " + (index + 1) + " choice is nonempty and unique.");
                app.wallLearningDisplay.RefreshDisplay();
                Click(quiz.Find("Option_" + q.correct).GetComponent<Button>(), "question " + (index + 1) + " correct option");
                Require(app.Answered && app.CorrectAnswers == index + 1, "Rendered correct option awards exactly one answer for question " + (index + 1));
                Click(quiz.Find("NextQuestion").GetComponent<Button>(), "next question " + (index + 1));
                report.questionsAnswered++;
                yield return null;
            }
            Require(app.QuizPassed && app.CorrectAnswers == 20 && app.questionIndex == 20 && app.panel == "", "All 20 real UI questions complete and unlock practice.");

            Require(app.TryStartStation(0), "Normal entry opens Station1 after passing the quiz.");
            yield return new WaitForSecondsRealtime(.8f);
            Require(app.TeachingPendantVisible, "Latest entry automatically opens the shared teaching pendant.");
            yield return Capture("Station01_Assessment_DemoEntry.png");
            var tracingPanelField = typeof(LabTrainingApp).GetField("tracingPanel", BindingFlags.NonPublic | BindingFlags.Instance);
            var tracingPanel = tracingPanelField != null ? tracingPanelField.GetValue(app) : null;
            var historyField = typeof(LabTracingPanel).GetField("historyVisible", BindingFlags.NonPublic | BindingFlags.Instance);
            Require(tracingPanel != null && historyField != null, "Scoring history view is available for visual capture.");
            // Pure display-state change; the smoke does not invent a score or dispatch a fake IMGUI click.
            historyField.SetValue(tracingPanel, true);
            yield return Capture("Station01_ScoreHistory.png");
            historyField.SetValue(tracingPanel, false);

            var station = app.ActiveStation;
            var program = app.ActiveTeachingPendant.Programming;
            string before = JsonUtility.ToJson(program.Model);
            string programName = program.ProgramName;
            app.OpenDemoVideo();
            // A newly created Canvas needs its first rendered frame before
            // GraphicRaycaster can use the graphics' nonnegative depth values.
            yield return new WaitForSecondsRealtime(.2f);
            Canvas.ForceUpdateCanvases();
            var viewer = app.DemoVideoViewer;
            Require(viewer && viewer.Visible && viewer.SelectedClipIndex == 1, "Station1 demo button opens its station-specific video.");
            Require(app.DemoInputBlocked && Mathf.Approximately(Time.timeScale, 0), "Video modal blocks station controls and suspends simulation.");
            program.HandleButton("SHIFT_LEFT");
            program.HandleButton("F1");
            Require(before == JsonUtility.ToJson(program.Model) && program.ManualDirection() == Vector3.zero,
                "Pendant recording and motion input are ignored while the video covers the workcell.");
            for (int index = 0; index < 4; index++)
            {
                Button tab = FindVideoButton("VideoTab" + index);
                Click(tab, "video tab " + index);
                yield return ReadyVideo(viewer, index);
                if (index == 0) yield return Capture("DemonstrationVideo_AllStations.png");
                double movingTime = viewer.Playback.time;
                yield return new WaitForSecondsRealtime(1.1f);
                Require(viewer.Playback.time > movingTime + .08, "Clip " + index + " advances while Time.timeScale is zero.");
                Click(FindVideoButton("ToggleVideoPlayback"), "pause clip " + index);
                double pausedTime = viewer.Playback.time;
                yield return new WaitForSecondsRealtime(.3f);
                Require(!viewer.Playback.isPlaying && Math.Abs(viewer.Playback.time - pausedTime) < .1, "Clip " + index + " remains paused.");
                Click(FindVideoButton("RestartVideo"), "restart clip " + index);
                yield return new WaitForSecondsRealtime(.25f);
                Require(viewer.Playback.isPlaying && viewer.Playback.time < .9 && viewer.Playback.time < pausedTime - .15,
                    "Clip " + index + " restarts from the beginning.");
            }
            Click(FindVideoButton("CloseVideo"), "close demonstration video");
            yield return null;
            Require(!viewer.Visible && Mathf.Approximately(Time.timeScale, 1) && !app.DemoInputBlocked, "Closing the real modal restores simulation and controls.");
            Require(app.ActiveStation == station && ReferenceEquals(program, app.ActiveTeachingPendant.Programming)
                && programName == program.ProgramName && before == JsonUtility.ToJson(program.Model), "Closing video preserves the active station and program document.");
            Require(!program.IsBusy, "Closing a video does not start a robot program.");

            // Exercise the exact shortcut handler used by Update when available.
            MethodInfo shortcut = typeof(LabDemoVideoViewer).GetMethod("HandleShortcut", BindingFlags.Public | BindingFlags.Instance);
            if (shortcut != null)
            {
                app.OpenDemoVideo();
                shortcut.Invoke(viewer, new object[] { KeyCode.Escape });
                yield return null;
                Require(!viewer.Visible && app.ActiveStation == station && before == JsonUtility.ToJson(program.Model), "Escape shortcut handler closes video without exiting the workcell or losing its program.");
            }
            else report.checks.Add("NOTE Escape uses the same Close method; physical keyboard injection is not part of this smoke.");
            for (int index = 1; index < 3; index++)
            {
                Require(app.TryStartStation(index), "Normal entry opens Station" + (index + 1));
                yield return null;
                var selectedStation = app.ActiveStation;
                app.OpenDemoVideo();
                Require(viewer.SelectedClipIndex == index + 1, "Station" + (index + 1) + " demo entry selects its own video.");
                viewer.Close();
                yield return null;
                Require(app.ActiveStation == selectedStation && app.TeachingPendantVisible,
                    "Station" + (index + 1) + " remains active after closing its demo.");
            }
            app.ClosePanel();
            app.OpenDemoVideo();
            Require(viewer.SelectedClipIndex == 0, "Outside a workcell the demo entry selects the combined lesson.");
            viewer.Close();
            yield return null;
        }

        IEnumerator ReadyVideo(LabDemoVideoViewer viewer, int index)
        {
            string path = Path.Combine(Application.streamingAssetsPath, LabDemoVideoViewer.FileNames[index]);
            Require(File.Exists(path) && new FileInfo(path).Length > 8192, "Real MP4 exists: " + path);
            float until = Time.realtimeSinceStartup + 15;
            while ((!viewer.Playback.isPrepared || !viewer.Playback.isPlaying || viewer.Playback.frame < 0) && Time.realtimeSinceStartup < until) yield return null;
            Require(viewer.SelectedClipIndex == index && viewer.Playback.isPrepared && viewer.Playback.isPlaying
                && viewer.Playback.length > 1 && viewer.Playback.width > 0 && viewer.Playback.height > 0,
                "Clip " + index + " prepares and decodes real video frames.");
            report.videoFiles.Add(path);
        }

        Button FindVideoButton(string name)
        {
            Transform target = app.DemoVideoViewer.transform.Find("DemonstrationVideoCanvas/Content/" + name);
            Require(target, "Video control exists: " + name);
            return target.GetComponent<Button>();
        }

        PointerEventData Hit(RectTransform rect, string label)
        {
            Canvas.ForceUpdateCanvases();
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var pointer = new PointerEventData(EventSystem.current) {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center))
            };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            bool matches = hits.Count > 0 && (hits[0].gameObject == rect.gameObject || hits[0].gameObject.transform.IsChildOf(rect));
            if (!matches) DiagnoseRaycast(rect, canvas, camera, pointer, hits, label);
            Require(matches,
                "Rendered raycast hits " + label + "; top=" + (hits.Count > 0 ? hits[0].gameObject.name : "none"));
            return pointer;
        }
        void Click(Button button, string label)
        {
            Require(button && button.gameObject.activeInHierarchy && button.IsInteractable(), "Button is visible and interactable: " + label);
            Require(ExecuteEvents.Execute(button.gameObject, Hit(button.GetComponent<RectTransform>(), label), ExecuteEvents.pointerClickHandler), "Pointer click dispatches " + label);
        }

        void DiagnoseRaycast(RectTransform rect, Canvas canvas, Camera camera, PointerEventData pointer, List<RaycastResult> hits, string label)
        {
            var details = new System.Text.StringBuilder();
            Vector3 center = rect.TransformPoint(rect.rect.center);
            details.AppendLine("Raycast target: " + label + " / " + Hierarchy(rect));
            details.AppendLine("panel=" + app.panel + "; active=" + rect.gameObject.activeInHierarchy + "; screen=" + Screen.width + "x" + Screen.height
                + "; projected=" + pointer.position + "; worldCenter=" + center + "; forward=" + rect.forward);
            details.AppendLine("canvas=" + (canvas ? Hierarchy(canvas.transform) : "none")
                + "; renderMode=" + (canvas ? canvas.renderMode.ToString() : "none"));
            var raycaster = canvas ? canvas.GetComponent<GraphicRaycaster>() : null;
            if (raycaster) details.AppendLine("GraphicRaycaster enabled=" + raycaster.isActiveAndEnabled
                + "; blocking=" + raycaster.blockingObjects + "; ignoreReversed=" + raycaster.ignoreReversedGraphics);
            if (camera)
            {
                details.AppendLine("camera=" + Hierarchy(camera.transform) + "; position=" + camera.transform.position
                    + "; forward=" + camera.transform.forward + "; viewport=" + camera.WorldToViewportPoint(center)
                    + "; near/far=" + camera.nearClipPlane + "/" + camera.farClipPlane);
                Ray ray = camera.ScreenPointToRay(pointer.position);
                var collisions = Physics.RaycastAll(ray, camera.farClipPlane, ~0, QueryTriggerInteraction.Collide);
                Array.Sort(collisions, (left, right) => left.distance.CompareTo(right.distance));
                foreach (RaycastHit collision in collisions)
                    details.AppendLine("Physics: " + Hierarchy(collision.transform) + "; distance=" + collision.distance
                        + "; point=" + collision.point + "; trigger=" + collision.collider.isTrigger);
            }
            foreach (RaycastResult hit in hits)
                details.AppendLine("UI: " + Hierarchy(hit.gameObject.transform) + "; distance=" + hit.distance
                    + "; order=" + hit.sortingOrder + "; module=" + hit.module);
            string diagnostic = details.ToString();
            report.raycastDiagnostics.Add(diagnostic);
            Debug.LogWarning(diagnostic);
        }

        static string Hierarchy(Transform item)
        {
            if (!item) return "none";
            string path = item.name;
            while (item.parent) { item = item.parent; path = item.name + "/" + path; }
            return path;
        }

        IEnumerator Capture(string name)
        {
            yield return new WaitForSecondsRealtime(.15f);
            string path = Path.Combine(Path.GetDirectoryName(outputPath), "Screenshots", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            float until = Time.realtimeSinceStartup + 8;
            while ((!File.Exists(path) || new FileInfo(path).Length < 2048) && Time.realtimeSinceStartup < until) yield return null;
            Require(File.Exists(path) && new FileInfo(path).Length >= 2048, "Rendered screenshot saved: " + name);
            var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            bool decoded = image.LoadImage(File.ReadAllBytes(path));
            float brightest = 0;
            if (decoded) for (int y = 0; y < 9; y++) for (int x = 0; x < 16; x++)
                brightest = Mathf.Max(brightest, image.GetPixel(x * (image.width - 1) / 15, y * (image.height - 1) / 8).grayscale);
            Destroy(image);
            Require(decoded && brightest > .05f, "Screenshot contains visible rendered content: " + name);
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
            try { if (app && app.DemoVideoViewer) app.DemoVideoViewer.Close(); if (app) app.ClosePanel(); }
            catch (Exception error) { report.exception += "\nCleanup: " + error; }
            report.passed = string.IsNullOrEmpty(report.exception) && report.questionsAnswered == 20 && report.videoFiles.Count == 4;
            report.wallSeconds = Time.realtimeSinceStartup - started;
            try { Directory.CreateDirectory(Path.GetDirectoryName(outputPath)); File.WriteAllText(outputPath, JsonUtility.ToJson(report, true)); }
            catch (Exception error) { report.passed = false; Debug.LogException(error); }
            Debug.Log("LEARNING_MEDIA_SMOKE=" + report.passed + "; report=" + outputPath);
            Application.Quit(report.passed ? 0 : 1);
        }
        static string ResolveOutput()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-learning-media-output" || args[i] == "--learning-media-output")
                {
                    string path = Path.GetFullPath(args[i + 1]);
                    return Path.HasExtension(path) ? path : Path.Combine(path, "LearningMediaSmoke.json");
                }
            var directory = new DirectoryInfo(Application.dataPath);
            for (int i = 0; directory != null && i < 7; i++, directory = directory.Parent)
                if (Directory.Exists(Path.Combine(directory.FullName, "Assets")) && Directory.Exists(Path.Combine(directory.FullName, "ProjectSettings")))
                    return Path.Combine(directory.FullName, "Documentation", "LearningMediaSmoke.json");
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), "LearningMediaSmoke.json");
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
