using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RoboticsLab
{
    public enum LabTrajectoryCommand { Move, Speed, Pen, Wait }
    public enum LabTrajectoryState { Ready, Running, Paused, Completed, Faulted }

    [Serializable]
    public class LabTrajectoryStep
    {
        public LabTrajectoryCommand command;
        public Vector3 localPosition;
        public float speedPercent=50;
        public bool penDown;
        public float waitSeconds;
        public int sourceLine;
        public LabTrajectoryStep Copy()=>new LabTrajectoryStep{command=command,localPosition=localPosition,speedPercent=speedPercent,penDown=penDown,waitSeconds=waitSeconds,sourceLine=sourceLine};
    }

    /// <summary>A bounded teaching language. This class never evaluates C# or launches external code.</summary>
    [DefaultExecutionOrder(1000)]
    public class LabTrajectoryProgram : MonoBehaviour
    {
        public const int MaximumSteps=256,MaximumTextLength=32768,MaximumFileBytes=131072;
        static readonly CultureInfo Invariant=CultureInfo.InvariantCulture;
        [SerializeField] List<LabTrajectoryStep> steps=new List<LabTrajectoryStep>();
        public IReadOnlyList<LabTrajectoryStep> Steps=>steps;
        public LabRobotStation Station {get;private set;}
        public LabTrajectoryState State {get;private set;}=LabTrajectoryState.Ready;
        // Paused execution retains the control reservation. The UI must suppress manual input.
        public bool IsExecuting=>State==LabTrajectoryState.Running||State==LabTrajectoryState.Paused;
        public bool IsPaused=>State==LabTrajectoryState.Paused;
        public int CurrentStepIndex {get;private set;}
        public string StatusText {get;private set;}="尚未载入程序";
        public string LastError {get;private set;}="";
        public string SourceText=>ExportText();
        public string ProgramsDirectory=>Path.Combine(Application.persistentDataPath,"Programs");
        public string LastFilePath {get;private set;}="";
        public int Count=>steps.Count;

        [Serializable] class ProgramFile
        {
            public int version=1;
            public Vector3 origin;
            public List<LabTrajectoryStep> steps;
        }
        List<LabTrajectoryStep> execution;
        int executionMode;
        bool instructionStarted,singleStep,penPrepared,programPen;
        float currentSpeed=50,waitRemaining,stepElapsed,noProgress,blockedSeconds,bestDistance,lastStationElapsed,stepTimeLimit;
        Vector3 penTarget;

        public void Bind(LabRobotStation station)
        {
            if(Station==station)return;
            Vector3 oldOrigin=Station?Station.exerciseCenter:Vector3.zero;
            bool hadStation=Station;
            Stop();Station=station;
            if(hadStation&&Station)
            {
                Vector3 change=Station.exerciseCenter-oldOrigin;
                foreach(var step in steps)if(step.command==LabTrajectoryCommand.Move)step.localPosition+=change;
            }
            StatusText=Station?"已绑定 "+Station.displayName:"未选择工位";
        }

        public bool Compile(string source)
        {
            if(!CanEdit())return false;
            List<LabTrajectoryStep> parsed;string error;
            if(!LabTrajectoryParser.TryParse(source,Station.exerciseCenter,out parsed,out error))return Error(error);
            if(!ValidateSteps(parsed,out error))return Error(error);
            steps=parsed;ResetCursor();StatusText="编译成功，共 "+steps.Count+" 步";return true;
        }

        public bool RecordCurrent(bool pen,float speed)
        {
            if(!CanEdit())return false;
            if(!Station.toolTarget)return Error("工位末端目标尚未初始化");
            if(steps.Count>=MaximumSteps)return Error("程序最多允许 256 步");
            Vector3 position=Station.toolTarget.localPosition;
            if(pen)position.y=Station.drawingHeight;
            var step=new LabTrajectoryStep{command=LabTrajectoryCommand.Move,localPosition=position,speedPercent=speed,penDown=pen};
            string error;if(!ValidateStep(step,steps.Count,out error))return Error(error);
            steps.Add(step);ResetCursor();StatusText="已记录第 "+steps.Count+" 个示教步骤";return true;
        }

        public bool UpdateStep(int index,LabTrajectoryStep replacement)
        {
            if(!CanEdit())return false;
            if(index<0||index>=steps.Count||replacement==null)return Error("步骤索引无效");
            string error;if(!ValidateStep(replacement,index,out error))return Error(error);
            steps[index]=replacement.Copy();ResetCursor();StatusText="步骤已更新";return true;
        }
        public bool DeleteStep(int index)
        {
            if(!CanEdit())return false;
            if(index<0||index>=steps.Count)return Error("步骤索引无效");
            steps.RemoveAt(index);ResetCursor();StatusText="步骤已删除";return true;
        }
        public bool MoveStep(int from,int to)
        {
            if(!CanEdit())return false;
            if(from<0||from>=steps.Count||to<0||to>=steps.Count)return Error("步骤索引无效");
            var value=steps[from];steps.RemoveAt(from);steps.Insert(to,value);ResetCursor();StatusText="步骤顺序已更新";return true;
        }

        public bool Run()=>StartExecution(true,false);
        public bool Step()
        {
            if(State==LabTrajectoryState.Running)return Error("请先暂停，再执行单步");
            if(State==LabTrajectoryState.Paused)return ContinueExecution(true);
            return StartExecution(true,true);
        }
        bool StartExecution(bool restart,bool oneStep)
        {
            if(!ReadyToOperate())return false;
            string error;if(!ValidateSteps(steps,out error))return Error(error);
            if(steps.Count==0)return Error("程序没有步骤");
            if(restart)CurrentStepIndex=0;
            execution=steps.Select(s=>s.Copy()).ToList();
            instructionStarted=false;singleStep=oneStep;programPen=false;currentSpeed=50;
            Station.SetPen(false);executionMode=Station.TrainingMode;lastStationElapsed=Station.Elapsed;
            State=LabTrajectoryState.Running;LastError="";StatusText=oneStep?"正在执行单步":"程序运行中";return true;
        }
        public void Pause()
        {
            if(State!=LabTrajectoryState.Running)return;
            State=LabTrajectoryState.Paused;if(Station)Station.SetPen(false);StatusText="程序已暂停，手动控制仍锁定";
        }
        public bool Resume()=>ContinueExecution(false);
        bool ContinueExecution(bool oneStep)
        {
            if(State!=LabTrajectoryState.Paused)return Error("程序当前未暂停");
            if(!ReadyToOperate())return false;
            if(Station.TrainingMode!=executionMode){Stop();return Error("训练模式已变化，请重新运行程序");}
            if(CurrentStepIndex>=execution.Count){State=LabTrajectoryState.Completed;StatusText="程序执行完毕";return true;}
            // Pausing lifts the pen. Re-prepare it through the motion adapter before continuing.
            singleStep=oneStep;penPrepared=false;noProgress=0;blockedSeconds=0;bestDistance=float.PositiveInfinity;
            State=LabTrajectoryState.Running;LastError="";StatusText=singleStep?"正在执行单步":"程序继续运行";return true;
        }
        public void Stop()
        {
            if(Station)Station.SetPen(false);
            execution=null;ResetCursor();StatusText="程序已停止，步骤保留";
        }
        public void Reset(){Stop();if(Station)Station.ResetExercise();StatusText="工位已复位，程序保留";}
        void ResetCursor(){CurrentStepIndex=0;State=LabTrajectoryState.Ready;instructionStarted=false;singleStep=false;LastError="";}

        void Update()
        {
            if(!IsExecuting)return;
            if(!Station||!Station.IsOperating){Stop();StatusText="已离开工位，程序停止";return;}
            if(Station.TrainingMode!=executionMode){Stop();StatusText="训练模式已切换，程序停止";return;}
            if(Station.Elapsed+.0001f<lastStationElapsed){Stop();StatusText="工位已复位，程序停止";return;}
            lastStationElapsed=Station.Elapsed;
            if(Station.EmergencyStopped){Pause();StatusText="急停已触发；解除急停后点击继续";return;}
            if(State!=LabTrajectoryState.Running||Time.deltaTime<=0)return;
            if(execution==null||CurrentStepIndex>=execution.Count){CompleteProgram();return;}
            var step=execution[CurrentStepIndex];
            if(!instructionStarted)
            {
                instructionStarted=true;penPrepared=false;stepElapsed=0;noProgress=0;blockedSeconds=0;bestDistance=float.PositiveInfinity;
                waitRemaining=step.waitSeconds;
                if(step.command==LabTrajectoryCommand.Move){currentSpeed=step.speedPercent;programPen=step.penDown;}
                if(step.command==LabTrajectoryCommand.Pen)programPen=step.penDown;
                penTarget=Station.toolTarget.localPosition;penTarget.y=Station.drawingHeight;
                float distance=step.command==LabTrajectoryCommand.Move?Vector3.Distance(Station.toolTarget.localPosition,step.localPosition):Mathf.Abs(penTarget.y-Station.toolTarget.localPosition.y);
                stepTimeLimit=Mathf.Max(10,distance/Mathf.Max(.00001f,Station.moveSpeed*currentSpeed*.01f)*3+8);
                StatusText=StepLabel(step)+"："+step.command;
            }
            stepElapsed+=Time.deltaTime;
            if(stepElapsed>stepTimeLimit+step.waitSeconds){Fault("执行超时，末端未能到达；请检查关节可达范围");return;}
            bool complete=false;
            switch(step.command)
            {
                case LabTrajectoryCommand.Speed:currentSpeed=step.speedPercent;complete=true;break;
                case LabTrajectoryCommand.Pen:complete=PreparePen();break;
                case LabTrajectoryCommand.Wait:
                    if(PreparePen()){waitRemaining-=Time.deltaTime;complete=waitRemaining<=0;}break;
                case LabTrajectoryCommand.Move:
                    if(PreparePen())complete=DriveTo(step.localPosition);break;
            }
            if(complete&&State==LabTrajectoryState.Running)
            {
                CurrentStepIndex++;instructionStarted=false;
                if(CurrentStepIndex>=execution.Count)CompleteProgram();
                else if(singleStep){Pause();StatusText="单步完成，下一步："+(CurrentStepIndex+1);}
            }
        }

        bool PreparePen()
        {
            if(penPrepared)return true;
            if(!programPen){Station.SetPen(false);penPrepared=true;return true;}
            if(Station.TrainingMode!=0){Fault("套圈模式不能落笔");return false;}
            if(!DriveTo(penTarget))return false;
            Station.SetPen(true);penPrepared=true;bestDistance=float.PositiveInfinity;noProgress=0;return true;
        }

        bool DriveTo(Vector3 destination)
        {
            Vector3 delta=destination-Station.toolTarget.localPosition;
            float targetDistance=delta.magnitude;
            // In ring mode the taught point is the ring centre, not its rim-gripping TCP.
            float actualDistance=Vector3.Distance(Station.ActualToolPosition,Station.transform.TransformPoint(destination));
            float tipTolerance=Station.TrainingMode==1?.0004f:.0025f;
            if(targetDistance<.0002f&&actualDistance<tipTolerance)return true;
            Vector3 before=Station.toolTarget.localPosition;
            float fraction=Mathf.Min(currentSpeed*.01f,targetDistance/Mathf.Max(.0000001f,Station.moveSpeed*Time.deltaTime));
            Station.MoveTool(Station.transform.TransformDirection(delta.normalized)*fraction);
            float remaining=Vector3.Distance(Station.toolTarget.localPosition,destination)
                +Vector3.Distance(Station.ActualToolPosition,Station.transform.TransformPoint(destination));
            if(remaining<bestDistance-.000025f){bestDistance=remaining;noProgress=0;}else noProgress+=Time.deltaTime;
            bool blocked=Station.TrainingMode==1&&Station.RingMovementBlocked;
            if(blocked&&Vector3.Distance(before,Station.toolTarget.localPosition)<.000005f)blockedSeconds+=Time.deltaTime;else blockedSeconds=0;
            if(blockedSeconds>.35f){Fault(BlockedMotionMessage());return false;}
            if(noProgress>2f){Fault(Station.RobotWireBlocked?BlockedMotionMessage():"练习工具停止前进，目标超出可达范围或受到边界限制");return false;}
            return false;
        }
        string BlockedMotionMessage()
        {
            if(Station.RobotWireBlocked)
                return "机械臂受到钢丝碰撞边界阻挡，目标不可达"
                    +(string.IsNullOrEmpty(Station.RobotCollisionReason)?"":"（"+Station.RobotCollisionReason+"）")+"；调整此步后重试";
            return "受到钢丝圆环或端点边界阻挡，目标不可达；调整此步后重试";
        }
        void CompleteProgram(){State=LabTrajectoryState.Completed;Station.SetPen(false);StatusText="程序执行完毕，共 "+steps.Count+" 步";LastError="";singleStep=false;}
        void Fault(string message){State=LabTrajectoryState.Faulted;if(Station)Station.SetPen(false);LastError=StepLabel(execution[CurrentStepIndex])+"："+message;StatusText=LastError;}
        void OnDisable(){if(IsExecuting)Stop();}
        bool ReadyToOperate()
        {
            if(!Station||!Station.IsOperating||!Station.toolTarget||!Station.endEffector)return Error("请先进入已解锁的机械臂工位");
            if(Station.IsTracing)return Error("01 工位只使用专用循迹控制，请在循迹面板选择图形");
            if(Station.EmergencyStopped)return Error("请先解除急停");
            return true;
        }
        bool CanEdit(){if(!Station)return Error("请先选择工位");if(Station.IsTracing)return Error("01 工位使用专用循迹面板");if(IsExecuting)return Error("请先停止程序，再编辑或载入");return true;}
        bool Error(string message){LastError=message;StatusText=message;return false;}
        string StepLabel(LabTrajectoryStep step)=>"第 "+(CurrentStepIndex+1)+" 步"+(step.sourceLine>0?"（第 "+step.sourceLine+" 行）":"");

        bool ValidateSteps(IList<LabTrajectoryStep> candidate,out string error)
        {
            error="";
            if(candidate==null||candidate.Count>MaximumSteps){error="程序最多允许 256 步";return false;}
            for(int i=0;i<candidate.Count;i++)if(!ValidateStep(candidate[i],i,out error))return false;
            return true;
        }
        bool ValidateStep(LabTrajectoryStep step,int index,out string error)
        {
            string prefix=step!=null&&step.sourceLine>0?"第 "+step.sourceLine+" 行：":"第 "+(index+1)+" 步：";
            error="";
            if(step==null||!Enum.IsDefined(typeof(LabTrajectoryCommand),step.command)){error=prefix+"未知指令";return false;}
            if(!Finite(step.speedPercent)||step.speedPercent<1||step.speedPercent>100){error=prefix+"速度必须为 1–100%";return false;}
            if(!Finite(step.waitSeconds)||step.waitSeconds<0||step.waitSeconds>60){error=prefix+"等待时间必须为 0–60 秒";return false;}
            if((step.command==LabTrajectoryCommand.Move||step.command==LabTrajectoryCommand.Pen)&&step.penDown&&Station.TrainingMode!=0){error=prefix+"套圈模式不能使用 PEN DOWN";return false;}
            if(step.command==LabTrajectoryCommand.Move)
            {
                Vector3 p=step.localPosition,c=Station.exerciseCenter,h=Station.workspaceHalfSize;
                float minimumY=Station.TrainingMode==0?Mathf.Max(Station.drawingHeight,c.y-h.y):c.y-h.y;
                if(!Finite(p.x)||!Finite(p.y)||!Finite(p.z)||p.x<c.x-h.x-.00001f||p.x>c.x+h.x+.00001f||p.y<minimumY-.00001f||p.y>c.y+h.y+.00001f||p.z<c.z-h.z-.00001f||p.z>c.z+h.z+.00001f)
                {error=prefix+"MOVE 超出当前工位工作范围";return false;}
                if(step.penDown&&Mathf.Abs(p.y-Station.drawingHeight)>.0002f){error=prefix+"落笔点的 Y 必须位于绘图平面（相对中心 "+((Station.drawingHeight-c.y)*1000).ToString("0.###",Invariant)+" mm）";return false;}
            }
            return true;
        }
        internal static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);

        public string ExportText()
        {
            if(!Station)return "";
            var text=new StringBuilder("# MOVE: relative to exercise centre, millimetres\n");
            foreach(var step in steps)
            {
                switch(step.command)
                {
                    case LabTrajectoryCommand.Speed:text.Append("SPEED ").Append(F(step.speedPercent));break;
                    case LabTrajectoryCommand.Pen:text.Append("PEN ").Append(step.penDown?"DOWN":"UP");break;
                    case LabTrajectoryCommand.Wait:text.Append("WAIT ").Append(F(step.waitSeconds));break;
                    case LabTrajectoryCommand.Move:
                        Vector3 p=(step.localPosition-Station.exerciseCenter)*1000;
                        text.Append("MOVE ").Append(F(p.x)).Append(' ').Append(F(p.y)).Append(' ').Append(F(p.z))
                            .Append(" SPEED ").Append(F(step.speedPercent)).Append(" PEN ").Append(step.penDown?"DOWN":"UP");break;
                }
                text.Append('\n');
            }
            return text.ToString();
        }
        public string ExampleText(bool triangle=false)
        {
            if(!Station)return "";
            var text=new StringBuilder("SPEED 60\nPEN UP\n");
            if(Station.TrainingMode==1)
            {
                if(Station.wirePoints!=null)foreach(Vector3 point in Station.wirePoints)
                {Vector3 p=(point-Station.exerciseCenter)*1000;text.Append("MOVE ").Append(F(p.x)).Append(' ').Append(F(p.y)).Append(' ').Append(F(p.z)).Append('\n');}
                return text.ToString();
            }
            float x=Mathf.Min(.10f*Station.exerciseScale,Station.workspaceHalfSize.x*.4f)*1000;
            float z=Mathf.Min(.13f*Station.exerciseScale,Station.workspaceHalfSize.z*.4f)*1000;
            float y=(Station.drawingHeight-Station.exerciseCenter.y)*1000;
            text.Append("MOVE ").Append(F(-x)).Append(' ').Append(F(y)).Append(' ').Append(F(-z)).Append("\nPEN DOWN\n");
            Vector2[] points=triangle?new[]{new Vector2(x,-z),new Vector2(0,z),new Vector2(-x,-z)}:
                new[]{new Vector2(x,-z),new Vector2(x,z),new Vector2(-x,z),new Vector2(-x,-z)};
            foreach(var p in points)text.Append("MOVE ").Append(F(p.x)).Append(' ').Append(F(y)).Append(' ').Append(F(p.y)).Append('\n');
            text.Append("PEN UP\nWAIT 0.2\n");return text.ToString();
        }
        static string F(float value)=>Math.Round(value,3,MidpointRounding.AwayFromZero).ToString("0.###",Invariant);

        public bool Save(string name)
        {
            if(!Station)return Error("请先选择工位");
            string path,error;if(!TryFilePath(name,out path,out error))return Error(error);
            string temporary=null;
            try
            {
                if(!ValidateSteps(steps,out error))return Error(error);
                Directory.CreateDirectory(ProgramsDirectory);
                string json=JsonUtility.ToJson(new ProgramFile{origin=Station.exerciseCenter,steps=steps.Select(s=>s.Copy()).ToList()},true);
                temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                File.WriteAllText(temporary,json,new UTF8Encoding(false));
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
                LastFilePath=path;LastError="";StatusText="已保存："+path;return true;
            }
            catch(Exception e){return Error("保存失败："+e.Message);}
            finally{if(temporary!=null&&File.Exists(temporary))File.Delete(temporary);}
        }
        public bool Load(string name)
        {
            if(!CanEdit())return false;
            string path,error;if(!TryFilePath(name,out path,out error))return Error(error);
            try
            {
                if(!File.Exists(path))return Error("程序文件不存在");
                if(new FileInfo(path).Length>MaximumFileBytes)return Error("程序文件超过 128 KiB 限制");
                var data=JsonUtility.FromJson<ProgramFile>(File.ReadAllText(path));
                if(data==null||data.version!=1||data.steps==null||!Finite(data.origin.x)||!Finite(data.origin.y)||!Finite(data.origin.z))return Error("程序 JSON 格式或版本无效");
                var candidate=new List<LabTrajectoryStep>();
                foreach(var item in data.steps)
                {
                    if(item==null)return Error("程序包含空步骤");
                    var step=item.Copy();if(step.command==LabTrajectoryCommand.Move)step.localPosition+=Station.exerciseCenter-data.origin;
                    candidate.Add(step);
                }
                if(!ValidateSteps(candidate,out error))return Error(error);
                steps=candidate;ResetCursor();LastFilePath=path;StatusText="已载入："+path;return true;
            }
            catch(Exception e){return Error("载入失败："+e.Message);}
        }
        public string[] SavedProgramNames()
        {
            try{return Directory.Exists(ProgramsDirectory)?Directory.GetFiles(ProgramsDirectory,"*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(x=>x).ToArray():new string[0];}
            catch(Exception e){Error("读取程序列表失败："+e.Message);return new string[0];}
        }
        bool TryFilePath(string name,out string path,out string error)
        {
            path="";error="";name=(name??"").Trim();
            if(name.EndsWith(".json",StringComparison.OrdinalIgnoreCase))name=name.Substring(0,name.Length-5);
            if(name.Length==0||name.Length>48||name.Contains("..")||name.IndexOfAny(new[]{'/','\\',':'})>=0||Path.IsPathRooted(name))
            {error="文件名需为 1–48 字，不可包含路径或 ..";return false;}
            string clean=new string(name.Select(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-'||c==' '?c:'_').ToArray()).Trim();
            if(string.IsNullOrEmpty(clean)){error="文件名无效";return false;}
            string upper=clean.ToUpperInvariant();
            if(new[]{"CON","PRN","AUX","NUL"}.Contains(upper)||System.Text.RegularExpressions.Regex.IsMatch(upper,"^(COM|LPT)[1-9]$"))clean="Program_"+clean;
            string directory=Path.GetFullPath(ProgramsDirectory)+Path.DirectorySeparatorChar;
            path=Path.GetFullPath(Path.Combine(directory,clean+".json"));
            if(!path.StartsWith(directory,StringComparison.OrdinalIgnoreCase)){error="程序路径必须位于 Programs 文件夹";return false;}
            return true;
        }
    }

    public static class LabTrajectoryParser
    {
        static readonly CultureInfo Invariant=CultureInfo.InvariantCulture;
        public static bool TryParse(string source,Vector3 origin,out List<LabTrajectoryStep> result,out string error)
        {
            result=null;error="";
            if(source==null||source.Length>LabTrajectoryProgram.MaximumTextLength){error="文本不可超过 32768 字符";return false;}
            var parsed=new List<LabTrajectoryStep>();float speed=50;bool pen=false;
            string[] lines=source.Replace("\r","").Split('\n');
            for(int i=0;i<lines.Length;i++)
            {
                string line=lines[i];int comment=line.IndexOf('#'),slash=line.IndexOf("//",StringComparison.Ordinal);
                if(slash>=0&&(comment<0||slash<comment))comment=slash;if(comment>=0)line=line.Substring(0,comment);
                var words=line.Split((char[])null,StringSplitOptions.RemoveEmptyEntries);if(words.Length==0)continue;
                string prefix="第 "+(i+1)+" 行：";
                if(parsed.Count>=LabTrajectoryProgram.MaximumSteps){error=prefix+"程序最多允许 256 步";return false;}
                var step=new LabTrajectoryStep{sourceLine=i+1,speedPercent=speed,penDown=pen};float value;
                switch(words[0].ToUpperInvariant())
                {
                    case "SPEED":
                        if(words.Length!=2||!Number(words[1],out value)||value<1||value>100){error=prefix+"用法 SPEED 1..100";return false;}
                        step.command=LabTrajectoryCommand.Speed;speed=value;step.speedPercent=speed;break;
                    case "PEN":
                        if(words.Length!=2||!Pen(words[1],out pen)){error=prefix+"用法 PEN UP 或 PEN DOWN";return false;}
                        step.command=LabTrajectoryCommand.Pen;step.penDown=pen;break;
                    case "WAIT":
                        if(words.Length!=2||!Number(words[1],out value)||value<0||value>60){error=prefix+"用法 WAIT 0..60（秒）";return false;}
                        step.command=LabTrajectoryCommand.Wait;step.waitSeconds=value;break;
                    case "MOVE":
                        float x,y,z;
                        if(words.Length<4||!Number(words[1],out x)||!Number(words[2],out y)||!Number(words[3],out z)){error=prefix+"用法 MOVE x y z（相对中心，毫米）";return false;}
                        step.command=LabTrajectoryCommand.Move;step.localPosition=origin+new Vector3(x,y,z)*.001f;
                        bool hasSpeed=false,hasPen=false;
                        for(int w=4;w<words.Length;w+=2)
                        {
                            if(w+1>=words.Length){error=prefix+"MOVE 内联参数应为 SPEED 数值 或 PEN UP|DOWN";return false;}
                            if(words[w].Equals("SPEED",StringComparison.OrdinalIgnoreCase)&&!hasSpeed&&Number(words[w+1],out value)&&value>=1&&value<=100)
                            {step.speedPercent=value;hasSpeed=true;}
                            else if(words[w].Equals("PEN",StringComparison.OrdinalIgnoreCase)&&!hasPen&&Pen(words[w+1],out step.penDown))hasPen=true;
                            else{error=prefix+"MOVE 内联速度/笔状态无效或重复";return false;}
                        }
                        break;
                    default:error=prefix+"未知指令 "+words[0]+"；仅支持 SPEED / MOVE / PEN / WAIT";return false;
                }
                parsed.Add(step);
            }
            result=parsed;return true;
        }
        static bool Number(string text,out float value)=>float.TryParse(text,NumberStyles.Float,Invariant,out value)&&LabTrajectoryProgram.Finite(value);
        static bool Pen(string text,out bool down){down=text.Equals("DOWN",StringComparison.OrdinalIgnoreCase);return down||text.Equals("UP",StringComparison.OrdinalIgnoreCase);}
    }
}
