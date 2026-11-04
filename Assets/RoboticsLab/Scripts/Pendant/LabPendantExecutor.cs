using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>
    /// Executes an immutable L/C program snapshot through the station's existing motion adapter.
    /// Neither point recording nor the screen owns motion, IK, collision handling or execution state.
    /// </summary>
    [DefaultExecutionOrder(1050)]
    public sealed class LabPendantExecutor : MonoBehaviour
    {
        sealed class Motion
        {
            public LabPendantMotionPath path;
            public float speed, commandSpeedMm;
            public bool pen;
            public Vector3[] samples;
        }

        const float TargetTolerance = .00008f;
        const float WorkspaceTolerance = .00002f;
        const float PaperTolerance = .0002f;
        const float SampleSpacing = .001f;

        public LabRobotStation Station { get; private set; }
        // Only the dedicated Station 1 programming controller opts in.
        public bool AllowTracing { get; set; }
        public LabPendantProgramModel Model { get; private set; }
        public LabTrajectoryState State { get; private set; } = LabTrajectoryState.Ready;
        public bool IsExecuting => State == LabTrajectoryState.Running || State == LabTrajectoryState.Paused;
        public bool IsPaused => State == LabTrajectoryState.Paused;
        public int CurrentInstructionIndex { get; private set; }
        public float CurrentPathDistance { get; private set; }
        public float CurrentEffectiveSpeedMm => CurrentInstructionIndex < execution.Count
            ? execution[CurrentInstructionIndex].speed * 1000f : 0;
        public float CurrentCommandSpeedMm => CurrentInstructionIndex < execution.Count
            ? execution[CurrentInstructionIndex].commandSpeedMm : 0;
        public float MaximumPathError { get; private set; }
        public string StatusText { get; private set; } = "程序尚未运行";
        public string LastError { get; private set; } = "";
        public event Action Changed;

        readonly List<Motion> execution = new List<Motion>();
        bool instructionStarted, penPrepared, singleStep, viaReached;
        int executionMode, executionRevision;
        float lastStationElapsed, stepElapsed, noProgress, blockedSeconds, offPathSeconds;
        float bestRemaining, stepTimeLimit;
        Vector3 previousActual;

        float ArrivalTolerance => Station && Station.IsRing ? .00035f : .001f;
        float FollowingTolerance => Station && Station.IsRing ? .0003f : .0015f;
        float PathTolerance => Station && Station.IsRing ? .0004f : .0025f;

        public void Bind(LabRobotStation station, LabPendantProgramModel model)
        {
            if (Station == station && ReferenceEquals(Model, model)) return;
            Stop();
            if (Model != null) Model.Changed -= OnProgramChanged;
            Station = station;
            Model = model;
            if (Model != null) Model.Changed += OnProgramChanged;
            StatusText = Station ? "已绑定 " + Station.displayName + "；FWD 运行，STEP 单步" : "尚未选择工位";
            Changed?.Invoke();
        }

        public bool Run()
        {
            if (IsPaused) return Resume();
            if (State == LabTrajectoryState.Running) return Reject("程序正在运行；可按 HOLD 暂停");
            return Begin(false);
        }

        public bool Step()
        {
            if (State == LabTrajectoryState.Running) return Reject("请先按 HOLD 暂停，再执行单步");
            return IsPaused ? Continue(true) : Begin(true);
        }

        bool Begin(bool oneStep)
        {
            if (!ReadyToOperate()) return false;
            List<Motion> validated;
            string error;
            if (!TryPrepare(out validated, out error))
            {
                State = LabTrajectoryState.Faulted;
                return Reject(error);
            }
            execution.Clear();
            execution.AddRange(validated);
            Station.SetPen(false);
            CurrentInstructionIndex = 0;
            CurrentPathDistance = MaximumPathError = 0;
            instructionStarted = penPrepared = false;
            singleStep = oneStep;
            executionMode = Station.TrainingMode;
            executionRevision = Model.Revision;
            lastStationElapsed = Station.Elapsed;
            previousActual = ActualLocal();
            State = LabTrajectoryState.Running;
            LastError = "";
            StatusText = oneStep ? "正在执行单步" : "程序运行中";
            Changed?.Invoke();
            return true;
        }

        public void Pause()
        {
            if (State != LabTrajectoryState.Running) return;
            State = LabTrajectoryState.Paused;
            if (Station) { Station.SetPen(false); Station.MoveTool(Vector3.zero); }
            penPrepared = false;
            StatusText = "HOLD 已暂停；FWD 继续，手动移动仍锁定";
            Changed?.Invoke();
        }

        public bool Resume() { return Continue(false); }

        bool Continue(bool oneStep)
        {
            if (!IsPaused) return Reject("程序当前未暂停");
            if (!ReadyToOperate()) return false;
            if (Station.TrainingMode != executionMode || Station.Elapsed + .0001f < lastStationElapsed)
            {
                StopWithMessage("工位已复位或模式已改变；请从头运行");
                return false;
            }
            if (CurrentInstructionIndex >= execution.Count) { Complete(); return true; }
            singleStep = oneStep;
            penPrepared = false;
            noProgress = blockedSeconds = offPathSeconds = 0;
            bestRemaining = float.PositiveInfinity;
            previousActual = ActualLocal();
            State = LabTrajectoryState.Running;
            LastError = "";
            StatusText = (oneStep ? "正在执行单步；" : "程序继续运行；") + FeedLabel(execution[CurrentInstructionIndex]);
            Changed?.Invoke();
            return true;
        }

        public void Stop()
        {
            // Binding/closing an idle pendant must not alter a legacy program's active tool.
            if (Station && (IsExecuting || !LegacyProgramBusy())) { Station.SetPen(false); Station.MoveTool(Vector3.zero); }
            execution.Clear();
            CurrentInstructionIndex = 0;
            CurrentPathDistance = 0;
            instructionStarted = penPrepared = singleStep = false;
            State = LabTrajectoryState.Ready;
            LastError = "";
            StatusText = "程序已停止，示教点和程序行保留";
            Changed?.Invoke();
        }

        void StopWithMessage(string message)
        {
            Stop();
            StatusText = message;
            Changed?.Invoke();
        }

        void Update()
        {
            if (!IsExecuting) return;
            if (!Station || !Station.IsOperating)
            { StopWithMessage("已离开工位，程序停止；重新进入后需明确运行"); return; }
            if (Station.TrainingMode != executionMode)
            { StopWithMessage("训练模式已改变，程序停止；请重新运行"); return; }
            if (Station.Elapsed + .0001f < lastStationElapsed)
            { StopWithMessage("工位已复位，程序停止；请重新运行"); return; }
            lastStationElapsed = Station.Elapsed;
            if (Station.EmergencyStopped)
            {
                if (State == LabTrajectoryState.Running)
                {
                    Pause();
                    StatusText = "急停已触发；解除急停后按 FWD 明确继续";
                    Changed?.Invoke();
                }
                return;
            }
            if (State != LabTrajectoryState.Running || Time.deltaTime <= 0) return;
            if (CurrentInstructionIndex >= execution.Count) { Complete(); return; }

            var motion = execution[CurrentInstructionIndex];
            if (!instructionStarted)
            {
                instructionStarted = true;
                penPrepared = false;
                CurrentPathDistance = 0;
                viaReached = !motion.path.IsArc || motion.path.ViaDistance <= TargetTolerance;
                stepElapsed = noProgress = blockedSeconds = offPathSeconds = 0;
                bestRemaining = float.PositiveInfinity;
                stepTimeLimit = Mathf.Max(10f, motion.path.Length / motion.speed * 6f + 8f);
                previousActual = ActualLocal();
                StatusText = "正在执行第 " + (CurrentInstructionIndex + 1) + " 行 "
                    + (motion.path.IsArc ? "C 圆弧" : "L 直线") + " / FINE；"
                    + FeedLabel(motion);
                Changed?.Invoke();
            }
            if (!Finite(Station.moveSpeed) || Station.moveSpeed <= 0 || motion.speed > Station.moveSpeed + .000001f)
            { Fault("工位速度配置在运行期间改变；请从头运行以重新计算限速"); return; }
            stepElapsed += Time.deltaTime;
            if (stepElapsed > stepTimeLimit)
            { Fault("执行超时，真实末端未到位；请检查轨迹可达性和碰撞"); return; }

            Vector3 actual = ActualLocal();
            if (!Finite(actual)) { Fault("真实工具坐标无效，已停止"); return; }
            float deviation = DistanceToPath(motion.samples, actual);
            MaximumPathError = Mathf.Max(MaximumPathError, deviation);
            offPathSeconds = deviation > PathTolerance ? offPathSeconds + Time.deltaTime : 0;
            if (deviation > PathTolerance * 2f || offPathSeconds > .12f)
            { Fault("真实末端偏离轨迹 " + (deviation * 1000).ToString("F2") + " mm；已停止，不能跳过此行"); return; }

            // Collision flags belong to the preceding committed IK frame. Inspect before MoveTool clears them.
            bool collision = Station.IsRing ? Station.RingMovementBlocked || Station.RobotWireBlocked
                : !string.IsNullOrEmpty(Station.DrawingBlockReason);
            if (collision && Vector3.Distance(previousActual, actual) < .000005f)
                blockedSeconds += Time.deltaTime;
            else blockedSeconds = 0;
            previousActual = actual;
            if (blockedSeconds > .35f) { Fault(BlockedReason()); return; }

            if (!penPrepared)
            {
                Station.SetPen(false);
                Vector3 resumePoint = motion.path.EvaluateDistance(CurrentPathDistance);
                if (!AtPoint(resumePoint, ArrivalTolerance))
                { Drive(resumePoint, motion.speed); WatchProgress(motion, resumePoint); return; }
                if (motion.pen)
                {
                    if (Station.IsRing) { Fault("套圈工位不能落笔"); return; }
                    // This never lowers or flattens an arc implicitly. Plane consistency was checked before motion.
                    Station.SetPen(true);
                }
                penPrepared = true;
            }

            Vector3 currentGoal = motion.path.EvaluateDistance(CurrentPathDistance);
            bool atVia = !viaReached && CurrentPathDistance >= motion.path.ViaDistance - .000001f;
            if (atVia && AtPoint(currentGoal, ArrivalTolerance)) viaReached = true;
            if (CurrentPathDistance >= motion.path.Length - .000001f && AtPoint(motion.path.End, ArrivalTolerance))
            { FinishInstruction(); return; }

            // Do not let the ideal target run away from the real TCP. Advance by arc length, not chords or time alone.
            if (TargetAtPoint(currentGoal)
                && Vector3.Distance(actual, currentGoal) <= FollowingTolerance && (!atVia || viaReached))
            {
                float boundary = viaReached ? motion.path.Length : motion.path.ViaDistance;
                // A slow render frame must not replace a curved segment with one long chord.
                // The feed is an upper limit; IK following and these spatial substeps may reduce it.
                float advance = Mathf.Min(motion.speed * Time.deltaTime, Station.IsRing ? .00035f : .003f);
                CurrentPathDistance = Mathf.Min(boundary, CurrentPathDistance + advance);
                currentGoal = motion.path.EvaluateDistance(CurrentPathDistance);
            }
            Drive(currentGoal, motion.speed);
            WatchProgress(motion, currentGoal);
        }

        void Drive(Vector3 destination, float speed)
        {
            Vector3 delta = destination - Station.toolTarget.localPosition;
            float fraction = Mathf.Min(speed / Station.moveSpeed,
                delta.magnitude / Mathf.Max(.0000001f, Station.moveSpeed * Time.deltaTime));
            Station.MoveTool(Station.transform.TransformDirection(delta.normalized) * fraction);
        }

        bool AtPoint(Vector3 destination, float tolerance)
        {
            return TargetAtPoint(destination)
                && Vector3.Distance(ActualLocal(), destination) <= tolerance;
        }

        bool TargetAtPoint(Vector3 destination)
        {
            // Normalize only when the workspace actually clamps Y. A raised pen
            // approaching the sheet can have valid substeps 0.1-0.2 mm above it:
            // Drive can reach those heights and must be allowed to advance past them.
            if (Station.TrainingMode == 0 && Mathf.Abs(destination.y - Station.drawingHeight) <= PaperTolerance
                && (Station.PenDown || destination.y <= Station.drawingHeight))
                destination.y = Station.drawingHeight;
            return Vector3.Distance(Station.toolTarget.localPosition, destination) <= TargetTolerance;
        }

        void WatchProgress(Motion motion, Vector3 goal)
        {
            float remaining = motion.path.Length - CurrentPathDistance
                + Vector3.Distance(Station.toolTarget.localPosition, goal) + Vector3.Distance(ActualLocal(), goal);
            if (remaining < bestRemaining - .00001f) { bestRemaining = remaining; noProgress = 0; }
            else noProgress += Time.deltaTime;
            if (noProgress > 2.5f)
                Fault(Station.RobotWireBlocked || Station.RingMovementBlocked || !string.IsNullOrEmpty(Station.DrawingBlockReason)
                    ? BlockedReason() : "真实末端无法继续前进；关节限位、工作区或 IK 阻挡了该轨迹，请调整此行");
        }

        void FinishInstruction()
        {
            Station.SetPen(false);
            CurrentInstructionIndex++;
            instructionStarted = penPrepared = false;
            CurrentPathDistance = 0;
            if (CurrentInstructionIndex >= execution.Count) { Complete(); return; }
            if (singleStep)
            {
                Pause();
                StatusText = "单步 FINE 到位；下一行 " + (CurrentInstructionIndex + 1) + "，FWD 继续 / STEP 单步";
            }
            Changed?.Invoke();
        }

        void Complete()
        {
            if (Station) { Station.SetPen(false); Station.MoveTool(Vector3.zero); }
            State = LabTrajectoryState.Completed;
            instructionStarted = penPrepared = singleStep = false;
            LastError = "";
            StatusText = "程序执行完毕，共 " + execution.Count + " 行；全部 FINE 到位";
            Changed?.Invoke();
        }

        void Fault(string message)
        {
            if (Station) { Station.SetPen(false); Station.MoveTool(Vector3.zero); }
            State = LabTrajectoryState.Faulted;
            LastError = "第 " + (CurrentInstructionIndex + 1) + " 行：" + message;
            StatusText = LastError;
            Changed?.Invoke();
        }

        string BlockedReason()
        {
            if (!Station.IsRing) return (Station.IsTracing ? "探针运动被阻挡：" : "绘图运动被阻挡：") + Station.DrawingBlockReason + "；已停止，请调整轨迹";
            if (Station.RobotWireBlocked)
                return "机械臂与钢丝的碰撞边界阻挡了轨迹"
                    + (string.IsNullOrEmpty(Station.RobotCollisionReason) ? "" : "（" + Station.RobotCollisionReason + "）")
                    + "；已停止，请调整此行";
            return "圆环被钢丝或两端边界阻挡；已停止，请调整此行，不能越界完成";
        }

        static string FeedLabel(Motion motion)
        {
            return (motion.commandSpeedMm > motion.speed * 1000f + .001f
                ? "指令 " + motion.commandSpeedMm.ToString("0.###") + " / 限速 " + (motion.speed * 1000f).ToString("0.###")
                : (motion.speed * 1000f).ToString("0.###")) + " mm/s";
        }

        bool ReadyToOperate()
        {
            if (LegacyProgramBusy()) return Reject("高级轨迹程序正在运行或暂停；请先停止高级轨迹，再运行 L/C 示教程序");
            if (!isActiveAndEnabled) return Reject("示教程序执行器未启用");
            if (!Station || !Station.IsOperating || !Station.toolTarget || !Station.endEffector)
                return Reject("请先进入已解锁的机械臂工位");
            if (Station.IsTracing && !AllowTracing) return Reject("01 工位请使用专用循迹示教器");
            if (Station.IsTracing && Station.GetComponent<LabTracingExercise>() && Station.GetComponent<LabTracingExercise>().IsAutomatic)
                return Reject("先停止预设自动循迹，再运行示教程序");
            if (Station.EmergencyStopped) return Reject("请先解除急停，再按 FWD 继续");
            if (!Finite(Station.moveSpeed) || Station.moveSpeed <= 0) return Reject("工位运动速度配置无效");
            if (Model == null) return Reject("示教程序尚未绑定");
            return true;
        }

        bool LegacyProgramBusy()
        {
            var app = GetComponent<LabTrainingApp>();
            if (!app) app = FindObjectOfType<LabTrainingApp>();
            if (app && app.trajectoryProgram && app.trajectoryProgram.IsExecuting) return true;
            foreach (var program in FindObjectsOfType<LabTrajectoryProgram>()) if (program.IsExecuting) return true;
            return false;
        }

        bool TryPrepare(out List<Motion> result, out string error)
        {
            result = new List<Motion>();
            error = "";
            if (Model.Instructions.Count == 0) { error = "程序为空；先移动工具并记录 L / C 点位"; return false; }
            Vector3 start = ActualLocal();
            for (int i = 0; i < Model.Instructions.Count; i++)
            {
                var instruction = Model.Instructions[i];
                string prefix = "第 " + (i + 1) + " 行：";
                if (instruction == null || !instruction.IsComplete)
                { error = prefix + "圆弧尚未完成，请补录中间点和终点"; return false; }
                if (!Finite(instruction.SpeedMm) || instruction.SpeedMm < 1f || instruction.SpeedMm > 1000f)
                { error = prefix + "指令速度必须为 1–1000 mm/sec；实际进给受工位速度上限约束"; return false; }
                var end = Model.FindPoint(instruction.TargetPoint);
                if (end == null) { error = prefix + "终点 P[" + instruction.TargetPoint + "] 不存在"; return false; }
                LabPendantMotionPath path;
                string pathError;
                if (instruction.Type == LabPendantInstructionType.Circular)
                {
                    var via = Model.FindPoint(instruction.ViaPoint);
                    if (via == null) { error = prefix + "中间点 P[" + instruction.ViaPoint + "] 不存在"; return false; }
                    if (!LabPendantMotionPath.TryArc(start, via.LocalPosition, end.LocalPosition, out path, out pathError))
                    { error = prefix + pathError; return false; }
                }
                else if (instruction.Type == LabPendantInstructionType.Linear)
                {
                    if (!LabPendantMotionPath.TryLine(start, end.LocalPosition, out path, out pathError))
                    { error = prefix + pathError; return false; }
                }
                else { error = prefix + "未知运动类型"; return false; }

                bool pen = !Station.IsTracing && instruction.PenDown;
                if (pen && Station.IsRing) { error = prefix + "套圈工位不能落笔"; return false; }
                Vector3 minimum, maximum;
                if (!path.TryGetBounds(out minimum, out maximum) || !InsideWorkspace(minimum) || !InsideWorkspace(maximum))
                { error = prefix + "轨迹坐标极值超出工位工作区；请调整示教点，不能裁剪圆弧"; return false; }
                if (pen && (Mathf.Abs(minimum.y - Station.drawingHeight) > PaperTolerance
                    || Mathf.Abs(maximum.y - Station.drawingHeight) > PaperTolerance))
                { error = prefix + "落笔轨迹必须整段贴合纸面；先抬笔接近纸面起点，再记录落笔 L/C"; return false; }
                int count = Mathf.Max(1, Mathf.CeilToInt(path.Length / SampleSpacing));
                if (count > 20000) { error = prefix + "轨迹过长，无法执行"; return false; }
                var samples = new Vector3[count + 1];
                for (int j = 0; j <= count; j++)
                {
                    Vector3 p = path.EvaluateDistance(path.Length * j / count);
                    if (!InsideWorkspace(p)) { error = prefix + "轨迹经过当前工位工作区外；请调整点位，不能裁剪圆弧"; return false; }
                    if (pen && Mathf.Abs(p.y - Station.drawingHeight) > PaperTolerance)
                    { error = prefix + "落笔轨迹必须整段贴合纸面；先抬笔接近纸面起点，再记录落笔 L/C"; return false; }
                    samples[j] = p;
                }
                result.Add(new Motion { path = path, speed = Mathf.Min(instruction.SpeedMm * .001f, Station.moveSpeed),
                    commandSpeedMm = instruction.SpeedMm, pen = pen, samples = samples });
                start = end.LocalPosition;
            }
            return true;
        }

        bool InsideWorkspace(Vector3 p)
        {
            Vector3 center = Station.exerciseCenter, half = Station.workspaceHalfSize;
            float minimumY = Station.IsRing ? center.y - half.y : Mathf.Max(Station.drawingHeight, center.y - half.y);
            float minimumYTolerance = !Station.IsRing && minimumY == Station.drawingHeight ? PaperTolerance : WorkspaceTolerance;
            return Finite(p) && p.x >= center.x - half.x - WorkspaceTolerance && p.x <= center.x + half.x + WorkspaceTolerance
                && p.z >= center.z - half.z - WorkspaceTolerance && p.z <= center.z + half.z + WorkspaceTolerance
                && p.y >= minimumY - minimumYTolerance && p.y <= center.y + half.y + WorkspaceTolerance;
        }

        static float DistanceToPath(Vector3[] samples, Vector3 point)
        {
            float best = float.PositiveInfinity;
            for (int i = 1; i < samples.Length; i++)
            {
                Vector3 a = samples[i - 1], delta = samples[i] - a;
                float t = delta.sqrMagnitude > 1e-14f ? Mathf.Clamp01(Vector3.Dot(point - a, delta) / delta.sqrMagnitude) : 0;
                best = Mathf.Min(best, (point - (a + delta * t)).sqrMagnitude);
            }
            return Mathf.Sqrt(best);
        }

        Vector3 ActualLocal() { return Station.transform.InverseTransformPoint(Station.ActualToolPosition); }
        static bool Finite(Vector3 p) { return Finite(p.x) && Finite(p.y) && Finite(p.z); }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        bool Reject(string message) { LastError = StatusText = message; Changed?.Invoke(); return false; }
        void OnProgramChanged()
        {
            if (IsExecuting && Model != null && Model.Revision != executionRevision)
                Fault("运行期间程序数据发生改变；请确认点位后从头运行");
        }
        void OnDisable() { if (IsExecuting) StopWithMessage("示教执行器已关闭，程序停止；需重新运行"); }
        void OnDestroy() { if (Model != null) Model.Changed -= OnProgramChanged; }
    }
}
