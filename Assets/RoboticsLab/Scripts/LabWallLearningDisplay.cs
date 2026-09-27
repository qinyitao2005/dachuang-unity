using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>All learning content is rendered on the physical classroom display.</summary>
    public class LabWallLearningDisplay : MonoBehaviour
    {
        public LabTrainingApp app;
        [SerializeField] Canvas wallCanvas;
        [SerializeField] GameObject welcomePage, videoPage, textPage, quizPage, passedPage, toolbar;
        [SerializeField] Text headerStatus, welcomeInfo, videoStatus, questionCounter, questionText, feedbackText, passedInfo;
        [SerializeField] RawImage videoImage;
        [SerializeField] Button beginButton, playbackButton, textButton, quizButton, exitButton, completeTextButton, nextButton, reviewButton;
        [SerializeField] Text playbackLabel;
        [SerializeField] Button[] optionButtons = new Button[4];
        [SerializeField] Text[] optionLabels = new Text[4];
        bool wired;
        float nextRefresh;
        int lastQuestion = -1, attemptedOption = -1;
        static readonly Color Navy = new Color(.025f,.065f,.095f,1);
        static readonly Color Panel = new Color(.055f,.13f,.18f,1);
        static readonly Color Teal = new Color(.035f,.46f,.54f,1);
        static readonly Color Light = new Color(.88f,.95f,.96f,1);
        static readonly Color Muted = new Color(.57f,.73f,.77f,1);

        void Awake() { EnsureBuilt(); }
        void Start() { EnsureBuilt(); RefreshDisplay(); }
        void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh=Time.unscaledTime+.1f;
            RefreshDisplay();
        }

        public void EnsureBuilt()
        {
            if (!app) app=FindObjectOfType<LabTrainingApp>();
            if (!app) return;
            if (!wallCanvas)
            {
                Transform obsolete=transform.Find("WallLearningCanvas");
                if(obsolete)
                {
                    obsolete.gameObject.SetActive(false);
                    if(Application.isPlaying)Destroy(obsolete.gameObject);else DestroyImmediate(obsolete.gameObject);
                }
                CreateCanvas();
                wired=false;
            }
            ConfigureCanvas();
            if (!wired) WireButtons();
            EnsureEventSystem();
            Transform graphics=transform.parent?transform.parent.Find("TeachingDisplayGraphics"):null;
            if(graphics)graphics.gameObject.SetActive(false);
        }

        void ConfigureCanvas()
        {
            wallCanvas.renderMode=RenderMode.WorldSpace;
            wallCanvas.worldCamera=app.player?app.player.viewCamera:null;
            wallCanvas.sortingOrder=0;
            RectTransform rect=(RectTransform)wallCanvas.transform;
            rect.localRotation=Quaternion.identity;
            rect.localPosition=new Vector3(0,0,-.018f);
            rect.sizeDelta=new Vector2(1600,900);
            Vector3 inherited=transform.lossyScale;
            rect.localScale=new Vector3(4.46f/1600f/Mathf.Max(.0001f,Mathf.Abs(inherited.x)),
                2.51f/900f/Mathf.Max(.0001f,Mathf.Abs(inherited.y)),1/Mathf.Max(.0001f,Mathf.Abs(inherited.z)));
            var heading=rect.Find("Heading") as RectTransform;
            if(heading)
            {
                heading.anchoredPosition=new Vector2(52,-9);
                heading.sizeDelta=new Vector2(680,62);
            }
        }

        void CreateCanvas()
        {
            var go=new GameObject("WallLearningCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            go.transform.SetParent(transform,false);
            wallCanvas=go.GetComponent<Canvas>();
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit=1;
            var raycaster=go.GetComponent<GraphicRaycaster>();
            raycaster.ignoreReversedGraphics=true;
            raycaster.blockingObjects=GraphicRaycaster.BlockingObjects.ThreeD;
            RectTransform canvas=(RectTransform)go.transform;
            ImageAt("DisplayBackground",canvas,0,0,1600,900,Navy);
            ImageAt("HeaderAccent",canvas,32,22,6,38,Teal);
            Label("Heading",canvas,"机械臂学习中心",52,17,680,48,34,Light);
            headerStatus=Label("LearningStatus",canvas,"安全准备 · 学习 · 问答 · 实操",875,23,685,34,22,Muted,TextAnchor.MiddleRight);
            ImageAt("HeaderRule",canvas,32,78,1536,2,Panel);

            welcomePage=Page("Welcome",canvas);
            Label("WelcomeEyebrow",welcomePage.transform,"ROBOT LAB  /  入门课程",74,122,1400,40,26,new Color(.21f,.73f,.77f));
            Label("WelcomeTitle",welcomePage.transform,"先学安全，再动手。",74,183,1400,100,64,Color.white);
            welcomeInfo=Label("WelcomeInfo",welcomePage.transform,"请先领取并戴好安全帽，再开始墙面课程。",78,305,1440,58,30,Light);
            string[] titles={"01  看教学","02  答问题","03  去实操"};
            string[] captions={"认识安全要求与机械臂基础","在本屏完成知识检查","通过后选择左侧机械臂工位"};
            for(int i=0;i<3;i++)
            {
                float x=76+i*493;
                ImageAt("LearningStep_"+i,welcomePage.transform,x,415,460,142,Panel);
                Label("Title",welcomePage.transform,titles[i],x+24,432,412,45,31,Color.white);
                Label("Caption",welcomePage.transform,captions[i],x+24,488,412,45,23,Muted);
            }
            beginButton=ButtonAt("BeginLearning",welcomePage.transform,"开始学习",490,628,620,82,Teal,36);
            Label("BeginHint",welcomePage.transform,"靠近大屏按 E，或点击开始；学习内容始终显示在这块屏幕上。",78,765,1440,55,23,Muted,TextAnchor.MiddleCenter);

            videoPage=Page("VideoLesson",canvas);
            ImageAt("VideoWell",videoPage.transform,32,94,1536,684,Color.black);
            var videoRect=Rect("TeachingVideo",videoPage.transform,192,94,1216,684);
            videoImage=videoRect.gameObject.AddComponent<RawImage>();
            videoImage.color=Color.white;videoImage.raycastTarget=false;
            videoStatus=Label("VideoStatus",videoPage.transform,"正在准备教学视频…",40,785,1520,34,22,Muted,TextAnchor.MiddleCenter);

            textPage=Page("TextLesson",canvas);
            Label("TextLessonHeading",textPage.transform,"机械臂基础  /  文字课程",64,109,1472,59,40,Color.white);
            var textBody=Label("TextLessonBody",textPage.transform,
                "1  安全准备\n进入实训室后，先在右侧装备架领取安全帽，完成教学和问答。\n\n"+
                "2  认识机械臂\n机械臂由底座、关节和末端工具组成；移动目标点时，关节协同运动。\n\n"+
                "3  小幅点动\n工位内 WASD 控制末端平移，Q / E 控制升降。先观察响应，再逐步调整。\n\n"+
                "4  绘图与套圈\n绘图先抬笔到起点，再落笔留下轨迹；套圈沿钢丝移动，尽量避免触碰。\n\n"+
                "5  暂停与退出\n空格切换仿真急停，Esc 退出工位。本课程用于本项目的虚拟操作演示。",
                68,191,1464,528,28,Light);
            textBody.resizeTextForBestFit=true;textBody.resizeTextMinSize=22;textBody.resizeTextMaxSize=28;
            completeTextButton=ButtonAt("CompleteTextLesson",textPage.transform,"已阅读，进入问答  →",1020,740,514,70,Teal,30);

            quizPage=Page("KnowledgeQuiz",canvas);
            questionCounter=Label("QuestionCounter",quizPage.transform,"知识检查",64,107,1450,40,25,new Color(.21f,.73f,.77f));
            questionText=Label("Question",quizPage.transform,"",64,153,1472,116,39,Color.white);
            questionText.resizeTextForBestFit=true;questionText.resizeTextMinSize=28;questionText.resizeTextMaxSize=39;
            for(int i=0;i<4;i++)
            {
                optionButtons[i]=ButtonAt("Option_"+i,quizPage.transform,"",64,284+i*97,1472,84,Panel,29);
                optionLabels[i]=optionButtons[i].GetComponentInChildren<Text>();
                optionLabels[i].alignment=TextAnchor.MiddleLeft;
                optionLabels[i].rectTransform.offsetMin=new Vector2(28,5);
                optionLabels[i].rectTransform.offsetMax=new Vector2(-28,-5);
            }
            feedbackText=Label("AnswerFeedback",quizPage.transform,"选择一项答案。",68,685,1450,63,25,Muted);
            feedbackText.resizeTextForBestFit=true;feedbackText.resizeTextMinSize=21;feedbackText.resizeTextMaxSize=25;
            nextButton=ButtonAt("NextQuestion",quizPage.transform,"下一题  →",1118,750,418,66,Teal,30);

            passedPage=Page("QuizPassed",canvas);
            Label("PassedEyebrow",passedPage.transform,"KNOWLEDGE CHECK  /  已完成",76,142,1440,44,26,new Color(.3f,.8f,.6f));
            Label("PassedTitle",passedPage.transform,"问答通过，开始实操。",76,239,1440,100,59,Color.white);
            passedInfo=Label("PassedInfo",passedPage.transform,"左侧三台机械臂工位已开放。",80,387,1430,124,33,Light);
            reviewButton=ButtonAt("ReviewLesson",passedPage.transform,"复习课程",500,616,600,82,Teal,34);
            Label("PracticeHint",passedPage.transform,"走近任一机械臂工位，按 E 选择操作。",80,749,1430,60,27,Muted,TextAnchor.MiddleCenter);

            toolbar=Page("LearningToolbar",canvas);
            ImageAt("ToolbarBackground",toolbar.transform,0,828,1600,72,Panel);
            playbackButton=ButtonAt("Playback",toolbar.transform,"播放 / 暂停",30,838,260,52,Teal,24);
            playbackLabel=playbackButton.GetComponentInChildren<Text>();
            textButton=ButtonAt("OpenText",toolbar.transform,"文字课程",310,838,260,52,new Color(.09f,.23f,.29f),24);
            quizButton=ButtonAt("StartQuiz",toolbar.transform,"开始问答",590,838,260,52,new Color(.09f,.23f,.29f),24);
            Label("ToolbarHint",toolbar.transform,"在屏幕上点击操作",886,842,390,42,22,Muted,TextAnchor.MiddleCenter);
            exitButton=ButtonAt("ExitLearning",toolbar.transform,"退出学习  [Esc]",1290,838,280,52,new Color(.09f,.23f,.29f),24);
        }

        void WireButtons()
        {
            Bind(beginButton,()=>app.OpenLesson());
            Bind(playbackButton,()=>{if(app.panel=="lesson")app.ToggleLessonPlayback();else app.OpenLesson();});
            Bind(textButton,()=>app.OpenTextLesson());
            Bind(quizButton,()=>app.StartQuiz());
            Bind(exitButton,()=>app.ClosePanel());
            Bind(completeTextButton,()=>app.CompleteTextLesson());
            Bind(nextButton,()=>app.NextQuestion());
            Bind(reviewButton,()=>app.OpenLesson());
            for(int i=0;i<optionButtons.Length;i++)
            {
                int option=i;
                Bind(optionButtons[i],()=>{attemptedOption=option;app.SubmitAnswer(option);});
            }
            wired=true;
        }

        void Bind(Button button,UnityAction action)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(()=>{if(!app)return;action();RefreshDisplay();});
        }

        public void RefreshDisplay()
        {
            if(!app || !wallCanvas)EnsureBuilt();
            if(!app || !wallCanvas)return;
            if(wallCanvas.worldCamera!=(app.player?app.player.viewCamera:null))
                wallCanvas.worldCamera=app.player?app.player.viewCamera:null;
            bool learning=app.IsLearningPanel;
            bool quiz=learning&&app.panel=="quiz";
            bool text=learning&&app.panel=="lessonText";
            bool video=learning&&!quiz&&!text;
            welcomePage.SetActive(!learning&&!app.QuizPassed);
            passedPage.SetActive(!learning&&app.QuizPassed);
            videoPage.SetActive(video);textPage.SetActive(text);quizPage.SetActive(quiz);toolbar.SetActive(learning);
            headerStatus.text=app.QuizPassed?"学习完成  ·  实操已开放":app.LessonCompleted?"课程已完成  ·  请完成知识检查":app.HelmetWorn?"安全帽已佩戴  ·  开始学习":"请先领取并戴好安全帽";
            welcomeInfo.text=app.HelmetWorn?"安全准备已完成。观看视频或阅读文字课程，再参加问答。":"请先在入口右侧领取并戴好安全帽，再开始墙面课程。";
            beginButton.interactable=!(app.player&&app.player.IsEquippingHelmet);
            passedInfo.text="已完成 "+app.QuestionCount+" 道知识检查题。\n左侧三台机械臂已开放：轨迹绘制与钢丝套圈。";
            quizButton.interactable=app.LessonCompleted&&!quiz;
            textButton.interactable=!text;
            playbackLabel.text=video?(app.lessonVideo&&app.lessonVideo.isPlaying?"暂停视频":"播放视频"):"返回视频";
            if(app.lessonVideo)
            {
                if(videoImage.texture!=app.lessonVideo.targetTexture)videoImage.texture=app.lessonVideo.targetTexture;
                string time=FormatTime(app.lessonVideo.time)+" / "+FormatTime(app.lessonVideo.length);
                videoStatus.text=app.LessonCompleted?"课程学习完成  ·  点击下方“开始问答”":app.lessonVideo.isPrepared?
                    (app.lessonVideo.isPlaying?"正在播放  ·  ":"已暂停  ·  ")+time:"正在准备教学视频；也可以选择下方“文字课程”。";
            }
            else videoStatus.text="暂未配置教学视频，请选择下方“文字课程”。";
            if(!quiz)return;
            if(lastQuestion!=app.questionIndex){lastQuestion=app.questionIndex;attemptedOption=-1;}
            LabQuestion q=app.CurrentQuestion;
            questionCounter.text="知识检查  /  "+Mathf.Min(app.questionIndex+1,app.QuestionCount)+" / "+app.QuestionCount;
            questionText.text=q!=null?q.question:"正在读取题目…";
            for(int i=0;i<4;i++)
            {
                bool visible=q!=null&&q.options!=null&&i<q.options.Length;
                optionButtons[i].gameObject.SetActive(visible);
                if(!visible)continue;
                optionLabels[i].text=((char)('A'+i))+"   "+q.options[i];
                optionButtons[i].interactable=!app.Answered;
                optionButtons[i].GetComponent<Image>().color=app.Answered&&i==q.correct?new Color(.055f,.43f,.30f):
                    attemptedOption==i&&!string.IsNullOrEmpty(app.Feedback)?new Color(.39f,.19f,.12f):Panel;
            }
            feedbackText.text=string.IsNullOrEmpty(app.Feedback)?"选择一项答案。答错后可以继续尝试。":app.Feedback;
            feedbackText.color=app.Answered?new Color(.4f,.87f,.66f):Light;
            nextButton.interactable=app.Answered;
            nextButton.GetComponentInChildren<Text>().text=app.questionIndex+1>=app.QuestionCount?"完成问答  →":"下一题  →";
        }

        static string FormatTime(double seconds)
        {
            if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<0)return "00:00";
            int value=(int)seconds;return (value/60).ToString("00")+":"+(value%60).ToString("00");
        }

        static void EnsureEventSystem()
        {
            EventSystem events=FindObjectOfType<EventSystem>();
            if(!events)
            {
                var go=new GameObject("LabWallEventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
                events=go.GetComponent<EventSystem>();events.sendNavigationEvents=false;
            }
            else if(!events.GetComponent<BaseInputModule>())events.gameObject.AddComponent<StandaloneInputModule>();
        }

        GameObject Page(string name,Transform parent)
        {
            return Rect(name,parent,0,0,1600,900).gameObject;
        }

        static RectTransform Rect(string name,Transform parent,float x,float y,float width,float height)
        {
            var go=new GameObject(name,typeof(RectTransform));
            var r=(RectTransform)go.transform;r.SetParent(parent,false);
            r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);
            r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(width,height);
            return r;
        }

        static Image ImageAt(string name,Transform parent,float x,float y,float width,float height,Color color)
        {
            var image=Rect(name,parent,x,y,width,height).gameObject.AddComponent<Image>();
            image.color=color;image.raycastTarget=false;return image;
        }

        Text Label(string name,Transform parent,string value,float x,float y,float width,float height,int size,Color color,TextAnchor align=TextAnchor.UpperLeft)
        {
            var text=Rect(name,parent,x,y,width,height).gameObject.AddComponent<Text>();
            text.font=app.uiFont?app.uiFont:Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize=size;text.text=value;text.color=color;text.alignment=align;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;
            text.lineSpacing=1.13f;text.supportRichText=false;text.raycastTarget=false;
            return text;
        }

        Button ButtonAt(string name,Transform parent,string caption,float x,float y,float width,float height,Color background,int fontSize)
        {
            var image=ImageAt(name,parent,x,y,width,height,background);image.raycastTarget=true;
            var button=image.gameObject.AddComponent<Button>();button.targetGraphic=image;
            var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.17f,1.17f,1.17f);
            colors.pressedColor=new Color(.72f,.83f,.86f);colors.disabledColor=new Color(.54f,.57f,.58f,.8f);colors.fadeDuration=.09f;button.colors=colors;
            var navigation=button.navigation;navigation.mode=Navigation.Mode.None;button.navigation=navigation;
            var label=Label("Label",image.transform,caption,12,5,width-24,height-10,fontSize,Color.white,TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin=Vector2.zero;label.rectTransform.anchorMax=Vector2.one;
            label.rectTransform.offsetMin=new Vector2(12,5);label.rectTransform.offsetMax=new Vector2(-12,-5);
            return button;
        }
    }
}
