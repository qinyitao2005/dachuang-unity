using UnityEngine;

namespace RoboticsLab
{
    /// <summary>Tracing lifecycle only; geometry, scoring, printing and automatic motion are separate modules.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class LabTracingExercise : MonoBehaviour
    {
        public LabRobotStation station;
        [Tooltip("Metres along station local X/Z, relative to exerciseCenter.")]
        public Vector2 layoutSize = new Vector2(.19f, .27f);
        public Vector2 layoutOffset;
        public float tolerance = .003f;
        public float contactTolerance = .0015f;
        public float automaticSpeed = .022f;
        public float travelLift = .018f;
        public static readonly string[] ShapeNames = LabTracingPaths.Names;
        public int CurrentShape { get; private set; }
        public string CurrentShapeName => ShapeNames[CurrentShape];
        public bool IsManual => CurrentShape == 0;
        public bool IsAutomatic => automatic != null && automatic.Running;
        public bool AttemptActive { get; private set; }
        public bool Completed { get; private set; }
        public float Coverage => scorer?.Coverage ?? 0;
        public float Accuracy => scorer?.Accuracy ?? 0;
        public float Score => scorer?.Score ?? 0;
        public float MeanErrorMm => scorer?.MeanErrorMm ?? 0;
        public float CurrentErrorMm => scorer?.CurrentErrorMm ?? 0;
        public string Status { get; private set; } = "沿印好的图形循迹，不留新线；绿色圆点是起点";
        LabTracingScore scorer;
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
            scorer = new LabTracingScore(GetPath(CurrentShape), tolerance);
        }

        public Vector2[] GetPath(int shape) => LabTracingPaths.Create(Mathf.Clamp(shape, 0, 4), layoutSize, layoutOffset);
        public Vector3 PaperToWorld(Vector2 point, float height = 0)
        {
            return station.transform.TransformPoint(new Vector3(station.exerciseCenter.x + point.x,
                station.drawingHeight + height, station.exerciseCenter.z + point.y));
        }
        public Vector2 WorldToPaper(Vector3 world)
        {
            Vector3 p = station.transform.InverseTransformPoint(world);
            return new Vector2(p.x - station.exerciseCenter.x, p.z - station.exerciseCenter.z);
        }

        public void RebuildBoard() { Initialize(); ResetAttempt(); if (Application.isPlaying) view?.Rebuild(); }
        public void SelectShape(int shape)
        {
            Initialize(); CurrentShape = Mathf.Clamp(shape, 0, 4); ResetAttempt(); view?.Highlight(CurrentShape);
        }
        public void ResetAttempt()
        {
            Initialize();
            automatic?.Stop("已重置");
            AttemptActive = Completed = false;
            if (station) { station.SetPen(false); station.ClearDrawing(); }
            scorer = new LabTracingScore(GetPath(CurrentShape), tolerance);
            Status = IsManual ? "手动 J：先将探针移到绿色起点，再点击开始" : "点击自动循迹，沿印线移动并评分";
        }
        public void BeginManual()
        {
            SelectShape(0);
            if (!Ready()) return;
            AttemptActive = true;
            station.SetPen(true);
            Status = "手动循迹：探针贴纸，从绿色起点沿 J 型前进";
        }
        public void StartAutomatic()
        {
            Initialize();
            if (IsManual) { Status = "J 型为手动项目，请使用方向控制"; return; }
            ResetAttempt();
            if (!Ready()) return;
            AttemptActive = true;
            automatic.Start(GetPath(CurrentShape));
            Status = automatic.Status;
        }
        bool Ready()
        {
            if (!station || !station.IsOperating || station.EmergencyStopped)
            { Status = "请先进入循迹工位并解除急停"; return false; }
            return true;
        }
        public void StopAutomatic()
        {
            string message = IsManual ? "手动循迹已停止，保留本次成绩" : "自动循迹已停止，保留本次成绩";
            automatic?.Stop(message); AttemptActive = false;
            Status = message;
        }
        public void FinishAttempt()
        {
            if (automatic != null && automatic.Running) automatic.Stop("本次循迹已结束");
            AttemptActive = false;
            Completed = scorer != null && scorer.Completed;
            if (station) station.SetPen(false);
            Status = Completed ? "循迹完成；评分按实际覆盖率和偏差计算" : "已结束；未走完的部分计入完成度扣分";
        }
        void Update()
        {
            if (!initialized) return;
            if (AttemptActive && (!station.IsOperating || station.EmergencyStopped))
            { automatic.Stop("已停止：工位退出或急停"); AttemptActive = false; Status = automatic.Status; return; }
            bool wasRunning = IsAutomatic;
            automatic.Tick(Time.deltaTime);
            if (wasRunning)
            {
                Status = automatic.Status;
                if (!IsAutomatic) { AttemptActive = false; Completed = scorer.Completed; }
            }
        }
        void LateUpdate()
        {
            if (!AttemptActive || !station || !station.IsOperating || station.EmergencyStopped) return;
            Vector3 local = station.transform.InverseTransformPoint(station.ActualToolPosition);
            bool contact = station.PenDown && Mathf.Abs(local.y - station.drawingHeight) <= contactTolerance;
            // Automatic approach/lowering is not part of the printed course. Sample after the robot's LateUpdate.
            if (IsAutomatic && !automatic.Following) return;
            scorer.Sample(WorldToPaper(station.ActualToolPosition), contact);
            if (IsManual)
            {
                if (scorer.Completed) { Completed = true; FinishAttempt(); }
                else Status = !contact ? "探针未接触纸面：贴纸后继续" : !scorer.Started ? "请将探针移到绿色起点" :
                    CurrentErrorMm > tolerance * 1000 ? "偏离参考线；退回未完成的位置继续" : "沿 J 型继续，完成后自动结算";
            }
        }
        void OnDisable() { if (initialized) { automatic.Stop("循迹已停止"); AttemptActive = false; } }
        void OnDestroy() { view?.Dispose(); }
    }
}
