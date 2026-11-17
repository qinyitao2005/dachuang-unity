using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>Opt-in recording of real shared-pendant UI, motion and assessment; never runs in an ordinary player.</summary>
    public sealed class LabStationDemoCapture : MonoBehaviour
    {
        [Serializable] sealed class CaptureReport
        {
            public bool passed;
            public int station, frames, fps = 20, instructions, points;
            public string exception, program, unityVersion;
            public float score, coverage, accuracy, returnErrorMm;
            public List<string> events = new List<string>();
            public List<string> photos = new List<string>();
        }
        readonly CaptureReport report = new CaptureReport();
        LabTrainingApp app;
        LabRobotStation station;
        LabStation1PendantView view;
        LabStation1Programming program;
        string output, frames, createdPath, heading = "", detail = "";
        bool recording, done;
        float markerUntil;
        Vector2 marker;
        GUIStyle titleStyle, detailStyle;
        Texture2D frameTexture;
        int index;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--station-demo-capture") < 0) return;
            new GameObject("StationDemoCapture").AddComponent<LabStationDemoCapture>();
        }
        IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 20;
            Time.captureFramerate = 20;
            var args = Environment.GetCommandLineArgs();
            output = Path.Combine(Path.GetDirectoryName(Application.dataPath), "StationDemos");
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-demo-output") output = Path.GetFullPath(args[i + 1]);
                if (args[i] == "-demo-station") index = Mathf.Clamp(int.Parse(args[i + 1]) - 1, 0, 2);
            }
            report.station = index + 1; report.unityVersion = Application.unityVersion;
            frames = Path.Combine(output, "Station0" + (index + 1), "frames");
            Directory.CreateDirectory(frames);
            Require(Directory.GetFiles(frames, "*.jpg").Length == 0, "Capture folder is new; existing frames will not be overwritten.");
            StartCoroutine(CaptureFrames());
            var sequence = Flatten(Run());
            while (true)
            {
                bool next = false; object waiting = null;
                try { next = sequence.MoveNext(); if (next) waiting = sequence.Current; }
                catch (Exception e) { report.exception = e.ToString(); Debug.LogException(e); }
                if (!next) break;
                yield return waiting;
            }
            recording = false; done = true;
            try
            {
                if (app) app.ClosePanel();
                // Only the exact unique file created by this capture is removed.
                if (!string.IsNullOrEmpty(createdPath) && File.Exists(createdPath)) File.Delete(createdPath);
            }
            catch (Exception e) { report.exception += "\nCleanup: " + e; }
            report.passed = string.IsNullOrEmpty(report.exception) && (report.frames > 100 || report.photos.Count > 0 && Array.IndexOf(args, "--demo-photo-only") >= 0);
            File.WriteAllText(Path.Combine(output, "Station0" + (index + 1), "CaptureReport.json"), JsonUtility.ToJson(report, true));
            Time.captureFramerate = 0;
            Debug.Log("STATION_DEMO_CAPTURE=" + report.passed + "; frames=" + report.frames);
            Application.Quit(report.passed ? 0 : 1);
        }
        IEnumerator Run()
        {
            while (!(app = FindObjectOfType<LabTrainingApp>())) yield return null;
            while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            yield return Pause(.5f);
            app.ClosePanel(); app.HelmetWorn = app.LessonCompleted = app.QuizPassed = true;
            Require(app.TryStartStation(index), "Ordinary station entry succeeds.");
            station = app.ActiveStation;
            for (int i = 0; i < 16; i++) yield return null;
            view = app.ActiveTeachingPendant; program = view ? view.Programming : null;
            Require(view && view.Visible && program, "The current shared teaching pendant is visible.");
            if (index == 2) LabRobotOnlyPhoto.Capture(station, output, report.photos);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--demo-photo-only") >= 0) yield break;
            if (index == 1) gameObject.AddComponent<LabDemoRingDetail>().Initialize(station, app.uiFont);
            recording = true;
            Caption("0" + (index + 1) + " · " + station.RoleLabel, "操作 → L/C 示教编程 → 实际运行。本视频由当前 Unity 工程实时渲染录制。");
            yield return Pause(4);
            yield return CreateProgram();
            if (index == 0) yield return TraceDemo();
            else if (index == 1) yield return RingDemo();
            else yield return DrawingDemo();
            report.instructions = program.Model.Instructions.Count; report.points = program.Model.Points.Count;
            Caption("演示完成 · " + station.RoleLabel, "程序点位自动保存；每台工位独立保存。退出后可从 SELECT PROGRAM 再次打开运行。");
            yield return Pause(5);
        }
        IEnumerator CreateProgram()
        {
            Caption("编程 · 新建工位程序", "SELECT 打开程序列表 → F2 CREATE → 输入名称 → F1 END 完成创建。");
            yield return Press("SELECT", .9f); yield return Press("F2", 1.2f);
            Require(program.TextEntryFocused, "CREATE opens the name field.");
            var input = program.Screen.Header.transform.parent.GetComponentInChildren<InputField>(true);
            string name = "DEMO_S" + (index + 1) + "_" + DateTime.Now.ToString("HHmmss");
            createdPath = LabPendantProgramStorage.PathFor(index, name);
            Require(!File.Exists(createdPath), "Recording uses a new program name.");
            input.text = name; report.program = name;
            yield return Pause(1); yield return Press("F1", 1.5f);
            Require(program.ProgramName == name, "Named program is created.");
            yield return Press("SHIFT_LEFT", .8f);
        }
        IEnumerator TraceDemo()
        {
            var trace = app.ActiveTracing;
            trace.SelectShape(1);
            var path = trace.GetPath(1);
            Vector3 top = station.transform.InverseTransformPoint(trace.PaperToWorld(path[0], .003f));
            Vector3 right = station.transform.InverseTransformPoint(trace.PaperToWorld(path[(path.Length - 1) / 4], .003f));
            Vector3 bottom = station.transform.InverseTransformPoint(trace.PaperToWorld(path[(path.Length - 1) / 2], .003f));
            Vector3 left = station.transform.InverseTransformPoint(trace.PaperToWorld(path[(path.Length - 1) * 3 / 4], .003f));
            Vector3 safe = top + Vector3.up * .015f;
            Caption("操作 · SHIFT 配合点动", "探针先到绿色起点正上方 18 mm 安全位置。循迹只沿印刷线移动，不生成新笔迹。");
            yield return JogTo(safe); yield return Press("F1", 1.8f);
            Require(program.Model.Points.Count == 1, "Trace safe point P1 is recorded.");
            Caption("L 直线 · 到循迹起点", "下降到离纸 3 mm，SHIFT + F1 记录 L P[2]。后续圆弧从这里开始。");
            yield return JogTo(top); yield return Press("F1", 1.8f);
            Caption("C 圆弧 · 经过点与终点", "先到右侧经过点；松开 SHIFT，F1 选择 C、ENTER 录点。随后到下端，SHIFT + F5 补齐终点。");
            yield return JogTo(right); yield return ChooseCircular();
            yield return JogTo(bottom); yield return Press("F5", 2);
            Caption("C 圆弧 · 完成另一半", "到左侧经过点 SHIFT + F1，再到起点 SHIFT + F5。两条三点圆弧构成整圆。");
            yield return JogTo(left); yield return Press("F1", 1);
            yield return JogTo(top); yield return Press("F5", 2);
            Caption("程序末行 · 返回安全点", "在 End 输入 1、ENTER，追加 L P[1]，复用同一个安全点。");
            yield return Press("1", .8f); yield return Press("ENTER", 2);
            yield return JogTo(safe);
            FirstRow();
            trace.ResetAttempt(); Require(trace.BeginAssessment(), "Circle assessment is armed at safe height.");
            Caption("运行 · SHIFT + FWD", "从首行运行。右侧俯视显示真实探针，左侧实时计算覆盖率、精度和成功程度。");
            yield return Pause(2); yield return Press("FWD", .3f);
            yield return WaitComplete(45);
            report.score = trace.Score; report.coverage = trace.Coverage; report.accuracy = trace.Accuracy;
            Require(trace.HasResult, "Tracing settles a real scored attempt.");
            Caption("循迹结果 · " + trace.Score.ToString("F1") + " / 100", "覆盖率 " + trace.Coverage.ToString("F1") + "% · 精度 " + trace.Accuracy.ToString("F1") + "%；结果来自实际 TCP 轨迹，纸上没有新增线条。");
            yield return Pause(5);
        }
        IEnumerator DrawingDemo()
        {
            Vector3 safe = Actual();
            safe.y = Mathf.Max(safe.y, station.drawingHeight + .02f);
            Caption("操作 · 先记录安全点", "点击 SHIFT 锁定点动；首条 L P[1] 至少高于纸面 15 mm。按 F1 记录真实笔尖位置。");
            yield return JogTo(safe); yield return Press("F1", 2);
            Vector3 start = station.exerciseCenter; start.y = station.drawingHeight + .003f;
            Vector3 endLine = start + Vector3.right * .035f;
            Vector3 via = endLine + new Vector3(.015f, 0, .015f);
            Vector3 endArc = endLine + Vector3.right * .03f;
            Caption("L 直线 · 记录起点和终点", "笔尖进入离纸 0–10 mm 范围自动留线。移动到起点、直线终点，各按一次 SHIFT + F1。");
            yield return JogTo(start); yield return Press("F1", 1.2f);
            yield return JogTo(endLine); yield return Press("F1", 2);
            Caption("C 圆弧 · 先录经过点", "点动到圆弧中间位置。松开 SHIFT → F1 POINT → 选 C → ENTER；C 行此时还缺终点。");
            yield return JogTo(via); yield return ChooseCircular();
            Caption("C 圆弧 · 再录终点", "移动到圆弧终点，SHIFT + F5 TOUCHUP。一个 C 指令包含经过点和终点。");
            yield return JogTo(endArc); yield return Press("F5", 2);
            Caption("抬高后回程 · 避免连接线", "沿原位置竖直抬高到 20 mm，录一条 L；然后在 End 输入 1、ENTER 返回安全点。");
            yield return JogTo(endArc + Vector3.up * .017f); yield return ChooseLinear();
            yield return Press("1", .8f); yield return Press("ENTER", 2);
            station.ClearDrawing();
            Caption("运行准备 · 擦除示教草稿", "清纸只删除笔迹，程序和点位保留。上键选中首行，保持 SHIFT，按 FWD。");
            yield return Pause(2); FirstRow(); yield return Press("FWD", .5f);
            yield return Pause(1.2f);
            yield return Press("HOLD", .1f);
            Require(program.Executor.IsPaused, "HOLD pauses the current actual trajectory.");
            Caption("HOLD 暂停 · FWD 继续", "暂停期间轨迹保持在当前位置；再次 SHIFT + FWD 后继续这一条指令。");
            yield return Pause(3); yield return Press("FWD", .3f);
            yield return WaitComplete(45);
            report.returnErrorMm = Vector3.Distance(Actual(), program.Model.FindPoint(1).LocalPosition) * 1000;
            Require(station.DrawingStrokes != null && station.DrawingStrokes.Count > 0, "Actual drawing replay produces paper strokes.");
            Caption("运行完成 · 直线与圆弧", "纸面留下实际执行的 L/C 轨迹；笔尖先竖直抬高，再在空中返回 P[1]，回程不画连接线。");
            yield return Pause(5);
        }
        IEnumerator RingDemo()
        {
            Caption("操作 · 工具夹持圆环", "SHIFT 点动控制实际圆环中心。运动保留圈缘、钢丝端点和机械臂碰撞检查。");
            yield return Press("F1", 2);
            Require(program.Model.Points.Count == 1, "Ring actual start is recorded.");
            for (int i = 1; i <= 6; i++)
            {
                Caption("L 分段示教 · 第 " + i + " 段", "沿钢丝实际走向短距离点动，到达后 SHIFT + F1 记录。每一行是当前位置的真实点位。");
                yield return JogTo(WirePointAt(i * .004f), .0007f);
                yield return Press("F1", 1.2f);
            }
            Caption("运行 · 从第一条 L 开始", "选中首行，SHIFT + FWD 按记录顺序复现。套圈不强制直线返回原点，也没有抬笔落笔。");
            FirstRow(); yield return Pause(2); yield return Press("FWD", .3f);
            yield return WaitComplete(30);
            Caption("套圈运行完成", "实际圆环逐点通过已示教钢丝段。更长的弯曲轨迹应继续分段示教，不能用穿越钢丝的捷径。");
            yield return Pause(5);
        }
        IEnumerator ChooseCircular()
        {
            if (program.ShiftActive) yield return Press("SHIFT_LEFT", .7f);
            yield return Press("F1", 1); yield return Press("DOWN", .6f); yield return Press("ENTER", 1.4f);
            Require(program.Model.HasPendingArc, "C via point is recorded from actual TCP.");
            yield return Press("SHIFT_LEFT", .7f);
        }
        IEnumerator ChooseLinear()
        {
            if (program.ShiftActive) yield return Press("SHIFT_LEFT", .5f);
            yield return Press("F1", .8f); yield return Press("UP", .5f); yield return Press("ENTER", 1);
            yield return Press("SHIFT_LEFT", .5f);
        }
        IEnumerator JogTo(Vector3 destination, float tolerance = .0008f)
        {
            Require(program.ShiftActive, "Manual teaching keeps SHIFT active.");
            float until = Time.time + 50; int stable = 0;
            while (Time.time < until)
            {
                Vector3 target = station.transform.InverseTransformPoint(station.toolTarget.position);
                Vector3 delta = destination - target;
                if (delta.magnitude < .00006f && Vector3.Distance(Actual(), destination) <= tolerance)
                { program.SetJog(Vector3.zero); if (++stable >= 4) yield break; }
                else
                {
                    stable = 0;
                    float speed = station.IsTracing ? Mathf.Min(station.moveSpeed, .012f) : station.moveSpeed;
                    float feed = station.IsRing ? .006f : .012f;
                    float magnitude = Mathf.Min(feed / Mathf.Max(speed, .0001f), delta.magnitude / Mathf.Max(.000001f, speed * Time.deltaTime));
                    program.SetJog(delta.normalized * magnitude);
                }
                yield return null;
            }
            program.SetJog(Vector3.zero);
            throw new InvalidOperationException("Cannot reach real teaching point " + destination.ToString("F5") + "; actual=" + Actual().ToString("F5") + "; " + station.DrawingBlockReason + "; " + station.RobotCollisionReason);
        }
        IEnumerator WaitComplete(float seconds)
        {
            float until = Time.time + seconds;
            Require(program.Executor.State == LabTrajectoryState.Running, "FWD starts actual program execution: " + program.Executor.StatusText);
            while (program.Executor.State == LabTrajectoryState.Running && Time.time < until) yield return null;
            Require(program.Executor.State == LabTrajectoryState.Completed, "Actual program completes: " + program.Executor.StatusText);
        }
        Vector3 WirePointAt(float distance)
        {
            for (int i = 1; i < station.wirePoints.Length; i++)
            {
                float length = Vector3.Distance(station.wirePoints[i - 1], station.wirePoints[i]);
                if (distance <= length) return Vector3.Lerp(station.wirePoints[i - 1], station.wirePoints[i], distance / length);
                distance -= length;
            }
            throw new InvalidOperationException("Authored wire is shorter than the requested demonstration.");
        }
        Vector3 Actual() => station.transform.InverseTransformPoint(station.ActualToolPosition);
        void FirstRow() { for (int i = 0; i <= program.Model.Instructions.Count; i++) Click("UP"); }
        IEnumerator Press(string key, float delay) { Click(key); yield return Pause(delay); }
        void Click(string key)
        {
            Require(view.Buttons.TryGetValue(key, out var button) && button, "Button " + key + " exists.");
            Canvas.ForceUpdateCanvases();
            var rect = button.GetComponent<RectTransform>();
            marker = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)); markerUntil = Time.time + 1;
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = marker };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Require(hits.Count > 0 && (hits[0].gameObject == button.gameObject || hits[0].gameObject.transform.IsChildOf(button.transform)), "Rendered button raycast reaches " + key);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        IEnumerator Pause(float seconds) { yield return new WaitForSeconds(seconds); }
        void Caption(string title, string body)
        {
            heading = title; detail = body;
            string entry = (report.frames / 20f).ToString("F2") + "s | " + title + " | " + body;
            report.events.Add(entry); Debug.Log("DEMO_CUE " + entry);
        }
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        IEnumerator CaptureFrames()
        {
            while (!done)
            {
                yield return new WaitForEndOfFrame();
                if (!recording) continue;
                if (!frameTexture || frameTexture.width != Screen.width || frameTexture.height != Screen.height)
                {
                    if (frameTexture) Destroy(frameTexture);
                    frameTexture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                }
                frameTexture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false); frameTexture.Apply(false, false);
                File.WriteAllBytes(Path.Combine(frames, report.frames.ToString("D6") + ".jpg"), frameTexture.EncodeToJPG(88));
                report.frames++;
            }
        }
        void OnGUI()
        {
            if (!recording || !app) return;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { font = app.uiFont, fontSize = 24, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow };
                titleStyle.normal.textColor = Color.white;
                detailStyle = new GUIStyle(titleStyle) { fontSize = 19, fontStyle = FontStyle.Normal, wordWrap = true };
                detailStyle.normal.textColor = new Color(.82f, .94f, 1);
            }
            GUI.depth = -500; var old = GUI.matrix; var color = GUI.color;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / 1600f, Screen.height / 900f, 1));
            GUI.color = new Color(.025f, .07f, .11f, .98f); GUI.DrawTexture(new Rect(316, 792, 1260, 95), Texture2D.whiteTexture);
            GUI.color = new Color(.02f, .7f, .73f); GUI.DrawTexture(new Rect(316, 792, 5, 95), Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(new Rect(336, 801, 1215, 31), heading, titleStyle);
            GUI.Label(new Rect(336, 836, 1215, 46), detail, detailStyle);
            if (Time.time < markerUntil)
            {
                float x = marker.x * 1600 / Screen.width, y = (Screen.height - marker.y) * 900 / Screen.height;
                GUI.color = new Color(1, .75f, .08f);
                GUI.DrawTexture(new Rect(x - 15, y - 15, 30, 3), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(x - 15, y + 12, 30, 3), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x - 15, y - 15, 3, 30), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(x + 12, y - 15, 3, 30), Texture2D.whiteTexture);
            }
            GUI.color = color; GUI.matrix = old;
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
