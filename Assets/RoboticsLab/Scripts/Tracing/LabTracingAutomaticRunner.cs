using UnityEngine;

namespace RoboticsLab
{
    /// <summary>Safe point following through MoveTool; never writes joint or TCP transforms.</summary>
    public sealed class LabTracingAutomaticRunner
    {
        enum Stage { Lift, Travel, Lower, Follow, Finish }
        readonly LabTracingExercise owner;
        Stage stage;
        Vector2[] path;
        int index;
        Vector3 target, lastPosition;
        float stalled, targetTime;
        public bool Running { get; private set; }
        public bool Following => Running && stage == Stage.Follow;
        public string Status { get; private set; }
        public bool Succeeded { get; private set; }

        public LabTracingAutomaticRunner(LabTracingExercise exercise) { owner = exercise; }

        public void Start(Vector2[] points)
        {
            path = points;
            index = 0;
            Running = true;
            Succeeded = false;
            owner.station.SetPen(false);
            Vector3 p = owner.station.transform.InverseTransformPoint(owner.station.ActualToolPosition);
            target = owner.station.transform.TransformPoint(new Vector3(p.x, owner.station.drawingHeight + owner.travelLift, p.z));
            lastPosition = owner.station.ActualToolPosition;
            stalled = targetTime = 0;
            stage = Stage.Lift;
            Status = "自动循迹：抬起探针";
        }

        public void Stop(string reason)
        {
            Running = false;
            Status = reason;
            if (owner.station)
            {
                owner.station.SetPen(false);
                // Discard the unconsumed requested motion on pause/help/exit. This changes
                // the command only; joints and the rigid CAD tool are never teleported.
                if (owner.station.IsOperating && owner.station.toolTarget)
                    owner.station.toolTarget.position = owner.station.ActualToolPosition;
            }
        }

        public void Tick(float dt)
        {
            if (!Running || dt <= 0) return;
            var station = owner.station;
            if (!station || !station.IsOperating || station.EmergencyStopped) { Stop("已停止：工位退出或急停"); return; }
            if (stage == Stage.Finish)
            {
                Succeeded = true;
                Stop("自动路径执行完毕，成绩依据实际探针轨迹");
                return;
            }
            Vector3 actual = station.ActualToolPosition;
            float distance = Vector3.Distance(target, actual);
            bool arrived = distance <= (stage == Stage.Follow ? .00055f : .0008f);
            if (arrived)
            {
                stalled = targetTime = 0;
                switch (stage)
                {
                    case Stage.Lift:
                        stage = Stage.Travel; target = owner.PaperToWorld(path[0], owner.travelLift); Status = "自动循迹：移动到绿色起点"; break;
                    case Stage.Travel:
                        stage = Stage.Lower; target = owner.PaperToWorld(path[0]); station.SetPen(true); Status = "自动循迹：探针贴纸"; break;
                    case Stage.Lower:
                        stage = Stage.Follow; target = owner.PaperToWorld(path[0]); Status = "自动循迹：沿印线移动，不留新线"; break;
                    case Stage.Follow:
                        if (++index >= path.Length) { stage = Stage.Finish; return; }
                        target = owner.PaperToWorld(path[index]); break;
                }
            }
            targetTime += dt;
            if ((actual - lastPosition).sqrMagnitude < .000025f * .000025f) stalled += dt; else stalled = 0;
            lastPosition = actual;
            if (stalled > 3 || targetTime > 15)
            {
                string reason = string.IsNullOrEmpty(station.DrawingBlockReason) ? "目标暂不可达，请调整布局或检查关节限位" : station.DrawingBlockReason;
                Stop("已安全停止：" + reason); return;
            }
            // MoveTool integrates from the requested target, so commanding from actual TCP here
            // would accumulate an overshooting backlog while IK is still catching up.
            Vector3 commanded = station.toolTarget ? station.toolTarget.position : actual;
            Vector3 delta = target - commanded;
            float speed = stage == Stage.Follow ? owner.automaticSpeed : Mathf.Max(owner.automaticSpeed, .04f);
            Vector3 velocity = Vector3.ClampMagnitude(delta / Mathf.Max(.0001f, dt), speed);
            station.MoveTool(velocity / Mathf.Max(.0001f, station.moveSpeed));
        }
    }
}
