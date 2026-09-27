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

        [Header("Part1教学接管")]
        public Part1启动桥 part1启动桥;

        [Tooltip("true 时 Station01 由 Part1 接管，大佬原机械臂输入和操作面板暂停。")]
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

        public string StateSummary => "helmet="+HelmetWorn+";lesson="+LessonCompleted+";quiz="+QuizPassed+";panel="+panel+";station="+(ActiveStation?ActiveStation.stationIndex:-1);

        public bool ProgrammingVisible => !Part1接管中&&trajectoryPanel&&trajectoryPanel.Visible&&panel=="operate"&&ActiveStation;

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

        }

        public void EnsureProgramming()

        {

            if(!trajectoryProgram)trajectoryProgram=GetComponent<LabTrajectoryProgram>();

            if(!trajectoryProgram)trajectoryProgram=gameObject.AddComponent<LabTrajectoryProgram>();

            if(!trajectoryPanel)trajectoryPanel=GetComponent<LabTrajectoryPanel>();

            if(!trajectoryPanel)trajectoryPanel=gameObject.AddComponent<LabTrajectoryPanel>();

            trajectoryPanel.app=this;trajectoryPanel.program=trajectoryProgram;

        }

        void Update()

        {

            elapsed += Time.deltaTime;

            if(Input.GetKeyDown(KeyCode.F1))

            {

                if(panel=="help")panel=helpReturnPanel;

                else {helpReturnPanel=panel;panel="help";if(trajectoryProgram&&trajectoryProgram.IsExecuting)trajectoryProgram.Pause();}

            }

            if(Input.GetKeyDown(KeyCode.Escape) && !Part1接管中) ClosePanel();

            if(Input.GetKeyDown(KeyCode.E) && !ActiveStation && panel=="") Interact();

            if(ActiveStation && !Part1接管中)

            {

                bool typing=ProgrammingVisible&&trajectoryPanel.TextEntryFocused;

                bool automatic=trajectoryProgram&&trajectoryProgram.IsExecuting;

                if(!typing&&Input.GetKeyDown(KeyCode.Space)) ActiveStation.SetEmergencyStop(!ActiveStation.EmergencyStopped);

                if(!typing&&Input.GetKeyDown(KeyCode.R)){if(trajectoryProgram)trajectoryProgram.Reset();else ActiveStation.ResetExercise();}

                if(!typing&&!automatic&&Input.GetKeyDown(KeyCode.P)) ActiveStation.SetPen(!ActiveStation.PenDown);

                if(panel=="operate"&&!automatic)

                {

                    Vector3 dir=typing?Vector3.zero:new Vector3(Input.GetAxisRaw("Horizontal"),(Input.GetKey(KeyCode.Q)?1:0)-(Input.GetKey(KeyCode.E)?1:0),Input.GetAxisRaw("Vertical"));

                    ActiveStation.MoveTool(Vector3.ClampMagnitude(dir+heldDirection,1));

                }

            }

            if(player)

            {

                player.inputEnabled=panel==""&&!ActiveStation&&!player.IsEquippingHelmet;

                player.focusMode=ActiveStation!=null;

            player.focusZoomEnabled=!ProgrammingVisible;

            player.focusHorizontalScreenOffset=ProgrammingVisible?.115f:0;

                player.learningFocus=IsLearningView&&learningScreen;

                if(player.learningFocus)

                {

                    player.learningLookAt=learningScreen.position;

                    player.learningCameraPosition=learningScreen.position-learningScreen.forward*3.25f;

                }

                if(ActiveStation){player.focusPoint=ActiveStation.transform.TransformPoint(ActiveStation.exerciseCenter);player.focusScale=Mathf.Clamp(ActiveStation.exerciseScale*1.3f,.5f,1f);}

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

            if(trajectoryProgram)trajectoryProgram.Stop();

            if(ActiveStation && !Part1接管中) ActiveStation.EndOperation();

            ActiveStation=stations[index];

            // Station01（index 0）交给 Part1，保留大佬的工位聚焦相机，
            // 但不启动原 LabRobotStation 的操作逻辑。
            if(index==0)

            {

                if(!part1启动桥)

                {

                    Debug.LogError("[LabTrainingApp] Station01 未绑定 Part1启动桥。",this);

                    Notify("Station01 的 Part1 启动桥未绑定，请检查 Inspector。",6);

                    ActiveStation=null;

                    Part1接管中=false;

                    panel="";

                    return false;

                }

                Part1接管中=true;

                if(trajectoryPanel)trajectoryPanel.Close();

                panel="operate";

                part1启动桥.启动Part1();

                Notify("已进入 Station01 / Part1 机械臂教学。",6);

                return true;

            }

            // Station02 / Station03 完全保持原流程。
            Part1接管中=false;

            ActiveStation.BeginOperation();

            ActiveStation.SetMode(LastMode);

            panel="operate";

            EnsureProgramming();trajectoryProgram.Bind(ActiveStation);trajectoryPanel.Close();

            Notify("已进入工位。WASD 平移，Q / E 升降，空格急停。",6);return true;

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

            if(trajectoryProgram)trajectoryProgram.Stop();

            if(trajectoryPanel)trajectoryPanel.Close();

            // Part1 没有调用 LabRobotStation.BeginOperation()，所以不要对它调用 EndOperation()。
            if(ActiveStation && !Part1接管中) ActiveStation.EndOperation();

            ActiveStation=null;

            Part1接管中=false;

            if(lessonVideo && lessonVideo.isPlaying)lessonVideo.Pause();

            panel="";heldDirection=Vector3.zero;

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

            Txt(new Rect(1240,27,330,35),"VIRTUAL TRAINING    •    F1 帮助",small);

            string[] steps={"01  安全准备","02  教学学习","03  知识问答","04  实操训练"};

            int current=!HelmetWorn?0:!LessonCompleted?1:!QuizPassed?2:3;

            for(int i=0;i<4;i++){float x=460+i*184;Box(new Rect(x,18,172,40),i==current?teal:muted);Txt(new Rect(x+10,26,162,30),(i<current?"✓ ":"")+steps[i],small);}

            if(panel=="" || (panel=="operate"&&!ProgrammingVisible))

            {

                Box(new Rect(24,102,270,148),navy);Txt(new Rect(44,120,234,30),"当前任务",heading);

                string task=!HelmetWorn?"前往入口右侧装备架\n领取安全帽并佩戴":!LessonCompleted?"沿右侧前往教学大屏\n观看机械臂基础课程":!QuizPassed?"完成教学屏上的问答\n解锁机械臂操作":ActiveStation?"手动操控末端工具\n完成当前工位训练":"选择左侧任一工位\n开始绘图或钢丝套圈";

                Txt(new Rect(44,164,234,72),task,small);

                Box(new Rect(24,268,270,46),navy);Txt(new Rect(44,279,230,30),HelmetWorn?"●  安全帽已佩戴":"○  尚未佩戴安全帽",small);

            }

            if(panel==""){Box(new Rect(365,823,870,50),navy);Txt(new Rect(385,837,850,35),Prompt(),small);}

            if(!IsLearningView && !ProgrammingVisible && Time.time<noticeUntil && !string.IsNullOrEmpty(notification)){Box(new Rect(355,91,890,48),navy);Txt(new Rect(374,102,854,36),notification,small);}

            if(IsLearningPanel){Box(new Rect(365,823,870,50),navy);Txt(new Rect(385,837,850,35),"直接点击墙上大屏操作    ·    Esc 返回漫游",small);}

            if(panel=="operate" && ActiveStation && !Part1接管中) OperationUI();

            if(ProgrammingVisible)trajectoryPanel.Draw();

            if(panel=="help") HelpUI();

        }

        void OperationUI()

        {

            var s=ActiveStation;

            bool manual=!trajectoryProgram||!trajectoryProgram.IsExecuting;

            if(Btn(new Rect(1200,100,375,43),ProgrammingVisible?"收起轨迹编程":"轨迹编程 / 示教 + 指令",true))

            {if(ProgrammingVisible)trajectoryPanel.Close();else {EnsureProgramming();trajectoryProgram.Bind(s);trajectoryPanel.Open();}}

            Box(new Rect(1200,156,375,637),navy);Txt(new Rect(1222,176,330,34),s.displayName,heading);

            Txt(new Rect(1222,220,330,30),manual?"末端点动控制  /  手动模式":"轨迹程序控制中",small);

            bool wasEnabled=GUI.enabled;GUI.enabled=manual;

            if(Btn(new Rect(1220,262,163,45),"轨迹绘图",LastMode==0))SetTrainingMode(0);

            if(Btn(new Rect(1394,262,160,45),"钢丝套圈",LastMode==1))SetTrainingMode(1);

            Txt(new Rect(1222,322,330,56),"W/S  前后    A/D  左右\nQ/E  升降    空格  急停",small);

            heldDirection=Vector3.zero;

            if(Hold(new Rect(1325,395,116,45),"前 +Z"))heldDirection=Vector3.forward;

            if(Hold(new Rect(1218,447,100,45),"左 −X"))heldDirection=Vector3.left;

            if(Hold(new Rect(1448,447,104,45),"右 +X"))heldDirection=Vector3.right;

            if(Hold(new Rect(1325,499,116,45),"后 −Z"))heldDirection=Vector3.back;

            if(Hold(new Rect(1218,561,162,43),"上 +Y"))heldDirection=Vector3.up;

            if(Hold(new Rect(1392,561,162,43),"下 −Y"))heldDirection=Vector3.down;

            if(Btn(new Rect(1220,620,163,45),LastMode==0?(s.PenDown?"抬笔  [P]":"落笔  [P]"):"重置训练")){if(LastMode==0)s.SetPen(!s.PenDown);else trajectoryProgram.Reset();}

            GUI.enabled=wasEnabled;

            if(Btn(new Rect(1394,620,160,45),"重置  [R]"))trajectoryProgram.Reset();

            if(Btn(new Rect(1220,680,334,48),s.EmergencyStopped?"解除急停":"■  紧急停止",true))s.SetEmergencyStop(!s.EmergencyStopped);

            if(Btn(new Rect(1220,741,334,36),"退出工位  [Esc]"))ClosePanel();

            if(!ProgrammingVisible)

            {

            Box(new Rect(24,336,270,182),navy);Txt(new Rect(44,354,230,30),LastMode==0?"轨迹记录":"钢丝挑战",heading);

            Txt(new Rect(44,398,230,74),"进度  "+Mathf.RoundToInt(s.Progress*100)+"%\n接触  "+s.Contacts+" 次     用时  "+s.Elapsed.ToString("F1")+"s",small);

            Txt(new Rect(44,471,230,64),s.EmergencyStopped?"■ 已急停":LastMode==1?(s.RobotWireBlocked?"机械臂接近钢丝，请反向调整":s.ToolInContact?"● 触及圈边，沿线调整或退回":s.RingMovementBlocked?"已到边界，可反向退回":"沿钢丝逐步前进"):(s.PenDown?"● 正在落笔":"○ 抬笔移动中"),small);

            }

        }

        void SetTrainingMode(int mode)

        {

            trajectoryProgram.Stop();LastMode=mode;ActiveStation.SetMode(mode);trajectoryProgram.Bind(ActiveStation);

            if(ProgrammingVisible)trajectoryPanel.Open();

        }

        void HelpUI()

        {

            Box(new Rect(360,210,880,500),navy);Txt(new Rect(400,244,800,44),"实训室操作指南",title);

            Txt(new Rect(400,316,785,284),"漫游：WASD 移动，鼠标直接转视角；Tab 释放/捕获光标。\n交互：靠近后按 E；Esc 关闭面板或退出工位。\n流程：佩戴安全帽 → 墙屏学习与问答 → 选择工位。\n机械臂：WASD 平移，Q/E 升降，P 画笔，R 重置，空格急停。\n轨迹编程：记录示教点，或输入 MOVE / SPEED / PEN / WAIT。\n代码应用后可运行、暂停、单步；运行中由程序控制。\n碰线会阻挡穿越，可以反向退回；钢丝两端有限位。",body);

            if(Btn(new Rect(400,621,785,52),"返回",true))panel=helpReturnPanel;

        }

        void OnEnable() { }

        void OnDisable(){heldDirection=Vector3.zero;}

        void OnDestroy(){if(font&&!uiFont)Destroy(font);}

        void OnApplicationFocus(bool focus){if(!focus)heldDirection=Vector3.zero;}

    }

}
