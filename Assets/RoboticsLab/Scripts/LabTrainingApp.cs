using System;

using System.IO;

using UnityEngine;

using UnityEngine.Video;



namespace RoboticsLab

{

    [Serializable] public class LabQuestion { public string question; public string[] options; public int correct; public string explanation; }

    [Serializable] public class LabQuestionBank { public LabQuestion[] questions; }

    public class LabTrainingApp : MonoBehaviour

    {

        public LabThirdPerson player;

        public LabRobotStation[] stations;

        public Transform helmetPickup, teachingPoint;

        public VideoPlayer lessonVideo;

        public TextAsset questionJson;

        public Font uiFont;

        public Material videoScreenMaterial;

        public LabWallLearningDisplay wallLearningDisplay;

        public LabTrajectoryProgram trajectoryProgram;

        public LabTrajectoryPanel trajectoryPanel;

        public LabPendantController pendantController;

        public LabStation1PendantView station1Pendant;

        Transform learningScreen;

        public LabQuestion CurrentQuestion => bank!=null&&bank.questions!=null&&bank.questions.Length>0?bank.questions[Mathf.Clamp(questionIndex,0,bank.questions.Length-1)]:null;

        public int QuestionCount => bank!=null&&bank.questions!=null?bank.questions.Length:0;

        public bool Answered => answered;

        public string Feedback => feedback;

        public bool IsLearningPanel => panel=="lesson"||panel=="lessonText"||panel=="quiz";

        bool IsLearningView => IsLearningPanel||(panel=="help"&&(helpReturnPanel=="lesson"||helpReturnPanel=="lessonText"||helpReturnPanel=="quiz"));

        public Transform NearestHelmet => player?LabHelmetRackSelection.FindNearest(player.transform):null;

        public bool HelmetWorn, LessonCompleted, QuizPassed;

        public int CorrectAnswers;

        public LabRobotStation ActiveStation;

        [Header("保留的 Part1 教学资源（专用循迹工位不启用）")]
        public Part1启动桥 part1启动桥;

        [Tooltip("兼容保留的 Part1 资源；当前三工位流程统一由专用工位控制器操作。")]
        public bool Part1接管中 = false;

        public string panel = "";

        public string notification = "欢迎进入机械臂实训室。先到入口右侧领取安全帽。";

        public int questionIndex;

        LabQuestionBank bank;

        Font font;

        GUIStyle body, small, title, heading, button, tiny;

        Texture2D pixel;

        float noticeUntil=12, elapsed;

        string feedback="";

        bool answered;

        string helpReturnPanel="";

        Vector3 heldDirection;

        public int LastMode;

        readonly LabTracingPanel tracingPanel = new LabTracingPanel();

        public LabTracingExercise ActiveTracing => ActiveStation && ActiveStation.IsTracing ? ActiveStation.GetComponent<LabTracingExercise>() : null;
        public bool TracingPaperVisible => ActiveTracing && tracingPanel.PaperViewVisible;

        public string StateSummary => "helmet="+HelmetWorn+";lesson="+LessonCompleted+";quiz="+QuizPassed+";panel="+panel+";station="+(ActiveStation?ActiveStation.stationIndex:-1);

        public bool PendantProgrammingVisible => !Part1接管中&&pendantController&&pendantController.Visible&&panel=="operate"&&ActiveStation&&!ActiveStation.IsTracing;
        public bool LegacyProgrammingVisible => !Part1接管中&&trajectoryPanel&&trajectoryPanel.Visible&&panel=="operate"&&ActiveStation&&!ActiveStation.IsTracing;
        public bool Station1PendantVisible => station1Pendant&&station1Pendant.Visible&&panel=="operate"&&ActiveStation&&ActiveStation.IsTracing;
        public bool Station1ProgramBusy => station1Pendant&&station1Pendant.Programming&&station1Pendant.Programming.IsBusy;
        public bool ProgrammingVisible => Station1PendantVisible||PendantProgrammingVisible||LegacyProgrammingVisible;

        public void ToggleStation1Pendant()
        {
            if(!ActiveStation||!ActiveStation.IsTracing||panel!="operate")return;
            if(Station1PendantVisible){station1Pendant.Close();return;}
            EnsureProgramming();
            if(!station1Pendant)station1Pendant=GetComponent<LabStation1PendantView>();
            if(!station1Pendant)station1Pendant=gameObject.AddComponent<LabStation1PendantView>();
            station1Pendant.Open(this,pendantController.pendantPrefab);
        }

        void Awake()

        {

            Application.targetFrameRate=60;

            string externalQuestions=Path.Combine(Application.streamingAssetsPath,"Questions.json");

            try

            {

                if(File.Exists(externalQuestions))bank=JsonUtility.FromJson<LabQuestionBank>(File.ReadAllText(externalQuestions));

                else if(questionJson) bank=JsonUtility.FromJson<LabQuestionBank>(questionJson.text);

            }

            catch(Exception e){Debug.LogWarning("Question file could not be read: "+e.Message);}

            if(!ValidBank(bank))

            {

                if(questionJson)bank=JsonUtility.FromJson<LabQuestionBank>(questionJson.text);

            }

            if(!ValidBank(bank))

                bank=JsonUtility.FromJson<LabQuestionBank>("{\"questions\":[{\"question\":\"进入操作区域前应先完成什么？\",\"options\":[\"佩戴安全帽并学习操作流程\",\"直接开始\"],\"correct\":0,\"explanation\":\"先完成准备与学习。\"}]}");

            if(lessonVideo)

            {

                lessonVideo.playOnAwake=false;

                lessonVideo.loopPointReached += v => { LessonCompleted=true; Notify("示例课程学习完成，可以参加问答。",6); };

                lessonVideo.errorReceived += (v,e) => {Notify("视频未能加载，可阅读课程文字后参加问答。",8); Debug.LogWarning(e);};

                if(!lessonVideo.clip) { lessonVideo.source=VideoSource.Url; lessonVideo.url=Path.Combine(Application.streamingAssetsPath,"TrainingLesson.mp4"); }

                lessonVideo.Prepare();

            }

            var screen=GameObject.Find("TeachingDisplay");learningScreen=screen?screen.transform:null;

            if(!wallLearningDisplay)wallLearningDisplay=FindObjectOfType<LabWallLearningDisplay>();

            if(!wallLearningDisplay&&learningScreen)wallLearningDisplay=learningScreen.gameObject.AddComponent<LabWallLearningDisplay>();

            if(wallLearningDisplay){wallLearningDisplay.app=this;wallLearningDisplay.EnsureBuilt();}

            EnsureProgramming();

            Part1接管中=false;

        }

        public void EnsureProgramming()

        {

            if(!trajectoryProgram)trajectoryProgram=GetComponent<LabTrajectoryProgram>();

            if(!trajectoryProgram)trajectoryProgram=gameObject.AddComponent<LabTrajectoryProgram>();

            if(!trajectoryPanel)trajectoryPanel=GetComponent<LabTrajectoryPanel>();

            if(!trajectoryPanel)trajectoryPanel=gameObject.AddComponent<LabTrajectoryPanel>();

            trajectoryPanel.app=this;trajectoryPanel.program=trajectoryProgram;

            if(!pendantController)pendantController=GetComponent<LabPendantController>();
            if(!pendantController)pendantController=gameObject.AddComponent<LabPendantController>();
            pendantController.app=this;

        }

        void Update()

        {

            elapsed += Time.deltaTime;

            CloseFailedStation();

            if(Input.GetKeyDown(KeyCode.F1)&&!PendantProgrammingVisible&&!Station1PendantVisible)

            {

                if(panel=="help")panel=helpReturnPanel;

                else {helpReturnPanel=panel;panel="help";if(trajectoryProgram&&trajectoryProgram.IsExecuting)trajectoryProgram.Pause();if(ActiveTracing)ActiveTracing.StopAutomatic();}

            }

            if(Input.GetKeyDown(KeyCode.Escape) && !Part1接管中) ClosePanel();

            if(Input.GetKeyDown(KeyCode.E) && !ActiveStation && panel=="") Interact();

            if(ActiveStation && !Part1接管中)

            {

                bool typing=ProgrammingVisible&&((Station1PendantVisible&&station1Pendant.Programming&&station1Pendant.Programming.TextEntryFocused)||(trajectoryPanel&&trajectoryPanel.TextEntryFocused)||(pendantController&&pendantController.TextEntryFocused));

                var trace=ActiveTracing;

                bool automatic=Station1ProgramBusy||(trajectoryProgram&&trajectoryProgram.IsExecuting)||(pendantController&&pendantController.Executor&&pendantController.Executor.IsExecuting)||(trace&&trace.IsAutomatic);

                if(!typing&&Input.GetKeyDown(KeyCode.Space)) ToggleEmergencyStop();

                if(!typing&&Input.GetKeyDown(KeyCode.R)) ResetActiveExercise();

                if(panel=="operate"&&!Station1PendantVisible&&!typing&&!automatic&&Input.GetKeyDown(KeyCode.P)&&(!trace||trace.IsManual)) ActiveStation.SetPen(!ActiveStation.PenDown);

                if(panel=="operate"&&!automatic&&(Station1PendantVisible||!trace||trace.IsManual))

                {

                    Vector3 dir=typing?Vector3.zero:new Vector3(Input.GetAxisRaw("Horizontal"),(Input.GetKey(KeyCode.Q)?1:0)-(Input.GetKey(KeyCode.E)?1:0),Input.GetAxisRaw("Vertical"));

                    heldDirection=typing?Vector3.zero:PointerJogDirection();

                    ActiveStation.MoveTool(Station1PendantVisible?station1Pendant.Programming.ManualDirection():Vector3.ClampMagnitude(dir+heldDirection+(PendantProgrammingVisible?pendantController.HeldDirection:Vector3.zero),1));

                }

            }

            CloseFailedStation();

            if(player)

            {

                player.inputEnabled=panel==""&&!ActiveStation&&!player.IsEquippingHelmet;

                player.focusMode=ActiveStation!=null;

            player.focusZoomEnabled=!ProgrammingVisible;

            player.focusHorizontalScreenOffset=Station1PendantVisible?-.155f:ProgrammingVisible?.115f:TracingPaperVisible?-.155f:0;

                player.learningFocus=IsLearningView&&learningScreen;

                if(player.learningFocus)

                {

                    player.learningLookAt=learningScreen.position;

                    player.learningCameraPosition=learningScreen.position-learningScreen.forward*3.25f;

                }

                if(ActiveStation)
                {
                    Vector3 center=ActiveStation.exerciseCenter;
                    if(ActiveStation.IsTracing)center.y=ActiveStation.drawingHeight;
                    player.focusPoint=ActiveStation.transform.TransformPoint(center);
                    player.focusScale=ActiveStation.IsTracing?((TracingPaperVisible||Station1PendantVisible)?.60f:.52f):Mathf.Clamp(ActiveStation.exerciseScale*1.3f,.5f,1f);
                    player.focusCameraOffset=ActiveStation.IsTracing?new Vector3(.75f,1.35f,-.90f):new Vector3(3.1f,2.2f,-2.5f);
                }

                player.UpdatePointerCapture();

            }

            if(wallLearningDisplay)wallLearningDisplay.RefreshDisplay();

        }

        public void Notify(string text,float duration=4) {notification=text;noticeUntil=Time.time+duration;}

        public void EquipHelmet()

        {

            if(HelmetWorn || !player || player.IsEquippingHelmet) return;

            var nearest=NearestHelmet;

            if(!nearest){Notify("请走近要领取的那顶安全帽，再按 E。");return;}

            helmetPickup=nearest;

            if(player.equipment)player.equipment.pickupVisual=nearest.gameObject;

            LabHelmetRackSelection.CopyAppearance(nearest,player.wornHelmet.transform);

            if(!player.WearHelmet(() => {

                HelmetWorn=true;

                Notify("安全帽已戴稳。前往右墙教学屏，按 E 开始学习。",6);

            })) return;

            Notify("正在佩戴安全帽：托帽、举起、戴稳。",4);

        }

        public void OpenLesson()

        {

            if(!HelmetWorn){Notify("请先在右侧装备架领取并戴好安全帽。");return;}

            if(!Near(teachingPoint,4.4f)){Notify("请走近右墙教学大屏，再按 E。");return;}

            panel="lesson";

            if(lessonVideo && !LessonCompleted) lessonVideo.Play();

            if(wallLearningDisplay){wallLearningDisplay.EnsureBuilt();wallLearningDisplay.RefreshDisplay();}

        }

        public void OpenTextLesson()

        {

            if(!HelmetWorn||!IsLearningPanel)return;

            if(lessonVideo)lessonVideo.Pause();

            panel="lessonText";

        }

        public void CompleteTextLesson()

        {

            if(panel!="lessonText"||!HelmetWorn)return;

            LessonCompleted=true;StartQuiz();

        }

        public void ToggleLessonPlayback()

        {

            if(!HelmetWorn||panel!="lesson"||!lessonVideo)return;

            if(lessonVideo.isPlaying)lessonVideo.Pause();else lessonVideo.Play();

        }

        public bool TryStartStation(int index)

        {

            if(!HelmetWorn){Notify("请先领取安全帽。");return false;}

            if(!QuizPassed){Notify("先完成右墙教学屏的课程和问答，再进入机械臂实操。");return false;}

            if(index<0 || index>=stations.Length) return false;

            if(station1Pendant)station1Pendant.Close();

            if(pendantController)pendantController.Close();

            if(trajectoryProgram)trajectoryProgram.Stop();

            if(ActiveTracing)ActiveTracing.StopAutomatic();

            if(ActiveStation) ActiveStation.EndOperation();

            ActiveStation=stations[index];

            // All three dedicated stations use the same validated motion adapter.
            Part1接管中=false;

            LastMode=ActiveStation.RequiredMode;

            ActiveStation.BeginOperation();

            if(CloseFailedStation())return false;

            panel="operate";

            EnsureProgramming();trajectoryProgram.Bind(ActiveStation.IsTracing?null:ActiveStation);trajectoryPanel.Close();pendantController.Close();

            if(ActiveStation.IsTracing)
            {
                if(!ActiveTracing){ClosePanel();Notify("循迹训练组件未配置，请检查 01 工位。",6);return false;}
                ActiveTracing.SelectShape(0);
            }

            Notify("已进入"+ActiveStation.RoleLabel+"工位。WASD 平移，Q / E 升降，空格急停。",6);return true;

        }

        bool CloseFailedStation()
        {
            if(!ActiveStation || ActiveStation.IsOperating)return false;
            string reason=ActiveStation.DrawingBlockReason;
            if(string.IsNullOrEmpty(reason))reason="工位无法进入操作状态，请检查配置后重试";
            ClosePanel();
            Notify(reason+"；已退出工位。",8);
            return true;
        }

        Vector3 PointerJogDirection()
        {
            // The old jog-button area now contains the read-only paper view.
            if(Station1PendantVisible)return Vector3.zero;
            if(!Input.GetMouseButton(0) || Screen.width<=0 || Screen.height<=0)return Vector3.zero;
            Vector3 mouse=Input.mousePosition;
            Vector2 point=new Vector2(mouse.x*1600f/Screen.width,(Screen.height-mouse.y)*900f/Screen.height);
            if(new Rect(1325,395,116,45).Contains(point))return Vector3.forward;
            if(new Rect(1218,447,100,45).Contains(point))return Vector3.left;
            if(new Rect(1448,447,104,45).Contains(point))return Vector3.right;
            if(new Rect(1325,499,116,45).Contains(point))return Vector3.back;
            if(new Rect(1218,561,162,43).Contains(point))return Vector3.up;
            if(new Rect(1392,561,162,43).Contains(point))return Vector3.down;
            return Vector3.zero;
        }

        public bool SubmitAnswer(int index)

        {

            if(panel!="quiz" || answered || questionIndex>=bank.questions.Length) return false;

            var q=bank.questions[questionIndex];

            if(index==q.correct){CorrectAnswers++;answered=true;feedback="回答正确。"+q.explanation;return true;}

            feedback="再想一想。"+q.explanation;return false;

        }

        public void NextQuestion()

        {

            if(!answered)return;

            questionIndex++; answered=false;feedback="";

            if(questionIndex>=bank.questions.Length){ QuizPassed=true;panel="";Notify("问答通过！左侧三台机械臂已开放，走近任一工位按 E。",8); }

        }

        public void StartQuiz()

        {

            if(!HelmetWorn || !LessonCompleted) {Notify("请先看完示例视频，或完成课程文字学习。");return;}

            if(lessonVideo)lessonVideo.Pause();

            questionIndex=0;CorrectAnswers=0;answered=false;feedback="";panel="quiz";

        }

        public void ClosePanel()

        {

            if(station1Pendant)station1Pendant.Close();

            if(pendantController)pendantController.Close();

            if(trajectoryProgram)trajectoryProgram.Stop();

            if(trajectoryPanel)trajectoryPanel.Close();

            if(pendantController)pendantController.Close();

            if(ActiveTracing)ActiveTracing.StopAutomatic();

            if(ActiveStation) ActiveStation.EndOperation();

            ActiveStation=null;

            Part1接管中=false;

            if(lessonVideo && lessonVideo.isPlaying)lessonVideo.Pause();

            panel="";heldDirection=Vector3.zero;

        }

        public void ResetActiveExercise()
        {
            if(station1Pendant&&station1Pendant.Programming)station1Pendant.Programming.Stop();
            if(!ActiveStation)return;
            if(pendantController)pendantController.StopProgram();
            if(ActiveTracing)ActiveTracing.ResetAttempt();
            else if(trajectoryProgram)trajectoryProgram.Reset();
            else ActiveStation.ResetExercise();
        }

        public void ToggleEmergencyStop()
        {
            if(!ActiveStation)return;
            if(!ActiveStation.EmergencyStopped&&ActiveTracing&&!ActiveTracing.IsProgrammed)ActiveTracing.StopAutomatic();
            ActiveStation.SetEmergencyStop(!ActiveStation.EmergencyStopped);
            if(ActiveStation.EmergencyStopped&&station1Pendant&&station1Pendant.Programming)station1Pendant.Programming.Pause();
            if(ActiveStation.EmergencyStopped&&pendantController)pendantController.PauseProgram();
        }

        public void Interact()

        {

            if(player && player.IsEquippingHelmet) return;

            if(!HelmetWorn && NearestHelmet){EquipHelmet();return;}

            if(Near(teachingPoint,4.4f)){OpenLesson();return;}

            for(int i=0;i<stations.Length;i++) if(Vector3.Distance(player.transform.position,stations[i].transform.position)<4f){TryStartStation(i);return;}

            Notify("靠近安全装备架、教学屏或机械臂工位后按 E 交互。");

        }

        bool Near(Transform t,float distance) { if(!t||!player)return false; var a=t.position; var b=player.transform.position;a.y=b.y=0;return Vector3.Distance(a,b)<distance; }

        static bool ValidBank(LabQuestionBank b)

        {

            if(b==null||b.questions==null||b.questions.Length==0)return false;

            foreach(var q in b.questions)

            {

                if(q==null||string.IsNullOrWhiteSpace(q.question)||q.options==null||q.options.Length<2||q.options.Length>4||q.correct<0||q.correct>=q.options.Length)return false;

                foreach(var option in q.options)if(string.IsNullOrWhiteSpace(option))return false;

            }

            return true;

        }

        string Prompt()

        {

            if(player && player.IsEquippingHelmet)return "正在佩戴安全帽  ·  请稍候";

            if(!HelmetWorn && NearestHelmet)return "E  ·  领取面前最近的安全帽";

            if(Near(teachingPoint,4.4f))return "E  ·  打开教学屏";

            if(stations!=null)for(int i=0;i<stations.Length;i++)if(Near(stations[i].transform,4))return "E  ·  操作 "+stations[i].displayName;

            return player&&player.pointerReleased?"光标已释放  ·  Tab 恢复鼠标转视角":"W A S D  移动    鼠标转视角    Tab 释放光标    Shift 快走";

        }

        void Styles()

        {

            if(body!=null)return;

            font=uiFont?uiFont:Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","SimHei","Arial"},20);

            body=new GUIStyle(GUI.skin.label){font=font,fontSize=21,wordWrap=true,normal={textColor=new Color(.88f,.93f,.95f)}};

            small=new GUIStyle(body){fontSize=17};tiny=new GUIStyle(body){fontSize=14};

            title=new GUIStyle(body){fontSize=32,fontStyle=FontStyle.Bold};heading=new GUIStyle(body){fontSize=23,fontStyle=FontStyle.Bold};

            button=new GUIStyle(){font=font,fontSize=19,wordWrap=true,alignment=TextAnchor.MiddleCenter,normal={textColor=Color.white},hover={textColor=Color.white},active={textColor=Color.white}};

            pixel=Texture2D.whiteTexture;

        }

        Color navy=new Color(.045f,.075f,.11f,.96f), teal=new Color(.04f,.61f,.67f), muted=new Color(.15f,.21f,.27f,.98f);

        void Box(Rect r,Color c){GUI.color=c;GUI.DrawTexture(r,pixel);GUI.color=Color.white;}

        void Txt(Rect r,string text,GUIStyle style=null){GUI.Label(r,text,style??body);}

        bool Btn(Rect r,string label, bool primary=false)

        {

            Box(r,r.Contains(Event.current.mousePosition)?new Color(.11f,.41f,.49f):(primary?teal:muted));return GUI.Button(r,label,button);

        }

        bool Hold(Rect r,string label){Box(r,muted);return GUI.RepeatButton(r,label,button);}

        void OnGUI()

        {

            Styles();GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(Screen.width/1600f,Screen.height/900f,1));

            Box(new Rect(0,0,1600,76),navy);Box(new Rect(28,23,6,30),teal);

            Txt(new Rect(48,17,390,40),"ROBOT LAB  /  机械臂实训室",heading);

            Txt(new Rect(1240,27,330,35),PendantProgrammingVisible?"F1 直线  ·  F2 圆弧  ·  Enter 记录":"VIRTUAL TRAINING    •    F1 帮助",small);

            string[] steps={"01  安全准备","02  教学学习","03  知识问答","04  实操训练"};

            int current=!HelmetWorn?0:!LessonCompleted?1:!QuizPassed?2:3;

            for(int i=0;i<4;i++){float x=460+i*184;Box(new Rect(x,18,172,40),i==current?teal:muted);Txt(new Rect(x+10,26,162,30),(i<current?"✓ ":"")+steps[i],small);}

            if(panel=="" || (panel=="operate"&&(!ProgrammingVisible||Station1PendantVisible)))

            {

                Box(new Rect(24,102,270,148),navy);Txt(new Rect(44,120,234,30),"当前任务",heading);

                string task=!HelmetWorn?"前往入口右侧装备架\n领取安全帽并佩戴":!LessonCompleted?"沿右侧前往教学大屏\n观看机械臂基础课程":!QuizPassed?"完成教学屏上的问答\n解锁机械臂操作":ActiveStation?(ActiveStation.IsTracing?"手动循迹 J 型\n自动循迹右侧四图":ActiveStation.IsRing?"沿钢丝移动圆环\n避开圈边与机械臂碰撞":"移动末端画笔\n自由绘图或编程轨迹"):"01 循迹  ·  02 套圈\n03 自由画图";

                Txt(new Rect(44,164,234,72),task,small);

                Box(new Rect(24,268,270,46),navy);Txt(new Rect(44,279,230,30),HelmetWorn?"●  安全帽已佩戴":"○  尚未佩戴安全帽",small);

            }

            if(panel==""){Box(new Rect(365,823,870,50),navy);Txt(new Rect(385,837,850,35),Prompt(),small);}

            if(!IsLearningView && !ProgrammingVisible && Time.time<noticeUntil && !string.IsNullOrEmpty(notification)){Box(new Rect(355,91,890,48),navy);Txt(new Rect(374,102,854,36),notification,small);}

            if(IsLearningPanel){Box(new Rect(365,823,870,50),navy);Txt(new Rect(385,837,850,35),"直接点击墙上大屏操作    ·    Esc 返回漫游",small);}

            if(panel=="operate" && ActiveStation && !Part1接管中) OperationUI();

            if(LegacyProgrammingVisible)trajectoryPanel.Draw();

            if(panel=="help") HelpUI();

        }

        void OperationUI()

        {

            var s=ActiveStation;

            var trace=ActiveTracing;

            bool manual=(!trajectoryProgram||!trajectoryProgram.IsExecuting)&&(!pendantController||!pendantController.Executor||!pendantController.Executor.IsExecuting)&&(!trace||(trace.IsManual&&!trace.IsAutomatic));

            if(s.IsTracing)
            {if(Btn(new Rect(1200,100,375,43),Station1PendantVisible?"收起示教器":"示教器",true))ToggleStation1Pendant();}

            else if(Btn(new Rect(1200,100,375,43),PendantProgrammingVisible?"收起示教器":"示教器 / L・C 编程",true))

            {if(PendantProgrammingVisible)pendantController.Close();else {EnsureProgramming();trajectoryProgram.Bind(s);trajectoryPanel.Close();pendantController.Open(s);}}

            if(Station1PendantVisible)
            {
                if(trace)tracingPanel.Draw(this,trace,font,true);
                return;
            }

            if(!s.IsTracing&&Btn(new Rect(1200,805,375,34),LegacyProgrammingVisible?"收起高级轨迹编辑":"高级轨迹编辑 / 原有示教与执行"))
            {if(LegacyProgrammingVisible)trajectoryPanel.Close();else {EnsureProgramming();pendantController.Close();trajectoryProgram.Bind(s);trajectoryPanel.Open();}}

            Box(new Rect(1200,156,375,637),navy);Txt(new Rect(1222,176,330,34),s.displayName,heading);

            Txt(new Rect(1222,220,330,30),manual?"末端点动控制  /  手动模式":trace?(trace.IsAutomatic?"自动循迹中，手动输入已锁定":"请在左侧启动自动循迹"):"轨迹程序控制中",small);

            bool wasEnabled=GUI.enabled;GUI.enabled=manual;

            Box(new Rect(1220,262,334,45),muted);
            Txt(new Rect(1232,272,316,30),"本机任务："+s.RoleLabel,heading);

            Txt(new Rect(1222,322,330,56),"W/S  前后    A/D  左右\nQ/E  升降    空格  急停",small);

            Hold(new Rect(1325,395,116,45),"前 +Z");

            Hold(new Rect(1218,447,100,45),"左 −X");

            Hold(new Rect(1448,447,104,45),"右 +X");

            Hold(new Rect(1325,499,116,45),"后 −Z");

            Hold(new Rect(1218,561,162,43),"上 +Y");

            Hold(new Rect(1392,561,162,43),"下 −Y");

            if(Btn(new Rect(1220,620,163,45),s.IsRing?"重置训练":s.IsTracing?(s.PenDown?"抬起探针 [P]":"探针贴纸 [P]"):(s.PenDown?"抬笔  [P]":"落笔  [P]"))){if(s.IsRing)ResetActiveExercise();else s.SetPen(!s.PenDown);}

            GUI.enabled=wasEnabled;

            if(Btn(new Rect(1394,620,160,45),"重置  [R]"))ResetActiveExercise();

            if(Btn(new Rect(1220,680,334,48),s.EmergencyStopped?"解除急停":"■  紧急停止",true))ToggleEmergencyStop();

            if(Btn(new Rect(1220,741,334,36),"退出工位  [Esc]"))ClosePanel();

            if(trace)tracingPanel.Draw(this,trace,font);

            else if(PendantProgrammingVisible&&pendantController.Executor)
            {
                var executor=pendantController.Executor;
                Box(new Rect(510,805,665,78),navy);
                Txt(new Rect(527,815,632,31),"FWD 运行/恢复 · HOLD 暂停 · STEP 单步 · RESET 停止 · DATA 文件",small);
                Txt(new Rect(527,846,632,34),executor.StatusText,small);
            }

            else if(!ProgrammingVisible)

            {

            Box(new Rect(24,336,270,216),navy);Txt(new Rect(44,354,230,30),s.IsDrawing?"轨迹记录":"钢丝挑战",heading);

            Txt(new Rect(44,398,230,74),"进度  "+Mathf.RoundToInt(s.Progress*100)+"%\n接触  "+s.Contacts+" 次     用时  "+s.Elapsed.ToString("F1")+"s",small);

            Txt(new Rect(44,471,230,72),s.EmergencyStopped?"■ 已急停":s.IsRing?(s.RobotWireBlocked?"机械臂接近钢丝，请反向调整":s.ToolInContact?"● 触及圈边，沿线调整或退回":s.RingMovementBlocked?"已到边界，可反向退回":"沿钢丝逐步前进"):(!string.IsNullOrEmpty(s.DrawingBlockReason)?s.DrawingBlockReason:s.PenDown?"● 正在落笔":"○ 抬笔移动中"),small);

            }

        }

        void HelpUI()

        {

            Box(new Rect(360,180,880,570),navy);Txt(new Rect(400,214,800,44),"实训室操作指南",title);

            Txt(new Rect(400,276,785,364),"漫游：WASD 移动，鼠标转视角；Tab 释放/捕获光标。\n流程：佩戴安全帽 → 墙屏学习与问答 → 靠近工位按 E。\n01 循迹：移至 J 型绿色起点，点“开始手动”，沿参考线移动；\n完成后点评估。圆、三角形、正方形和复杂图形由自动循迹演示。\n02 套圈：沿钢丝前进，碰线与端点会阻挡，可反向调整。\n03 画图：自由绘图，或用示教点 / MOVE 指令编程。\n机械臂：WASD 平移，Q/E 升降，P 抬笔 / 落笔，R 重置。\n空格急停，Esc 退出。自动循迹时手动移动锁定；帮助会停止自动。\n循迹显示覆盖率、精度与成功程度，按真实笔尖轨迹评分。",body);

            if(Btn(new Rect(400,664,785,52),"返回",true))panel=helpReturnPanel;

        }

        void OnEnable() { }

        void OnDisable(){heldDirection=Vector3.zero;if(station1Pendant)station1Pendant.Close();if(ActiveTracing)ActiveTracing.StopAutomatic();}

        void OnDestroy(){tracingPanel.Dispose();if(font&&!uiFont)Destroy(font);}

        void OnApplicationFocus(bool focus){if(!focus)heldDirection=Vector3.zero;}

    }

}
