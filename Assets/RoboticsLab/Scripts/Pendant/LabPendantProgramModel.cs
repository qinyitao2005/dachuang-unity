using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RoboticsLab
{
    public enum LabPendantInstructionType { Linear, Circular }

    [Serializable]
    public sealed class LabPendantPoint
    {
        [SerializeField] int number;
        [SerializeField] Vector3 localPosition;

        public int Number => number;
        public Vector3 LocalPosition => localPosition;
        internal void SetPosition(Vector3 position) { localPosition = position; }

        internal LabPendantPoint(int number, Vector3 localPosition)
        {
            this.number = number;
            this.localPosition = localPosition;
        }
    }

    [Serializable]
    public sealed class LabPendantInstruction
    {
        [SerializeField] LabPendantInstructionType type;
        [SerializeField] int viaPoint;
        [SerializeField] int targetPoint;
        [SerializeField] float speedMm;
        [SerializeField] bool penDown;

        public LabPendantInstructionType Type => type;
        // A value of zero means that this instruction does not have that point yet.
        public int ViaPoint => viaPoint;
        public int TargetPoint => targetPoint;
        public float SpeedMm => speedMm;
        public bool PenDown => penDown;
        public bool IsComplete => targetPoint > 0 && (type != LabPendantInstructionType.Circular || viaPoint > 0);

        internal LabPendantInstruction(LabPendantInstructionType type, int viaPoint, int targetPoint, float speedMm, bool penDown = false)
        {
            this.type = type;
            this.viaPoint = viaPoint;
            this.targetPoint = targetPoint;
            this.speedMm = speedMm;
            this.penDown = penDown;
        }

        internal void SetTargetPoint(int number) { targetPoint = number; }
    }

    /// <summary>
    /// Records pendant program data only. It does not move a robot or evaluate program text.
    /// Point numbers remain stable when a line is removed; only Clear restarts numbering.
    /// </summary>
    [Serializable]
    public sealed class LabPendantProgramModel
    {
        public const int MaximumInstructions = 256;
        public const int MaximumPoints = 512;

        [SerializeField] List<LabPendantInstruction> instructions = new List<LabPendantInstruction>();
        [SerializeField] List<LabPendantPoint> points = new List<LabPendantPoint>();
        [SerializeField] int nextPointNumber = 1;
        [NonSerialized] int revision;
        [SerializeField] string statusText = "移动机械臂后，按 L 记录直线终点；按 C 依次记录圆弧中间点和终点。";

        // Includes status changes so the screen can also display a rejected recording's reason.
        [field: NonSerialized] public event Action Changed;

        public IReadOnlyList<LabPendantInstruction> Instructions => instructions;
        public IReadOnlyList<LabPendantPoint> Points => points;
        public bool HasPendingArc => instructions.Count > 0
            && instructions[instructions.Count - 1].Type == LabPendantInstructionType.Circular
            && !instructions[instructions.Count - 1].IsComplete;
        public string StatusText => statusText;
        public int NextPointNumber => nextPointNumber;
        public int Revision => revision;

        public bool AddLinear(Vector3 localPosition, float speedMm = 100f, bool penDown = false)
        {
            if (HasPendingArc) return Reject("请先记录当前圆弧的终点，或撤销当前圆弧，再添加直线。");
            if (!ValidInput(localPosition, speedMm)) return false;
            if (instructions.Count >= MaximumInstructions) return Reject("程序最多允许 256 行。");
            if (points.Count >= MaximumPoints) return Reject("程序最多允许 512 个点。");

            int target = AppendPoint(localPosition);
            instructions.Add(new LabPendantInstruction(LabPendantInstructionType.Linear, 0, target, speedMm, penDown));
            revision++;
            Notify("已添加直线：P[" + target + "]。");
            return true;
        }

        public bool AddLinearReference(int pointNumber, float speedMm = 100f, bool penDown = false)
        {
            if (HasPendingArc) return Reject("请先完成或撤销当前圆弧。");
            var point = FindPoint(pointNumber);
            if (point == null) return Reject("引用的点位不存在。");
            if (!ValidInput(point.LocalPosition, speedMm)) return false;
            if (instructions.Count >= MaximumInstructions) return Reject("程序最多允许 256 行。");
            instructions.Add(new LabPendantInstruction(LabPendantInstructionType.Linear, 0, pointNumber, speedMm, penDown));
            revision++;
            Notify("已添加返回点位：L P[" + pointNumber + "]。");
            return true;
        }

        public bool RecordCircularPoint(Vector3 localPosition, float speedMm = 100f, bool penDown = false)
        {
            if (!ValidInput(localPosition, speedMm)) return false;
            if (points.Count >= MaximumPoints) return Reject("程序最多允许 512 个点。");

            if (!HasPendingArc)
            {
                if (instructions.Count >= MaximumInstructions) return Reject("程序最多允许 256 行。");
                if (points.Count + 2 > MaximumPoints) return Reject("圆弧需要中间点和终点，剩余点位不足。");
                int via = AppendPoint(localPosition);
                instructions.Add(new LabPendantInstruction(LabPendantInstructionType.Circular, via, 0, speedMm, penDown));
                revision++;
                Notify("已记录圆弧中间点 P[" + via + "]；移动到终点后再按 C。");
                return true;
            }

            var arc = instructions[instructions.Count - 1];
            var midpoint = FindPoint(arc.ViaPoint);
            if (midpoint == null) return Reject("当前圆弧的中间点不存在，请撤销后重新记录。");
            if ((midpoint.LocalPosition - localPosition).sqrMagnitude <= 0.000000000001f)
                return Reject("圆弧终点不能与中间点重合，请移动机械臂后再按 C。");

            int target = AppendPoint(localPosition);
            arc.SetTargetPoint(target);
            revision++;
            Notify("已完成圆弧：中间点 P[" + arc.ViaPoint + "]，终点 P[" + target + "]。");
            return true;
        }

        public bool RemoveLast()
        {
            if (instructions.Count == 0) return Reject("没有可以撤销的程序行。");
            instructions.RemoveAt(instructions.Count - 1);
            points.RemoveAll(point => !instructions.Exists(row => row.ViaPoint == point.Number || row.TargetPoint == point.Number));
            revision++;
            Notify("已撤销最后一行，已有点号保持不变。");
            return true;
        }

        public void Clear()
        {
            instructions.Clear();
            points.Clear();
            nextPointNumber = 1;
            revision++;
            Notify("程序已清空，点号从 P[1] 重新开始。");
        }

        public LabPendantPoint FindPoint(int number)
        {
            for (int i = 0; i < points.Count; i++)
                if (points[i].Number == number) return points[i];
            return null;
        }

        public bool TouchUpPoint(int number, Vector3 position)
        {
            var point = FindPoint(number);
            if (point == null || !ValidPosition(position)) return Reject("点位不存在或坐标无效。");
            point.SetPosition(position);
            revision++;
            Notify("已更新 P[" + number + "] 的实际位置。");
            return true;
        }

        public bool SetTargetReference(int row, int number)
        {
            if (row < 0 || row >= instructions.Count || FindPoint(number) == null) return Reject("程序行或引用点位不存在。");
            if (instructions[row].Type == LabPendantInstructionType.Circular && instructions[row].ViaPoint == number)
                return Reject("圆弧终点不能与经过点相同。");
            instructions[row].SetTargetPoint(number);
            points.RemoveAll(point => !instructions.Exists(item => item.ViaPoint == point.Number || item.TargetPoint == point.Number));
            revision++;
            Notify("目标点已改为 P[" + number + "]。");
            return true;
        }

        public bool SetViaReference(int row, int number)
        {
            if (row < 0 || row >= instructions.Count || instructions[row].Type != LabPendantInstructionType.Circular
                || FindPoint(number) == null || instructions[row].TargetPoint == number)
                return Reject("圆弧经过点引用无效。");
            var old = instructions[row];
            instructions[row] = new LabPendantInstruction(old.Type, number, old.TargetPoint, old.SpeedMm, old.PenDown);
            revision++; Notify("已修改圆弧经过点。"); return true;
        }

        public bool ChangeMotionType(int row, LabPendantInstructionType type)
        {
            if (row < 0 || row >= instructions.Count || HasPendingArc) return Reject("先补齐当前圆弧，再修改运动类型。");
            var old = instructions[row];
            if (old.Type == type) return true;
            if (type == LabPendantInstructionType.Circular)
            {
                // Pending arcs are always at the end, as required by the recording/file model.
                if (row != instructions.Count - 1 || row == 0) return Reject("本实验仅支持将末行 L 改为 C；首行保留安全点。");
                instructions[row] = new LabPendantInstruction(type, old.TargetPoint, 0, old.SpeedMm, old.PenDown);
                revision++; Notify("原终点改为 C 经过点；请补录圆弧终点。");
            }
            else
            {
                if (instructions.Count >= MaximumInstructions) return Reject("程序行数已满，无法拆分圆弧。");
                instructions[row] = new LabPendantInstruction(type, 0, old.ViaPoint, old.SpeedMm, old.PenDown);
                instructions.Insert(row + 1, new LabPendantInstruction(type, 0, old.TargetPoint, old.SpeedMm, old.PenDown));
                revision++; Notify("C 已拆为经过点、终点两条 L。");
            }
            return true;
        }

        public string[] GetRows()
        {
            var rows = new string[instructions.Count];
            for (int i = 0; i < rows.Length; i++) rows[i] = FormatRow(i);
            return rows;
        }

        public string ToProgramText() { return string.Join("\n", GetRows()); }

        public string ToJson() { return JsonUtility.ToJson(this, true); }

        public static bool TryFromJson(string json, out LabPendantProgramModel model, out string error)
        {
            model = null;
            error = "程序文件内容无效。";
            if (string.IsNullOrWhiteSpace(json) || json.Length > 524288) return false;
            LabPendantProgramModel candidate;
            try { candidate = JsonUtility.FromJson<LabPendantProgramModel>(json); }
            catch (Exception ex) { error = "程序格式错误：" + ex.Message; return false; }
            if (candidate == null || candidate.instructions == null || candidate.points == null
                || candidate.instructions.Count > MaximumInstructions || candidate.points.Count > MaximumPoints)
                return false;
            var ids = new HashSet<int>();
            int maximum = 0;
            foreach (var point in candidate.points)
            {
                if (point == null || point.Number <= 0 || point.Number >= int.MaxValue - MaximumPoints
                    || !ids.Add(point.Number) || !ValidPosition(point.LocalPosition)) return false;
                maximum = Math.Max(maximum, point.Number);
            }
            if (candidate.nextPointNumber <= maximum || candidate.nextPointNumber < 1
                || candidate.nextPointNumber >= int.MaxValue - MaximumPoints) return false;
            for (int i = 0; i < candidate.instructions.Count; i++)
            {
                var row = candidate.instructions[i];
                if (row == null || !Enum.IsDefined(typeof(LabPendantInstructionType), row.Type)
                    || !Finite(row.SpeedMm) || row.SpeedMm < 1 || row.SpeedMm > 1000) return false;
                if (row.Type == LabPendantInstructionType.Linear)
                {
                    if (row.ViaPoint != 0 || !ids.Contains(row.TargetPoint)) return false;
                }
                else if (!ids.Contains(row.ViaPoint) || row.TargetPoint < 0
                    || row.TargetPoint == row.ViaPoint
                    || (row.TargetPoint == 0 ? i != candidate.instructions.Count - 1 : !ids.Contains(row.TargetPoint)))
                    return false;
            }
            candidate.revision = 0;
            candidate.statusText = "程序已加载。";
            model = candidate;
            error = "";
            return true;
        }

        string FormatRow(int index)
        {
            var instruction = instructions[index];
            string number = (index + 1).ToString(CultureInfo.InvariantCulture);
            string speed = instruction.SpeedMm.ToString("0.###", CultureInfo.InvariantCulture);
            string target = instruction.TargetPoint > 0
                ? instruction.TargetPoint.ToString(CultureInfo.InvariantCulture) : "?";
            if (instruction.Type == LabPendantInstructionType.Circular)
                return number + ":C P[" + instruction.ViaPoint.ToString(CultureInfo.InvariantCulture)
                    + "] P[" + target + "] " + speed + "mm/sec FINE";
            return number + ":L P[" + target + "] " + speed + "mm/sec FINE";
        }

        int AppendPoint(Vector3 localPosition)
        {
            int number = nextPointNumber++;
            points.Add(new LabPendantPoint(number, localPosition));
            return number;
        }

        bool ValidInput(Vector3 localPosition, float speedMm)
        {
            if (!ValidPosition(localPosition))
                return Reject("当前点位坐标无效，无法记录。");
            if (!Finite(speedMm) || speedMm < 1 || speedMm > 1000)
                return Reject("编程速度范围为 1 到 1000 毫米/秒。");
            if (nextPointNumber >= int.MaxValue - MaximumPoints)
                return Reject("点号已达到上限，请新建程序。");
            return true;
        }

        static bool ValidPosition(Vector3 p) { return Finite(p.x) && Finite(p.y) && Finite(p.z); }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        bool Reject(string reason) { Notify(reason); return false; }
        void Notify(string message) { statusText = message; Changed?.Invoke(); }
    }
}
