using UnityEngine;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>Read-only image window. Does not change the simulation clock or station execution.</summary>
    public sealed class LabGamepadGuide : MonoBehaviour
    {
        const string ResourcePath = "RoboticsLabUI/XboxGamepadGuide";
        GameObject canvasObject;
        public bool Visible => canvasObject && canvasObject.activeSelf;
        public int ClosedFrame { get; private set; } = -1;
        public bool PointerBlocked => Visible || ClosedFrame == Time.frameCount;

        public void Open()
        {
            if (!canvasObject)
            {
                var texture = Resources.Load<Texture2D>(ResourcePath);
                if (!texture) { Debug.LogWarning("手柄键位指南图片未找到：" + ResourcePath); return; }
                Build(texture);
            }
            canvasObject.SetActive(true);
        }

        public void Close()
        {
            ClosedFrame = Time.frameCount;
            if (canvasObject) canvasObject.SetActive(false);
        }

        void Build(Texture2D texture)
        {
            canvasObject = new GameObject("GamepadGuideCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32760;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var backdrop = new GameObject("ClickOutsideToClose", typeof(RectTransform), typeof(Image), typeof(Button));
            backdrop.transform.SetParent(canvasObject.transform, false);
            var shield = backdrop.GetComponent<RectTransform>();
            shield.anchorMin = Vector2.zero; shield.anchorMax = Vector2.one;
            shield.offsetMin = shield.offsetMax = Vector2.zero;
            var background = backdrop.GetComponent<Image>();
            background.color = new Color(.02f, .035f, .05f, .65f);
            var dismiss = backdrop.GetComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.navigation = new Navigation { mode = Navigation.Mode.None };
            dismiss.onClick.AddListener(Close);

            // The image is a sibling of the dismiss button, so clicks on it never bubble to Close.
            var bounds = new GameObject("ImageBounds", typeof(RectTransform)).GetComponent<RectTransform>();
            bounds.SetParent(canvasObject.transform, false);
            bounds.anchorMin = new Vector2(.035f, .04f);
            bounds.anchorMax = new Vector2(.965f, .89f);
            bounds.offsetMin = bounds.offsetMax = Vector2.zero;
            var picture = new GameObject("XboxGamepadGuide", typeof(RectTransform), typeof(RawImage),
                typeof(AspectRatioFitter));
            picture.transform.SetParent(bounds, false);
            var image = picture.GetComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = true;
            var aspect = picture.GetComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = texture.width / (float)texture.height;
        }

        void OnDestroy() { if (canvasObject) Destroy(canvasObject); }
    }
}
