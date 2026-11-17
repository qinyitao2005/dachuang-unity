using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>Reveals a quiz hint only while the pointer is over its cover.</summary>
    public sealed class LabQuizAnswerReveal : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] Text label;
        [SerializeField] Image background;
        string answer = "";
        int questionIndex = -1;
        bool hovered;
        public bool IsRevealed => hovered && !string.IsNullOrEmpty(answer);

        public void Configure(Text answerLabel, Image answerBackground)
        {
            label = answerLabel;
            background = answerBackground;
            Refresh();
        }

        public void SetQuestion(int index, LabQuestion question)
        {
            string nextAnswer = question != null && question.options != null &&
                question.correct >= 0 && question.correct < question.options.Length
                ? "正确答案：" + (char)('A' + question.correct) + "\n" + question.options[question.correct] : "";
            if (questionIndex == index && answer == nextAnswer) return;
            questionIndex = index;
            answer = nextAnswer;
            hovered = false;
            Refresh();
        }

        public void OnPointerEnter(PointerEventData eventData) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData eventData) { hovered = false; Refresh(); }
        void OnDisable() { hovered = false; Refresh(); }

        void Refresh()
        {
            if (label) label.text = IsRevealed ? answer : "正确答案已遮盖\n将鼠标移到这里查看";
            if (background) background.color = IsRevealed
                ? new Color(.035f, .30f, .35f, 1) : new Color(.09f, .18f, .23f, 1);
        }
    }
}
