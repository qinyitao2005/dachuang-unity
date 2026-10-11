using UnityEngine;

namespace RoboticsLab
{
    /// <summary>工位门面：保留场景绑定和控制器接口，协调各独立模块的调用顺序。</summary>
    public class Group0927Station : MonoBehaviour
    {
        public int stationIndex;
        public string displayName = "六轴机械臂";
        public bool IsOperating { get; private set; }
        public bool EmergencyStopped { get; private set; }
        public bool PenDown { get; private set; }
        public int TrainingMode { get; private set; }
        public float Progress => TrainingMode == 0 ? (drawing?.Progress ?? 0) : (ringExercise?.Progress ?? 0);
        public int Contacts => ringExercise?.Contacts ?? 0;
        public float Elapsed { get; private set; }
        public Transform modelRoot, toolTarget, endEffector;
        public Transform ringGripTcp, ringGripTool, originalEndTool;
        public Transform drawingTcp;
        public Transform drawingGripTool;
        public Transform DrawingToolRoot => drawingGripTool ? drawingGripTool : originalEndTool;
        public float ringGripRoll;
        public bool ringUsePoseSeed;
        public JointConfig[] joints;
        public Transform drawingBoard, wireRoot, ringRoot, penRoot;
        public LineRenderer wireRenderer, ringRenderer;
        public Renderer ringSolidRenderer;
        public Vector3 exerciseCenter = new Vector3(.65f, 1.25f, 0);
        public Vector3 workspaceHalfSize = new Vector3(.38f,.36f,.53f);
        public float moveSpeed = .22f;
        public float drawingHeight = 1.055f;
        public float exerciseScale = 1f, tableHeight = .93f;
        public float ringRadius = .105f, ringTubeRadius = .009f, wireRadius = .007f;
        public Vector3[] wirePoints;
        public bool ToolInContact => ringExercise?.ToolInContact ?? false;
        public bool RingMovementBlocked => ringMotion?.RingMovementBlocked ?? false;
        public bool RobotWireBlocked => ringMotion?.RobotWireBlocked ?? false;
        public string RobotCollisionReason => ringMotion?.RobotCollisionReason ?? "";
        public Vector3 RingGripPosition => ringGripTcp ? ringGripTcp.position : endEffector ? endEffector.position : transform.position;
        public Vector3 ActualToolPosition
        {
            get { if(TrainingMode==0&&drawingTcp)return drawingTcp.position;
                if(TrainingMode!=1||!ringGripTcp)return endEffector?endEffector.position:transform.position;
                Vector3 center;Quaternion rotation;ToolView.GetActualRingPose(CurrentRingFrame, out center, out rotation);return center; }
        }
        public float PositionError => toolTarget && endEffector ? Vector3.Distance(toolTarget.position,ActualToolPosition) : 0;

        LabRobotJointRig rig;
        LabPositionIkSolver positionIk;
        LabDrawingPoseIkSolver drawingIk;
        LabDrawingMotionController drawingMotion;
        LabDrawingCollision drawingCollision;
        LabRingPoseIkSolver ringIk;
        LabDrawingExercise drawing;
        LabRobotToolView tools;
        LabRingGeometry ringGeometry;
        LabRingMotionController ringMotion;
        LabRingExercise ringExercise;
        LabRobotWorkspace workspace;
        Group0927WireCollision robotWireCollision;
        armProperty legacyState;
        bool initialized;
        Vector3 requestedMotion;

        // 几何查询也供未进入 Start 的编辑器诊断使用，不触发重置或关节运动。
        LabRingGeometry Geometry => ringGeometry ?? (ringGeometry = new LabRingGeometry(this));
        LabRobotToolView ToolView => tools ?? (tools = new LabRobotToolView(this));
        Quaternion CurrentRingFrame => Geometry.Frame(toolTarget ? toolTarget.localPosition
            : transform.InverseTransformPoint(RingGripPosition), ringMotion?.Segment ?? 0);

        void Start() { Initialize(); ResetExercise(); }

        void Initialize()
        {
            if (initialized) return;
            if (joints == null) joints = new JointConfig[0];
            if (!toolTarget)
            {
                toolTarget = new GameObject("ToolTarget").transform;
                toolTarget.SetParent(transform, false);
            }
            rig = new LabRobotJointRig(joints);
            positionIk = new LabPositionIkSolver();
            drawingIk = new LabDrawingPoseIkSolver();
            drawingCollision = new LabDrawingCollision(this);
            drawingMotion = new LabDrawingMotionController(this, rig, drawingIk, drawingCollision);
            ringIk = new LabRingPoseIkSolver();
            drawing = new LabDrawingExercise(this);
            tools = ToolView;
            ringGeometry = Geometry;
            ringExercise = new LabRingExercise(this);
            workspace = new LabRobotWorkspace(this);
            robotWireCollision = GetComponent<Group0927WireCollision>() ?? gameObject.AddComponent<Group0927WireCollision>();
            robotWireCollision.station = this;
            ringMotion = new LabRingMotionController(this, rig, ringGeometry, robotWireCollision, SolveIK);
            if (modelRoot) legacyState = modelRoot.GetComponentInChildren<armProperty>(true);
            initialized = true;
            SetMode(TrainingMode);
        }

        public void BeginOperation()
        {
            Initialize();
            IsOperating = true;
            requestedMotion = Vector3.zero;
            ApplyExerciseVisibility();
            ResetExercise();
        }

        public void EndOperation()
        {
            IsOperating = PenDown = false;
            requestedMotion = Vector3.zero;
            drawing?.EndStroke();
            ApplyExerciseVisibility();
            // 退出后原装轨迹夹具会重新显示，机械臂回到原生停放姿态。
            if (initialized) rig.ResetHome();
            if (legacyState) legacyState.armIsMoving = false;
        }

        public void SetEmergencyStop(bool stopped)
        {
            EmergencyStopped = stopped;
            requestedMotion = Vector3.zero;
            if (stopped) { PenDown = false; drawing?.EndStroke(); }
        }

        public void SetMode(int mode)
        {
            TrainingMode = Mathf.Clamp(mode, 0, 1);
            ApplyExerciseVisibility();
            if (initialized) ResetExercise();
        }

        void ApplyExerciseVisibility()
        {
            ToolView.ApplyVisibility(IsOperating, TrainingMode);
            drawing?.SetVisible(IsOperating && TrainingMode == 0);
            if (robotWireCollision) robotWireCollision.RebuildCache();
            drawingCollision?.RebuildCache();
        }

        public void SetPen(bool down)
        {
            PenDown = down && IsOperating && !EmergencyStopped && TrainingMode == 0;
            if (PenDown && toolTarget)
            {
                Vector3 p = toolTarget.localPosition;
                p.y = drawingHeight;
                toolTarget.localPosition = p;
            }
            else drawing?.EndStroke();
        }

        public void ClearDrawing() { drawing?.Clear(); }

        public void ResetExercise()
        {
            if (!initialized) { Initialize(); return; }
            rig.CaptureCollisionPose();
            Vector3 previousTarget = toolTarget.localPosition;
            PenDown = EmergencyStopped = false;
            Elapsed = 0;
            requestedMotion = Vector3.zero;
            drawing.Clear();
            ringExercise.Reset();
            ringMotion.Reset();
            rig.ResetHome();
            Vector3 start = exerciseCenter;
            if (TrainingMode == 0) start.y = drawingHeight + .12f * exerciseScale;
            else if (wirePoints != null && wirePoints.Length > 1) start = wirePoints[0];
            toolTarget.localPosition = start;
            if (IsOperating)
            {
                float settled = TrainingMode == 1 ? .0003f : .001f;
                for (int i = 0; i < 60; i++)
                {
                    bool seedPose = TrainingMode == 1 && ringUsePoseSeed && i < 10;
                    SolvePose(.18f, seedPose);
                    if (!seedPose && endEffector && PositionError < settled) break;
                }
            }
            ringMotion.AcceptCurrentTarget();
            drawingMotion.Reset();
            if (IsOperating && TrainingMode == 0 && !drawingMotion.ValidateReset())
            {
                rig.RestoreCollisionPose();
                toolTarget.localPosition = previousTarget;
                EndOperation();
                EmergencyStopped = true;
            }
            tools.UpdatePose(CurrentRingFrame);
        }

        /// <summary>控制器/轨迹程序每帧调用一次，输入世界 XYZ 方向，时间积分由本入口完成。</summary>
        public void MoveTool(Vector3 input)
        {
            if (!IsOperating || EmergencyStopped || !toolTarget) return;
            ringMotion.ClearInputBlock();
            drawingMotion.ClearInputBlock();
            requestedMotion = Vector3.ClampMagnitude(input, 1);
            Vector3 local = workspace.Integrate(requestedMotion, PenDown, Time.deltaTime);
            if (TrainingMode == 1 && wirePoints != null && wirePoints.Length > 1)
                ringMotion.MoveTo(local, Time.deltaTime);
            else toolTarget.localPosition = local;
        }

        void LateUpdate()
        {
            if (!initialized) return;
            if (IsOperating && !EmergencyStopped)
            {
                Elapsed += Time.deltaTime;
                if (TrainingMode == 1 && wirePoints != null && wirePoints.Length > 1)
                    ringMotion.MoveTo(toolTarget.localPosition, Time.deltaTime);
                else if (TrainingMode == 0) drawingMotion.Tick(Time.deltaTime);
                else SolveIK(Time.deltaTime);
            }
            if (legacyState) legacyState.armIsMoving = IsOperating && !EmergencyStopped && requestedMotion.sqrMagnitude > .001f;
            tools.UpdatePose(CurrentRingFrame);
            if (IsOperating && !EmergencyStopped)
            {
                if (TrainingMode == 0) drawing.Tick(PenDown);
                else
                {
                    ringExercise.Tick(ringMotion.RimBlocked, Time.deltaTime);
                    tools.SetRingContact(ringExercise.ToolInContact);
                }
            }
            requestedMotion = Vector3.zero;
            ringMotion.EndFrame();
        }

        void SolveIK(float deltaTime) { SolvePose(deltaTime, false); }

        void SolvePose(float deltaTime, bool initializeRingPose)
        {
            if (!endEffector || !toolTarget || rig.Count == 0) return;
            if (TrainingMode == 1 && ringGripTcp)
                ringIk.Solve(rig, ringGripTcp, toolTarget.position, CurrentRingFrame, ringRadius, initializeRingPose, deltaTime);
            else if (TrainingMode == 0 && drawingTcp)
                drawingIk.Solve(rig, drawingTcp, toolTarget.position, Vector3.up, Vector3.up, deltaTime, DrawingToolRoot);
            else
                positionIk.Solve(rig, endEffector, toolTarget.position, deltaTime,
                    TrainingMode == 1 ? Mathf.Max(.00006f, .00025f * exerciseScale) : .002f);
        }

        // 碰撞组件继续使用原接口；几何实现集中在 LabRingGeometry。
        internal float HeldRingSafetyMargin => Geometry.Skin;
        internal bool HeldRingClearance(out float clearance) { return Geometry.HeldRingClearance(out clearance); }
        internal bool HeldRingThreadAllowed() { return Geometry.HeldRingThreadAllowed(ringMotion?.Segment ?? 0); }
        public string DescribeRingConstraint() { return ringMotion?.DescribeConstraint() ?? "station-not-initialized"; }
        public bool DrawingMovementBlocked => drawingMotion?.Blocked ?? false;
        public string DrawingBlockReason => drawingMotion?.Reason ?? "";
        public float DrawingTiltDegrees => drawingTcp ? Vector3.Angle(drawingTcp.up, Vector3.up) : 180;
        public float DrawingGripTiltDegrees => DrawingToolRoot
            ? Mathf.Max(Vector3.Angle(DrawingToolRoot.forward, Vector3.down),
                Mathf.Abs(90f - Vector3.Angle(DrawingToolRoot.up, Vector3.up))) : 180;

        void OnDestroy() { drawing?.Dispose(); }
    }
}
