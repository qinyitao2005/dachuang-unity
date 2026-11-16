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

        bool historyVisible;
        public void ResetView() { historyVisible = false; PaperViewVisible = true; }

        public void Draw(LabTrainingApp app, LabTracingExercise trace, Font font, bool pendantVisible = false)
        {
            Styles(font);
            DrawPaperView(trace, font, pendantVisible);
            Box(new Rect(24, 336, 270, 544), background);
            if (historyVisible) DrawHistory();
            else DrawAssessment(trace);
            if (Button(new Rect(42, 827, 234, 42), "退出工位  [Esc]")) app.ClosePanel();
        }

        void DrawAssessment(LabTracingExercise trace)
        {
            GUI.Label(new Rect(42, 352, 234, 30), "轨迹评分 / Station1", heading);
            bool canSelect = !trace.AttemptActive && !trace.IsAutomatic;
            if (Button(new Rect(42, 391, 234, 35), "J 型", trace.CurrentShape == 0, canSelect)) trace.SelectShape(0);
            for (int i = 1; i < LabTracingExercise.ShapeNames.Length; i++)
            {
                float x = 42 + ((i - 1) % 2) * 122;
                float y = 435 + ((i - 1) / 2) * 41;
                if (Button(new Rect(x, y, 112, 33), LabTracingExercise.ShapeNames[i], trace.CurrentShape == i, canSelect))
                    trace.SelectShape(i);
            }
            bool ready = trace.station && trace.station.IsOperating && !trace.station.EmergencyStopped
                && !trace.AttemptActive && !trace.HasResult && !trace.IsAutomatic;
            if (Button(new Rect(42, 519, 112, 36), "开始评分", true, ready)) trace.BeginAssessment();
            if (Button(new Rect(164, 519, 112, 36), "重置评分")) trace.ResetAttempt();
            if (Button(new Rect(42, 565, 234, 34), "历史得分")) historyVisible = true;
            GUI.Label(new Rect(42, 610, 234, 30), (trace.HasResult ? "本次得分  " : "当前得分  ") + trace.Score.ToString("F1") + " / 100", heading);
            GUI.Label(new Rect(42, 648, 234, 82),
                "覆盖率  " + trace.Coverage.ToString("F1") + "%    精度  " + trace.Accuracy.ToString("F1") + "%\n" +
                "平均偏差  " + trace.MeanErrorMm.ToString("F2") + " mm\n" +
                "当前偏差  " + trace.CurrentErrorMm.ToString("F2") + " mm", text);
            GUI.Label(new Rect(42, 736, 234, 79), trace.Status, text);
        }

        void DrawHistory()
        {
            GUI.Label(new Rect(42, 352, 234, 30), "历史得分 / 本次游戏", heading);
            GUI.Label(new Rect(42, 393, 234, 43), "各图形最高分；重启游戏清空\n重置评分会保留这里的成绩", text);
            for (int i = 0; i < LabTracingExercise.ShapeNames.Length; i++)
            {
                float y = 453 + i * 60;
                int count = LabTracingExercise.History.Attempts(i);
                Box(new Rect(42, y, 234, 50), muted);
                GUI.Label(new Rect(50, y + 4, 112, 25), LabTracingExercise.ShapeNames[i], text);
                GUI.Label(new Rect(161, y + 4, 109, 25), count == 0 ? "暂无成绩" : LabTracingExercise.History.Best(i).ToString("F1") + " 分", text);
                GUI.Label(new Rect(50, y + 27, 210, 22), "已完成评测：" + count + " 次", text);
            }
            if (Button(new Rect(42, 773, 234, 35), "返回评分")) historyVisible = false;
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
