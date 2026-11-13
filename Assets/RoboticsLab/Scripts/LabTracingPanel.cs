using UnityEngine;

namespace RoboticsLab
{
    /// <summary>专用循迹界面。图形、自动控制与评分由 LabTracingExercise 管理。</summary>
    public sealed class LabTracingPanel
    {
        GUIStyle text, heading, button;
        Font currentFont;
        readonly LabTracingPaperView paperView = new LabTracingPaperView();
        public bool PaperViewVisible { get; set; } = true;
        readonly Color background = new Color(.045f, .075f, .11f, .96f);
        readonly Color muted = new Color(.15f, .21f, .27f, .98f);
        readonly Color accent = new Color(.04f, .61f, .67f);

        void Styles(Font font)
        {
            if (text != null && currentFont == font) return;
            currentFont = font;
            text = new GUIStyle(GUI.skin.label)
            {
                font = font, fontSize = 16, wordWrap = true,
                normal = { textColor = new Color(.88f, .93f, .95f) }
            };
            heading = new GUIStyle(text) { fontSize = 21, fontStyle = FontStyle.Bold };
            button = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter };
        }

        static void Box(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        bool Button(Rect rect, string label, bool active = false, bool enabled = true)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            Box(rect, active ? accent : muted);
            bool pressed = GUI.Button(rect, label, button);
            GUI.enabled = previous;
            return pressed;
        }

        public void Draw(LabTrainingApp app, LabTracingExercise trace, Font font, bool pendantVisible = false)
        {
            Styles(font);
            DrawPaperView(trace, font, pendantVisible);
            Box(new Rect(24, 336, 270, 544), background);
            GUI.Label(new Rect(42, 352, 234, 30), "循迹训练 / 成功程度", heading);

            bool canSelect = !trace.IsAutomatic && !app.Station1ProgramBusy;
            if (Button(new Rect(42, 391, 234, 35), "J 型 · 手动循迹", trace.CurrentShape == 0, canSelect))
                trace.SelectShape(0);
            for (int i = 1; i < LabTracingExercise.ShapeNames.Length; i++)
            {
                float x = 42 + ((i - 1) % 2) * 122;
                float y = 435 + ((i - 1) / 2) * 41;
                if (Button(new Rect(x, y, 112, 33), LabTracingExercise.ShapeNames[i], trace.CurrentShape == i, canSelect))
                    trace.SelectShape(i);
            }

            bool ready = trace.station && trace.station.IsOperating && !trace.station.EmergencyStopped && !app.Station1PendantVisible;
            if (trace.IsManual)
            {
                if (Button(new Rect(42, 519, 234, 36), trace.AttemptActive ? "重新开始手动 J" : "开始手动 J", true, ready))
                    trace.BeginManual();
            }
            else if (Button(new Rect(42, 519, 234, 36), trace.IsAutomatic ? "停止自动循迹" : "开始自动循迹", true, ready || trace.IsAutomatic))
            {
                if (trace.IsAutomatic) trace.StopAutomatic(); else trace.StartAutomatic();
            }
            if (Button(new Rect(42, 564, 112, 34), "重置成绩 [R]")) app.ResetActiveExercise();
            if (Button(new Rect(164, 564, 112, 34), "结束并评估", false, trace.IsManual && trace.AttemptActive && !app.Station1ProgramBusy))
                trace.FinishAttempt();

            GUI.Label(new Rect(42, 610, 234, 30), "成功程度  " + trace.Score.ToString("F1") + " / 100", heading);
            GUI.Label(new Rect(42, 647, 234, 91),
                "覆盖率  " + trace.Coverage.ToString("F1") + "%    精度  " + trace.Accuracy.ToString("F1") + "%\n" +
                "平均偏差  " + trace.MeanErrorMm.ToString("F2") + " mm\n" +
                "当前偏差  " + trace.CurrentErrorMm.ToString("F2") + " mm", text);
            GUI.Label(new Rect(42, 740, 234, 69), trace.Status, text);
            if (pendantVisible)
            {
                if (Button(new Rect(42, 827, 234, 42), "退出工位  [Esc]")) app.ClosePanel();
            }
            else GUI.Label(new Rect(42, 815, 234, 58), trace.IsManual
                ? "抬起探针移到绿色起点。\n开始后贴纸沿 J 线移动，\n不留新线，按轨迹评分。"
                : "沿印线循迹，不留新线。\n空格急停；R 清空成绩。", text);
        }

        void DrawPaperView(LabTracingExercise trace, Font font, bool pendantVisible)
        {
            Rect panel = pendantVisible ? new Rect(1200, 156, 375, 650) : new Rect(768, 156, 412, 650);
            Rect title = new Rect(panel.x + 16, panel.y + 12, pendantVisible ? 212 : 218, 30);
            Rect paper = new Rect(panel.x + 14, panel.y + 52, panel.width - 26, 580);
            Rect toggle = pendantVisible ? new Rect(1435, 166, 124, 32) : new Rect(1010, 166, 154, 32);
            if (PaperViewVisible)
            {
                Box(panel, background);
                GUI.Label(title, "纸面俯视 / 实际探针", heading);
                paperView.Draw(trace, paper, font);
            }
            string caption = pendantVisible ? (PaperViewVisible ? "收起俯视" : "展开俯视")
                : (PaperViewVisible ? "收起纸面俯视" : "展开纸面俯视");
            if (Button(toggle, caption, PaperViewVisible))
                PaperViewVisible = !PaperViewVisible;
        }

        public void Dispose() { paperView.Dispose(); }
    }
}
