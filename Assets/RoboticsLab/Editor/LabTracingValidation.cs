using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoboticsLab.Editor
{
    /// <summary>New explicit-assessment rules, independent of robot motion.</summary>
    public static class LabTracingValidation
    {
        [MenuItem("Robot Lab/Diagnostics/Validate Station1 Assessment")]
        public static void Run() { Debug.Log(Validate()); }
        public static string Validate()
        {
            string[] lines = CheckRules();
            Directory.CreateDirectory("Documentation");
            File.WriteAllLines("Documentation/Station1AssessmentValidation.txt", lines);
            foreach (string line in lines) if (line.StartsWith("FAIL")) throw new Exception(string.Join("\n", lines));
            return "STATION1_ASSESSMENT " + lines.Length + "/" + lines.Length + " checks passed";
        }
        public static string[] CheckRules()
        {
            var results = new List<string>();
            Action<bool,string> check = (ok,label) => results.Add((ok ? "PASS " : "FAIL ") + label);
            var history = new LabTracingSessionHistory();
            var size = new Vector2(.19f,.27f);
            for (int shape=0; shape<5; shape++)
            {
                Vector2[] path = LabTracingPaths.Create(shape,size,Vector2.zero);
                var a = new LabTracingAssessment(path,shape,history);
                a.Tick(path[0],0);
                check(a.State==LabTracingAssessment.Phase.Ready && a.Score==0,"Shape "+shape+": FWD/movement alone cannot start scoring");
                check(a.Arm() && !a.Arm(),"Shape "+shape+": explicit start arms once");
                a.Tick(path[0],.020f); a.Tick(path[0],.010f);
                check(a.State==LabTracingAssessment.Phase.Waiting,"Shape "+shape+": safe height and exactly 10 mm wait");
                foreach (Vector2 point in path) a.Tick(point,.009f);
                check(a.HasResult && a.Completed && a.Score>99.9f,"Shape "+shape+": ideal course auto-finishes with full score");
                check(history.Attempts(shape)==1 && history.Best(shape)>99.9f,"Shape "+shape+": result enters the correct history row");
                a.Tick(path[0],0); a.Tick(path[0],.020f);
                check(history.Attempts(shape)==1 && a.Score>99.9f,"Shape "+shape+": result freezes without duplicate history");
            }
            Vector2[] j=LabTracingPaths.Create(0,size,Vector2.zero);
            var partial=new LabTracingAssessment(j,0,history);partial.Arm();
            for(int i=0;i<j.Length/4;i++)partial.Tick(j[i],.005f);
            partial.Tick(j[j.Length/4-1],.010f);
            check(partial.State==LabTracingAssessment.Phase.Sampling,"Exactly 10 mm keeps active assessment sampling");
            partial.Tick(j[j.Length/4-1],.0101f);
            check(partial.HasResult && !partial.Completed && partial.Score>15 && partial.Score<35,"Lift before completion settles a partial score");
            check(history.Attempts(0)==2 && history.Best(0)>99.9f,"Lower score increases attempts but cannot replace a best score");
            var reset=new LabTracingAssessment(j,0,history);
            check(reset.Score==0 && !reset.Active && history.Best(0)>99.9f,"New/reset attempt clears current work, preserves best");
            reset.Arm();reset.Tick(j[0],0);reset.Cancel();reset.Tick(j[1],.020f);
            check(!reset.HasResult && history.Attempts(0)==2,"Cancelling/resetting does not publish an incomplete result");
            var stationary=new LabTracingAssessment(j,0);stationary.Arm();
            for(int i=0;i<1000;i++)stationary.Tick(j[0],0);
            check(stationary.Coverage<1 && stationary.Score==0 && !stationary.HasResult,"Standing still cannot earn coverage, score or finish");
            var jump=new LabTracingAssessment(j,0);jump.Arm();jump.Tick(j[0],0);jump.Tick(j[j.Length-1],0);
            check(jump.Coverage<1 && !jump.Completed,"Teleport from start to end cannot finish");
            var circle=LabTracingPaths.Create(1,size,Vector2.zero);
            var reverse=new LabTracingAssessment(circle,1);reverse.Arm();
            for(int i=circle.Length-1;i>=0;i--)reverse.Tick(circle[i],0);
            check(reverse.Coverage<10 && !reverse.Completed,"Reverse traversal cannot bypass course order");
            var off=new LabTracingAssessment(j,0);off.Arm();foreach(Vector2 point in j)off.Tick(point+new Vector2(.012f,0),0);
            off.Tick(j[j.Length-1],.02f);
            check(off.Score<35 && off.HasResult,"Far-off course lowers score; early lift still settles");
            var line=new Vector2[101];for(int i=0;i<line.Length;i++)line[i]=new Vector2(i*.001f,0);
            var approximate=new LabTracingAssessment(line,0);approximate.Arm();
            foreach(Vector2 point in line)approximate.Tick(point+Vector2.up*.003f,0);
            check(approximate.Completed && Mathf.Abs(approximate.Accuracy-60)<.1f,"3 mm offset can complete with 60 percent accuracy");
            var close=new LabTracingAssessment(circle,1);close.Arm();close.Tick(circle[0],0);
            check(!close.Completed && close.Coverage<1,"Closed course start/end overlap cannot instantly finish");
            var pause=new LabTracingAssessment(j,0);pause.Arm();for(int i=0;i<j.Length/4;i++)pause.Tick(j[i],0);
            float before=pause.Score;for(int i=0;i<1000;i++)pause.Tick(j[j.Length/4-1],0);
            check(Mathf.Abs(before-pause.Score)<.0001f,"Paused samples do not improve accuracy or coverage");
            var invalid=new LabTracingAssessment(j,0);invalid.Arm();invalid.Tick(new Vector2(float.NaN,0),0);invalid.Tick(j[0],float.NaN);
            check(invalid.State==LabTracingAssessment.Phase.Waiting,"Invalid coordinates cannot start or corrupt assessment");
            history.Clear();bool empty=true;for(int i=0;i<5;i++)empty &= history.Attempts(i)==0 && history.Best(i)==0;
            check(empty,"New game clears every shape and attempt count");
            return results.ToArray();
        }
    }
}
