using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>The teach list and source editor share one executable program.</summary>
    public class LabTrajectoryPanel : MonoBehaviour
    {
        public bool Visible;
        public bool TextEntryFocused { get; private set; }
        public LabTrajectoryProgram program;
        public LabTrainingApp app;
        int tab, selected=-1, fileIndex;
        Vector2 listScroll, codeScroll;
        string source="", programName="我的轨迹", editX="0",editY="0",editZ="0",editSpeed="60";
        bool editPen, sourceDirty;
        string message="";
        GUIStyle label,small,button,field,code;
        Font currentFont;
        readonly Color panel=new Color(.035f,.075f,.105f,.98f);
        readonly Color muted=new Color(.11f,.19f,.24f,1);
        readonly Color accent=new Color(.035f,.54f,.61f,1);
        LineRenderer preview;
        Material previewMaterial;

        public void Open()
        {
            if(!program||!program.Station||program.Station.IsTracing){Close();return;}
            Visible=true;selected=-1;message="";if(!sourceDirty)SyncSource();RefreshPreview();
        }
        public void Close(){Visible=false;TextEntryFocused=false;GUIFocusReset();if(preview)preview.enabled=false;}
        // Focus is cleared on the next IMGUI pass, where GUI.FocusControl is valid.
        bool clearFocus;
        void GUIFocusReset(){clearFocus=true;}
        void OnDisable(){TextEntryFocused=false;if(preview)preview.enabled=false;}
        void OnDestroy(){if(preview)Destroy(preview.gameObject);if(previewMaterial)Destroy(previewMaterial);}
        void Update(){if(preview)preview.enabled=Visible&&program&&program.Station&&program.Station.IsOperating;}

        void Styles()
        {
            if(label!=null&&currentFont==app.uiFont)return;
            currentFont=app.uiFont?app.uiFont:Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label=new GUIStyle(GUI.skin.label){font=currentFont,fontSize=20,wordWrap=true,normal={textColor=Color.white}};
            small=new GUIStyle(label){fontSize=15};
            button=new GUIStyle{font=currentFont,fontSize=17,wordWrap=true,alignment=TextAnchor.MiddleCenter,
                normal={textColor=Color.white},hover={textColor=Color.white},active={textColor=Color.white}};
            field=new GUIStyle(GUI.skin.textField){font=currentFont,fontSize=17};
            code=new GUIStyle(GUI.skin.textArea){font=currentFont,fontSize=17,wordWrap=false};
        }
        void Box(Rect r,Color color){var old=GUI.color;GUI.color=color;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        bool Button(float x,float y,float w,string text,bool enabled=true)
        {
            bool previous=GUI.enabled;GUI.enabled=previous&&enabled;
            var rect=new Rect(x,y,w,34);
            Box(rect,!GUI.enabled?new Color(.065f,.115f,.145f):rect.Contains(Event.current.mousePosition)?accent:muted);
            var oldColor=GUI.color;if(!GUI.enabled)GUI.color=new Color(.5f,.57f,.61f);
            bool clicked=GUI.Button(rect,text,button);GUI.color=oldColor;GUI.enabled=previous;
            if(clicked)GUIFocusReset();
            return clicked;
        }
        string Field(float x,float y,float w,string value,string name)
        {
            GUI.SetNextControlName("Trajectory_"+name);
            return GUI.TextField(new Rect(x,y,w,32),value,64,field);
        }
        public void Draw()
        {
            if(!Visible||!program||!program.Station)return;
            Styles();
            if(clearFocus){GUI.FocusControl(null);clearFocus=false;}
            bool unlocked=!program.IsExecuting;
            Box(new Rect(24,92,736,788),panel);
            GUI.Label(new Rect(44,107,590,32),"轨迹编程 / "+(program.Station.stationIndex+1)+" 号工位",label);
            if(Button(675,105,65,"收起")){Close();return;}
            if(Button(44,151,170,"示教点列表"))tab=0;
            if(Button(224,151,170,"指令编辑器"))tab=1;
            if(Button(404,151,162,program.Station.TrainingMode==1?"沿线示例":"方形示例",unlocked)){ApplyExample(false);tab=0;}
            if(Button(576,151,164,program.Station.TrainingMode==1?"沿线指令":"三角示例",unlocked)){ApplyExample(true);tab=1;}
            GUI.Label(new Rect(44,198,60,30),"名称",small);
            bool wasEnabled=GUI.enabled;GUI.enabled=unlocked;
            programName=Field(98,193,294,programName,"Name");GUI.enabled=wasEnabled;
            if(Button(404,193,103,"保存",unlocked&&!sourceDirty))SetResult(program.Save(programName),"已保存："+programName);
            if(Button(517,193,103,"加载",unlocked)){if(SetResult(program.Load(programName),"已加载："+programName)){selected=-1;SyncSource();RefreshPreview();}}
            if(Button(630,193,110,"已有文件",unlocked))
            {
                var names=program.SavedProgramNames();
                if(names.Length==0)message="还没有保存过轨迹。";
                else {programName=names[fileIndex++%names.Length];message="已选文件名，点击“加载”打开。";}
            }
            GUI.Label(new Rect(44,232,696,26),"坐标：相对工位练习中心，单位 mm；速度：手动最高速度的百分比。",small);
            if(tab==0)DrawPoints(unlocked);else DrawSource(unlocked);
            DrawExecution(unlocked);
            TextEntryFocused=GUI.GetNameOfFocusedControl().StartsWith("Trajectory_",StringComparison.Ordinal);
        }

        void DrawPoints(bool unlocked)
        {
            Box(new Rect(44,264,696,245),new Color(.065f,.12f,.16f));
            GUI.Label(new Rect(54,266,676,26),"步骤     动作 / 坐标                         速度     画笔",small);
            var rows=program.Steps;
            listScroll=GUI.BeginScrollView(new Rect(48,296,688,207),listScroll,new Rect(0,0,660,Mathf.Max(198,rows.Count*35)));
            for(int i=0;i<rows.Count;i++)
            {
                var step=rows[i];
                if(i==selected)Box(new Rect(0,i*35,654,33),accent);
                else if(i==program.CurrentStepIndex&&program.IsExecuting)Box(new Rect(0,i*35,654,33),muted);
                var pos=(step.localPosition-program.Station.exerciseCenter)*1000;
                string caption=step.command==LabTrajectoryCommand.Move?
                    string.Format(CultureInfo.InvariantCulture,"{0:00}   MOVE   {1:0.#}, {2:0.#}, {3:0.#}    {4:0}%    {5}",i+1,pos.x,pos.y,pos.z,step.speedPercent,step.penDown?"落笔":"抬笔"):
                    (i+1).ToString("00")+"   "+step.command+"   "+(step.command==LabTrajectoryCommand.Speed?step.speedPercent.ToString("0")+"%":step.command==LabTrajectoryCommand.Pen?(step.penDown?"落笔":"抬笔"):step.waitSeconds.ToString("0.##")+" s");
                if(GUI.Button(new Rect(2,i*35,650,33),caption,small)){selected=i;LoadSelection();GUIFocusReset();}
            }
            GUI.EndScrollView();
            if(rows.Count==0)GUI.Label(new Rect(67,320,640,104),"先用右侧按钮移动机械臂，再点“记录当前位置”。\n也可以加载示例或在指令编辑器中输入轨迹。",label);
            if(Button(44,520,194,"记录当前位置",unlocked))
            {
                float speed;
                if(ParseNumber(editSpeed,out speed)&&SetResult(program.RecordCurrent(program.Station.PenDown,speed),"已记录当前位置和画笔状态"))
                {selected=program.Steps.Count-1;LoadSelection();SyncSource();RefreshPreview();}
            }
            if(Button(248,520,105,"删除",unlocked&&selected>=0))
            {if(program.DeleteStep(selected)){selected=Mathf.Min(selected,program.Steps.Count-1);SyncSource();RefreshPreview();}}
            if(Button(363,520,105,"上移",unlocked&&selected>0))
            {if(program.MoveStep(selected,selected-1)){selected--;SyncSource();RefreshPreview();}}
            if(Button(478,520,105,"下移",unlocked&&selected>=0&&selected<program.Steps.Count-1))
            {if(program.MoveStep(selected,selected+1)){selected++;SyncSource();RefreshPreview();}}
            if(Button(593,520,147,"生成指令",unlocked)){SyncSource();tab=1;}
            bool enabled=GUI.enabled;GUI.enabled=unlocked;
            GUI.Label(new Rect(44,564,55,26),"X mm",small);editX=Field(98,559,106,editX,"X");
            GUI.Label(new Rect(219,564,55,26),"Y mm",small);editY=Field(273,559,106,editY,"Y");
            GUI.Label(new Rect(394,564,55,26),"Z mm",small);editZ=Field(448,559,106,editZ,"Z");
            GUI.Label(new Rect(569,564,55,26),"速度%",small);editSpeed=Field(629,559,111,editSpeed,"Speed");
            Box(new Rect(44,601,200,34),editPen?accent:muted);
            if(GUI.Button(new Rect(44,601,200,34),(editPen?"✓":"□")+" 落笔（仅绘图）",button))editPen=!editPen;
            GUI.enabled=enabled;
            if(Button(258,601,224,"应用到选中点",unlocked&&selected>=0))ApplySelection();
            GUI.Label(new Rect(498,605,240,49),"蓝线仅为轨迹预览",small);
        }
        void DrawSource(bool unlocked)
        {
            GUI.Label(new Rect(44,263,696,28),"MOVE x y z  ·  SPEED 1–100  ·  PEN UP/DOWN  ·  WAIT 秒",small);
            bool enabled=GUI.enabled;GUI.enabled=unlocked;
            codeScroll=GUI.BeginScrollView(new Rect(44,297,696,282),codeScroll,new Rect(0,0,Mathf.Max(672,900),Mathf.Max(270,CountLines(source)*25+20)));
            GUI.SetNextControlName("Trajectory_Source");
            string edited=GUI.TextArea(new Rect(0,0,900,Mathf.Max(270,CountLines(source)*25+20)),source,24000,code);
            if(edited!=source){source=edited;sourceDirty=true;}
            GUI.EndScrollView();GUI.enabled=enabled;
            if(Button(44,589,215,"应用代码",unlocked))
            {if(SetResult(program.Compile(source),"代码已检查并应用，可运行。")){sourceDirty=false;selected=-1;RefreshPreview();}}
            if(Button(269,589,220,"从列表重新生成",unlocked)){SyncSource();message="已从当前示教列表生成代码。";}
            GUI.Label(new Rect(503,590,237,52),sourceDirty?"有未应用的代码修改":"代码与运行轨迹已同步",small);
            GUI.Label(new Rect(44,632,696,25),"例：SPEED 60   /   PEN UP   /   MOVE -50 30 0；每条指令单独一行。",small);
        }
        void DrawExecution(bool unlocked)
        {
            Box(new Rect(44,662,696,2),accent);
            bool ready=program.Steps.Count>0&&!sourceDirty;
            if(Button(44,677,126,"从头运行",unlocked&&ready))SetResult(program.Run(),"程序开始运行");
            if(Button(180,677,126,program.IsPaused?"继续":"暂停",program.IsExecuting))
            {if(program.IsPaused)SetResult(program.Resume(),"继续运行");else {program.Pause();message="已暂停，点“继续”恢复。";}}
            if(Button(316,677,126,"单步",ready&&(unlocked||program.IsPaused)))SetResult(program.Step(),"执行一个步骤");
            if(Button(452,677,126,"停止",program.IsExecuting)){program.Stop();message="已停止，轨迹保留。";}
            if(Button(588,677,152,"重置训练")){program.Reset();message="训练已重置，轨迹保留。";}
            string state="状态："+program.StatusText;
            if(program.IsExecuting)state+="   步骤 "+(program.CurrentStepIndex+1)+"/"+program.Steps.Count;
            GUI.Label(new Rect(44,722,696,44),state,label);
            string detail=!string.IsNullOrEmpty(program.LastError)?program.LastError:sourceDirty?"请先应用代码修改，再运行或保存。":message;
            GUI.Label(new Rect(44,766,696,51),detail,small);
            GUI.Label(new Rect(44,824,696,42),"运行/暂停期间由程序控制机械臂；空格或右侧按钮急停。Esc 退出会停止程序。",small);
        }
        bool SetResult(bool ok,string success){message=ok?success:program.LastError;return ok;}
        void SyncSource(){if(program){source=program.ExportText();sourceDirty=false;}GUIFocusReset();}
        void ApplyExample(bool triangle)
        {
            if(SetResult(program.Compile(program.ExampleText(triangle)),"已载入示例，可运行或修改。"))
            {selected=-1;SyncSource();RefreshPreview();}
        }
        void LoadSelection()
        {
            if(selected<0||selected>=program.Steps.Count)return;
            var step=program.Steps[selected];var p=(step.localPosition-program.Station.exerciseCenter)*1000;
            editX=p.x.ToString("0.###",CultureInfo.InvariantCulture);editY=p.y.ToString("0.###",CultureInfo.InvariantCulture);editZ=p.z.ToString("0.###",CultureInfo.InvariantCulture);
            editSpeed=step.speedPercent.ToString("0.##",CultureInfo.InvariantCulture);editPen=step.penDown;
        }
        void ApplySelection()
        {
            var original=program.Steps[selected];
            if(original.command!=LabTrajectoryCommand.Move){message="此行不是移动点，请在指令编辑器修改。";return;}
            float x,y,z,speed;
            if(!ParseNumber(editX,out x)||!ParseNumber(editY,out y)||!ParseNumber(editZ,out z)||!ParseNumber(editSpeed,out speed))return;
            var edited=new LabTrajectoryStep{command=LabTrajectoryCommand.Move,localPosition=program.Station.exerciseCenter+new Vector3(x,y,z)/1000,speedPercent=speed,penDown=editPen,sourceLine=original.sourceLine};
            if(SetResult(program.UpdateStep(selected,edited),"示教点已更新")){SyncSource();RefreshPreview();}
        }
        bool ParseNumber(string text,out float value)
        {
            bool valid=float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!float.IsNaN(value)&&!float.IsInfinity(value);
            if(!valid)message="请输入有效数字，小数点使用英文句点。";return valid;
        }
        static int CountLines(string text){int count=1;foreach(char c in text)if(c=='\n')count++;return count;}
        void RefreshPreview()
        {
            if(!program||!program.Station)return;
            if(!preview)
            {
                var go=new GameObject("ProgramTrajectoryPreview");preview=go.AddComponent<LineRenderer>();
                previewMaterial=new Material(Shader.Find("Sprites/Default"));preview.sharedMaterial=previewMaterial;
                preview.useWorldSpace=true;preview.widthMultiplier=.0017f;preview.startColor=preview.endColor=new Color(.12f,.8f,.96f,.7f);
                preview.numCornerVertices=3;preview.numCapVertices=3;
            }
            var points=new System.Collections.Generic.List<Vector3>();
            foreach(var step in program.Steps)if(step.command==LabTrajectoryCommand.Move)
                points.Add(program.Station.transform.TransformPoint(step.localPosition+Vector3.up*.001f));
            preview.positionCount=points.Count;preview.SetPositions(points.ToArray());preview.enabled=Visible;
        }
    }
}
