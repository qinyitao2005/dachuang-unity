using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RoboticsLab.Editor
{
    /// <summary>Runs the real pickup flow in a fresh Play session without resetting or hiding props.</summary>
    public static class LabUserFlowValidation
    {
        static IEnumerator<int> routine;
        static List<string> report;
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        [MenuItem("Robot Lab/Diagnostics/Validate Nearest Helmet User Flow")]
        public static void Validate()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                throw new InvalidOperationException("Start a fresh, unpaused Play session first.");
            if (routine != null) throw new InvalidOperationException("User-flow validation is already running.");
            var app = Object.FindObjectOfType<LabTrainingApp>();
            if (!app || !app.player || !app.player.equipment || !app.player.wornHelmet)
                throw new InvalidOperationException("Training app, player, helmet, or equipment is missing.");
            if (app.HelmetWorn || app.player.equipment.IsEquipped || app.player.IsEquippingHelmet)
                throw new InvalidOperationException("Exit and re-enter Play Mode: this check requires all four untouched rack hats.");
            report = new List<string>
            {
                "UTC=" + DateTime.UtcNow.ToString("O", Invariant),
                "Scene=" + SceneManager.GetActiveScene().path,
                "Real nearest-pickup flow. Props are never manually hidden or reset by this diagnostic."
            };
            routine = Checks(app);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Debug.Log("ROBOT_USER_FLOW_VALIDATION_STARTED");
        }

        static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode stopped during the user-flow check.");
                if (routine != null && routine.MoveNext()) return;
            }
            catch (Exception exception)
            {
                report.Add("FAIL: " + exception.Message);
                Debug.LogException(exception);
            }
            EditorApplication.update -= Tick;
            if (routine != null) routine.Dispose();
            routine = null;
            Directory.CreateDirectory("Documentation");
            File.WriteAllLines("Documentation/UserFlowValidation.txt", report, new UTF8Encoding(false));
            Debug.Log("ROBOT_USER_FLOW_VALIDATION_FINISHED " + string.Join(" | ", report));
        }

        static IEnumerator<int> Checks(LabTrainingApp app)
        {
            LabThirdPerson player = app.player;
            LabHelmetEquipment equipment = player.equipment;
            app.ClosePanel();
            var helmets = new List<Transform>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var rack in root.GetComponentsInChildren<Transform>(true))
                    if (rack.name == "SafetyHelmetRack")
                        foreach (Transform child in rack)
                            if (child.name == "HelmetPickup" || child.name.StartsWith("SpareHelmet", StringComparison.Ordinal))
                                helmets.Add(child);
            Check(helmets.Count == 4 && helmets.All(h => h.gameObject.activeInHierarchy),
                "Fresh session contains four visible rack helmets");
            helmets.Sort((a, b) => a.position.z.CompareTo(b.position.z));
            Transform blue = helmets.SingleOrDefault(h => h.name == "SpareHelmetBlue");
            Check(blue, "The right-side blue helmet exists as a selectable rack prop");
            Check(blue.GetComponent<Renderer>() && blue.GetComponent<Renderer>().sharedMaterial
                && blue.GetComponent<Renderer>().sharedMaterial.HasProperty("_Color")
                && blue.GetComponent<Renderer>().sharedMaterial.color.b > blue.GetComponent<Renderer>().sharedMaterial.color.r,
                "The selected blue prop has a blue-dominant shell material");

            Vector3 fittedSize = HelmetDimensions(player.wornHelmet.transform);
            foreach (Transform helmet in helmets)
            {
                Vector3 size = HelmetDimensions(helmet);
                Check(RelativeSizeError(size, fittedSize) < .002f,
                    helmet.name + " matches the existing fitted physical size " + V(size));
                player.Teleport(FrontOf(helmet), 90);
                yield return 0;
                Check(LabHelmetRackSelection.FindNearest(player.transform) == helmet,
                    "Standing directly in front of " + helmet.name + " selects that exact helmet");
            }

            Vector3 center = Vector3.zero;
            foreach (Transform helmet in helmets) center += helmet.position;
            center /= helmets.Count;
            player.Teleport(new Vector3(center.x - 10, .08f, center.z), 90);
            yield return 0;
            Check(!LabHelmetRackSelection.FindNearest(player.transform), "No helmet is selectable from far outside the interaction range");

            player.Teleport(FrontOf(blue), 90);
            yield return 0;
            Check(LabHelmetRackSelection.FindNearest(player.transform) == blue,
                "Returning to the right-side blue helmet selects blue before the real EquipHelmet call");
            Vector3 pickupSize = HelmetDimensions(blue);
            LocalPose[] wornBefore = CaptureLocalPoses(player.wornHelmet.transform);
            Dictionary<string, Material[]> sourceMaterials = CaptureMaterials(blue);
            float maximumSizeError = 0;
            bool handoffObserved = false;
            double deadline = EditorApplication.timeSinceStartup + 20;
            app.EquipHelmet();
            Check(equipment.IsEquipping && !app.HelmetWorn, "EquipHelmet starts the real asynchronous wearing sequence");
            Check(app.helmetPickup == blue && equipment.pickupVisual == blue.gameObject,
                "The application and wearing component both reference the nearest blue helmet");
            Check(LocalPosesUnchanged(wornBefore),
                "Appearance transfer preserves the worn helmet hierarchy, local pose, and local scale before animation");
            Check(MaterialsMatch(sourceMaterials, player.wornHelmet.transform),
                "The actual pickup flow copies every corresponding blue-helmet renderer material");

            while (equipment.IsEquipping)
            {
                if (EditorApplication.timeSinceStartup > deadline)
                    throw new TimeoutException("The real wearing animation did not finish within 20 seconds.");
                foreach (Transform helmet in helmets)
                    if (helmet != blue && !helmet.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("An unselected helmet became hidden during wearing: " + helmet.name);
                if (player.wornHelmet.activeInHierarchy)
                {
                    handoffObserved |= !blue.gameObject.activeInHierarchy;
                    maximumSizeError = Mathf.Max(maximumSizeError, RelativeSizeError(HelmetDimensions(player.wornHelmet.transform), pickupSize));
                }
                yield return 0;
            }
            yield return 0;
            Check(app.HelmetWorn && equipment.IsEquipped && player.wornHelmet.activeInHierarchy,
                "The wearing animation completes and the visible character is helmet-equipped");
            Check(handoffObserved && !blue.gameObject.activeSelf && !blue.gameObject.activeInHierarchy,
                "Only the selected blue pickup disappears during the real handoff");
            foreach (Transform helmet in helmets)
                if (helmet != blue) Check(helmet.gameObject.activeSelf && helmet.gameObject.activeInHierarchy,
                    "Unselected " + helmet.name + " remains visible on the rack");
            Check(MaterialsMatch(sourceMaterials, player.wornHelmet.transform),
                "The fitted helmet retains the selected blue material on all corresponding parts");
            Vector3 finalSize = HelmetDimensions(player.wornHelmet.transform);
            maximumSizeError = Mathf.Max(maximumSizeError, RelativeSizeError(finalSize, pickupSize));
            Check(maximumSizeError < .002f,
                "Helmet physical dimensions stay constant through handoff and fit: pickup=" + V(pickupSize)
                + ", fitted=" + V(finalSize) + ", maxRelativeError=" + maximumSizeError.ToString("F6", Invariant));
            Transform remaining = LabHelmetRackSelection.FindNearest(player.transform);
            Check(!remaining || remaining != blue, "The inactive picked helmet is excluded from subsequent nearest searches");
            foreach (int step in WallLearningChecks(app)) yield return step;
            report.Add("NOTE: The character remains wearing the blue helmet with the wall-screen knowledge check completed. No lesson or quiz flags were set by the diagnostic.");
        }

        static IEnumerable<int> WallLearningChecks(LabTrainingApp app)
        {
            LabThirdPerson player = app.player;
            player.Teleport(new Vector3(4.9f, .08f, 10.5f), 90);
            yield return 0;
            app.OpenLesson();
            Check(app.panel == "lesson", "Approaching the wall and opening the lesson activates the learning flow");
            double settleUntil = EditorApplication.timeSinceStartup + 1.2;
            while (EditorApplication.timeSinceStartup < settleUntil) yield return 0;
            var canvas = Object.FindObjectsOfType<Canvas>().SingleOrDefault(c => c.name == "WallLearningCanvas");
            Check(canvas && canvas.renderMode == RenderMode.WorldSpace && canvas.gameObject.activeInHierarchy,
                "The active lesson is displayed on a world-space WallLearningCanvas");
            Check(canvas.transform.parent && canvas.transform.parent.name == "TeachingDisplay",
                "The learning canvas belongs to the physical TeachingDisplay");
            Check(player.learningFocus && canvas.worldCamera == player.viewCamera,
                "The learning camera and canvas event camera use the player's settled wall view");
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            Check(raycaster && raycaster.isActiveAndEnabled && EventSystem.current,
                "The wall has an active GraphicRaycaster and EventSystem");
            Check(app.lessonVideo, "The wall lesson has a configured VideoPlayer");
            long before = app.lessonVideo.frame;
            double deadline = EditorApplication.timeSinceStartup + 12;
            while ((!app.lessonVideo.isPrepared || app.lessonVideo.frame <= before) && EditorApplication.timeSinceStartup < deadline)
                yield return 0;
            Check(app.lessonVideo.isPrepared && app.lessonVideo.frame > before,
                "The wall lesson video decodes and advances frames: " + before + " -> " + app.lessonVideo.frame);
            var videoImage = canvas.GetComponentsInChildren<RawImage>(true).SingleOrDefault(r => r.name == "TeachingVideo");
            Check(videoImage && videoImage.gameObject.activeInHierarchy && videoImage.texture
                && videoImage.texture == app.lessonVideo.targetTexture,
                "The visible wall video image uses the playing VideoPlayer render texture");
            foreach (int step in CaptureGameView("WallLesson")) yield return step;

            ClickWallButton(canvas, "OpenText");
            yield return 0;
            Check(app.panel == "lessonText", "Clicking the wall's text-course button opens the text lesson");
            foreach (int step in CaptureGameView("WallTextLesson")) yield return step;
            ClickWallButton(canvas, "CompleteTextLesson");
            yield return 0;
            Check(app.LessonCompleted && app.panel == "quiz" && app.questionIndex == 0 && app.CorrectAnswers == 0,
                "Clicking the wall's read-complete button enters the quiz through the actual lesson API");
            foreach (int step in CaptureGameView("WallQuiz")) yield return step;

            int questionCount = app.QuestionCount;
            Check(questionCount > 0, "The loaded question bank contains at least one question");
            for (int question = 0; question < questionCount; question++)
            {
                LabQuestion current = app.CurrentQuestion;
                Check(app.panel == "quiz" && app.questionIndex == question && current != null,
                    "Wall quiz displays question " + (question + 1) + " from the loaded bank");
                if (question == 0)
                {
                    int wrong = (current.correct + 1) % current.options.Length;
                    ClickWallButton(canvas, "Option_" + wrong);
                    yield return 0;
                    Check(!app.Answered && app.CorrectAnswers == 0 && !app.QuizPassed && !string.IsNullOrEmpty(app.Feedback),
                        "A real wall-button wrong answer shows feedback without scoring or unlocking practice");
                    Check(!FindCanvasButton(canvas, "NextQuestion").interactable,
                        "The wall Next button stays disabled after a wrong answer");
                    foreach (int step in CaptureGameView("WallQuizWrongAnswer")) yield return step;
                }
                ClickWallButton(canvas, "Option_" + current.correct);
                yield return 0;
                Check(app.Answered && app.CorrectAnswers == question + 1,
                    "The correct wall-button answer scores question " + (question + 1));
                if (question == 0)
                    foreach (int step in CaptureGameView("WallQuizCorrectAnswer")) yield return step;
                ClickWallButton(canvas, "NextQuestion");
                yield return 0;
            }
            Check(app.QuizPassed && app.CorrectAnswers == questionCount && !app.IsLearningPanel,
                "Completing all questions via wall buttons unlocks practice and exits the learning view");
            Check(canvas.transform.Find("QuizPassed") && canvas.transform.Find("QuizPassed").gameObject.activeInHierarchy,
                "The physical wall screen displays the completed-learning page");
            foreach (int step in CaptureGameView("WallQuizPassed")) yield return step;
        }

        static Button FindCanvasButton(Canvas canvas, string name)
        {
            Button button = canvas.GetComponentsInChildren<Button>(true).SingleOrDefault(b => b.name == name);
            if (!button) throw new InvalidOperationException("Missing wall button: " + name);
            return button;
        }

        static void ClickWallButton(Canvas canvas, string name)
        {
            Button button = FindCanvasButton(canvas, name);
            Check(button.gameObject.activeInHierarchy && button.isActiveAndEnabled && button.IsInteractable(),
                "Wall button is visible and interactable: " + name);
            Camera camera = canvas.worldCamera;
            RectTransform rect = (RectTransform)button.transform;
            Vector3 projected = camera.WorldToScreenPoint(rect.TransformPoint(rect.rect.center));
            Check(projected.z > 0 && camera.pixelRect.Contains(new Vector2(projected.x, projected.y)),
                "Wall button center projects inside the actual camera viewport: " + name);
            var pointer = new PointerEventData(EventSystem.current)
            {
                pointerId = -1,
                position = new Vector2(projected.x, projected.y),
                pressPosition = new Vector2(projected.x, projected.y),
                button = PointerEventData.InputButton.Left,
                eligibleForClick = true
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            RaycastResult hit = hits.FirstOrDefault(h => ExecuteEvents.GetEventHandler<IPointerClickHandler>(h.gameObject));
            GameObject target = hit.gameObject ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject) : null;
            Check(target == button.gameObject && hit.module is GraphicRaycaster,
                "A screen-point raycast reaches the intended wall button first: " + name);
            pointer.pointerCurrentRaycast = hit;
            pointer.pointerPressRaycast = hit;
            pointer.pointerPress = button.gameObject;
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            Check(ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler),
                "The normal pointer-click handler executes for wall button: " + name);
        }

        static IEnumerable<int> CaptureGameView(string name)
        {
            // Capture the actual Game view, including IMGUI, so review can detect any central
            // overlay regression. A camera-only RenderTexture would omit those overlays.
            yield return 0;
            yield return 0;
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Documentation/Screenshots"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name + ".png");
            DateTime previousWrite = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path, 1);
            double deadline = EditorApplication.timeSinceStartup + 8;
            while ((!File.Exists(path) || File.GetLastWriteTimeUtc(path) == previousWrite) && EditorApplication.timeSinceStartup < deadline)
                yield return 0;
            Check(File.Exists(path) && File.GetLastWriteTimeUtc(path) != previousWrite,
                "Captured actual Game-view screenshot " + name + " (" + Screen.width + "x" + Screen.height + ")");
        }

        static Vector3 FrontOf(Transform helmet) => new Vector3(helmet.position.x - .85f, .08f, helmet.position.z);
        static float RelativeSizeError(Vector3 actual, Vector3 expected) => Vector3.Distance(actual, expected) / Mathf.Max(.0001f, expected.magnitude);

        static Vector3 HelmetDimensions(Transform helmet)
        {
            // Rotation-independent physical dimensions; world AABBs change when the head tilts.
            Bounds total = new Bounds();
            bool first = true;
            Quaternion inverse = Quaternion.Inverse(helmet.rotation);
            foreach (var filter in helmet.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) continue;
                Bounds bounds = filter.sharedMesh.bounds;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 p = inverse * (filter.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z))) - helmet.position);
                    if (first) { total = new Bounds(p, Vector3.zero); first = false; } else total.Encapsulate(p);
                }
            }
            if (first) throw new InvalidOperationException("No mesh geometry found for helmet " + helmet.name);
            return total.size;
        }

        struct LocalPose
        {
            public Transform transform, parent;
            public Vector3 position, scale;
            public Quaternion rotation;
        }

        static LocalPose[] CaptureLocalPoses(Transform root)
        {
            return root.GetComponentsInChildren<Transform>(true).Select(t => new LocalPose
            {
                transform = t, parent = t.parent, position = t.localPosition, rotation = t.localRotation, scale = t.localScale
            }).ToArray();
        }

        static bool LocalPosesUnchanged(LocalPose[] poses)
        {
            return poses.All(p => p.transform && p.transform.parent == p.parent
                && Vector3.Distance(p.transform.localPosition, p.position) < .000001f
                && Quaternion.Angle(p.transform.localRotation, p.rotation) < .001f
                && Vector3.Distance(p.transform.localScale, p.scale) < .000001f);
        }

        static Dictionary<string, Material[]> CaptureMaterials(Transform root)
        {
            var result = new Dictionary<string, Material[]>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                string path = "";
                for (Transform t = renderer.transform; t != root; t = t.parent)
                    path = "/" + t.name + "[" + t.GetSiblingIndex() + "]" + path;
                Renderer[] components = renderer.GetComponents<Renderer>();
                result.Add(path + ":" + Array.IndexOf(components, renderer), renderer.sharedMaterials);
            }
            return result;
        }

        static bool MaterialsMatch(Dictionary<string, Material[]> expected, Transform root)
        {
            Dictionary<string, Material[]> actual = CaptureMaterials(root);
            if (expected.Count != actual.Count) return false;
            foreach (var pair in expected)
            {
                if (!actual.TryGetValue(pair.Key, out Material[] materials) || materials.Length != pair.Value.Length) return false;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != pair.Value[i]) return false;
            }
            return true;
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            report.Add("PASS: " + message);
        }

        static string V(Vector3 value) => "(" + value.x.ToString("F6", Invariant) + ","
            + value.y.ToString("F6", Invariant) + "," + value.z.ToString("F6", Invariant) + ")";
    }
}
