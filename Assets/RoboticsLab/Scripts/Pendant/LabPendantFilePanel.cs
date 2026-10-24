using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>Program files stay inside the imported controller's LCD; this view owns no robot state.</summary>
    public sealed class LabPendantFilePanel : MonoBehaviour
    {
        const float Resolution = 2f;
        const int RowsPerPage = 3;
        public bool IsOpen => surface && surface.gameObject.activeSelf;
        public bool TextEntryFocused => IsOpen && nameInput && nameInput.isFocused;
        RectTransform surface;
        InputField nameInput;
        Text pageLabel, status;
        readonly Button[] rowButtons = new Button[RowsPerPage];
        readonly Text[] rowLabels = new Text[RowsPerPage];
        LabPendantController controller;
        string[] names = new string[0];
        int page;

        public void Initialize(RectTransform screen, Font font, LabPendantController owner)
        {
            if (!screen) throw new ArgumentNullException(nameof(screen));
            controller = owner;
            if (surface) Destroy(surface.gameObject);
            surface = Rect(screen, "ProgramFilesLCD", Vector2.zero, screen.rect.size);
            surface.sizeDelta = screen.rect.size * Resolution;
            surface.localScale = Vector3.one / Resolution;
            var background = surface.gameObject.AddComponent<Image>();
            background.color = new Color(.91f, .94f, .97f, 1); background.raycastTarget = true;
            surface.gameObject.AddComponent<RectMask2D>();

            Label(surface, "Title", "程序文件 / FILE", font, 9, new Vector2(3, -2), new Vector2(180, 13), new Color(.08f, .18f, .28f));
            pageLabel = Label(surface, "Page", "已存程序", font, 7.5f, new Vector2(3, -17), new Vector2(129, 12), new Color(.18f, .27f, .36f));
            Button(surface, "PreviousPage", "‹", font, new Vector2(139, -16), new Vector2(20, 13), () => TurnPage(-1));
            Button(surface, "NextPage", "›", font, new Vector2(163, -16), new Vector2(20, 13), () => TurnPage(1));
            for (int i = 0; i < RowsPerPage; i++)
            {
                int row = i;
                rowButtons[i] = Button(surface, "SavedProgram" + i, "", font, new Vector2(3, -32 - i * 14), new Vector2(180, 13), () => SelectRow(row));
                rowLabels[i] = rowButtons[i].GetComponentInChildren<Text>();
                rowLabels[i].alignment = TextAnchor.MiddleLeft;
            }

            Label(surface, "NameCaption", "名称", font, 8, new Vector2(3, -79), new Vector2(27, 16), new Color(.13f, .21f, .29f));
            var inputRect = Rect(surface, "ProgramName", new Vector2(30, -78), new Vector2(153, 18));
            var inputImage = inputRect.gameObject.AddComponent<Image>(); inputImage.color = Color.white;
            var nameText = Label(inputRect, "Text", "", font, 9, new Vector2(3, -1), new Vector2(147, 16), new Color(.06f, .10f, .16f));
            nameText.supportRichText = false; nameText.alignment = TextAnchor.MiddleLeft;
            nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            var placeholder = Label(inputRect, "Placeholder", "输入程序名", font, 8, new Vector2(3, -1), new Vector2(147, 16), new Color(.44f, .48f, .52f));
            placeholder.alignment = TextAnchor.MiddleLeft;
            nameInput = inputRect.gameObject.AddComponent<InputField>();
            nameInput.targetGraphic = inputImage; nameInput.textComponent = nameText; nameInput.placeholder = placeholder;
            nameInput.characterLimit = 32; nameInput.lineType = InputField.LineType.SingleLine;
            nameInput.contentType = InputField.ContentType.Standard; nameInput.text = "DEMO";
            nameInput.caretColor = new Color(.05f, .25f, .45f); nameInput.customCaretColor = true;
            DisableNavigation(nameInput);

            Button(surface, "SaveProgram", "保存", font, new Vector2(3, -100), new Vector2(43, 18), Save);
            Button(surface, "LoadProgram", "加载", font, new Vector2(49, -100), new Vector2(43, 18), Load);
            Button(surface, "ReturnFirstPoint", "回 P[1]", font, new Vector2(95, -100), new Vector2(43, 18), ReturnFirst);
            Button(surface, "CloseProgramFiles", "关闭", font, new Vector2(141, -100), new Vector2(42, 18), Close);
            status = Label(surface, "FileStatus", "选择已有程序，或输入名称后保存。", font, 7, new Vector2(3, -120), new Vector2(180, 17), new Color(.05f, .25f, .45f));
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            surface.gameObject.SetActive(false);
        }

        public void Open()
        {
            if (!surface) return;
            surface.gameObject.SetActive(true); surface.SetAsLastSibling(); RefreshNames();
            status.text = "选择已有程序，或输入名称后保存。";
            ClearSelection();
        }

        public void Close()
        {
            if (nameInput) nameInput.DeactivateInputField();
            ClearSelection();
            if (surface) surface.gameObject.SetActive(false);
        }

        public string RefreshNames()
        {
            names = controller ? controller.ProgramNames() ?? new string[0] : new string[0];
            int pages = Mathf.Max(1, Mathf.CeilToInt(names.Length / (float)RowsPerPage));
            page = Mathf.Clamp(page, 0, pages - 1);
            if (pageLabel) pageLabel.text = names.Length == 0 ? "暂无已存程序" : "已存 " + names.Length + " 个  " + (page + 1) + "/" + pages;
            for (int i = 0; i < RowsPerPage; i++)
            {
                int index = page * RowsPerPage + i; bool exists = index < names.Length;
                rowButtons[i].interactable = exists;
                rowLabels[i].text = exists ? "  " + Shorten(names[index], 22) : i == 0 && names.Length == 0 ? "  （保存后将在这里显示）" : "";
            }
            return pageLabel ? pageLabel.text : "";
        }

        void TurnPage(int direction)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(names.Length / (float)RowsPerPage));
            page = (page + direction + pages) % pages; RefreshNames(); ClearSelection();
        }

        void SelectRow(int row)
        {
            int index = page * RowsPerPage + row;
            if (index < names.Length) { nameInput.text = names[index]; status.text = "已选择：" + names[index]; }
            ClearSelection();
        }

        void Save()
        {
            if (!controller) return;
            nameInput.DeactivateInputField(); controller.SaveProgram(nameInput.text); RefreshNames();
            int selected = Array.IndexOf(names, nameInput.text.Trim());
            if (selected >= 0) { page = selected / RowsPerPage; RefreshNames(); }
            ShowControllerStatus();
        }
        void Load()
        {
            if (!controller) return;
            nameInput.DeactivateInputField(); controller.LoadProgram(nameInput.text); RefreshNames(); ShowControllerStatus();
        }
        void ReturnFirst()
        {
            if (!controller) return;
            nameInput.DeactivateInputField();
            bool added = controller.ReturnToFirstPoint();
            status.text = added ? "已在末尾添加 L P[1]；按 FWD 执行。" : controller.Model != null ? controller.Model.StatusText : "请先记录首点。";
            ClearSelection();
        }
        void ShowControllerStatus()
        {
            status.text = string.IsNullOrEmpty(controller.LastFileStatus) ? "文件操作已结束。" : controller.LastFileStatus;
            ClearSelection();
        }
        static void ClearSelection() { if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null); }
        static string Shorten(string text, int length) { return text.Length <= length ? text : text.Substring(0, length - 1) + "…"; }
        static void DisableNavigation(Selectable control) { var navigation = control.navigation; navigation.mode = Navigation.Mode.None; control.navigation = navigation; }

        static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var result = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            result.SetParent(parent, false); result.anchorMin = result.anchorMax = result.pivot = new Vector2(0, 1);
            result.anchoredPosition = position * Resolution; result.sizeDelta = size * Resolution; return result;
        }
        static Text Label(Transform parent, string name, string text, Font font, float size, Vector2 position, Vector2 dimensions, Color color)
        {
            var result = Rect(parent, name, position, dimensions).gameObject.AddComponent<Text>();
            result.font = font; result.fontSize = Mathf.RoundToInt(size * Resolution); result.text = text; result.color = color;
            result.raycastTarget = false; result.horizontalOverflow = HorizontalWrapMode.Overflow; result.verticalOverflow = VerticalWrapMode.Truncate;
            return result;
        }
        static Button Button(Transform parent, string name, string caption, Font font, Vector2 position, Vector2 size, Action action)
        {
            var rect = Rect(parent, name, position, size);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.78f, .85f, .91f, 1);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image; DisableNavigation(button);
            var text = Label(rect, "Caption", caption, font, 8, Vector2.zero, size, new Color(.05f, .16f, .26f)); text.alignment = TextAnchor.MiddleCenter;
            button.onClick.AddListener(() => action()); return button;
        }
    }
}
