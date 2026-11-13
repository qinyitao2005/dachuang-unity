using UnityEngine;

namespace RoboticsLab
{
    /// <summary>Read-only top view of Station03's actual paper strokes and physical pen tip.</summary>
    public sealed class LabDrawingPaperView
    {
        static readonly Color Panel = new Color(.045f, .075f, .11f, .96f);
        static readonly Color Paper = new Color(.97f, .98f, .97f);
        static readonly Color Grid = new Color(.83f, .87f, .89f);
        static readonly Color Ink = new Color(.02f, .24f, .7f);
        static readonly Color Tip = new Color(.98f, .43f, .04f);
        GUIStyle title, legend;

        public void Draw(LabRobotStation station, Font font)
        {
            if (!station || !station.IsDrawing) return;
            PrepareStyles(font);
            Rect panel = new Rect(1200, 156, 375, 650);
            Rect area = new Rect(1218, 212, 339, 514);
            Box(panel, Panel);
            GUI.Label(new Rect(1218, 172, 300, 32), "纸面俯视 / 实际笔迹", title);

            Vector3 center = station.exerciseCenter;
            Vector3 half = station.workspaceHalfSize;
            float scale = Mathf.Min((area.width - 18) / (2 * half.x), (area.height - 18) / (2 * half.z));
            Rect paper = new Rect(area.center.x - half.x * scale, area.center.y - half.z * scale,
                2 * half.x * scale, 2 * half.z * scale);
            Box(paper, Paper);

            if (Event.current.type == EventType.Repaint)
            {
                for (int step = -20; step <= 20; step++)
                {
                    float offset = step * .05f;
                    if (Mathf.Abs(offset) <= half.x)
                        Line(Project(new Vector3(center.x + offset, 0, center.z - half.z), center, paper, scale),
                            Project(new Vector3(center.x + offset, 0, center.z + half.z), center, paper, scale), Grid, 1);
                    if (Mathf.Abs(offset) <= half.z)
                        Line(Project(new Vector3(center.x - half.x, 0, center.z + offset), center, paper, scale),
                            Project(new Vector3(center.x + half.x, 0, center.z + offset), center, paper, scale), Grid, 1);
                }

                var strokes = station.DrawingStrokes;
                if (strokes != null)
                    foreach (var stroke in strokes)
                    {
                        if (!stroke || !stroke.gameObject.activeInHierarchy) continue;
                        for (int i = 1; i < stroke.positionCount; i++)
                        {
                            Vector2 a = Project(stroke.GetPosition(i - 1), center, paper, scale);
                            Vector2 b = Project(stroke.GetPosition(i), center, paper, scale);
                            PaperLine(a, b, paper, Ink, 2.6f);
                        }
                    }

                Vector3 actual = station.transform.InverseTransformPoint(station.ActualToolPosition);
                Vector2 tip = Project(actual, center, paper, scale);
                tip.x = Mathf.Clamp(tip.x, paper.xMin + 9, paper.xMax - 9);
                tip.y = Mathf.Clamp(tip.y, paper.yMin + 9, paper.yMax - 9);
                Box(new Rect(tip.x - 4, tip.y - 4, 8, 8), Tip);
                Line(tip - Vector2.right * 10, tip + Vector2.right * 10, Tip, 1.5f);
                Line(tip - Vector2.up * 10, tip + Vector2.up * 10, Tip, 1.5f);
                Box(new Rect(1221, 749, 12, 4), Ink);
                Box(new Rect(1392, 746, 8, 8), Tip);
            }

            GUI.Label(new Rect(1238, 737, 150, 27), "纸上笔迹", legend);
            GUI.Label(new Rect(1406, 737, 142, 27), "实际笔尖", legend);
            float gapMm = (station.transform.InverseTransformPoint(station.ActualToolPosition).y - station.drawingHeight) * 1000f;
            GUI.Label(new Rect(1219, 768, 337, 26),
                "笔尖距纸 " + gapMm.ToString("F1") + " mm    +Z ↑  +X →", legend);
        }

        void PrepareStyles(Font font)
        {
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold };
                title.normal.textColor = new Color(.88f, .93f, .95f);
                legend = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                legend.normal.textColor = new Color(.88f, .93f, .95f);
            }
            title.font = legend.font = font;
        }

        static Vector2 Project(Vector3 local, Vector3 center, Rect paper, float scale)
        {
            return new Vector2(paper.center.x + (local.x - center.x) * scale,
                paper.center.y - (local.z - center.z) * scale);
        }

        static void Box(Rect bounds, Color color)
        {
            Color before = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(bounds, Texture2D.whiteTexture);
            GUI.color = before;
        }

        static void Line(Vector2 a, Vector2 b, Color color, float width)
        {
            Vector2 delta = b - a;
            if (delta.sqrMagnitude < .0001f) return;
            Matrix4x4 before = GUI.matrix;
            GUI.matrix = before * Matrix4x4.TRS(new Vector3(a.x, a.y, 0),
                Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg), Vector3.one);
            try { Box(new Rect(0, -width * .5f, delta.magnitude, width), color); }
            finally { GUI.matrix = before; }
        }

        static void PaperLine(Vector2 a, Vector2 b, Rect paper, Color color, float width)
        {
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
    }
}
