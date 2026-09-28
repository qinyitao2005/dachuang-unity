using UnityEngine;

namespace RoboticsLab
{
    /// <summary>只负责附件显隐、工具姿态和碰线颜色，不驱动机械臂关节。</summary>
    public sealed class LabRobotToolView
    {
        readonly Group0927Station station;
        readonly Transform originalTrajectoryModule;
        readonly bool originalTrajectoryVisible;
        MaterialPropertyBlock ringColorProperties;

        public LabRobotToolView(Group0927Station station)
        {
            this.station = station;
            if (!station.modelRoot) return;
            foreach (var child in station.modelRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child.name != "轨迹模块") continue;
                originalTrajectoryModule = child;
                originalTrajectoryVisible = child.gameObject.activeSelf;
                break;
            }
        }

        public void ApplyVisibility(bool operating, int mode)
        {
            // 原装夹具保留在模型中，离开工位后恢复其初始显隐状态。
            if (originalTrajectoryModule)
                originalTrajectoryModule.gameObject.SetActive(!operating && originalTrajectoryVisible);
            bool drawing = operating && mode == 0;
            bool ring = operating && mode == 1;
            if (station.drawingBoard) station.drawingBoard.gameObject.SetActive(drawing);
            if (station.wireRoot) station.wireRoot.gameObject.SetActive(ring);
            if (station.ringRoot) station.ringRoot.gameObject.SetActive(ring);
            if (station.penRoot) station.penRoot.gameObject.SetActive(drawing);
            if (station.ringGripTool) station.ringGripTool.gameObject.SetActive(ring);
            if (station.drawingGripTool) station.drawingGripTool.gameObject.SetActive(drawing);
            if (station.originalEndTool) station.originalEndTool.gameObject.SetActive(!ring && (!drawing || !station.drawingGripTool));
        }

        public void UpdatePose(Quaternion fallbackRingFrame)
        {
            if (!station.endEffector) return;
            // 旧场景的独立笔可跟随末端；刚性笔即使丢失 TCP 引用也不能改写安装偏移。
            if (station.penRoot && !station.drawingTcp &&
                (!station.DrawingToolRoot || !station.penRoot.IsChildOf(station.DrawingToolRoot)))
            {
                station.penRoot.position = station.endEffector.position;
                station.penRoot.rotation = Quaternion.identity;
            }
            if (station.ringRoot)
            {
                Vector3 center;
                Quaternion rotation;
                GetActualRingPose(fallbackRingFrame, out center, out rotation);
                station.ringRoot.SetPositionAndRotation(center, rotation);
            }
        }

        /// <summary>夹爪闭合轴为圈面法线，夹持点在圈缘；无夹持 TCP 时使用传入的轨道朝向。</summary>
        public void GetActualRingPose(Quaternion fallbackRingFrame, out Vector3 center, out Quaternion rotation)
        {
            Transform grip = station.ringGripTcp;
            Vector3 position = grip ? grip.position
                : station.endEffector ? station.endEffector.position : station.transform.position;
            if (!grip)
            {
                center = position;
                rotation = fallbackRingFrame;
                return;
            }
            rotation = Quaternion.LookRotation(grip.right, grip.up);
            center = position - grip.up * station.ringRadius;
        }

        public void SetRingContact(bool contact)
        {
            Color color = contact ? new Color(1, .22f, .14f) : new Color(1, .7f, .15f);
            if (station.ringRenderer)
                station.ringRenderer.startColor = station.ringRenderer.endColor = color;
            if (!station.ringSolidRenderer) return;
            if (ringColorProperties == null) ringColorProperties = new MaterialPropertyBlock();
            station.ringSolidRenderer.GetPropertyBlock(ringColorProperties);
            ringColorProperties.SetColor("_Color", color);
            station.ringSolidRenderer.SetPropertyBlock(ringColorProperties);
        }
    }
}
