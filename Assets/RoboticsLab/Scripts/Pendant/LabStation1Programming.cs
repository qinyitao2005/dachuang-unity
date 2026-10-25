using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>Station 1 teaching workflow. Motion is delegated to the shared L/C executor.</summary>
    public sealed class LabStation1Programming : MonoBehaviour
    {
        enum Page { Program, Motion, Functions }
        public LabPendantProgramModel Model { get; private set; }
        public LabPendantExecutor Executor { get; private set; }
        public LabPendantScreenView Screen { get; private set; }
        public bool IsBusy => Executor && Executor.IsExecuting;
        public bool ShiftActive => shiftLatched || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        public bool StepMode { get; private set; }
        public string Message { get; private set; } = "先移到安全点，选择 L，再 SHIFT+POINT";

        LabTrainingApp app;
        LabStation1PendantView view;
        LabRobotStation station;
        LabTracingExercise trace;
        Page page;
        int selection, cursor;
        bool shiftLatched, arcMode, scoring;
        string number = "";
        Vector3 heldJog;
        const float SpeedMm = 20;

        public void Initialize(LabTrainingApp owner, LabStation1PendantView shell, RectTransform lcd)
        {
            app = owner; view = shell;
            Model = new LabPendantProgramModel();
            Executor = gameObject.AddComponent<LabPendantExecutor>();
            Executor.AllowTracing = true;
            Screen = gameObject.AddComponent<LabPendantScreenView>();
            Screen.Initialize(lcd, app.uiFont ? app.uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            string[] captions = { "POINT", "", "", "CHOICE", "TOUCHUP" };
            foreach (var label in lcd.GetComponentsInChildren<Text>(true))
                for (int i = 0; i < captions.Length; i++)
                    if (label.name == "FunctionF" + (i + 1)) label.text = captions[i];
            view.ButtonClicked += HandleButton;
            Executor.Changed += ExecutionChanged;
            Model.Changed += Refresh;
            Refresh();
        }

        public void Activate(LabRobotStation target)
        {
            station = target; trace = target.GetComponent<LabTracingExercise>();
            if (trace) trace.StopAutomatic();
            Executor.Bind(station, Model);
            page = Page.Program; cursor = Model.Instructions.Count;
            number = ""; heldJog = Vector3.zero; shiftLatched = false;
            Message = Model.Points.Count == 0 ? "先移到安全点，选择 L，再 SHIFT+POINT" : "程序保留；SHIFT+FWD 运行";
            Refresh();
        }

        public void Deactivate()
        {
            heldJog = Vector3.zero; shiftLatched = false;
            if (Executor) Executor.Stop();
            EndScoring();
            Refresh();
        }

        public Vector3 ManualDirection()
        {
            if (!view || !view.Visible || !station || !station.IsOperating || station.EmergencyStopped || IsBusy || !ShiftActive)
                return Vector3.zero;
            Vector3 keyboard = new Vector3(Input.GetAxisRaw("Horizontal"),
                (Input.GetKey(KeyCode.Q) ? 1 : 0) - (Input.GetKey(KeyCode.E) ? 1 : 0), Input.GetAxisRaw("Vertical"));
            return station.transform.TransformDirection(Vector3.ClampMagnitude(heldJog + keyboard, 1))
                * Mathf.Min(1, .012f / Mathf.Max(.0001f, station.moveSpeed));
        }

        public void SetJog(Vector3 direction) { heldJog = direction; }
        public void Pause() { if (Executor) Executor.Pause(); heldJog = Vector3.zero; }
        public void Stop() { heldJog = Vector3.zero; if (Executor) Executor.Stop(); EndScoring(); }

        public void HandleButton(string id)
        {
            if (!view || !view.Visible) return;
            if (id == "SHIFT_LEFT" || id == "SHIFT_RIGHT")
            {
                shiftLatched = !shiftLatched;
                if (!ShiftActive) Pause();
                Message = shiftLatched ? "SHIFT 已锁定；再次点击松开" : "SHIFT 已松开";
                Refresh(); return;
            }
            if (id == "EMERGENCY_STOP") { app.ToggleEmergencyStop(); Message = station.EmergencyStopped ? "已急停；解除后 SHIFT+FWD" : "急停已解除；需明确重新运行"; Refresh(); return; }
            if (id == "HOLD") { Pause(); return; }
            if (id == "RESET") { Stop(); Message = "已停止，程序保留"; Refresh(); return; }
            if (id == "STEP") { if (!IsBusy || Executor.IsPaused) StepMode = !StepMode; Message = StepMode ? "STEP 开：SHIFT+FWD 执行一行" : "STEP 关：SHIFT+FWD 连续执行"; Refresh(); return; }
            if (id == "FWD") { Run(); return; }
            if (IsBusy) { SetMessage("程序占用控制；RESET 后才能编辑"); return; }
            if (id == "EDIT" || id == "SELECT" || id == "PREV") { page = Page.Program; number = ""; Refresh(); return; }
            if (id == "FCTN") { page = Page.Functions; selection = 0; Refresh(); return; }
            if (id == "UP" || id == "DOWN")
            {
                int delta = id == "UP" ? -1 : 1;
                if (page == Page.Program) cursor = Mathf.Clamp(cursor + delta, 0, Model.Instructions.Count);
                else selection = Mathf.Clamp(selection + delta, 0, 1);
                number = ""; Refresh(); return;
            }
            if (id == "ENTER") { Enter(); return; }
            if (id.Length == 1 && char.IsDigit(id[0]))
            {
                if (page != Page.Program) selection = Mathf.Clamp(int.Parse(id) - 1, 0, 1);
                else if (number.Length < 3) number += id;
                Refresh(); return;
            }
            if (id == "BACKSPACE") { if (number.Length > 0) number = number.Substring(0, number.Length - 1); Refresh(); return; }
            if (id == "F1" && ShiftActive) { RecordPoint(); return; }
            if (id == "F5" && ShiftActive) { TouchUp(); return; }
            if (id == "F1" || id == "F4") { page = Page.Motion; selection = arcMode ? 1 : 0; Refresh(); return; }
            if (id == "F5") { SetMessage("按 SHIFT+F5 更新所选点或补齐 C 终点"); return; }
            if (id.StartsWith("J", StringComparison.Ordinal))
            { SetMessage(ShiftActive ? "按住 ±X/±Y/±Z 点动" : "先按住或锁定 SHIFT，再点动"); return; }
            if (id == "COORD" || id == "POSN") { SetMessage("USER 坐标：X 左右 / Y 前后 / Z 升降"); return; }
            SetMessage("当前按键暂未接入循迹编程");
        }

        bool Editable()
        {
            if (!view.Visible || !station || !station.IsOperating || station.EmergencyStopped || IsBusy)
            { SetMessage("先停止程序、解除急停，再录点"); return false; }
            if (trace && trace.IsAutomatic) { SetMessage("先停止预设自动循迹"); return false; }
            return true;
        }

        Vector3 ActualLocal()
        {
            var point = station.transform.InverseTransformPoint(station.ActualToolPosition);
            if (Mathf.Abs(point.y - station.drawingHeight) <= .0002f) point.y = station.drawingHeight;
            return point;
        }

        void RecordPoint()
        {
            if (!Editable()) return;
            if (page != Page.Program) { SetMessage("先 ENTER 确认运动类型"); return; }
            if (Model.HasPendingArc) { SetMessage("C 经过点已录；移到终点后 SHIFT+F5"); return; }
            Vector3 position = ActualLocal();
            if (Model.Points.Count == 0 && (arcMode || position.y < station.drawingHeight + .005f))
            { SetMessage("首点须为 L 安全点，至少高于纸面 5mm"); return; }
            bool ok = arcMode ? Model.RecordCircularPoint(position, SpeedMm) : Model.AddLinear(position, SpeedMm);
            cursor = Model.HasPendingArc ? Model.Instructions.Count - 1 : Model.Instructions.Count;
            Message = !ok ? Model.StatusText : Model.Points.Count == 1 ? "安全点 P[1] 已记录" : Model.HasPendingArc
                ? "C 经过点已录；终点用 SHIFT+F5" : "L 终点已记录；继续点动或选 C";
            Refresh();
        }

        void TouchUp()
        {
            if (!Editable()) return;
            if (Model.HasPendingArc)
            {
                bool ok = Model.RecordCircularPoint(ActualLocal(), SpeedMm);
                Message = ok ? "C 终点已记录，圆弧完整" : Model.StatusText;
                if (ok) cursor = Model.Instructions.Count;
            }
            else if (cursor < Model.Instructions.Count)
            {
                int point = Model.Instructions[cursor].TargetPoint;
                if (point == 1 && ActualLocal().y < station.drawingHeight + .005f) { SetMessage("安全点需高于纸面至少 5mm"); return; }
                Model.TouchUpPoint(point, ActualLocal()); Message = Model.StatusText;
            }
            else Message = "先用上下键选中要更新的程序行";
            Refresh();
        }

        void Enter()
        {
            if (!Editable()) return;
            if (page == Page.Motion)
            {
                if (Model.HasPendingArc) { SetMessage("先用 SHIFT+F5 完成当前 C 指令"); return; }
                arcMode = selection == 1; page = Page.Program;
                Message = arcMode ? "C：先到经过点，再 SHIFT+POINT" : "L：到终点后 SHIFT+POINT";
            }
            else if (page == Page.Functions)
            {
                if (selection == 0) Model.RemoveLast(); else Model.Clear();
                if (Model.Instructions.Count == 0) Model.Clear();
                page = Page.Program; cursor = Model.Instructions.Count; number = "";
                if (Model.Points.Count == 0) arcMode = false;
                Message = Model.StatusText;
            }
            else if (int.TryParse(number, out int point))
            {
                bool ok = cursor < Model.Instructions.Count ? Model.SetTargetReference(cursor, point)
                    : !arcMode && !Model.HasPendingArc && Model.AddLinearReference(point, SpeedMm);
                Message = ok ? "已引用 P[" + point + "]；无需重复录点" : "引用失败：检查点号与 L/C 类型";
                number = ""; if (ok) cursor = Model.Instructions.Count;
            }
            Refresh();
        }

        bool ValidateProgram(out string error)
        {
            error = "";
            var safe = Model.FindPoint(1);
            if (safe == null || safe.LocalPosition.y < station.drawingHeight + .005f)
                error = "先记录高于纸面至少 5mm 的安全点 P[1]";
            else if (Model.HasPendingArc) error = "C 缺少终点：请 SHIFT+F5 补录";
            else if (Model.Instructions.Count < 3 || Model.Instructions[0].Type != LabPendantInstructionType.Linear
                || Model.Instructions[0].TargetPoint != 1) error = "程序须从 L P[1] 安全点开始";
            else
            {
                var last = Model.Instructions[Model.Instructions.Count - 1];
                if (last.Type != LabPendantInstructionType.Linear || last.TargetPoint != 1)
                    error = "末尾需 L P[1]；在 End 输入 1 再 ENTER";
            }
            return error.Length == 0;
        }

        void Run()
        {
            if (!ShiftActive) { SetMessage("先按住或锁定 SHIFT，再按 FWD"); return; }
            if (station.EmergencyStopped) { SetMessage("先解除急停，再 SHIFT+FWD"); return; }
            if (!Executor.IsPaused && !ValidateProgram(out string error)) { SetMessage(error); return; }
            page = Page.Program; number = ""; heldJog = Vector3.zero;
            bool starting = !Executor.IsExecuting;
            bool ok = StepMode ? Executor.Step() : Executor.Run();
            if (ok && starting && trace) { trace.BeginProgrammed(); scoring = true; }
            Message = Executor.StatusText;
            Refresh();
        }

        void ExecutionChanged()
        {
            if (Executor.State == LabTrajectoryState.Completed || Executor.State == LabTrajectoryState.Faulted
                || Executor.State == LabTrajectoryState.Ready) EndScoring();
            Message = Executor.StatusText;
            Refresh();
        }

        void EndScoring() { if (scoring && trace) trace.FinishAttempt(); scoring = false; }
        void SetMessage(string message) { Message = message; Refresh(); }

        void Refresh()
        {
            if (!Screen || Model == null) return;
            Screen.Header.text = "S1_TRACE  " + (Executor && IsBusy ? (Executor.IsPaused ? "HOLD" : "RUN") : "EDIT")
                + (ShiftActive ? " SHIFT" : "") + (StepMode ? " STEP" : "");
            string rows;
            if (page == Page.Motion) rows = "SELECT MOTION\n" + (selection == 0 ? "> " : "  ") + "1 L 20mm/sec FINE\n"
                + (selection == 1 ? "> " : "  ") + "2 C 20mm/sec FINE\n\nUP/DOWN + ENTER";
            else if (page == Page.Functions) rows = "FCTN\n" + (selection == 0 ? "> " : "  ") + "1 删除最后一行\n"
                + (selection == 1 ? "> " : "  ") + "2 清空 / 新建程序\n\nUP/DOWN + ENTER";
            else
            {
                var lines = Model.GetRows();
                for (int i = 0; i < lines.Length; i++)
                    lines[i] = (IsBusy && i == Executor.CurrentInstructionIndex ? "▶" : cursor == i ? ">" : " ") + lines[i];
                rows = string.Join("\n", lines) + "\n" + (cursor == lines.Length ? ">" : " ") + "[End]";
                if (number.Length > 0) rows += "\n目标 P[" + number + "]  ENTER 确认";
            }
            Screen.ProgramRows.text = rows;
            var content = Screen.Scroll.content;
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(166, (rows.Split('\n').Length + 1) * 26));
            Screen.Scroll.verticalNormalizedPosition = page != Page.Program ? 1 :
                1 - Mathf.Clamp01(((IsBusy ? Executor.CurrentInstructionIndex : cursor) - 2f) / Mathf.Max(1, Model.Instructions.Count - 5));
            Screen.SetStatus(Message);
            foreach (string key in new[] { "SHIFT_LEFT", "SHIFT_RIGHT" })
                if (view.Buttons.TryGetValue(key, out var button))
                { var colors = button.colors; colors.normalColor = ShiftActive ? new Color(.05f, .85f, .45f, .5f) : Color.clear; button.colors = colors; }
        }

        void Update()
        {
            if (!view || !view.Visible) return;
            if (Executor.State == LabTrajectoryState.Running && !ShiftActive) Pause();
            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift)
                || Input.GetKeyUp(KeyCode.LeftShift) || Input.GetKeyUp(KeyCode.RightShift)) Refresh();
            if (Input.GetKeyDown(KeyCode.F1)) HandleButton("F1");
            if (Input.GetKeyDown(KeyCode.F4)) HandleButton("F4");
            if (Input.GetKeyDown(KeyCode.F5)) HandleButton("F5");
            if (Input.GetKeyDown(KeyCode.UpArrow)) HandleButton("UP");
            if (Input.GetKeyDown(KeyCode.DownArrow)) HandleButton("DOWN");
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) HandleButton("ENTER");
            if (Input.GetKeyDown(KeyCode.Backspace)) HandleButton("BACKSPACE");
            for (int i = 0; i <= 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i)) HandleButton(i.ToString(CultureInfo.InvariantCulture));
            if (station) Screen.SetPose(ActualLocal() - new Vector3(station.exerciseCenter.x, station.drawingHeight, station.exerciseCenter.z));
        }

        void OnApplicationFocus(bool focused) { if (!focused) { shiftLatched = false; Pause(); Refresh(); } }
        void OnDisable() { if (Executor) Deactivate(); }
        void OnDestroy()
        {
            if (view) view.ButtonClicked -= HandleButton;
            if (Executor) Executor.Changed -= ExecutionChanged;
            if (Model != null) Model.Changed -= Refresh;
        }
    }
}
