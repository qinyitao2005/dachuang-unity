using UnityEngine;

namespace RoboticsLab
{
    /// <summary>目标输入：每帧积分世界方向、限制工作空间和落笔高度。</summary>
    internal sealed class LabRobotWorkspace
    {
        readonly Group0927Station station;
        public LabRobotWorkspace(Group0927Station station) { this.station = station; }

        public Vector3 Integrate(Vector3 worldDirection, bool penDown, float deltaTime)
        {
            Vector3 local = station.toolTarget.localPosition
                + station.transform.InverseTransformDirection(worldDirection) * station.moveSpeed * deltaTime;
            Vector3 center = station.exerciseCenter;
            Vector3 half = station.workspaceHalfSize;
            local.x = Mathf.Clamp(local.x, center.x - half.x, center.x + half.x);
            float minimumY = center.y - half.y;
            if (station.TrainingMode == 0) minimumY = Mathf.Max(station.drawingHeight, minimumY);
            local.y = Mathf.Clamp(local.y, minimumY, center.y + half.y);
            local.z = Mathf.Clamp(local.z, center.z - half.z, center.z + half.z);
            if (station.TrainingMode == 0 && penDown) local.y = station.drawingHeight;
            return local;
        }

    }
}
