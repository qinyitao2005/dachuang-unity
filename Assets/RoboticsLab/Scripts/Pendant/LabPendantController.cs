using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>Bridges the authored pendant buttons to real station teaching positions.</summary>
    public sealed class LabPendantController : MonoBehaviour
    {
        public LabTrainingApp app;
        public GameObject pendantPrefab;
        public bool Visible {get;private set;}
        public LabPendantProgramModel Model {get;private set;}
        public LabPendantScreenView ScreenView {get;private set;}
        public LabPendantFilePanel FilePanel {get;private set;}
        public LabPendantExecutor Executor {get;private set;}
        public bool TextEntryFocused => FilePanel && FilePanel.TextEntryFocused;
        public string LastFileStatus {get;private set;} = "DATA：保存 / 加载 / 返回 P[1]";
        public IReadOnlyDictionary<string,Button> Buttons=>buttons;
        public Vector3 HeldDirection=>Visible&&station&&(!Executor||!Executor.IsExecuting)&&!TextEntryFocused
            ?station.transform.TransformDirection(jog):Vector3.zero;
        readonly Dictionary<int,LabPendantProgramModel> documents=new Dictionary<int,LabPendantProgramModel>();
        readonly Dictionary<string,Button> buttons=new Dictionary<string,Button>();
        LabRobotStation station;
        GameObject canvasObject;
        RectTransform root;
        Vector3 jog;
        bool circular;
        float speedMm=100;

        void Awake()
        {
            Executor=GetComponent<LabPendantExecutor>();
            if(!Executor)Executor=gameObject.AddComponent<LabPendantExecutor>();
            Executor.Changed+=ExecutionChanged;
        }

        public void Open(LabRobotStation target)
        {
            if(!target||target.IsTracing||!target.IsOperating)return;
            if(!pendantPrefab){if(app)app.Notify("示教器资源未配置，请运行 Robot Lab / Install Teaching Pendant");return;}
            station=target;
            if(!documents.TryGetValue(target.stationIndex,out var document))
            {document=new LabPendantProgramModel();documents.Add(target.stationIndex,document);}
            Model=document;circular=document.HasPendingArc||document.Instructions.Count>0&&document.Instructions[document.Instructions.Count-1].Type==LabPendantInstructionType.Circular;
            Executor.Bind(station,Model);
            jog=Vector3.zero;EnsureView();ScreenView.Bind(Model);Visible=true;canvasObject.SetActive(true);CloseFiles();UpdateHeader();
        }
        public void Close(){Visible=false;jog=Vector3.zero;if(Executor&&Executor.IsExecuting)Executor.Stop();CloseFiles();if(canvasObject)canvasObject.SetActive(false);}
        public void SetJog(Vector3 direction){jog=Visible&&station&&!station.EmergencyStopped&&!TextEntryFocused
            &&(!Executor||!Executor.IsExecuting)&&(!app||!app.trajectoryProgram||!app.trajectoryProgram.IsExecuting)?direction:Vector3.zero;}
        public void RecordLinear(){Record(false);}
        public void RecordCircular(){Record(true);}
        public void RepeatRecord(){Record(circular||Model!=null&&Model.HasPendingArc);}
        public void Undo(){if(Editable()){Model.RemoveLast();ScreenView.SetStatus(Model.StatusText);UpdateHeader();}}
        public void Clear(){if(Editable()){Model.Clear();circular=false;ScreenView.SetStatus("已清空，下一点为 P[1]");UpdateHeader();}}
        public void CycleSpeed(){if(!Editable())return;speedMm=speedMm==100?50:speedMm==50?150:100;UpdateHeader();ScreenView.SetStatus("新指令速度："+speedMm+" mm/sec");}
        void Record(bool arc)
        {
            if(!Editable())return;
            circular=arc||Model.HasPendingArc;
            // Capture the actual training tool, in the same station-local frame as the old editor.
            var position=station.transform.InverseTransformPoint(station.ActualToolPosition);
            // Paper contact is authored as one plane; discard only the tiny IK contact tolerance.
            if(station.PenDown&&Mathf.Abs(position.y-station.drawingHeight)<=.0025f)position.y=station.drawingHeight;
            bool ok=arc?Model.RecordCircularPoint(position,speedMm,station.PenDown):Model.AddLinear(position,speedMm,station.PenDown);
            ScreenView.SetStatus(!ok?Model.StatusText:Model.HasPendingArc?"C：移动到终点，再按 F2 / ENTER":arc?"圆弧已完成：含中间点和终点":"已添加 L P["+(Model.NextPointNumber-1)+"]");
            UpdateHeader();
        }
        bool Editable()
        {
            bool valid=Visible&&station&&station.IsOperating&&!station.EmergencyStopped
                &&(!Executor||!Executor.IsExecuting)
                &&(!app||!app.trajectoryProgram||!app.trajectoryProgram.IsExecuting);
            if(!valid&&ScreenView)ScreenView.SetStatus("先停止程序、解除急停，再编辑");
            return valid;
        }
        public bool ReturnToFirstPoint()
        {
            if(!Editable())return false;
            if(Model.Points.Count==0){LastFileStatus="请先记录起点 P[1]";ScreenView.SetStatus(LastFileStatus);return false;}
            bool ok=Model.AddLinearReference(Model.Points[0].Number,speedMm,station.PenDown);
            LastFileStatus=Model.StatusText;ScreenView.SetStatus(LastFileStatus);UpdateHeader();return ok;
        }
        public bool SaveProgram(string name)
        {
            if(!Visible||!station||Model==null){LastFileStatus="请先进入工位打开示教器";return false;}
            bool ok=LabPendantProgramStorage.Save(station.stationIndex,name,Model,out var error);
            LastFileStatus=ok?"已保存："+name:error;ScreenView.SetStatus(LastFileStatus);return ok;
        }
        public bool LoadProgram(string name)
        {
            if(!Editable()){LastFileStatus="先停止程序，再加载";return false;}
            if(!LabPendantProgramStorage.Load(station.stationIndex,name,out var loaded,out var error))
            {LastFileStatus=error;ScreenView.SetStatus(error);return false;}
            Model=loaded;documents[station.stationIndex]=loaded;Executor.Bind(station,Model);ScreenView.Bind(Model);
            circular=Model.HasPendingArc;LastFileStatus="已加载："+name;ScreenView.SetStatus(LastFileStatus);UpdateHeader();return true;
        }
        public string[] ProgramNames(){return station?LabPendantProgramStorage.Names(station.stationIndex):Array.Empty<string>();}
        public void OpenFiles(){if(!Visible)return;jog=Vector3.zero;FilePanel.Open();}
        public void CloseFiles(){if(FilePanel)FilePanel.Close();}
        public void RunProgram(){if(!Visible||TextEntryFocused)return;CloseFiles();jog=Vector3.zero;Executor.Run();}
        public void PauseProgram(){if(Executor)Executor.Pause();jog=Vector3.zero;}
        public void StepProgram(){if(!Visible||TextEntryFocused)return;CloseFiles();jog=Vector3.zero;Executor.Step();}
        public void StopProgram(){if(Executor)Executor.Stop();jog=Vector3.zero;}
        public void ToggleEmergency()
        {
            if(!station)return;
            if(app)app.ToggleEmergencyStop();else station.SetEmergencyStop(!station.EmergencyStopped);
            if(station.EmergencyStopped)PauseProgram();
            ScreenView.SetStatus(station.EmergencyStopped?"急停已触发；解除后 FWD 恢复":"已解除急停；按 FWD 明确恢复");
        }
        void ExecutionChanged()
        {
            if(!ScreenView||!Executor)return;
            ScreenView.SetExecution(Executor.CurrentInstructionIndex,Executor.State,Executor.StatusText);
            UpdateHeader();
        }
        void UpdateHeader()
        {
            if(!ScreenView)return;
            string state=Executor&&Executor.IsExecuting?(Executor.IsPaused?"HOLD":"RUN"):"TEACH";
            ScreenView.Header.text=state+" "+(station?station.stationIndex+1:0)+"   "+(circular||Model!=null&&Model.HasPendingArc?"C":"L")+"   "+speedMm+"mm/sec";
        }
        void Update()
        {
            if(!Visible)return;
            if(!station||!station.IsOperating||app&&(app.ActiveStation!=station||app.panel!="operate")){Close();return;}
            if(station.EmergencyStopped||Executor.IsExecuting||TextEntryFocused||app&&app.trajectoryProgram&&app.trajectoryProgram.IsExecuting)jog=Vector3.zero;
            if(!TextEntryFocused&&(!FilePanel||!FilePanel.IsOpen))
            {
                if(Input.GetKeyDown(KeyCode.F1))RecordLinear();
                if(Input.GetKeyDown(KeyCode.F2))RecordCircular();
                if(Input.GetKeyDown(KeyCode.F3))Undo();
                if(Input.GetKeyDown(KeyCode.F4))CycleSpeed();
                if(Input.GetKeyDown(KeyCode.F5))Clear();
                if(Input.GetKeyDown(KeyCode.Return)||Input.GetKeyDown(KeyCode.KeypadEnter))RepeatRecord();
                if(Input.GetKeyDown(KeyCode.Backspace))Undo();
            }
            ScreenView.SetPose(station.transform.InverseTransformPoint(station.ActualToolPosition)-station.exerciseCenter);
        }
        void OnApplicationFocus(bool focused){if(!focused){jog=Vector3.zero;PauseProgram();}}
        void OnDisable(){Close();}
        void OnDestroy(){if(Executor)Executor.Changed-=ExecutionChanged;if(canvasObject)Destroy(canvasObject);}

        void EnsureView()
        {
            if(canvasObject)return;
            canvasObject=new GameObject("TeachingPendantCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform,false);
            var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=80;
            var scaler=canvasObject.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=1;
            root=Instantiate(pendantPrefab,canvasObject.transform,false).GetComponent<RectTransform>();
            root.name="TeachingPendant";root.anchorMin=root.anchorMax=new Vector2(.5f,.5f);
            root.anchoredPosition=new Vector2(-733,-7);root.localScale=new Vector3(1.5f,1.5f,1);
            foreach(var image in root.GetComponentsInChildren<Graphic>(true))image.raycastTarget=false;
            foreach(var menu in root.GetComponentsInChildren<ScreenMenuNavigator>(true)){menu.enabled=false;menu.gameObject.SetActive(false);}
            var screen=root.Find("Screen").GetComponent<RectTransform>();
            ScreenView=root.gameObject.AddComponent<LabPendantScreenView>();
            var font=app&&app.uiFont?app.uiFont:Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ScreenView.Initialize(screen,font);
            FilePanel=root.gameObject.AddComponent<LabPendantFilePanel>();FilePanel.Initialize(screen,font,this);
            Wire("F1",RecordLinear);Wire("F2",RecordCircular,new Vector2(121.85f,-14.313f));
            Wire("F3",Undo);Wire("F4",CycleSpeed,new Vector2(169.55f,-14.313f));Wire("F5",Clear,new Vector2(193.4f,-14.313f));
            Wire("MENU/Enter",RepeatRecord);
            Wire("FWD",RunProgram);Wire("HOLD",PauseProgram,new Vector2(160.9f,-84));
            Wire("STEP",StepProgram,new Vector2(160.9f,-62));Wire("RESET",StopProgram,new Vector2(78.8f,-110));
            Wire("DATA",()=>{if(FilePanel.IsOpen)CloseFiles();else OpenFiles();},new Vector2(156,-38));
            Wire("EmergencyStop",ToggleEmergency,new Vector2(231,213),new Vector2(65,65));
            Jog("J1-",Vector3.left);Jog("J1+",Vector3.right);
            Jog("J2-",Vector3.back);Jog("J2+",Vector3.forward);
            Jog("J3-",Vector3.down);Jog("J3+",Vector3.up);
            var viewport=ScreenView.Scroll.viewport.GetComponent<Image>();viewport.raycastTarget=true;
            if(!FindObjectOfType<EventSystem>())
                new GameObject("PendantEventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
        }
        void Wire(string path,Action action,Vector2? location=null,Vector2? size=null)
        {
            var target=root.Find(path) as RectTransform;
            if(!target)
            {
                target=new GameObject(path,typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();
                target.SetParent(root,false);target.anchorMin=target.anchorMax=new Vector2(.5f,.5f);
                target.anchoredPosition=location??Vector2.zero;target.sizeDelta=size??new Vector2(15.5f,15.5f);
                target.GetComponent<Image>().color=new Color(.2f,.65f,1,.001f);
            }
            var image=target.GetComponent<Image>();image.raycastTarget=true;
            var button=target.GetComponent<Button>()??target.gameObject.AddComponent<Button>();
            button.targetGraphic=image;button.onClick.AddListener(()=>{if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);action();});buttons[path]=button;
            var navigation=button.navigation;navigation.mode=Navigation.Mode.None;button.navigation=navigation;
        }
        void Jog(string path,Vector3 direction)
        {
            var target=root.Find(path);if(!target)return;
            target.GetComponent<Image>().raycastTarget=true;
            var button=target.gameObject.AddComponent<LabPendantJogButton>();button.controller=this;button.localDirection=direction;
        }
    }
}
