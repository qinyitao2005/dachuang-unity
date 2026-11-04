using UnityEngine;

namespace RoboticsLab
{
    /// <summary>绘图运动提交：保持工具水平、限制接近速度，碰到实体时恢复整套姿态。</summary>
    internal sealed class LabDrawingMotionController
    {
        readonly Group0927Station station;
        readonly LabRobotJointRig rig;
        readonly LabDrawingPoseIkSolver solver;
        readonly LabDrawingCollision collision;
        public bool Blocked { get; private set; }
        public string Reason { get; private set; } = "";

        public LabDrawingMotionController(Group0927Station station, LabRobotJointRig rig,
            LabDrawingPoseIkSolver solver, LabDrawingCollision collision)
        {
            this.station = station;
            this.rig = rig;
            this.solver = solver;
            this.collision = collision;
        }

        public void Reset()
        {
            ClearInputBlock();
        }

        public bool ValidateReset()
        {
            string reason = "";
            float clearance;
            if (station.drawingTcp && station.DrawingTiltDegrees <= 1f && station.DrawingGripTiltDegrees <= 1f
                && collision.Check(out reason, out clearance))
                return true;
            Blocked = true;
            Reason = !station.drawingTcp ? "缺少工具尖端绑定" : station.DrawingTiltDegrees > 1f || station.DrawingGripTiltDegrees > 1f
                ? (station.stationIndex == 0 ? "复位无法保持探针竖直，请检查工作区和关节限位" : "复位无法保持夹具水平，请检查工作区和关节限位") : "复位被阻挡：" + reason;
            return false;
        }

        public void ClearInputBlock() { Blocked = false; Reason = ""; }

        public void Tick(float deltaTime)
        {
            if (!station.drawingTcp) { Blocked = true; Reason = "缺少工具尖端绑定"; return; }
            Vector3 requested = station.toolTarget.position;
            float travelBudget = station.moveSpeed * Mathf.Max(0, deltaTime);
            Vector3 next = Vector3.MoveTowards(station.drawingTcp.position, requested, travelBudget);
            rig.CaptureCollisionPose();
            // At high frame rates one permitted step can be smaller than the normal
            // 0.4 mm IK tolerance. That step must still move the physical pen.
            solver.Solve(rig, station.drawingTcp, next, Vector3.up, Vector3.up, deltaTime,
                station.DrawingToolRoot, travelBudget * .5f);
            string reason = "";
            float clearance;
            bool horizontal = station.DrawingTiltDegrees <= 1f && station.DrawingGripTiltDegrees <= 1f;
            if (!horizontal || !collision.CheckSweep(rig.CollisionRotations, out reason, out clearance))
            {
                rig.RestoreCollisionPose();
                // 目标可能已落到纸面，但实体笔仍在下降途中；恢复到真实安全位置，
                // 避免反向输入每帧都被积压的旧目标抵消。
                station.toolTarget.position = station.drawingTcp.position;
                Blocked = true;
                Reason = horizontal ? reason : station.stationIndex == 0
                    ? "此位置无法保持探针竖直，请退回调整" : "此位置无法保持夹具水平，请退回调整";
                return;
            }
        }
    }
}
