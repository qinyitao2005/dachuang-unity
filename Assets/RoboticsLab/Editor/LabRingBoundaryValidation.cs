using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;

namespace RoboticsLab.Editor
{
    /// <summary>Exercises the public input adapter once per real player frame.</summary>
    public static class LabRingBoundaryValidation
    {
        static IEnumerator<int> routine;
        public static int stationFilter=-1;
        static readonly List<string> report=new List<string>();
        static LabTrainingApp app;
        static LabRobotStation station;
        static bool appWasEnabled,loopInstalled,steering,axisSteering;
        static Vector3 direction,waypoint;
        static int frames,blockedFrames,axisCursor;
        static readonly int[] axisFrames=new int[3];
        static float simulatedSeconds,minDt,maxDt,maxVisualError,maxApertureDistance,maxOverrun,minRimGap;
        const string ReportPath="Documentation/RingBoundaryValidation.txt";

        [MenuItem("Robot Lab/Diagnostics/Validate Ring Boundaries")]
        public static void Run()
        {
            if(routine!=null)throw new Exception("Ring boundary validation is already running.");
            if(!EditorApplication.isPlaying)throw new Exception("Play Mode required.");
            app=UnityEngine.Object.FindObjectOfType<LabTrainingApp>();
            if(!app||!app.QuizPassed)throw new Exception("Complete the learning integration validation first.");
            report.Clear();report.Add("Ring boundary validation: "+DateTime.Now.ToString("s"));
            report.Add("Public MoveTool only; one call per real Update; input integrates the actual Time.deltaTime. No direct target writes.");
            appWasEnabled=app.enabled;
            // Disable the app's zero-input Update during this test so its normal input adapter
            // cannot issue a second MoveTool call in the same frame. All station physics remains live.
            app.enabled=false;
            var loop=PlayerLoop.GetCurrentPlayerLoop();
            if(!Install(ref loop)){app.enabled=appWasEnabled;throw new Exception("Could not install the Update input driver.");}
            PlayerLoop.SetPlayerLoop(loop);loopInstalled=true;
            routine=Checks();EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }

        static bool Install(ref PlayerLoopSystem loop)
        {
            if(loop.type==typeof(UnityEngine.PlayerLoop.Update))
            {
                var children=new List<PlayerLoopSystem>(loop.subSystemList??new PlayerLoopSystem[0]);
                children.Add(new PlayerLoopSystem{type=typeof(LabRingBoundaryValidation),updateDelegate=Drive});
                loop.subSystemList=children.ToArray();return true;
            }
            if(loop.subSystemList==null)return false;
            for(int i=0;i<loop.subSystemList.Length;i++)if(Install(ref loop.subSystemList[i]))return true;
            return false;
        }
        static void Remove(ref PlayerLoopSystem loop)
        {
            if(loop.subSystemList==null)return;
            var children=new List<PlayerLoopSystem>();
            foreach(var original in loop.subSystemList)
            {
                if(original.type==typeof(LabRingBoundaryValidation))continue;
                var child=original;Remove(ref child);children.Add(child);
            }
            loop.subSystemList=children.ToArray();
        }
        static void Drive()
        {
            if(!EditorApplication.isPlaying||!station)return;
            RecordPose();
            Vector3 input=direction;
            if(steering)
            {
                Vector3 delta=waypoint-station.toolTarget.localPosition;
                input=station.transform.TransformDirection(delta)/Mathf.Max(.000001f,station.moveSpeed*Time.deltaTime);
                if(axisSteering)
                {
                    Vector3 single=Vector3.zero;
                    for(int n=0;n<3;n++)
                    {
                        int axis=(axisCursor+n)%3;
                        if(Mathf.Abs(input[axis])<.008f)continue;
                        single[axis]=input[axis];axisFrames[axis]++;axisCursor=(axis+1)%3;break;
                    }
                    input=single;
                }
            }
            station.MoveTool(Vector3.ClampMagnitude(input,1));
            if(station.RingMovementBlocked)blockedFrames++;
            frames++;simulatedSeconds+=Time.deltaTime;
            minDt=Mathf.Min(minDt,Time.deltaTime);maxDt=Mathf.Max(maxDt,Time.deltaTime);
        }
        static void Tick()
        {
            try
            {
                if(!EditorApplication.isPlaying)throw new Exception("Play Mode stopped before validation finished.");
                if(routine!=null&&routine.MoveNext())return;
            }
            catch(Exception e){report.Add("FAIL: "+e.Message);Debug.LogException(e);}
            Cleanup();
            Directory.CreateDirectory("Documentation");File.WriteAllLines(ReportPath,report);
            Debug.Log("ROBOT_LAB_RING_BOUNDARY_VALIDATION "+string.Join(" | ",report));
        }
        static void Cleanup()
        {
            EditorApplication.update-=Tick;routine=null;direction=Vector3.zero;steering=false;
            if(station){station.ResetExercise();station.EndOperation();}station=null;
            if(app){app.ClosePanel();app.enabled=appWasEnabled;}app=null;
            if(loopInstalled){var loop=PlayerLoop.GetCurrentPlayerLoop();Remove(ref loop);PlayerLoop.SetPlayerLoop(loop);loopInstalled=false;}
        }

        static IEnumerator<int> Checks()
        {
            foreach(var next in app.stations)
            {
                if(stationFilter>=0&&next.stationIndex!=stationFilter)continue;
                app.LastMode=1;
                if(!app.TryStartStation(next.stationIndex))throw new Exception("Could not start station "+next.stationIndex);
                station=next;station.SetMode(1);station.ResetExercise();
                frames=blockedFrames=0;simulatedSeconds=0;minDt=float.PositiveInfinity;maxDt=0;
                maxVisualError=maxApertureDistance=maxOverrun=0;minRimGap=float.PositiveInfinity;
                direction=Vector3.zero;steering=false;
                foreach(var wait in WaitFrames(2))yield return wait;
                int contacts=station.Contacts,blocked=blockedFrames;
                direction=-WorldTangent(0);
                foreach(var wait in WaitSeconds(.65f))yield return wait;
                Stop();RecordPose();
                Require(blockedFrames>blocked&&EndpointOverrun()<=.0005f,"start reverse is capped");
                Require(station.Contacts==contacts,"start cap does not count a wire contact");

                for(int i=0;i<=10;i++)foreach(var wait in Seek(station.wirePoints[i]))yield return wait;
                Vector3 localTangent=(station.wirePoints[11]-station.wirePoints[10]).normalized;
                Vector3 localRadial=Vector3.Cross(localTangent,Vector3.up).normalized;
                if(localRadial.sqrMagnitude<.5f)localRadial=Vector3.right;
                direction=station.transform.TransformDirection(localRadial);
                contacts=station.Contacts;blocked=blockedFrames;
                foreach(var wait in WaitSeconds(1f))yield return wait;
                Stop();RecordPose();
                report.Add("LATERAL: "+station.DescribeRingConstraint()+" contacts="+station.Contacts+" robot="+station.RobotWireBlocked+" reason="+station.RobotCollisionReason);
                Require(blockedFrames>blocked&&station.Contacts>contacts,"lateral escape is blocked and counts contact");
                Require(ApertureDistance()<=station.ringRadius-station.ringTubeRadius-station.wireRadius+.0003f,"wire remains inside the aperture");
                int heldContacts=station.Contacts;
                direction=station.transform.TransformDirection(localRadial);
                foreach(var wait in WaitSeconds(.4f))yield return wait;
                Stop();Require(station.Contacts==heldContacts,"holding against the same rim does not repeatedly count contact");
                Vector3 before=station.ActualToolPosition;float outsideDistance=ApertureDistance();
                direction=-station.transform.TransformDirection(localRadial);
                foreach(var wait in WaitSeconds(.15f))yield return wait;
                Stop();
                Require(Vector3.Distance(before,station.ActualToolPosition)>.002f&&ApertureDistance()<outsideDistance-.002f,"reverse input releases rim contact");

                station.ResetExercise();Stop();
                Require(station.Progress==0&&station.Contacts==0&&!station.RingMovementBlocked&&!station.ToolInContact,"reset clears ring progress, contact and limits");
                foreach(var wait in WaitFrames(2))yield return wait;
                int cleanContacts=station.Contacts;
                for(int i=0;i<station.wirePoints.Length;i++)foreach(var wait in Seek(station.wirePoints[i]))yield return wait;
                Stop();foreach(var wait in WaitFrames(2))yield return wait;
                Require(station.Progress>.999f,"complete legal path reaches 100 percent");
                Require(station.Contacts==cleanContacts,"centered legal path remains contact-free");
                contacts=station.Contacts;blocked=blockedFrames;
                direction=WorldTangent(station.wirePoints.Length-2);
                foreach(var wait in WaitSeconds(.8f))yield return wait;
                Stop();RecordPose();
                Require(blockedFrames>blocked&&EndpointOverrun()<=.0005f,"end overshoot is capped");
                Require(station.Contacts==contacts,"end cap does not count a wire contact");
                before=station.ActualToolPosition;
                direction=-WorldTangent(station.wirePoints.Length-2);
                foreach(var wait in WaitSeconds(.16f))yield return wait;
                Stop();
                Require(Vector3.Distance(before,station.ActualToolPosition)>.002f&&station.Progress>.999f,"reverse movement from finish remains available and preserves completion");

                station.SetEmergencyStop(true);before=station.ActualToolPosition;Vector3 target=station.toolTarget.position;
                direction=-WorldTangent(station.wirePoints.Length-2);
                foreach(var wait in WaitSeconds(.5f))yield return wait;
                Stop();
                Require(Vector3.Distance(before,station.ActualToolPosition)<.000001f&&Vector3.Distance(target,station.toolTarget.position)<.000001f,"emergency stop freezes target and actual tool");
                station.SetEmergencyStop(false);
                // The preceding timed retreat can already land near the sixth-last point
                // at low frame rates. Choose a genuinely distinct target for this check.
                foreach(var wait in Seek(station.wirePoints[station.wirePoints.Length-20]))yield return wait;
                Require(Vector3.Distance(before,station.ActualToolPosition)>.002f,"resume after emergency stop moves again");

                // Reproduce a human holding W at the outside of the first bend, after
                // reaching its edge with D. A centreline-only test never exercises this case.
                station.ResetExercise();Stop();
                for(int i=0;i<=15;i+=3)foreach(var wait in Seek(station.wirePoints[i]))yield return wait;
                float pathSign=Vector3.Dot(WorldTangent(0),station.transform.forward)>=0?1:-1;
                direction=station.transform.TransformDirection(Vector3.right)*pathSign;
                foreach(var wait in WaitSeconds(.7f))yield return wait;
                Stop();before=station.ActualToolPosition;blocked=blockedFrames;
                float beforeProgress=station.Progress;
                report.Add("CORNER BEFORE: "+station.DescribeRingConstraint());
                if(station.stationIndex==0)CaptureCorner("RingCornerBefore");
                direction=station.transform.TransformDirection(Vector3.forward)*pathSign;
                foreach(var wait in WaitSeconds(.8f))yield return wait;
                Stop();RecordPose();
                float advance=Vector3.Dot(station.ActualToolPosition-before,station.transform.forward*pathSign);
                report.Add("CORNER AFTER: "+station.DescribeRingConstraint());
                if(station.stationIndex==0)CaptureCorner("RingCornerAfter");
                Require(advance>.02f,"single forward axis slides around the outside bend after rim contact; advance="+advance.ToString("F5"));
                Require(station.Progress>beforeProgress,"legal movement along the rim still advances training progress");

                station.ResetExercise();Stop();axisCursor=0;Array.Clear(axisFrames,0,axisFrames.Length);
                for(int i=0;i<station.wirePoints.Length;i+=6)foreach(var wait in Seek(station.wirePoints[i],true))yield return wait;
                foreach(var wait in Seek(station.wirePoints[station.wirePoints.Length-1],true))yield return wait;
                Stop();foreach(var wait in WaitFrames(2))yield return wait;
                Require(station.Progress>.999f&&axisFrames[0]>0&&axisFrames[1]>0&&axisFrames[2]>0,"alternating single-axis X/Y/Z controls navigate all bends and finish; frames="+string.Join(",",axisFrames));

                RecordPose();
                Require(maxVisualError<.00001f,"ring visual follows the actual end effector");
                Require(maxOverrun<=.0005f&&minRimGap>=-.00015f,"all observed actual poses respect endpoint and finite-width rim bounds");
                report.Add("METRICS station="+station.stationIndex+" frames="+frames+" dt="+minDt.ToString("F4")+".."+maxDt.ToString("F4")+" maxVisualError="+maxVisualError.ToString("F6")+" maxApertureDistance="+maxApertureDistance.ToString("F6")+" maxEndpointOverrun="+maxOverrun.ToString("F6")+" minimumRimSurfaceGap="+minRimGap.ToString("F6"));
                station.ResetExercise();app.ClosePanel();station=null;
            }
            report.Add("PASS: "+(stationFilter<0?"all three stations":"selected station "+stationFilter)+" passed actual-frame ring boundary checks.");
        }
        static void Stop(){direction=Vector3.zero;steering=false;axisSteering=false;}
        static Vector3 WorldTangent(int index){return station.transform.TransformDirection(station.wirePoints[index+1]-station.wirePoints[index]).normalized;}
        static IEnumerable<int> WaitFrames(int count)
        {
            int until=frames+count;double timeout=EditorApplication.timeSinceStartup+15;
            while(frames<until){if(EditorApplication.timeSinceStartup>timeout)throw new Exception("No real player frames received.");yield return 0;}
        }
        static IEnumerable<int> WaitSeconds(float seconds)
        {
            float until=simulatedSeconds+seconds;double timeout=EditorApplication.timeSinceStartup+Mathf.Max(15,seconds*8);
            while(simulatedSeconds<until){if(EditorApplication.timeSinceStartup>timeout)throw new Exception("Timed out waiting for input frames.");yield return 0;}
        }
        static IEnumerable<int> Seek(Vector3 destination,bool singleAxis=false)
        {
            steering=true;axisSteering=singleAxis;waypoint=destination;direction=Vector3.zero;
            double timeout=EditorApplication.timeSinceStartup+10+Vector3.Distance(station.toolTarget.localPosition,destination)/Mathf.Max(.001f,station.moveSpeed)*6;
            int first=frames;
            while(frames==first||Vector3.Distance(station.toolTarget.localPosition,destination)>.0001f||Vector3.Distance(station.transform.InverseTransformPoint(station.ActualToolPosition),destination)>.00045f)
            {
                if(EditorApplication.timeSinceStartup>timeout)
                    throw new Exception("Station "+station.stationIndex+" cannot reach legal waypoint "+destination.ToString("F4")+"; target="+station.toolTarget.localPosition.ToString("F4")+" actual="+station.transform.InverseTransformPoint(station.ActualToolPosition).ToString("F4")+" blocked="+station.RingMovementBlocked+" contact="+station.ToolInContact);
                yield return 0;
            }
            Stop();
        }
        static void CaptureCorner(string name)
        {
            var go=new GameObject("RingBoundaryDiagnosticCamera");
            var camera=go.AddComponent<Camera>();camera.enabled=false;
            var target=new RenderTexture(1000,760,24);Texture2D image=null;
            RenderTexture previous=RenderTexture.active;
            try
            {
                if(app.player&&app.player.viewCamera)camera.CopyFrom(app.player.viewCamera);
                camera.enabled=false;camera.orthographic=true;camera.orthographicSize=.115f;
                camera.nearClipPlane=.01f;camera.farClipPlane=30;
                Vector3 focus=station.transform.TransformPoint(station.wirePoints[19]);
                go.transform.position=focus+station.transform.TransformDirection(new Vector3(.24f,.16f,-.20f));
                go.transform.LookAt(focus,Vector3.up);camera.targetTexture=target;camera.Render();
                RenderTexture.active=target;image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                Directory.CreateDirectory("Documentation/Screenshots");
                File.WriteAllBytes("Documentation/Screenshots/"+name+".png",image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;camera.targetTexture=null;target.Release();
                UnityEngine.Object.DestroyImmediate(target);if(image)UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(go);
            }
        }
        static void Require(bool result,string description)
        {
            string line=(result?"PASS":"FAIL")+": station "+station.stationIndex+" "+description;
            report.Add(line);if(!result)throw new Exception(line);
        }
        static float ApertureDistance()
        {
            Vector3 center=station.transform.InverseTransformPoint(station.ActualToolPosition);
            float distance=float.PositiveInfinity;
            for(int i=0;i<station.wirePoints.Length-1;i++)
            {
                Vector3 a=station.wirePoints[i],d=station.wirePoints[i+1]-a;
                float t=Mathf.Clamp01(Vector3.Dot(center-a,d)/Mathf.Max(.00000001f,d.sqrMagnitude));
                distance=Mathf.Min(distance,(a+t*d-center).magnitude);
            }
            return distance;
        }
        static float EndpointOverrun()
        {
            Vector3 c=station.transform.InverseTransformPoint(station.ActualToolPosition);var points=station.wirePoints;
            int last=points.Length-1;
            // The fixture's endpoints are separated along Z; evaluate caps only in their local neighborhoods.
            float start=Vector3.Distance(c,points[0])<station.ringRadius*1.5f?-Vector3.Dot(c-points[0],(points[1]-points[0]).normalized):0;
            float end=Vector3.Distance(c,points[last])<station.ringRadius*1.5f?Vector3.Dot(c-points[last],(points[last]-points[last-1]).normalized):0;
            return Mathf.Max(0,start,end);
        }
        static void RecordPose()
        {
            if(!station||!station.ringRoot||!station.endEffector)return;
            maxVisualError=Mathf.Max(maxVisualError,Vector3.Distance(station.ringRoot.position,station.ActualToolPosition));
            maxApertureDistance=Mathf.Max(maxApertureDistance,ApertureDistance());maxOverrun=Mathf.Max(maxOverrun,EndpointOverrun());
            float tube=station.ringTubeRadius+station.wireRadius;
            for(int i=0;i<station.wirePoints.Length-1;i++)
            {
                for(int s=0;s<=4;s++)
                {
                    Vector3 point=station.transform.TransformPoint(Vector3.Lerp(station.wirePoints[i],station.wirePoints[i+1],s/4f));
                    Vector3 q=station.ringRoot.InverseTransformPoint(point);
                    float radial=Mathf.Sqrt(q.x*q.x+q.y*q.y)-station.ringRadius;
                    float gap=Mathf.Sqrt(radial*radial+q.z*q.z)-tube;minRimGap=Mathf.Min(minRimGap,gap);
                }
            }
        }
    }
}
