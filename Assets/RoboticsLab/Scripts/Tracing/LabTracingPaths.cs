using System.Collections.Generic;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>Paper geometry only. Coordinates are metres in the station's local X/Z plane.</summary>
    public static class LabTracingPaths
    {
        public static readonly string[] Names = { "J 型（手动）", "圆形", "三角形", "正方形", "复合曲线" };

        public static Vector2[] Create(int shape, Vector2 size, Vector2 offset, float spacing = .001f)
        {
            var points = new List<Vector2>();
            if (shape == 0)
            {
                points.Add(new Vector2(-.43f, .39f));
                points.Add(new Vector2(-.14f, .39f));
                points.Add(new Vector2(-.14f, -.20f));
                for (int i = 1; i <= 40; i++)
                {
                    float a = -Mathf.PI * i / 40f;
                    points.Add(new Vector2(-.285f + .145f * Mathf.Cos(a), -.20f + .15f * Mathf.Sin(a)));
                }
                points.Add(new Vector2(-.43f, -.11f));
            }
            else if (shape == 1)
            {
                for (int i = 0; i <= 64; i++)
                {
                    float a = Mathf.PI * .5f - 2 * Mathf.PI * i / 64f;
                    // Correct for unequal paper extents, keeping this a physical circle.
                    float radius = Mathf.Min(size.x * .145f, size.y * .105f);
                    points.Add(new Vector2(.04f + Mathf.Cos(a) * radius / size.x,
                        .255f + Mathf.Sin(a) * radius / size.y));
                }
            }
            else if (shape == 2)
            {
                points.AddRange(new[] { new Vector2(.29f, .355f), new Vector2(.44f, .14f),
                    new Vector2(.15f, .14f), new Vector2(.29f, .355f) });
            }
            else if (shape == 3)
            {
                float side = Mathf.Min(size.x * .29f, size.y * .21f);
                float x = side * .5f / size.x, y = side * .5f / size.y;
                Vector2 c = new Vector2(.04f, -.025f);
                points.AddRange(new[] { c + new Vector2(-x, y), c + new Vector2(x, y),
                    c + new Vector2(x, -y), c + new Vector2(-x, -y), c + new Vector2(-x, y) });
            }
            else
            {
                // Photo: left rising arc, inward concave trough, rising diagonal, upright right edge, flat base.
                Vector2 lo = new Vector2(-.09f, -.37f), top = new Vector2(.14f, -.205f);
                points.Add(lo);
                Bezier(points, lo, new Vector2(-.04f, -.265f), new Vector2(.045f, -.215f), top, 24);
                Bezier(points, top, new Vector2(.115f, -.305f), new Vector2(.22f, -.345f), new Vector2(.28f, -.31f), 24);
                points.Add(new Vector2(.45f, -.21f));
                points.Add(new Vector2(.45f, -.37f));
                points.Add(lo);
            }
            for (int i = 0; i < points.Count; i++) points[i] = Vector2.Scale(points[i], size) + offset;
            return Resample(points, Mathf.Max(.0002f, spacing));
        }

        static void Bezier(List<Vector2> points, Vector2 a, Vector2 b, Vector2 c, Vector2 d, int count)
        {
            for (int i = 1; i <= count; i++)
            {
                float t = i / (float)count, u = 1 - t;
                points.Add(u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d);
            }
        }

        public static Vector2[] Resample(IList<Vector2> source, float spacing)
        {
            var result = new List<Vector2>();
            if (source == null || source.Count == 0) return result.ToArray();
            result.Add(source[0]);
            for (int i = 1; i < source.Count; i++)
            {
                int count = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(source[i - 1], source[i]) / spacing));
                for (int j = 1; j <= count; j++) result.Add(Vector2.Lerp(source[i - 1], source[i], j / (float)count));
            }
            return result.ToArray();
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            float t = d.sqrMagnitude < 1e-12f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude);
            return Vector2.Distance(p, a + t * d);
        }
    }
}
