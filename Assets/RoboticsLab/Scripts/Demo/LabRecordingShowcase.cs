using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>Explicit recording entry point; ordinary game startup is unchanged.</summary>
    public sealed class LabRecordingShowcase : MonoBehaviour
    {
        public LabTrainingApp App {get;private set;}
        string title="L / C 示教编程 · 慢速演示",detail="只展示第三台机械臂的示教、执行与文件操作。";
        string logPath;
        float start,markerUntil;
        Vector2 marker;
        GUIStyle titleStyle,detailStyle;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--recording-showcase")<0
                &&!Application.dataPath.Replace('\\','/').EndsWith("/RobotLabRecording_Data",StringComparison.OrdinalIgnoreCase))return;
            if(!FindObjectOfType<LabRecordingShowcase>())new GameObject("SlowPendantPresentation").AddComponent<LabRecordingShowcase>();
        }
        IEnumerator Start()
        {
            Application.runInBackground=true;Application.targetFrameRate=60;start=Time.realtimeSinceStartup;
            var root=new DirectoryInfo(Application.dataPath);
            while(root.Parent!=null&&!Directory.Exists(Path.Combine(root.FullName,"Assets")))root=root.Parent;
            var logs=Path.Combine(root.FullName,"Logs");Directory.CreateDirectory(logs);logPath=Path.Combine(logs,"RecordingShowcase.log");
            File.WriteAllText(logPath,"L/C paced showcase / "+DateTime.UtcNow.ToString("O")+"\n");
            while(!(App=FindObjectOfType<LabTrainingApp>()))yield return null;
            while(!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
            yield return Pause(2);
            // This explicitly selected presentation only demonstrates L/C; the ordinary startup retains all prerequisites.
            App.ClosePanel();App.HelmetWorn=App.LessonCompleted=App.QuizPassed=true;
            if(!App.TryStartStation(2)){Caption("无法进入画图工位",App.StateSummary);yield break;}
            Caption("L / C 示教编程 · 慢速演示","本段仅展示 03 画图工位：录点 → 三点圆弧 → 运行与单步 → 保存加载。每个步骤会停留。");
            yield return Pause(6);
            var sequence=Flatten(LabRecordingPendantFlow.Run(this));
            while(true)
            {
                bool next=false;object waiting=null;Exception failure=null;
                try{next=sequence.MoveNext();if(next)waiting=sequence.Current;}catch(Exception e){failure=e;}
                if(failure!=null){Caption("演示暂停",failure.Message);Debug.LogException(failure);yield break;}
                if(!next)break;
                yield return waiting;
            }
            Caption("L / C 编程演示完成","直线与圆弧已实际画出，运行、暂停、单步、保存加载和急停恢复均已展示。现在可以结束录制。");
        }
        public void Caption(string heading,string text)
        {
            title=heading;detail=text;
            var message=(Time.realtimeSinceStartup-start).ToString("F1")+"s | "+heading+" | "+text;
            if(logPath!=null)File.AppendAllText(logPath,message+"\n");Debug.Log("RECORDING_CUE "+message);
        }
        public IEnumerator Pause(float seconds){yield return new WaitForSeconds(seconds);}
        public void Highlight(Vector2 screenPixelPosition){marker=screenPixelPosition;markerUntil=Time.unscaledTime+1.5f;}
        static IEnumerator Flatten(IEnumerator root)
        {
            var stack=new Stack<IEnumerator>();stack.Push(root);
            while(stack.Count>0)
            {
                var current=stack.Peek();if(!current.MoveNext()){(current as IDisposable)?.Dispose();stack.Pop();continue;}
                if(current.Current is IEnumerator nested)stack.Push(nested);else yield return current.Current;
            }
        }
        void OnGUI()
        {
            if(!App)return;
            if(titleStyle==null)
            {
                var font=App.uiFont?App.uiFont:Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                titleStyle=new GUIStyle(GUI.skin.label){font=font,fontSize=23,fontStyle=FontStyle.Bold,wordWrap=true};titleStyle.normal.textColor=Color.white;
                detailStyle=new GUIStyle(titleStyle){fontSize=18,fontStyle=FontStyle.Normal};detailStyle.normal.textColor=new Color(.84f,.95f,1);
            }
            GUI.depth=-300;var matrix=GUI.matrix;var color=GUI.color;
            GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(Screen.width/1600f,Screen.height/900f,1));
            GUI.color=new Color(.035f,.09f,.14f,.97f);GUI.DrawTexture(new Rect(510,90,665,105),Texture2D.whiteTexture);
            GUI.color=new Color(.05f,.7f,.72f);GUI.DrawTexture(new Rect(510,90,5,105),Texture2D.whiteTexture);GUI.color=color;
            GUI.Label(new Rect(527,98,632,32),title,titleStyle);GUI.Label(new Rect(527,133,632,58),detail,detailStyle);
            if(Time.unscaledTime<markerUntil)
            {
                float x=marker.x*1600/Screen.width,y=(Screen.height-marker.y)*900/Screen.height;
                GUI.color=new Color(1,.78f,.1f);GUI.DrawTexture(new Rect(x-18,y-18,36,3),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(x-18,y+15,36,3),Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x-18,y-18,3,36),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(x+15,y-18,3,36),Texture2D.whiteTexture);GUI.color=color;
            }
            GUI.matrix=matrix;
        }
    }
}
