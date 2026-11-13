using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>Shared teaching workflow; each station owns an instance. The executor retains all motion checks.</summary>
    public sealed class LabStation1Programming : MonoBehaviour
    {
        enum Page { Program, Programs, Create, Motion, Functions, DeleteConfirm }
        public LabPendantProgramModel Model { get; private set; }
        public LabPendantExecutor Executor { get; private set; }
        public LabPendantScreenView Screen { get; private set; }
        public string ProgramName { get; private set; } = "";
        public bool IsBusy => Executor && Executor.IsExecuting;
        public bool TextEntryFocused => page == Page.Create;
        public bool ShiftActive => shiftLatched || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
            || (Gamepad.current != null && Gamepad.current.rightShoulder.isPressed);
        public bool StepMode { get; private set; }
        public string Message { get; private set; } = "SELECT 新建或选择程序";

        LabTrainingApp app;
        LabStation1PendantView view;
        LabRobotStation station;
        LabPendantStationProfile profile;
        LabTracingExercise trace;
        InputField nameInput;
        Page page = Page.Programs;
        int selection, cursor;
        bool shiftLatched, arcMode, scoring, modifyingMotion, editVia, dirty, resetRequired;
        float saveAfter, clearHoldStarted = -1;
        int openedFrame = -1;
        bool clearHoldTriggered, clearHoldEligible;
        string number = "", storageError = "", pendingDeleteName = "";
        string[] names = Array.Empty<string>();
        Vector3 heldJog;
        // Lab speed is fixed. Do not expose feed editing or override controls.
        const float SpeedMm = LabPendantStationProfile.SpeedMm;
        int StationIndex => station ? station.stationIndex : 0;

        public void Initialize(LabTrainingApp owner, LabStation1PendantView shell, RectTransform lcd)
        {
            app = owner; view = shell;
            Executor = gameObject.AddComponent<LabPendantExecutor>();
            Screen = gameObject.AddComponent<LabPendantScreenView>();
            var font = app.uiFont ? app.uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Screen.Initialize(lcd, font); nameInput = Screen.ConfigureStation1(font);
            SetModel(new LabPendantProgramModel(), "");
            view.ButtonClicked += HandleButton; Executor.Changed += ExecutionChanged;
            Refresh();
        }

        void SetModel(LabPendantProgramModel model, string name)
        {
            if (Model != null) Model.Changed -= ModelChanged;
            Model = model; ProgramName = name; dirty = false; storageError = "";
            Model.Changed += ModelChanged;
            cursor = Model.Instructions.Count; editVia = false; arcMode = false; number = "";
            if (station) Executor.Bind(station, Model);
        }
        void ModelChanged() { dirty = true; saveAfter = Time.unscaledTime + .4f; Refresh(); }
        bool SaveCurrent()
        {
            if (!dirty || string.IsNullOrEmpty(ProgramName)) return true;
            if (!LabPendantProgramStorage.Save(StationIndex, ProgramName, Model, out string error))
            {
                storageError = error; saveAfter = Time.unscaledTime + 5;
                return false;
            }
            dirty = false; storageError = ""; return true;
        }

        public void Activate(LabRobotStation target)
        {
            if (station && station != target) throw new InvalidOperationException("A teaching session belongs to one station.");
            station = target; profile = new LabPendantStationProfile(target);
            trace = target.IsTracing ? target.GetComponent<LabTracingExercise>() : null;
            Executor.AllowTracing = target.IsTracing;
            if (trace) trace.StopAutomatic();
            Executor.Bind(station, Model);
            page = string.IsNullOrEmpty(ProgramName) ? Page.Programs : Page.Program;
            names = LabPendantProgramStorage.Names(StationIndex); selection = 0; cursor = 0;
            number = ""; heldJog = Vector3.zero; shiftLatched = false;
            pendingDeleteName = "";
            openedFrame = Time.frameCount; clearHoldStarted = -1; clearHoldTriggered = clearHoldEligible = false;
            Message = page == Page.Programs ? "F2 CREATE 新建；F3 DELETE 删除；ENTER 打开" : "程序保留；选起始行后 SHIFT+FWD";
            Refresh();
        }
        public void Deactivate()
        {
            heldJog = Vector3.zero; shiftLatched = false;
            pendingDeleteName = "";
            clearHoldStarted = -1; clearHoldTriggered = clearHoldEligible = false;
            if (Executor) Executor.Stop();
            EndScoring(); SaveCurrent();
            if (nameInput) nameInput.DeactivateInputField();
            Refresh();
        }
        public Vector3 ManualDirection()
        {
            if (!view || !view.Visible || !station || !station.IsOperating || station.EmergencyStopped
                || IsBusy || OtherProgramBusy || !ShiftActive || TextEntryFocused) return Vector3.zero;
            // Arrow keys navigate the program even while SHIFT is latched; only WASD/QE jog.
            Vector3 keyboard = new Vector3((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                (Input.GetKey(KeyCode.Q) ? 1 : 0) - (Input.GetKey(KeyCode.E) ? 1 : 0),
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            Vector3 gamepadJog = LabTeachingGamepad.JogLocal();
            return station.transform.TransformDirection(Vector3.ClampMagnitude(heldJog + keyboard + gamepadJog, 1))
                * (station.IsTracing ? Mathf.Min(1, .012f / Mathf.Max(.0001f, station.moveSpeed)) : 1f);
        }
        public void SetJog(Vector3 direction) { heldJog = direction; }
        public void Pause() { if (Executor) Executor.Pause(); heldJog = Vector3.zero; }
        public void Stop() { heldJog = Vector3.zero; if (Executor) Executor.Stop(); EndScoring(); }

        public void HandleButton(string id)
        {
            if (!view || !view.Visible) return;
            if (id == "SHIFT_LEFT" || id == "SHIFT_RIGHT")
            {
                shiftLatched = !shiftLatched; if (!ShiftActive) Pause();
                SetMessage(shiftLatched ? "SHIFT 已锁定；再次点击松开" : "SHIFT 已松开"); return;
            }
            if (id == "EMERGENCY_STOP")
            {
                app.ToggleEmergencyStop(); if (station.EmergencyStopped) resetRequired = true;
                SetMessage(station.EmergencyStopped ? "已急停；解除后需 RESET，再 SHIFT+FWD" : "急停已解除；RESET 后再运行"); return;
            }
            if (id == "HOLD") { Pause(); return; }
            if (id == "RESET")
            {
                if (station.EmergencyStopped) { SetMessage("先解除急停，再按 RESET"); return; }
                if (Executor.State == LabTrajectoryState.Faulted) Executor.Stop();
                resetRequired = false;
                SetMessage(IsBusy ? "RESET 不终止程序；HOLD 暂停，FCTN 中 ABORT 终止" : "可复位故障已清除；选择起始行再运行"); return;
            }
            if (page == Page.DeleteConfirm)
            {
                if (id == "F4") ConfirmDelete();
                else if (id == "F5" || id == "PREV" || id == "SELECT")
                {
                    pendingDeleteName = ""; page = Page.Programs;
                    SetMessage("已取消删除，程序保留");
                }
                else SetMessage("删除整个程序：F4 YES 确认，F5 NO 取消");
                return;
            }
            if (id == "SELECT")
            {
                if (IsBusy) { SetMessage("程序占用控制；先 FCTN → ABORT，再切换程序"); return; }
                OpenPrograms(); return;
            }
            if (id == "FCTN") { LeaveNameInput(); page = Page.Functions; selection = 0; number = ""; Refresh(); return; }
            if (id == "PREV")
            {
                LeaveNameInput(); page = string.IsNullOrEmpty(ProgramName) ? Page.Programs : Page.Program;
                number = ""; Refresh(); return;
            }
            // Run/resume and step remain accessible while the program owns motion.
            if (id == "STEP")
            {
                if (!IsBusy || Executor.IsPaused) StepMode = !StepMode;
                SetMessage(StepMode ? "STEP 开：SHIFT+FWD 执行一条 L/C" : "STEP 关：SHIFT+FWD 连续执行"); return;
            }
            if (id == "FWD")
            {
                if (page == Page.Create || page == Page.Programs) { SetMessage("先 SELECT 打开程序并选择起始行"); return; }
                Run(); return;
            }
            if (page == Page.Functions)
            {
                if (id == "UP" || id == "DOWN") selection = Mathf.Clamp(selection + (id == "UP" ? -1 : 1), 0, 1);
                else if (id == "1" || id == "2") selection = int.Parse(id) - 1;
                else if (id == "ENTER") { FunctionEnter(); return; }
                Refresh(); return;
            }
            if (IsBusy) { SetMessage("程序占用控制；先 FCTN → ABORT，再编辑或切换程序"); return; }
            if (id == "SELECT") { OpenPrograms(); return; }
            if (page == Page.Create)
            {
                if (id == "F1" || id == "ENTER") { CreateProgram(); return; }
                if (id == "BACKSPACE" && nameInput.text.Length > 0) nameInput.text = nameInput.text.Substring(0, nameInput.text.Length - 1);
                else if (id.Length == 1 && char.IsDigit(id[0])) nameInput.text += id;
                return;
            }
            if (page == Page.Programs)
            {
                if (id == "F2") { OpenCreate(); return; }
                if (id == "F3") { StartDelete(); return; }
                if (id == "UP" || id == "DOWN") selection = Mathf.Clamp(selection + (id == "UP" ? -1 : 1), 0, Mathf.Max(0, names.Length - 1));
                else if (id == "ENTER") { LoadSelected(); return; }
                Refresh(); return;
            }
            if (id == "EDIT") { page = Page.Program; number = ""; Refresh(); return; }
            if (id == "UP" || id == "DOWN")
            {
                int delta = id == "UP" ? -1 : 1;
                if (page == Page.Program) { cursor = Mathf.Clamp(cursor + delta, 0, Model.Instructions.Count); editVia = false; }
                else selection = Mathf.Clamp(selection + delta, 0, 1);
                number = ""; Refresh(); return;
            }
            if (id == "LEFT" || id == "RIGHT")
            {
                editVia = id == "LEFT" && cursor < Model.Instructions.Count && Model.Instructions[cursor].Type == LabPendantInstructionType.Circular;
                Refresh(); return;
            }
            if (id == "ENTER") { Enter(); return; }
            if (id.Length == 1 && char.IsDigit(id[0]))
            {
                if (page == Page.Motion) selection = Mathf.Clamp(int.Parse(id) - 1, 0, 1);
                else if (number.Length < 3) number += id;
                Refresh(); return;
            }
            if (id == "BACKSPACE") { if (number.Length > 0) number = number.Substring(0, number.Length - 1); Refresh(); return; }
            if (id == "F1" && ShiftActive && page == Page.Program) { RecordPoint(); return; }
            if (id == "F5" && ShiftActive && page == Page.Program) { TouchUp(); return; }
            if (id == "F1" || id == "F4")
            {
                if (id == "F4" && cursor >= Model.Instructions.Count) { SetMessage("先选中程序行，F4 CHOICE 才能修改类型"); return; }
                modifyingMotion = id == "F4";
                selection = modifyingMotion ? (Model.Instructions[cursor].Type == LabPendantInstructionType.Circular ? 1 : 0) : arcMode ? 1 : 0;
                page = Page.Motion; Refresh(); return;
            }
            if (id == "F5") { SetMessage("按 SHIFT+F5 更新选中点；C 可用左右键选经过点/终点"); return; }
            if (id == "F3") { SetMessage("F3 DELETE 仅在 SELECT 程序列表中删除整个程序"); return; }
            if (id.StartsWith("J", StringComparison.Ordinal)) { SetMessage("SHIFT 配合 ±X/±Y/±Z 点动"); return; }
            if (id == "IO") { TogglePen(); return; }
            if (id == "COORD" || id == "POSN") { SetMessage(profile.CoordinateHint); return; }
            if (id.Contains("OVERRIDE")) { SetMessage("实验室速度固定，不提供调速操作"); return; }
            SetMessage("当前按键暂未接入 L/C 编程");
        }

        bool OtherProgramBusy => app && ((app.trajectoryProgram && app.trajectoryProgram.IsExecuting)
            || (app.pendantController && app.pendantController.Executor && app.pendantController.Executor.IsExecuting));
        public void TogglePen()
        {
            if (station && station.IsDrawing) { SetMessage("笔尖距纸面 0–10mm 自动留线，无需 IO / P 切换"); return; }
            if (!station) { SetMessage("当前工位不使用绘图落笔"); return; }
            if (page != Page.Program || !Editable()) return;
            if (Model.HasPendingArc) { SetMessage("先补齐 C 终点；该圆弧沿用经过点录制时的笔状态"); return; }
            station.SetPen(!station.PenDown);
            SetMessage(station.PenDown ? "新录指令：落笔；IO / P 切换抬笔" : "新录指令：抬笔；IO / P 切换落笔");
        }

        void LeaveNameInput() { if (nameInput) nameInput.DeactivateInputField(); }
        void OpenPrograms()
        {
            if (!SaveCurrent()) { Refresh(); return; }
            LeaveNameInput(); page = Page.Programs; names = LabPendantProgramStorage.Names(StationIndex);
            selection = Mathf.Max(0, Array.IndexOf(names, ProgramName)); number = "";
            SetMessage("F2 CREATE 新建；F3 DELETE 删除选中程序；ENTER 打开");
        }
        void StartDelete()
        {
            if (names.Length == 0) { SetMessage("程序列表为空，没有可删除的程序"); return; }
            if (!SaveCurrent()) { Refresh(); return; }
            pendingDeleteName = names[selection]; page = Page.DeleteConfirm;
            SetMessage("确认删除 " + pendingDeleteName + "？F4 YES / F5 NO");
        }
        void ConfirmDelete()
        {
            if (string.IsNullOrEmpty(pendingDeleteName)) { page = Page.Programs; Refresh(); return; }
            string deletedName = pendingDeleteName;
            if (!LabPendantProgramStorage.Delete(StationIndex, deletedName, out string error))
            { SetMessage(error); return; }
            if (string.Equals(ProgramName, deletedName, StringComparison.OrdinalIgnoreCase))
            {
                Stop();
                SetModel(new LabPendantProgramModel(), "");
            }
            pendingDeleteName = ""; page = Page.Programs;
            names = LabPendantProgramStorage.Names(StationIndex);
            selection = Mathf.Clamp(selection, 0, Mathf.Max(0, names.Length - 1));
            SetMessage("已删除程序 " + deletedName + "；程序和点位文件已移除");
        }
        void OpenCreate()
        {
            int index = 1;
            while (Array.Exists(names, item => string.Equals(item, profile.NamePrefix + index.ToString("000"), StringComparison.OrdinalIgnoreCase))) index++;
            nameInput.text = profile.NamePrefix + index.ToString("000"); page = Page.Create;
            SetMessage("键盘输入字母、数字、下划线；F1 完成创建");
            nameInput.ActivateInputField(); nameInput.Select();
        }
        void CreateProgram()
        {
            string name = nameInput.text.Trim().ToUpperInvariant();
            if (Array.Exists(LabPendantProgramStorage.Names(StationIndex), item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)))
            { SetMessage("同名程序已存在，请换一个名称"); return; }
            var model = new LabPendantProgramModel();
            if (!SaveCurrent()) { Refresh(); return; }
            if (!LabPendantProgramStorage.Save(StationIndex, name, model, out string error)) { SetMessage(error); return; }
            LeaveNameInput(); SetModel(model, name); page = Page.Program;
            SetMessage(station.IsTracing ? "先点动到安全点；F1 选 L 并录点" : "F1 选 L 并记录起点；按 SHIFT 点动");
        }
        void LoadSelected()
        {
            if (names.Length == 0) { SetMessage("暂无程序，按 F2 CREATE 新建"); return; }
            if (!SaveCurrent()) { Refresh(); return; }
            if (!LabPendantProgramStorage.Load(StationIndex, names[selection], out var model, out string error)) { SetMessage(error); return; }
            foreach (var row in model.Instructions)
                if (Mathf.Abs(row.SpeedMm - SpeedMm) > .0001f) { SetMessage("程序速度与实验室固定速度不符，未加载"); return; }
            SetModel(model, names[selection]); page = Page.Program; cursor = 0;
            SetMessage("已打开程序；选起始行后 SHIFT+FWD");
        }
        void FunctionEnter()
        {
            if (selection == 0)
            {
                Stop(); page = string.IsNullOrEmpty(ProgramName) ? Page.Programs : Page.Program;
                SetMessage("ABORT 已终止；点位保留，选起始行再运行"); return;
            }
            if (!Editable()) return;
            Model.RemoveLast(); if (Model.Instructions.Count == 0) { Model.Clear(); arcMode = false; }
            cursor = Model.Instructions.Count; editVia = false; page = Page.Program; SetMessage(Model.StatusText);
        }
        bool Editable()
        {
            if (!view.Visible || !station || !station.IsOperating || station.EmergencyStopped || IsBusy || OtherProgramBusy)
            { SetMessage("先 ABORT 终止程序、解除急停，再编辑"); return false; }
            if (string.IsNullOrEmpty(ProgramName)) { SetMessage("先 SELECT → CREATE 新建程序"); return false; }
            if (trace && trace.IsAutomatic) { SetMessage("先停止预设自动循迹"); return false; }
            return true;
        }
        Vector3 ActualLocal()
        {
            return profile.CapturePosition();
        }
        void RecordPoint()
        {
            if (!Editable()) return;
            if (Model.HasPendingArc) { SetMessage("C 经过点已录；到终点后 SHIFT+F5"); return; }
            if (cursor != Model.Instructions.Count) { SetMessage("到 End 追加录点；修改已有点请用 TOUCHUP"); return; }
            Vector3 position = ActualLocal();
            if (Model.Points.Count == 0 && !profile.ValidateFirstPoint(arcMode, position, out string error))
            { SetMessage(error); return; }
            bool ok = arcMode ? Model.RecordCircularPoint(position, SpeedMm, profile.RecordPenDown) : Model.AddLinear(position, SpeedMm, profile.RecordPenDown);
            cursor = Model.HasPendingArc ? Model.Instructions.Count - 1 : Model.Instructions.Count; editVia = false;
            SetMessage(!ok ? Model.StatusText : Model.Points.Count == 1 ? (station.IsTracing ? "安全点 P[1] 已记录；继续点动到循迹起点" : "起点 P[1] 已记录；继续示教 L/C 轨迹") : Model.HasPendingArc
                ? "C 经过点已录；到终点后 SHIFT+F5" : "L 终点已记录；SHIFT+F1 继续录点");
        }
        void TouchUp()
        {
            if (!Editable()) return;
            if (Model.HasPendingArc && cursor == Model.Instructions.Count - 1 && !editVia)
            {
                bool ok = Model.RecordCircularPoint(ActualLocal(), SpeedMm, profile.RecordPenDown);
                if (ok) cursor = Model.Instructions.Count;
                SetMessage(ok ? "C 终点已记录，一条圆弧完成" : Model.StatusText);
            }
            else if (cursor < Model.Instructions.Count)
            {
                var row = Model.Instructions[cursor]; int point = editVia ? row.ViaPoint : row.TargetPoint;
                if (!profile.ValidateTouchPoint(point, ActualLocal(), out string error)) { SetMessage(error); return; }
                Model.TouchUpPoint(point, ActualLocal()); SetMessage(Model.StatusText);
            }
            else SetMessage("先用上下键选中程序行，再 TOUCHUP");
        }
        void Enter()
        {
            if (!Editable()) return;
            if (page == Page.Motion)
            {
                if (Model.HasPendingArc) { SetMessage("先完成当前 C 指令，再选择运动类型"); return; }
                bool circular = selection == 1; page = Page.Program;
                if (modifyingMotion)
                {
                    Model.ChangeMotionType(cursor, circular ? LabPendantInstructionType.Circular : LabPendantInstructionType.Linear);
                    SetMessage(Model.StatusText); return;
                }
                arcMode = circular; RecordPoint(); return;
            }
            if (int.TryParse(number, out int point))
            {
                bool ok = cursor < Model.Instructions.Count
                    ? editVia ? Model.SetViaReference(cursor, point) : Model.SetTargetReference(cursor, point)
                    : !Model.HasPendingArc && Model.AddLinearReference(point, SpeedMm, profile.RecordPenDown);
                number = ""; if (ok) { cursor = Model.Instructions.Count; editVia = false; }
                SetMessage(ok ? "已引用 P[" + point + "]；无需重新录点" : "引用失败：检查点号与 L/C 类型");
            }
        }
        bool ValidateProgram(out string error)
        {
            return profile.ValidateProgram(Model, out error);
        }
        void Run()
        {
            if (!ShiftActive) { SetMessage("先按住或锁定 SHIFT，再按 FWD"); return; }
            if (OtherProgramBusy) { SetMessage("先终止其他轨迹程序，再运行本示教程序"); return; }
            if (station.EmergencyStopped || resetRequired) { SetMessage("先解除急停并 RESET，再 SHIFT+FWD"); return; }
            if (!Executor.IsPaused && !ValidateProgram(out string error)) { SetMessage(error); return; }
            if (!Executor.IsPaused && cursor >= Model.Instructions.Count) { SetMessage("End 不执行；上下键选择起始行，再 FWD"); return; }
            if (!SaveCurrent()) { Refresh(); return; }
            page = Page.Program; number = ""; heldJog = Vector3.zero;
            bool starting = !Executor.IsExecuting;
            bool ok = StepMode ? Executor.Step(cursor) : Executor.Run(cursor);
            if (ok && starting && trace) { trace.BeginProgrammed(); scoring = true; }
            SetMessage(Executor.StatusText);
        }
        void ExecutionChanged()
        {
            if (Executor.State == LabTrajectoryState.Completed || Executor.State == LabTrajectoryState.Faulted || Executor.State == LabTrajectoryState.Ready) EndScoring();
            if (Executor.State == LabTrajectoryState.Faulted) resetRequired = true;
            if (Executor.State == LabTrajectoryState.Completed) cursor = 0;
            else if (Executor.IsExecuting || Executor.State == LabTrajectoryState.Faulted) cursor = Executor.CurrentInstructionIndex;
            Message = Executor.StatusText; Refresh();
        }
        void EndScoring() { if (scoring && trace) trace.FinishAttempt(); scoring = false; }
        void SetMessage(string message) { Message = message; Refresh(); }

        void Refresh()
        {
            if (!Screen || Model == null) return;
            Screen.Header.text = "S" + (StationIndex + 1) + " " + (page == Page.DeleteConfirm ? "SELECT DELETE" : string.IsNullOrEmpty(ProgramName) ? "SELECT" : ProgramName) + (dirty ? " *" : "") + "  "
                + (IsBusy ? Executor.IsPaused ? "HOLD" : "RUN" : "EDIT") + (ShiftActive ? " SHIFT" : "") + (StepMode ? " STEP" : "");
            nameInput.gameObject.SetActive(page == Page.Create);
            string rows; int active = cursor;
            if (page == Page.Programs)
            {
                rows = "SELECT PROGRAM\n"; active = selection + 1;
                for (int i = 0; i < names.Length; i++) rows += (i == selection ? "> " : "  ") + names[i] + "\n";
                if (names.Length == 0) rows += "暂无程序，F2 CREATE";
                Screen.SetFunctionKeys("", "CREATE", names.Length > 0 ? "DELETE" : "", "", "");
            }
            else if (page == Page.DeleteConfirm)
            {
                rows = "DELETE PROGRAM\n\n" + pendingDeleteName + "\n\n删除程序及全部点位？\nF4 YES / F5 NO";
                Screen.SetFunctionKeys("", "", "", "YES", "NO");
            }
            else if (page == Page.Create) { rows = "CREATE PROGRAM\n\n\n\n\nF1 END / PREV 返回"; Screen.SetFunctionKeys("END", "", "", "", ""); }
            else if (page == Page.Motion)
            {
                rows = (modifyingMotion ? "MODIFY MOTION" : "POINT / 选择并录点") + "\n" + (selection == 0 ? "> " : "  ") + "1 L 20mm/sec FINE\n"
                    + (selection == 1 ? "> " : "  ") + "2 C 20mm/sec FINE\n速度固定；ENTER 确认";
                Screen.SetFunctionKeys("", "", "", "", "");
            }
            else if (page == Page.Functions)
            {
                rows = "FCTN\n" + (selection == 0 ? "> " : "  ") + "1 ABORT 终止程序\n" + (selection == 1 ? "> " : "  ") + "2 删除最后一行\nUP/DOWN + ENTER";
                Screen.SetFunctionKeys("", "", "", "", "");
            }
            else
            {
                var lines = Model.GetRows();
                for (int i = 0; i < lines.Length; i++) lines[i] = (IsBusy && i == Executor.CurrentInstructionIndex ? "▶" : cursor == i ? ">" : " ") + lines[i];
                rows = string.Join("\n", lines) + "\n" + (cursor == lines.Length ? ">" : " ") + "[End]";
                if (number.Length > 0) rows += "\n引用 P[" + number + "]  ENTER";
                else if (cursor < lines.Length && Model.Instructions[cursor].Type == LabPendantInstructionType.Circular)
                    rows += "\n当前编辑：" + (editVia ? "经过点" : "终点") + "（左右键切换）";
                if (station && station.IsDrawing) rows += "\n笔尖距纸 0–10mm 时自动留线";
                Screen.SetFunctionKeys("POINT", "", "", cursor < lines.Length ? "CHOICE" : "", "TOUCHUP");
            }
            Screen.ProgramRows.text = rows;
            var content = Screen.Scroll.content;
            float textHeight = Screen.ProgramRows.preferredHeight;
            float viewportHeight = Screen.Scroll.viewport.rect.height;
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(viewportHeight, textHeight + 4));
            float lineHeight = textHeight / Mathf.Max(1, rows.Split('\n').Length);
            float overflow = Mathf.Max(1, content.sizeDelta.y - viewportHeight);
            Screen.Scroll.verticalNormalizedPosition = page == Page.Program || page == Page.Programs
                ? 1 - Mathf.Clamp01(Mathf.Max(0, active - 2) * lineHeight / overflow) : 1;
            Screen.SetStatus(string.IsNullOrEmpty(storageError) ? Message : "未保存：" + storageError);
            foreach (string key in new[] { "SHIFT_LEFT", "SHIFT_RIGHT" })
                if (view.Buttons.TryGetValue(key, out var button))
                { var colors = button.colors; colors.normalColor = ShiftActive ? new Color(.05f, .85f, .45f, .5f) : Color.clear; button.colors = colors; }
        }

        void Update()
        {
            if (!view || !view.Visible) return;
            HandleGamepad();
            if (!view.Visible) return;
            if (station && station.EmergencyStopped) resetRequired = true;
            if (Executor.State == LabTrajectoryState.Running && !ShiftActive) Pause();
            if (dirty && Time.unscaledTime >= saveAfter) { SaveCurrent(); Refresh(); }
            if (page != Page.Create && EventSystem.current && EventSystem.current.currentSelectedGameObject
                && EventSystem.current.currentSelectedGameObject.GetComponent<Button>()) EventSystem.current.SetSelectedGameObject(null);
            // Name entry owns keyboard input. Submit is deliberately handled only once here.
            if (page == Page.Create)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.F1)) CreateProgram();
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift) || Input.GetKeyUp(KeyCode.LeftShift) || Input.GetKeyUp(KeyCode.RightShift)) Refresh();
                if (Input.GetKeyDown(KeyCode.F1)) HandleButton("F1");
                if (Input.GetKeyDown(KeyCode.F2)) HandleButton("F2");
                if (Input.GetKeyDown(KeyCode.F3)) HandleButton("F3");
                if (Input.GetKeyDown(KeyCode.F4)) HandleButton("F4");
                if (Input.GetKeyDown(KeyCode.F5)) HandleButton("F5");
                if (Input.GetKeyDown(KeyCode.P) && station && station.IsDrawing) TogglePen();
                if (Input.GetKeyDown(KeyCode.UpArrow)) HandleButton("UP");
                if (Input.GetKeyDown(KeyCode.DownArrow)) HandleButton("DOWN");
                if (Input.GetKeyDown(KeyCode.LeftArrow)) HandleButton("LEFT");
                if (Input.GetKeyDown(KeyCode.RightArrow)) HandleButton("RIGHT");
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) HandleButton("ENTER");
                if (Input.GetKeyDown(KeyCode.Backspace)) HandleButton("BACKSPACE");
                for (int i = 0; i <= 9; i++) if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i)) HandleButton(i.ToString(CultureInfo.InvariantCulture));
            }
            if (station) Screen.SetPose(ActualLocal() - profile.UserOrigin);
        }
        void HandleGamepad()
        {
            var pad = Gamepad.current;
            if (pad == null) { clearHoldStarted = -1; clearHoldTriggered = clearHoldEligible = false; return; }
            if (pad.rightShoulder.wasPressedThisFrame || pad.rightShoulder.wasReleasedThisFrame) Refresh();
            if (Time.frameCount == openedFrame) return;
            if (pad.selectButton.wasPressedThisFrame)
            {
                clearHoldStarted = Time.unscaledTime;
                clearHoldTriggered = false;
                clearHoldEligible = page == Page.Program && !string.IsNullOrEmpty(ProgramName);
            }
            if (pad.selectButton.wasReleasedThisFrame && clearHoldStarted >= 0)
            {
                float held = Time.unscaledTime - clearHoldStarted;
                bool clear = !clearHoldTriggered && clearHoldEligible && page == Page.Program && held >= 5f;
                bool prev = !clearHoldTriggered && held < 5f;
                clearHoldStarted = -1;
                clearHoldTriggered = clearHoldEligible = false;
                if (clear) ClearCurrentProgram();
                else if (prev) HandleButton("PREV");
                return;
            }
            if (pad.selectButton.isPressed && clearHoldStarted >= 0 && clearHoldEligible && page == Page.Program
                && !clearHoldTriggered && Time.unscaledTime - clearHoldStarted >= 5f)
            {
                clearHoldTriggered = true;
                ClearCurrentProgram();
                return;
            }
            if (page == Page.DeleteConfirm)
            {
                // If both confirmation buttons arrive in one frame, cancellation wins.
                if (pad.buttonNorth.wasPressedThisFrame) { HandleButton("F5"); return; }
                if (pad.buttonWest.wasPressedThisFrame) { HandleButton("F4"); return; }
            }
            if (pad.dpad.up.wasPressedThisFrame) HandleButton("UP");
            if (pad.dpad.down.wasPressedThisFrame) HandleButton("DOWN");
            if (pad.dpad.left.wasPressedThisFrame) HandleButton("FCTN");
            if (pad.dpad.right.wasPressedThisFrame) HandleButton("ENTER");
            if (pad.buttonSouth.wasPressedThisFrame) HandleButton("F1");
            if (pad.buttonEast.wasPressedThisFrame) HandleButton("F2");
            if (pad.rightStickButton.wasPressedThisFrame) HandleButton("F3");
            if (pad.buttonWest.wasPressedThisFrame) HandleButton("F4");
            if (pad.buttonNorth.wasPressedThisFrame) HandleButton("F5");
            if (pad.leftStickButton.wasPressedThisFrame) HandleButton("1");
            if (pad.leftShoulder.wasPressedThisFrame) HandleButton("FWD");
        }
        public void HandleGamepadMenuShort()
        {
            if (!view || !view.Visible || !station || !station.IsOperating) return;
            if (page == Page.Programs) app.ClosePanel();
            else HandleButton("SELECT");
        }
        void ClearCurrentProgram()
        {
            if (string.IsNullOrEmpty(ProgramName)) { SetMessage("先创建或打开当前工位程序"); return; }
            Stop();
            Model.Clear();
            page = Page.Program; cursor = 0; selection = 0;
            arcMode = false; modifyingMotion = false; editVia = false; number = "";
            if (SaveCurrent()) SetMessage("View 长按 5 秒：已清空本程序的全部指令和点位，从 P[1] 重新录入");
            else Refresh();
        }
        void OnApplicationFocus(bool focused) { if (!focused) { shiftLatched = false; clearHoldStarted = -1; clearHoldTriggered = clearHoldEligible = false; Pause(); SaveCurrent(); Refresh(); } }
        void OnApplicationQuit() { SaveCurrent(); }
        void OnDisable() { if (Executor) Deactivate(); }
        void OnDestroy()
        {
            SaveCurrent();
            if (view) view.ButtonClicked -= HandleButton;
            if (Executor) Executor.Changed -= ExecutionChanged;
            if (Model != null) Model.Changed -= ModelChanged;
        }
    }

    // All three stations: USER X/Y on the left stick, USER Z on the triggers.
    internal static class LabTeachingGamepad
    {
        public static Vector3 JogLocal()
        {
            var pad = Gamepad.current;
            if (pad == null) return Vector3.zero;
            Vector2 stick = pad.leftStick.ReadValue();
            if (stick.sqrMagnitude < .0225f) stick = Vector2.zero;
            float z = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
            if (Mathf.Abs(z) < .15f) z = 0;
            return Vector3.ClampMagnitude(new Vector3(stick.x, z, stick.y), 1);
        }
    }
}
