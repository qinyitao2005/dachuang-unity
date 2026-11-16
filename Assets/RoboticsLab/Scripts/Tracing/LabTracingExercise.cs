using UnityEngine;

namespace RoboticsLab
{
    /// <summary>Printed course and explicit assessment lifecycle. Motion remains owned by the pendant.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class LabTracingExercise : MonoBehaviour
    {
        public LabRobotStation station;
        public Vector2 layoutSize = new Vector2(.19f, .27f);
        public Vector2 layoutOffset;
        // Retained serialized geometry/motion settings for diagnostic runners.
        public float tolerance = .003f, contactTolerance = .0015f;
        public float automaticSpeed = .022f, travelLift = .018f;
        public static readonly string[] ShapeNames = { "J 型", "圆形", "三角形", "正方形", "复合曲线" };
        public static LabTracingSessionHistory History { get; } = new LabTracingSessionHistory();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSessionHistory() { History.Clear(); }
        public int CurrentShape { get; private set; }
        public string CurrentShapeName => ShapeNames[CurrentShape];
        public bool IsManual => CurrentShape == 0;
        public bool IsAutomatic => automatic != null && automatic.Running;
        public bool AttemptActive => assessment != null && assessment.Active;
        public bool Sampling => assessment != null && assessment.State == LabTracingAssessment.Phase.Sampling;
        public bool HasResult => assessment != null && assessment.HasResult;
        public bool IsProgrammed => AttemptActive;
        public bool Completed => assessment != null && assessment.Completed;
        public float Coverage => assessment?.Coverage ?? 0;
        public float Accuracy => assessment?.Accuracy ?? 0;
        public float Score => assessment?.Score ?? 0;
        public float MeanErrorMm => assessment?.MeanErrorMm ?? 0;
        public float CurrentErrorMm => assessment?.CurrentErrorMm ?? 0;
        public string Status { get; private set; } = "先选图形，再点击开始评分";
        LabTracingAssessment assessment;
        LabTracingAutomaticRunner automatic;
        LabTracingBoardView view;
        bool initialized;

        void Start() { Initialize(); }
        void Initialize()
        {
            if (initialized) return;
            if (!station) station = GetComponent<LabRobotStation>();
            if (!station) return;
            initialized = true;
            automatic = new LabTracingAutomaticRunner(this);
            view = new LabTracingBoardView(this);
            if (Application.isPlaying) view.Rebuild();
            assessment = NewAssessment();
        }
        LabTracingAssessment NewAssessment() => new LabTracingAssessment(GetPath(CurrentShape), CurrentShape, History);
        public Vector2[] GetPath(int shape) => LabTracingPaths.Create(Mathf.Clamp(shape, 0, 4), layoutSize, layoutOffset);
        public Vector3 PaperToWorld(Vector2 point, float height = 0) => station.transform.TransformPoint(
            new Vector3(station.exerciseCenter.x + point.x, station.drawingHeight + height, station.exerciseCenter.z + point.y));
        public Vector2 WorldToPaper(Vector3 world)
        {
            Vector3 p = station.transform.InverseTransformPoint(world);
            return new Vector2(p.x - station.exerciseCenter.x, p.z - station.exerciseCenter.z);
        }
        public void RebuildBoard() { Initialize(); ResetAttempt(); if (Application.isPlaying) view?.Rebuild(); }
        public void SelectShape(int shape)
        {
            Initialize();
            if (AttemptActive || IsAutomatic) return;
            CurrentShape = Mathf.Clamp(shape, 0, 4);
            ResetAttempt(); view?.Highlight(CurrentShape);
        }
        public void ResetAttempt()
        {
            Initialize();
            if (IsAutomatic) automatic.Stop("已重置诊断演示");
            assessment?.Cancel();
            assessment = NewAssessment();
            Status = "已选" + CurrentShapeName + "；到安全点后点击开始评分";
        }
        public bool BeginAssessment()
        {
            Initialize();
            if (!station || !station.IsOperating || station.EmergencyStopped || IsAutomatic)
            { Status = "请先进入工位并解除急停"; return false; }
            if (!assessment.Arm()) return false;
            Status = "等待 Z＜10 mm；继续 SHIFT+FWD 或点动，从绿色起点循迹";
            return true;
        }
        // Kept solely for existing motion diagnostics, with the new assessment core.
        public void BeginManual() { ResetAttempt(); SelectShape(0); BeginAssessment(); }
        public void StartAutomatic()
        {
            ResetAttempt();
            if (BeginAssessment()) automatic.Start(GetPath(CurrentShape));
        }
        public void StopAutomatic()
        {
            if (IsAutomatic) automatic.Stop("诊断演示已停止");
            if (AttemptActive) { assessment.Cancel(); Status = "评测已取消；重置评分后重试，历史最高分保留"; }
        }
        public void FinishAttempt()
        {
            // Explicit settlement adapter for historical synthetic motion diagnostics only.
            if (assessment == null || !Sampling) return;
            assessment.Tick(WorldToPaper(station.ActualToolPosition), LabTracingAssessment.HeightLimit + .001f);
            UpdateResultStatus();
        }
        void Update()
        {
            if (!initialized) return;
            if (!station.IsOperating) { StopAutomatic(); return; }
            if (IsAutomatic) automatic.Tick(Time.deltaTime);
        }
        void LateUpdate()
        {
            if (!AttemptActive || !station || !station.IsOperating || station.EmergencyStopped) return;
            // Legacy automatic motion diagnostics sample only once they reach the printed course.
            if (IsAutomatic && !automatic.Following) return;
            float height = station.transform.InverseTransformPoint(station.ActualToolPosition).y - station.drawingHeight;
            assessment.Tick(WorldToPaper(station.ActualToolPosition), height);
            if (HasResult) UpdateResultStatus();
            else Status = Sampling ? "评分中：" + CurrentShapeName + "；走完自动结算，抬笔 Z＞10 mm 可提前结算"
                : "等待 Z＜10 mm；从绿色起点开始，按参考线顺序前进";
        }
        void UpdateResultStatus()
        {
            if (!HasResult) return;
            Status = (Completed ? "图形完成" : "抬笔提前结束，未完成部分扣分")
                + "；本次 " + Score.ToString("F1") + " 分。重置评分后可重试";
        }
        void OnDisable() { if (initialized) StopAutomatic(); }
        void OnDestroy() { view?.Dispose(); }
    }
}
