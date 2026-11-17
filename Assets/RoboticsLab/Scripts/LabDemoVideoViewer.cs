using System;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;

namespace RoboticsLab
{
    /// <summary>On-demand demonstrations. Keeps the current workcell/program intact while viewing.</summary>
    public sealed class LabDemoVideoViewer : MonoBehaviour
    {
        public static readonly string[] FileNames = { "TrainingLesson.mp4", "LabDemoStation01.mp4", "LabDemoStation02.mp4", "LabDemoStation03.mp4" };
        public static readonly string[] Titles = { "三工位完整演示", "01  循迹", "02  套圈", "03  画图" };
        public bool Visible => canvasObject && canvasObject.activeSelf;
        public int ClosedFrame { get; private set; } = -1;
        public int SelectedClipIndex { get; private set; }
        public VideoPlayer Playback => video;

        LabTrainingApp app;
        GameObject canvasObject;
        VideoPlayer video;
        RenderTexture videoTexture;
        RawImage picture;
        Text status, playbackLabel;
        Button[] tabs;
        float previousTimeScale = 1;
        bool suspended, wasLessonPlaying;
        string loadError = "";
        readonly Color accent = new Color(.035f, .58f, .64f);
        readonly Color panelColor = new Color(.10f, .18f, .23f);

        public void Open(LabTrainingApp owner, int clipIndex = 0)
        {
            app = owner;
            if (!app) return;
            if (!canvasObject) Build();
            if (!Visible)
            {
                if (app.ActiveTeachingPendant && app.ActiveTeachingPendant.Programming)
                    app.ActiveTeachingPendant.Programming.Pause();
                if (app.trajectoryProgram) app.trajectoryProgram.Pause();
                if (app.pendantController) app.pendantController.PauseProgram();
                wasLessonPlaying = app.lessonVideo && app.lessonVideo.isPlaying;
                if (wasLessonPlaying) app.lessonVideo.Pause();
                previousTimeScale = Time.timeScale;
                Time.timeScale = 0;
                suspended = true;
            }
            canvasObject.SetActive(true);
            if (app.player) { app.player.inputEnabled = false; app.player.UpdatePointerCapture(); }
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            SelectVideo(clipIndex);
        }

        public void Close()
        {
            ClosedFrame = Time.frameCount;
            if (video) video.Stop();
            if (canvasObject) canvasObject.SetActive(false);
            RestoreClock();
            if (wasLessonPlaying && app && app.lessonVideo && app.panel == "lesson") app.lessonVideo.Play();
            wasLessonPlaying = false;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }

        void RestoreClock()
        {
            if (!suspended) return;
            Time.timeScale = previousTimeScale;
            suspended = false;
        }

        public void SelectVideo(int index)
        {
            if (!video) return;
            SelectedClipIndex = Mathf.Clamp(index, 0, FileNames.Length - 1);
            video.Stop();
            loadError = "";
            picture.enabled = false;
            video.url = Path.Combine(Application.streamingAssetsPath, FileNames[SelectedClipIndex]);
            for (int i = 0; i < tabs.Length; i++) tabs[i].GetComponent<Image>().color = i == SelectedClipIndex ? accent : panelColor;
            if (!File.Exists(video.url)) loadError = "找不到演示视频：" + FileNames[SelectedClipIndex];
            else video.Prepare();
            Refresh();
        }

        public void TogglePlayback()
        {
            if (!video || !video.isPrepared) return;
            if (video.isPlaying) video.Pause(); else video.Play();
            Refresh();
        }

        public void Restart()
        {
            if (!video || !video.isPrepared) return;
            video.time = 0;
            video.Play();
        }

        void Update()
        {
            if (!Visible) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Input.GetKeyDown(KeyCode.Space)) TogglePlayback();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Refresh();
        }

        void Refresh()
        {
            if (!status || !video) return;
            bool ready = video.isPrepared;
            status.text = loadError.Length > 0 ? loadError : !ready ? "正在加载演示视频…" :
                (video.isPlaying ? "播放中  " : "已暂停  ") + FormatTime(video.time) + " / " + FormatTime(video.length);
            playbackLabel.text = video.isPlaying ? "暂停  [空格]" : "播放  [空格]";
        }

        static string FormatTime(double seconds)
        {
            int value = Math.Max(0, (int)seconds);
            return (value / 60).ToString("00") + ":" + (value % 60).ToString("00");
        }

        void Build()
        {
            canvasObject = new GameObject("DemonstrationVideoCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var backdrop = new GameObject("ModalInputShield", typeof(RectTransform), typeof(Image));
            var shield = (RectTransform)backdrop.transform;
            shield.SetParent(canvasObject.transform, false);
            shield.anchorMin = Vector2.zero; shield.anchorMax = Vector2.one;
            shield.offsetMin = shield.offsetMax = Vector2.zero;
            backdrop.GetComponent<Image>().color = new Color(.025f, .045f, .065f, .995f);
            backdrop.GetComponent<Image>().raycastTarget = true;
            var root = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(canvasObject.transform, false);
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
            root.sizeDelta = new Vector2(1600, 900);
            Label(root, "Title", "工位演示视频  /  操作 · 编程 · 运行", 42, 24, 1150, 44, 30);
            MakeButton(root, "CloseVideo", "返回实操  [Esc]", 1330, 25, 228, 45, Close);
            tabs = new Button[4];
            for (int i = 0; i < tabs.Length; i++)
            {
                int target = i;
                tabs[i] = MakeButton(root, "VideoTab" + i, Titles[i], 200 + i * 305, 86, 285, 44, () => SelectVideo(target));
            }
            picture = At(root, "DemonstrationVideo", 200, 143, 1200, 675).gameObject.AddComponent<RawImage>();
            picture.raycastTarget = false;
            videoTexture = new RenderTexture(1600, 900, 0, RenderTextureFormat.ARGB32) { name = "OnDemandDemonstration" };
            videoTexture.Create();
            picture.texture = videoTexture;
            video = gameObject.AddComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.waitForFirstFrame = true;
            video.source = VideoSource.Url;
            video.renderMode = VideoRenderMode.RenderTexture;
            video.targetTexture = videoTexture;
            video.aspectRatio = VideoAspectRatio.FitInside;
            video.audioOutputMode = VideoAudioOutputMode.None;
            video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            video.prepareCompleted += Prepared;
            video.errorReceived += Failed;
            video.loopPointReached += Finished;
            var play = MakeButton(root, "ToggleVideoPlayback", "暂停  [空格]", 200, 837, 208, 44, TogglePlayback);
            playbackLabel = play.GetComponentInChildren<Text>();
            MakeButton(root, "RestartVideo", "从头播放", 422, 837, 176, 44, Restart);
            status = Label(root, "VideoStatus", "", 618, 837, 782, 44, 21);
            status.alignment = TextAnchor.MiddleRight;
            canvasObject.SetActive(false);
        }

        void Prepared(VideoPlayer source)
        {
            if (!Visible || source != video) return;
            picture.enabled = true;
            source.Play();
        }
        void Failed(VideoPlayer source, string reason)
        {
            loadError = "视频加载失败，请检查演示文件。";
            Debug.LogWarning("Demo video " + source.url + ": " + reason);
        }
        void Finished(VideoPlayer source) { source.Pause(); Refresh(); }

        RectTransform At(Transform parent, string name, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h);
            return rect;
        }
        Text Label(Transform parent, string name, string value, float x, float y, float w, float h, int size)
        {
            var label = At(parent, name, x, y, w, h).gameObject.AddComponent<Text>();
            label.font = app.uiFont ? app.uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = value; label.fontSize = size; label.color = new Color(.90f, .95f, .97f);
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 17; label.resizeTextMaxSize = size;
            label.raycastTarget = false;
            return label;
        }
        Button MakeButton(Transform parent, string name, string value, float x, float y, float w, float h, UnityEngine.Events.UnityAction action)
        {
            var rect = At(parent, name, x, y, w, h);
            rect.gameObject.AddComponent<Image>().color = panelColor;
            var result = rect.gameObject.AddComponent<Button>();
            result.onClick.AddListener(action);
            var label = Label(rect, "Label", value, 10, 3, w - 20, h - 6, 22);
            label.alignment = TextAnchor.MiddleCenter;
            return result;
        }

        void OnDestroy()
        {
            RestoreClock();
            if (video)
            {
                video.prepareCompleted -= Prepared; video.errorReceived -= Failed; video.loopPointReached -= Finished;
                video.Stop(); Destroy(video);
            }
            if (videoTexture) { videoTexture.Release(); Destroy(videoTexture); }
            if (canvasObject) Destroy(canvasObject);
        }
    }
}
