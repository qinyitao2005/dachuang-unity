using UnityEngine;

namespace RoboticsLab
{
    /// <summary>Station-specific teaching coordinates and classroom rules. Does not drive motion.</summary>
    public sealed class LabPendantStationProfile
    {
        public const float SpeedMm = 20;
        readonly LabRobotStation station;

        public LabPendantStationProfile(LabRobotStation station) { this.station = station; }
        public string NamePrefix => station.IsTracing ? "TRACE" : station.IsRing ? "RING" : "DRAW";
        public bool RecordPenDown => station.IsDrawing && station.PenDown;
        public Vector3 UserOrigin => station.IsRing ? station.exerciseCenter
            : new Vector3(station.exerciseCenter.x, station.drawingHeight, station.exerciseCenter.z);
        public string CoordinateHint => station.IsRing
            ? "USER：X 左右 / Y 前后 / Z 升降；套圈中心为工具点"
            : "USER：X 左右 / Y 前后 / Z 离纸高度；" + (station.IsTracing ? "固定探针姿态" : "保持原绘图夹具姿态");

        public Vector3 CapturePosition()
        {
            var point = station.transform.InverseTransformPoint(station.ActualToolPosition);
            float tolerance = RecordPenDown ? .0025f : .0002f;
            if (!station.IsRing && Mathf.Abs(point.y - station.drawingHeight) <= tolerance)
                point.y = station.drawingHeight;
            return point;
        }

        public bool ValidateFirstPoint(bool circular, Vector3 point, out string error)
        {
            error = circular ? "首条指令请使用 L，作为后续圆弧的起点" : "";
            if (station.IsTracing && (circular || point.y < station.drawingHeight + .005f))
                error = "课堂要求：首点为 L 安全点，高于纸面至少 5mm";
            return error.Length == 0;
        }

        public bool ValidateTouchPoint(int pointNumber, Vector3 point, out string error)
        {
            error = station.IsTracing && pointNumber == 1 && point.y < station.drawingHeight + .005f
                ? "课堂安全点需高于纸面至少 5mm" : "";
            return error.Length == 0;
        }

        public bool ValidateProgram(LabPendantProgramModel model, out string error)
        {
            error = "";
            if (model.Instructions.Count == 0) error = "先录下 L 起点，再添加轨迹";
            else if (model.HasPendingArc) error = "C 缺终点：到终点后 SHIFT+F5";
            else if (model.Instructions[0].Type != LabPendantInstructionType.Linear)
                error = "程序首行需为 L 起点";
            else if (station.IsTracing)
            {
                var safe = model.FindPoint(1);
                var last = model.Instructions[model.Instructions.Count - 1];
                if (safe == null || safe.LocalPosition.y < station.drawingHeight + .005f)
                    error = "课堂要求：先录高于纸面至少 5mm 的安全点 P[1]";
                else if (model.Instructions.Count < 3 || model.Instructions[0].TargetPoint != 1)
                    error = "课堂要求：程序从 L P[1] 开始";
                else if (last.Type != LabPendantInstructionType.Linear || last.TargetPoint != 1)
                    error = "课堂要求：末尾追加 L P[1] 返回安全点";
            }
            foreach (var row in model.Instructions)
            {
                if (Mathf.Abs(row.SpeedMm - SpeedMm) > .0001f) error = "程序速度不符合实验室固定速度";
                if (station.IsRing && row.PenDown) error = "套圈程序不能含落笔指令";
            }
            return error.Length == 0;
        }
    }
}
