using System;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>
    /// An immutable Cartesian L segment or a genuine three-point C arc, in metres.
    /// This class contains no robot, UI, timing, or scene state.
    /// </summary>
    public sealed class LabPendantMotionPath
    {
        const double MinimumRadius = 0.00001;
        const double MinimumPointSeparation = 0.0000001;
        // Twice the triangle area divided by its longest side squared. Testing all
        // three sides also rejects an almost coincident via point on a large arc.
        const double MinimumTriangleQuality = 0.00001;
        const double FullTurn = Math.PI * 2.0;

        readonly Vector3 start;
        readonly Vector3 via;
        readonly Vector3 end;
        readonly float length;
        readonly float viaDistance;
        readonly bool isArc;
        readonly DoubleVector center;
        readonly DoubleVector axisU;
        readonly DoubleVector axisV;
        readonly double radius;
        readonly double sweep;

        public Vector3 Start { get { return start; } }
        // A line has no intermediate point: Via == Start and ViaDistance == 0.
        public Vector3 Via { get { return via; } }
        public Vector3 End { get { return end; } }
        public float Length { get { return length; } }
        public float ViaDistance { get { return viaDistance; } }
        public bool IsArc { get { return isArc; } }

        LabPendantMotionPath(Vector3 start, Vector3 end, float length)
        {
            this.start = start;
            this.via = start;
            this.end = end;
            this.length = length;
        }

        LabPendantMotionPath(Vector3 start, Vector3 via, Vector3 end,
            double length, double viaDistance, DoubleVector center,
            DoubleVector axisU, DoubleVector axisV, double radius)
        {
            this.start = start;
            this.via = via;
            this.end = end;
            this.length = (float)length;
            this.viaDistance = (float)viaDistance;
            this.center = center;
            this.axisU = axisU;
            this.axisV = axisV;
            this.radius = radius;
            sweep = length / radius;
            isArc = true;
        }

        /// <summary>
        /// Bounds include the analytic extrema of every coordinate over the whole
        /// arc, with float limits rounded outwards. Returns false if a coordinate
        /// cannot be represented by Unity's float-based Vector3.
        /// </summary>
        public bool TryGetBounds(out Vector3 minimum, out Vector3 maximum)
        {
            DoubleVector low = DoubleVector.Min(DoubleVector.From(start), DoubleVector.From(end));
            DoubleVector high = DoubleVector.Max(DoubleVector.From(start), DoubleVector.From(end));
            if (isArc)
            {
                low = DoubleVector.Min(low, DoubleVector.From(via));
                high = DoubleVector.Max(high, DoubleVector.From(via));
                for (int component = 0; component < 3; component++)
                {
                    double first = Math.Atan2(axisV.Component(component), axisU.Component(component));
                    if (first < 0.0) first += FullTurn;
                    for (int opposite = 0; opposite < 2; opposite++)
                    {
                        double angle = first + opposite * Math.PI;
                        if (angle >= FullTurn) angle -= FullTurn;
                        if (angle <= 0.0 || angle >= sweep) continue;
                        DoubleVector p = center + axisU * (radius * Math.Cos(angle))
                            + axisV * (radius * Math.Sin(angle));
                        low = DoubleVector.Min(low, p);
                        high = DoubleVector.Max(high, p);
                    }
                }
            }

            minimum = new Vector3(RoundDown(low.Component(0)), RoundDown(low.Component(1)), RoundDown(low.Component(2)));
            maximum = new Vector3(RoundUp(high.Component(0)), RoundUp(high.Component(1)), RoundUp(high.Component(2)));
            return Finite(minimum) && Finite(maximum);
        }

        /// <summary>
        /// Samples by travelled arc length, with distances outside the segment
        /// clamped to its endpoints. Recorded endpoints and ViaDistance are exact.
        /// </summary>
        public Vector3 EvaluateDistance(float distance)
        {
            if (!Finite(distance))
                throw new ArgumentOutOfRangeException("distance", "路径距离必须是有效数值。");
            if (distance <= 0f) return start;
            if (distance >= length) return end;
            if (!isArc)
            {
                double fraction = (double)distance / length;
                return (DoubleVector.From(start)
                    + (DoubleVector.From(end) - DoubleVector.From(start)) * fraction).ToVector3();
            }

            if (distance == viaDistance) return via;
            double angle = distance / radius;
            return (center + axisU * (radius * Math.Cos(angle))
                + axisV * (radius * Math.Sin(angle))).ToVector3();
        }

        public static bool TryLine(Vector3 start, Vector3 end,
            out LabPendantMotionPath path, out string error)
        {
            path = null;
            error = null;
            if (!Finite(start) || !Finite(end))
            {
                error = "直线点位坐标含有无效数值，请重新示教。";
                return false;
            }

            double distance = (DoubleVector.From(end) - DoubleVector.From(start)).Magnitude;
            if (!Finite(distance) || distance > float.MaxValue)
            {
                error = "直线距离超出支持范围，请检查点位坐标。";
                return false;
            }

            // Recording the current position twice is a valid, zero-duration L.
            path = new LabPendantMotionPath(start, end, (float)distance);
            return true;
        }

        public static bool TryArc(Vector3 start, Vector3 via, Vector3 end,
            out LabPendantMotionPath path, out string error)
        {
            path = null;
            error = null;
            if (!Finite(start) || !Finite(via) || !Finite(end))
            {
                error = "圆弧点位坐标含有无效数值，请重新示教。";
                return false;
            }

            DoubleVector origin = DoubleVector.From(start);
            DoubleVector a = DoubleVector.From(via) - origin;
            DoubleVector b = DoubleVector.From(end) - origin;
            DoubleVector c = b - a;
            double aLength = a.Magnitude;
            double bLength = b.Magnitude;
            double cLength = c.Magnitude;
            if (Math.Min(aLength, Math.Min(bLength, cLength)) <= MinimumPointSeparation)
            {
                error = "圆弧需要三个互不重合的点；整圆请分成两段圆弧。";
                return false;
            }

            DoubleVector normal = DoubleVector.Cross(a, b);
            double normalLength = normal.Magnitude;
            double longestSide = Math.Max(aLength, Math.Max(bLength, cLength));
            double triangleQuality = normalLength / (longestSide * longestSide);
            if (!Finite(triangleQuality) || triangleQuality <= MinimumTriangleQuality)
            {
                error = "圆弧三点共线或过于接近共线，请重新示教中间点。";
                return false;
            }

            // Local offsets avoid cancellation when a small workpiece has a
            // large world-space position. All geometry remains double precision.
            double normalSquared = normalLength * normalLength;
            DoubleVector offset = (DoubleVector.Cross(b, normal) * a.SquaredMagnitude
                + DoubleVector.Cross(normal, a) * b.SquaredMagnitude) / (2.0 * normalSquared);
            double radius = offset.Magnitude;
            if (!Finite(radius) || radius < MinimumRadius)
            {
                error = "圆弧半径过小或计算无效，请增大示教点间距。";
                return false;
            }

            DoubleVector axisU = offset * (-1.0 / radius);
            DoubleVector axisV = DoubleVector.Cross(normal / normalLength, axisU);
            double viaAngle = PositiveAngle(a - offset, axisU, axisV);
            double endAngle = PositiveAngle(b - offset, axisU, axisV);

            // Select the sweep containing the via point, including sweeps longer
            // than 180 degrees and arcs crossing the conventional zero angle.
            if (viaAngle > endAngle)
            {
                axisV = axisV * -1.0;
                viaAngle = PositiveAngle(a - offset, axisU, axisV);
                endAngle = PositiveAngle(b - offset, axisU, axisV);
            }

            double distance = radius * endAngle;
            double intermediateDistance = radius * viaAngle;
            if (!(viaAngle > 0.0 && viaAngle < endAngle && endAngle < FullTurn)
                || !Finite(distance) || distance > float.MaxValue
                || !((float)intermediateDistance > 0f
                    && (float)intermediateDistance < (float)distance))
            {
                error = "圆弧点位无法稳定确定经过中间点的路径，请重新示教。";
                return false;
            }

            var candidate = new LabPendantMotionPath(start, via, end, distance, intermediateDistance,
                origin + offset, axisU, axisV, radius);
            Vector3 minimum;
            Vector3 maximum;
            if (!candidate.TryGetBounds(out minimum, out maximum))
            {
                error = "圆弧中间轨迹坐标超出支持范围，请检查点位。";
                return false;
            }
            path = candidate;
            return true;
        }

        static float RoundDown(double value) { return -RoundUp(-value); }

        static float RoundUp(double value)
        {
            float result = (float)value;
            if (!Finite(result) || (double)result >= value) return result;
            if (result == 0f) return float.Epsilon;
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(result), 0);
            bits += result > 0f ? 1 : -1;
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        static double PositiveAngle(DoubleVector radial, DoubleVector u, DoubleVector v)
        {
            double angle = Math.Atan2(DoubleVector.Dot(radial, v), DoubleVector.Dot(radial, u));
            return angle < 0.0 ? angle + FullTurn : angle;
        }

        static bool Finite(Vector3 value)
        {
            return Finite(value.x) && Finite(value.y) && Finite(value.z);
        }

        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        struct DoubleVector
        {
            readonly double x;
            readonly double y;
            readonly double z;

            DoubleVector(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
            public double SquaredMagnitude { get { return x * x + y * y + z * z; } }
            public double Magnitude { get { return Math.Sqrt(SquaredMagnitude); } }
            public double Component(int index) { return index == 0 ? x : index == 1 ? y : z; }
            public static DoubleVector From(Vector3 v) { return new DoubleVector(v.x, v.y, v.z); }
            public Vector3 ToVector3() { return new Vector3((float)x, (float)y, (float)z); }
            public static DoubleVector operator +(DoubleVector a, DoubleVector b)
            { return new DoubleVector(a.x + b.x, a.y + b.y, a.z + b.z); }
            public static DoubleVector operator -(DoubleVector a, DoubleVector b)
            { return new DoubleVector(a.x - b.x, a.y - b.y, a.z - b.z); }
            public static DoubleVector operator *(DoubleVector a, double scale)
            { return new DoubleVector(a.x * scale, a.y * scale, a.z * scale); }
            public static DoubleVector operator /(DoubleVector a, double scale) { return a * (1.0 / scale); }
            public static double Dot(DoubleVector a, DoubleVector b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
            public static DoubleVector Min(DoubleVector a, DoubleVector b)
            { return new DoubleVector(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z)); }
            public static DoubleVector Max(DoubleVector a, DoubleVector b)
            { return new DoubleVector(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z)); }
            public static DoubleVector Cross(DoubleVector a, DoubleVector b)
            { return new DoubleVector(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        }
    }
}
