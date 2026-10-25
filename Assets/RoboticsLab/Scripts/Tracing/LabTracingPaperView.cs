using System;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>
    /// Read-only paper view. Reference paths come from the exercise and the cursor
    /// from the physical probe TCP. Tracing leaves no new ink on either paper view.
    /// It never changes a target, robot transform, renderer, or collision setting.
    /// </summary>
    public sealed class LabTracingPaperView : IDisposable
    {
        static readonly Color Paper = new Color(.97f, .98f, .97f);
        static readonly Color Guide = new Color(.25f, .31f, .34f);
        static readonly Color Selected = new Color(.05f, .32f, .72f);
        static readonly Color Start = new Color(.02f, .59f, .27f);
        static readonly Color End = new Color(.82f, .16f, .16f);
        static readonly Color Tip = new Color(.98f, .43f, .04f);
        static readonly string[] Labels = { "J / 手动", "1 圆形", "2 三角形", "3 正方形", "4 复合曲线" };
        static readonly Vector2[] LabelPositions = {
            new Vector2(-.31f, .455f), new Vector2(.025f, .405f),
            new Vector2(.30f, .405f), new Vector2(.025f, .115f), new Vector2(.27f, -.15f)
        };

        readonly Vector2[][] paths = new Vector2[5][];
        LabTracingExercise cachedExercise;
        Vector2 cachedSize, cachedOffset;
        Texture2D dot;
        GUIStyle label, footer;

        /// <param name="bounds">Coordinates in the existing 1600 by 900 GUI canvas.</param>
        public void Draw(LabTracingExercise trace, Rect bounds, Font font)
        {
            if (!trace || !trace.station || Event.current.type != EventType.Repaint ||
                bounds.width < 120 || bounds.height < 180) return;
            Prepare(trace, font);
            Vector2 size = new Vector2(Mathf.Max(.001f, trace.layoutSize.x), Mathf.Max(.001f, trace.layoutSize.y));
            float scale = Mathf.Min((bounds.width - 16) / size.x, (bounds.height - 60) / size.y);
            Vector2 extent = size * scale;
            Rect paper = new Rect(bounds.x + (bounds.width - extent.x) * .5f, bounds.y + 8, extent.x, extent.y);
            Color previousColor = GUI.color;
            Matrix4x4 previousMatrix = GUI.matrix;
            try
            {
                Fill(paper, Paper);
                for (int shape = 0; shape < paths.Length; shape++)
                {
                    Vector2[] path = paths[shape];
                    Color color = shape == trace.CurrentShape ? Selected : Guide;
                    for (int i = 1; i < path.Length; i++)
                        PaperLine(Project(path[i - 1], trace.layoutOffset, paper, scale),
                            Project(path[i], trace.layoutOffset, paper, scale), color,
                            shape == trace.CurrentShape ? 2.1f : 1.4f, paper);
                }

                for (int shape = 0; shape < paths.Length; shape++)
                {
                    Vector2[] path = paths[shape];
                    Dot(Project(path[0], trace.layoutOffset, paper, scale), 4, Start);
                    if (shape == 0) Dot(Project(path[path.Length - 1], trace.layoutOffset, paper, scale), 4, End);
                    Vector2 position = Project(Vector2.Scale(LabelPositions[shape], size) + trace.layoutOffset,
                        trace.layoutOffset, paper, scale);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(position.x - 51, position.y - 9, 102, 18), Labels[shape], label);
                }

                // ActualToolPosition resolves to Group0927Station.drawingTcp.position.
                Vector2 actual = Project(trace.WorldToPaper(trace.station.ActualToolPosition), trace.layoutOffset, paper, scale);
                Vector2 visible = new Vector2(Mathf.Clamp(actual.x, paper.xMin + 11, paper.xMax - 11),
                    Mathf.Clamp(actual.y, paper.yMin + 11, paper.yMax - 11));
                Dot(visible, 7, Color.white);
                Line(visible - Vector2.right * 10, visible + Vector2.right * 10, Tip, 1.5f);
                Line(visible - Vector2.up * 10, visible + Vector2.up * 10, Tip, 1.5f);
                Dot(visible, 4, Tip);
                bool probeOnPaper = (trace.IsProgrammed || trace.station.PenDown)
                    && Mathf.Abs(trace.station.transform.InverseTransformPoint(trace.station.ActualToolPosition).y - trace.station.drawingHeight) <= trace.contactTolerance;
                if (!probeOnPaper) Dot(visible, 2, Paper);
                if ((actual - visible).sqrMagnitude > 100)
                {
                    Vector2 direction = (actual - visible).normalized;
                    Vector2 normal = new Vector2(-direction.y, direction.x);
                    Vector2 point = visible + direction * 7;
                    Line(point, point - direction * 5 + normal * 4, Tip, 2);
                    Line(point, point - direction * 5 - normal * 4, Tip, 2);
                }
                float legendY = paper.yMax + 10;
                Dot(new Vector2(bounds.x + 13, legendY + 8), 3.5f, Start);
                Text(new Rect(bounds.x + 22, legendY, 50, 18), "起点");
                Dot(new Vector2(bounds.x + 79, legendY + 8), 3.5f, End);
                Text(new Rect(bounds.x + 88, legendY, 65, 18), "J 终点");
                Line(new Vector2(bounds.x + 154, legendY + 8), new Vector2(bounds.x + 170, legendY + 8), Selected, 2.2f);
                Text(new Rect(bounds.x + 176, legendY, 92, 18), "参考线");
                Text(new Rect(bounds.xMax - 100, legendY, 98, 18), "+Z ↑  +X →");
                Vector2 paperPoint = trace.WorldToPaper(trace.station.ActualToolPosition) - trace.layoutOffset;
                bool outside = Mathf.Abs(paperPoint.x) > size.x * .5f || Mathf.Abs(paperPoint.y) > size.y * .5f;
                float contactHeight = trace.station.transform.InverseTransformPoint(trace.station.ActualToolPosition).y;
                bool sampling = (trace.IsProgrammed || trace.station.PenDown) && Mathf.Abs(contactHeight - trace.station.drawingHeight) <= trace.contactTolerance;
                string probeState = sampling ? "触纸采样" : "未采样";
                Dot(new Vector2(bounds.x + 13, legendY + 29), 4, Tip);
                Text(new Rect(bounds.x + 22, legendY + 20, bounds.width - 26, 20),
                    "实际探针：" + probeState + (outside ? " · 在纸面范围外，箭头指示方向" : " · 循迹不留新线"));
            }
            finally
            {
                GUI.color = previousColor;
                GUI.matrix = previousMatrix;
            }
        }

        void Prepare(LabTracingExercise trace, Font font)
        {
            if (cachedExercise != trace || cachedSize != trace.layoutSize || cachedOffset != trace.layoutOffset)
            {
                cachedExercise = trace;
                cachedSize = trace.layoutSize;
                cachedOffset = trace.layoutOffset;
                for (int i = 0; i < paths.Length; i++) paths[i] = trace.GetPath(i);
            }
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11, clipping = TextClipping.Clip };
                footer = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12, clipping = TextClipping.Clip };
                label.normal.textColor = Guide;
                footer.normal.textColor = new Color(.88f, .93f, .95f);
            }
            label.font = footer.font = font;
            if (dot) return;
            dot = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "TracingPaperDot_Runtime", hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                pixels[y * 32 + x] = new Color(1, 1, 1, Mathf.Clamp01(15.5f - Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f))));
            dot.SetPixels(pixels);
            dot.Apply(false, true);
        }

        static Vector2 Project(Vector2 point, Vector2 offset, Rect paper, float scale)
        {
            point -= offset;
            return new Vector2(paper.center.x + point.x * scale, paper.center.y - point.y * scale);
        }

        static void Fill(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }

        static void Line(Vector2 a, Vector2 b, Color color, float width)
        {
            Vector2 delta = b - a;
            if (delta.sqrMagnitude < .0001f) return;
            Matrix4x4 before = GUI.matrix;
            // Work entirely in the caller's absolute canvas. GUIUtility's screen
            // pivot conversion is incorrect inside scaled/nested GUI groups.
            GUI.matrix = before * Matrix4x4.TRS(new Vector3(a.x, a.y, 0),
                Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg), Vector3.one);
            try { Fill(new Rect(0, -width * .5f, delta.magnitude, width), color); }
            finally { GUI.matrix = before; }
        }

        static void PaperLine(Vector2 a, Vector2 b, Color color, float width, Rect paper)
        {
            // Clip before rotating, so guides beyond the physical paper cannot leak
            // onto the 3D scene or controls. Inset also contains the line's width.
            float inset = width * .5f;
            Vector2 delta = b - a;
            float from = 0, to = 1;
            if (!Clip(-delta.x, a.x - paper.xMin - inset, ref from, ref to) ||
                !Clip(delta.x, paper.xMax - inset - a.x, ref from, ref to) ||
                !Clip(-delta.y, a.y - paper.yMin - inset, ref from, ref to) ||
                !Clip(delta.y, paper.yMax - inset - a.y, ref from, ref to)) return;
            Line(a + delta * from, a + delta * to, color, width);
        }

        static bool Clip(float direction, float distance, ref float from, ref float to)
        {
            if (Mathf.Abs(direction) < .00001f) return distance >= 0;
            float value = distance / direction;
            if (direction < 0) from = Mathf.Max(from, value); else to = Mathf.Min(to, value);
            return from <= to;
        }

        void Dot(Vector2 center, float radius, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), dot);
        }

        void Text(Rect rect, string text)
        {
            GUI.color = Color.white;
            GUI.Label(rect, text, footer);
        }

        public void Dispose()
        {
            if (dot)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(dot); else UnityEngine.Object.DestroyImmediate(dot);
            }
            dot = null;
            cachedExercise = null;
            for (int i = 0; i < paths.Length; i++) paths[i] = null;
        }
    }
}
