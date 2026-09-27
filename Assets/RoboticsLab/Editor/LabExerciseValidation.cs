using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RoboticsLab.Editor
{
    public static class LabExerciseValidation
    {
        static IEnumerator<int> routine;
        static List<string> report;
        [MenuItem("Robot Lab/Diagnostics/Validate Drawing And Ring")]
        public static void Run()
        {
            if(!EditorApplication.isPlaying)throw new Exception("Play Mode required");
            report=new List<string>();routine=Checks();EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            try{if(routine!=null&&routine.MoveNext())return;}
            catch(Exception e){report.Add("FAIL: "+e.Message);Debug.LogException(e);}
            EditorApplication.update-=Tick;routine=null;File.WriteAllLines("Documentation/ExerciseValidation.txt",report);
            Debug.Log("ROBOT_LAB_EXERCISE_VALIDATION "+string.Join(" | ",report));
        }
        static IEnumerator<int> Checks()
        {
            var app=UnityEngine.Object.FindObjectOfType<LabTrainingApp>();
            if(!app || !app.QuizPassed)throw new Exception("Run the learning integration validation first.");
            app.LessonCompleted=false;app.lessonVideo.time=app.lessonVideo.length-1;app.lessonVideo.Play();
            double videoUntil=EditorApplication.timeSinceStartup+2;
            while(EditorApplication.timeSinceStartup<videoUntil)yield return 0;
            report.Add((app.LessonCompleted?"PASS":"FAIL")+": video completion event unlocks the next learning step");
            foreach(var station in app.stations)
            {
                app.LastMode=0;app.TryStartStation(station.stationIndex);station.SetMode(0);station.SetPen(true);
                var c=station.exerciseCenter;float maxError=0;
                for(int i=0;i<=64;i++)
                {
                    float angle=i/64f*Mathf.PI*2;
                    station.toolTarget.localPosition=new Vector3(c.x+.15f*station.exerciseScale*Mathf.Cos(angle),station.drawingHeight,c.z+.23f*station.exerciseScale*Mathf.Sin(angle));
                    double until=EditorApplication.timeSinceStartup+.06;
                    while(EditorApplication.timeSinceStartup<until)yield return 0;
                    maxError=Mathf.Max(maxError,station.PositionError);
                }
                int vertices=0;foreach(var line in station.GetComponentsInChildren<LineRenderer>())if(line.name=="DrawingStroke")vertices+=line.positionCount;
                report.Add((vertices>30?"PASS":"FAIL")+": station "+station.stationIndex+" drawing vertices="+vertices+", progress="+station.Progress.ToString("F3")+", max target error="+maxError.ToString("F4"));
                if(station.stationIndex==0)
                {
                    ScreenCapture.CaptureScreenshot("Documentation/Screenshots/DrawingPractice.png");
                    double shot=EditorApplication.timeSinceStartup+.25;while(EditorApplication.timeSinceStartup<shot)yield return 0;
                }
                app.LastMode=1;station.SetMode(1);maxError=0;
                for(int i=0;i<station.wirePoints.Length;i++)
                {
                    station.toolTarget.localPosition=station.wirePoints[i];
                    double until=EditorApplication.timeSinceStartup+.06;
                    while(EditorApplication.timeSinceStartup<until)yield return 0;
                    maxError=Mathf.Max(maxError,station.PositionError);
                }
                report.Add((station.Progress>.96f?"PASS":"FAIL")+": station "+station.stationIndex+" centered ring guide progress="+station.Progress.ToString("F3")+", contacts="+station.Contacts+", max target error="+maxError.ToString("F4"));
                if(station.stationIndex==0)
                {
                    ScreenCapture.CaptureScreenshot("Documentation/Screenshots/RingPractice.png");
                    double shot=EditorApplication.timeSinceStartup+.25;while(EditorApplication.timeSinceStartup<shot)yield return 0;
                }
                station.ResetExercise();
                double settle=EditorApplication.timeSinceStartup+.5;while(EditorApplication.timeSinceStartup<settle)yield return 0;
                int before=station.Contacts;
                Vector3 radial=station.transform.InverseTransformDirection(station.ringRoot.up)*station.ringRadius;
                for(int i=0;i<=18;i++)
                {
                    station.toolTarget.localPosition=station.wirePoints[1]+radial*(i/18f);
                    double until=EditorApplication.timeSinceStartup+.07;while(EditorApplication.timeSinceStartup<until)yield return 0;
                }
                report.Add((station.Contacts>before?"PASS":"FAIL")+": station "+station.stationIndex+" intentional rim contact counted="+(station.Contacts-before));
                station.ResetExercise();app.ClosePanel();
            }
            app.player.Teleport(new Vector3(0,.1f,2.1f),0);app.ClosePanel();
        }
    }
}
