using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using Object=UnityEngine.Object;

namespace RoboticsLab.Editor
{
    /// <summary>Live rigid grasp and whole-arm wire checks, independent of teaching gates.</summary>
    public static class LabRingGripMotionValidation
    {
        sealed class InputPhase { }
        sealed class ObservePhase { }
        static IEnumerator<int> routine;
        static readonly List<string> report=new List<string>(),trace=new List<string>();
        static LabTrainingApp app;
        static LabRobotStation station;
        static LabRobotWireCollision collision;
        static MeshFilter leftPad,rightPad;
        static bool appWasEnabled,installed,steering;
        static string pendingFailure;
        static Vector3 waypoint;
        static int pointIndex,frames,blockedFrames;
        static float seconds,minDt,maxDt,maxGripError,maxNormalAngle,minGap,maxGap,minClearance,maxFollowingError;
        static readonly CultureInfo Invariant=CultureInfo.InvariantCulture;
        const string ReportPath="Documentation/RingGripMotionValidation.txt";
        const string TracePath="Documentation/RingGripMotionFrames.csv";

        [MenuItem("Robot Lab/Diagnostics/Validate Ring Grip Motion")]
        public static void Run()
        {
            if(routine!=null)throw new InvalidOperationException("Ring grip motion validation is already running.");
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play Mode is required.");
            if(Object.FindObjectsOfType<LabTrajectoryProgram>().Any(p=>p.IsExecuting))
                throw new InvalidOperationException("Stop the user's trajectory program before running this isolated fixture.");
            app=Object.FindObjectOfType<LabTrainingApp>();
            if(!app)throw new InvalidOperationException("Open the teaching scene first.");
            report.Clear();trace.Clear();pendingFailure=null;
            report.Add("Ring grip live motion validation / "+DateTime.Now.ToString("s"));
            report.Add("Isolated BeginOperation/SetMode/ResetExercise fixture; learning and quiz gates are NOT tested here. Their state is not changed.");
            report.Add("One public MoveTool per real Update; measurements run after all MonoBehaviour LateUpdates. No target writes or direct joint solving.");
            report.Add("Dynamic wirePoints are traversed in order. Distances are simulation world metres, not hardware accuracy.");
            trace.Add("station,point,frame,dt,elapsed,centerX,centerY,centerZ,gripError,normalAngleDegrees,jawGap,bodyClearance,bodySafe,positionError,progress,contacts,blocked,reason");
            appWasEnabled=app.enabled;app.ClosePanel();app.enabled=false;
            var loop=PlayerLoop.GetCurrentPlayerLoop();
            if(Install(ref loop)!=2){app.enabled=appWasEnabled;throw new InvalidOperationException("Could not install live input and observation phases.");}
            PlayerLoop.SetPlayerLoop(loop);installed=true;
            routine=Checks();EditorApplication.update-=Tick;EditorApplication.update+=Tick;
            AssemblyReloadEvents.beforeAssemblyReload-=Abort;AssemblyReloadEvents.beforeAssemblyReload+=Abort;
        }
        static int Install(ref PlayerLoopSystem loop)
        {
            int count=0;
            if(loop.type==typeof(UnityEngine.PlayerLoop.Update)||loop.type==typeof(UnityEngine.PlayerLoop.PreLateUpdate))
            {
                bool input=loop.type==typeof(UnityEngine.PlayerLoop.Update);
                var children=new List<PlayerLoopSystem>(loop.subSystemList??new PlayerLoopSystem[0]);
                children.Add(new PlayerLoopSystem{type=input?typeof(InputPhase):typeof(ObservePhase),updateDelegate=input?(PlayerLoopSystem.UpdateFunction)Drive:Observe});
                loop.subSystemList=children.ToArray();count++;
            }
            if(loop.subSystemList!=null)for(int i=0;i<loop.subSystemList.Length;i++)count+=Install(ref loop.subSystemList[i]);
            return count;
        }
        static void Remove(ref PlayerLoopSystem loop)
        {
            if(loop.subSystemList==null)return;
            var children=new List<PlayerLoopSystem>();
            foreach(var original in loop.subSystemList)
            {
                if(original.type==typeof(InputPhase)||original.type==typeof(ObservePhase))continue;
                var child=original;Remove(ref child);children.Add(child);
            }
            loop.subSystemList=children.ToArray();
        }
        static void Drive()
        {
            if(!station||pendingFailure!=null||!EditorApplication.isPlaying)return;
            try
            {
                Vector3 input=Vector3.zero;
                if(steering&&Time.deltaTime>0)
                {
                    Vector3 local=waypoint-station.toolTarget.localPosition;
                    input=station.transform.TransformDirection(local)/Mathf.Max(.0000001f,station.moveSpeed*Time.deltaTime);
                }
                station.MoveTool(Vector3.ClampMagnitude(input,.6f));
                if(station.RingMovementBlocked)blockedFrames++;
            }
            catch(Exception e){pendingFailure="Input driver: "+e;}
        }
        static void Observe()
        {
            if(!station||pendingFailure!=null||!EditorApplication.isPlaying)return;
            try
            {
                Vector3 center=station.ringRoot.position,tcp=station.RingGripPosition;
                float gripError=Vector3.Distance(center+station.ringRoot.up*station.ringRadius,tcp);
                float normalAngle=DirectionAngle(station.ringRoot.forward,station.ringGripTcp.right);
                float leftFace,rightFace;
                bool leftCovers=MeasureContactFace(leftPad,true,out leftFace),rightCovers=MeasureContactFace(rightPad,false,out rightFace);
                bool covers=leftCovers&&rightCovers;
                float gap=rightFace-leftFace;
                string reason;float clearance;bool safe=collision.Check(out reason,out clearance);
                frames++;seconds+=Time.deltaTime;minDt=Mathf.Min(minDt,Time.deltaTime);maxDt=Mathf.Max(maxDt,Time.deltaTime);
                maxGripError=Mathf.Max(maxGripError,gripError);maxNormalAngle=Mathf.Max(maxNormalAngle,normalAngle);
                minGap=Mathf.Min(minGap,gap);maxGap=Mathf.Max(maxGap,gap);minClearance=Mathf.Min(minClearance,clearance);
                maxFollowingError=Mathf.Max(maxFollowingError,station.PositionError);
                trace.Add(string.Join(",",new[]{station.stationIndex.ToString(),pointIndex.ToString(),Time.frameCount.ToString(),F(Time.deltaTime),F(seconds),
                    F(center.x),F(center.y),F(center.z),F(gripError),F(normalAngle),F(gap),F(clearance),safe?"true":"false",F(station.PositionError),
                    F(station.Progress),station.Contacts.ToString(),station.RingMovementBlocked?"true":"false",Csv(reason)}));
                if(gripError>.00005f)pendingFailure="Rigid rim grip separated by "+F(gripError)+" m.";
                else if(normalAngle>.01f)pendingFailure="Ring normal and actual TCP closing axis differ by "+F(normalAngle)+" degrees.";
                else if(!covers||Mathf.Abs(gap-.0072f)>.00005f||Mathf.Abs(leftFace+rightFace)>.00005f)
                    pendingFailure="Real rubber contact faces do not symmetrically hold the 7.2 mm tube: left="+F(leftFace)+", right="+F(rightFace)+", coverage="+covers;
                else if(!safe)pendingFailure="Whole-arm/held-ring collision check rejected live pose: "+reason+"; clearance="+F(clearance);
                if(pendingFailure!=null)pendingFailure="Station "+station.stationIndex+", waypoint "+pointIndex+", frame "+Time.frameCount+": "+pendingFailure;
            }
            catch(Exception e){pendingFailure="Pose observer: "+e;}
        }
        static bool MeasureContactFace(MeshFilter pad,bool left,out float face)
        {
            Transform tcp=station.ringGripTcp;Bounds bounds=pad.sharedMesh.bounds;
            float minX=float.PositiveInfinity,maxX=float.NegativeInfinity,minY=minX,maxY=maxX,minZ=minX,maxZ=maxX;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {
                Vector3 world=pad.transform.TransformPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)))-tcp.position;
                float px=Vector3.Dot(world,tcp.right),py=Vector3.Dot(world,tcp.up),pz=Vector3.Dot(world,tcp.forward);
                minX=Mathf.Min(minX,px);maxX=Mathf.Max(maxX,px);minY=Mathf.Min(minY,py);maxY=Mathf.Max(maxY,py);minZ=Mathf.Min(minZ,pz);maxZ=Mathf.Max(maxZ,pz);
            }
            face=left?maxX:minX;
            var renderer=pad.GetComponent<Renderer>();
            return pad.gameObject.activeInHierarchy&&renderer&&renderer.enabled&&Mathf.Abs(Vector3.Dot(pad.transform.right,tcp.right))>.99999f
                &&minY<=.00001f&&maxY>=-.00001f&&minZ<=.00001f&&maxZ>=-.00001f&&(left?face<0:face>0);
        }
        static IEnumerator<int> Checks()
        {
            var stations=Object.FindObjectsOfType<LabRobotStation>().OrderBy(s=>s.stationIndex).ToArray();
            Require(stations.Length==3,"Three training robots are available");
            foreach(var next in stations)
            {
                station=next;station.BeginOperation();station.SetMode(1);station.ResetExercise();
                collision=station.GetComponent<LabRobotWireCollision>();
                Require(station.ringGripTcp&&station.ringGripTool&&station.ringRoot&&collision,"Station "+station.stationIndex+" has rigid gripper and whole-body collision components");
                Require(station.wirePoints!=null&&station.wirePoints.Length>1,"Current authored guide contains ordered waypoints");
                leftPad=station.ringGripTool.Find("DistalJawShoes/Left_RubberContact")?.GetComponent<MeshFilter>();
                rightPad=station.ringGripTool.Find("DistalJawShoes/Right_RubberContact")?.GetComponent<MeshFilter>();
                Require(leftPad&&leftPad.sharedMesh&&rightPad&&rightPad.sharedMesh,"Both real rubber contact meshes are present; contact markers alone are not evidence");
                Require(Mathf.Abs(station.ringTubeRadius*2-.0072f)<.00001f,"Authored ring tube diameter is 7.2 mm");
                collision.RebuildCache();
                Require(collision.ProxyCount>0&&string.IsNullOrEmpty(collision.CacheError),"Full moving robot mesh cache is valid, proxies="+collision.ProxyCount);
                ResetMetrics();pointIndex=0;steering=false;
                int settle=Time.frameCount+3;while(Time.frameCount<settle)yield return 0;
                Capture("Start",false);
                double stationDeadline=EditorApplication.timeSinceStartup+240;
                int startContacts=station.Contacts;
                for(pointIndex=0;pointIndex<station.wirePoints.Length;pointIndex++)
                {
                    waypoint=station.wirePoints[pointIndex];steering=true;
                    double deadline=Math.Min(stationDeadline,EditorApplication.timeSinceStartup+12);
                    while(Vector3.Distance(station.toolTarget.localPosition,waypoint)>.00015f
                        ||Vector3.Distance(station.ActualToolPosition,station.transform.TransformPoint(waypoint))>.0003f)
                    {
                        if(EditorApplication.timeSinceStartup>deadline)
                            throw new InvalidOperationException("Station "+station.stationIndex+" could not reach waypoint "+pointIndex+"/"+station.wirePoints.Length
                                +" through MoveTool. "+station.RobotCollisionReason+" "+station.DescribeRingConstraint());
                        yield return 0;
                    }
                    steering=false;
                    // Require a real observed frame at every waypoint, including the initial point.
                    int observed=frames;while(frames==observed)yield return 0;
                    if(pointIndex==station.wirePoints.Length/2)Capture("Middle",false);
                }
                steering=false;settle=Time.frameCount+3;while(Time.frameCount<settle)yield return 0;
                Require(station.Progress>=.99f,"Station "+station.stationIndex+" traversed every "+station.wirePoints.Length+" authored waypoint; progress="+F(station.Progress));
                Require(station.Contacts==startContacts,"Centred full path introduced no new rim contacts");
                Require(frames>=station.wirePoints.Length,"Rigid grasp, real contact faces and full-body safety checked after LateUpdate throughout motion");
                Capture("End",false);Capture("Overview",true);AppendMetrics("COMPLETE");
                station.EndOperation();station=null;collision=null;
            }
            report.Add("PASS: All three robots completed the actual rigid-grip/full-body motion fixture.");
        }
        static void ResetMetrics()
        {
            frames=blockedFrames=0;seconds=maxDt=maxGripError=maxNormalAngle=maxGap=maxFollowingError=0;
            minDt=minGap=minClearance=float.PositiveInfinity;
        }
        static void AppendMetrics(string state)
        {
            if(!station)return;
            report.Add(state+" station="+station.stationIndex+" point="+pointIndex+"/"+station.wirePoints.Length+" frames="+frames+" simulatedSeconds="+F(seconds)
                +" dt="+F(minDt)+".."+F(maxDt)+" gripErrorMax="+F(maxGripError)+" normalAngleMax="+F(maxNormalAngle)
                +" realJawGap="+F(minGap)+".."+F(maxGap)+" bodyClearanceMin="+F(minClearance)+" followingErrorMax="+F(maxFollowingError)
                +" blockedFrames="+blockedFrames+" progress="+F(station.Progress)+" contacts="+station.Contacts);
        }
        static void Tick()
        {
            try
            {
                if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play Mode stopped before completion.");
                if(pendingFailure!=null)throw new InvalidOperationException(pendingFailure);
                if(routine!=null&&routine.MoveNext())return;
            }
            catch(Exception e){AppendMetrics("STOPPED");report.Add("FAIL: "+e.Message);Debug.LogException(e);}
            Finish();
        }
        static void Abort(){report.Add("FAIL: Script reload interrupted the live validation.");AppendMetrics("ABORTED");Finish();}
        static void Finish()
        {
            EditorApplication.update-=Tick;AssemblyReloadEvents.beforeAssemblyReload-=Abort;
            steering=false;routine?.Dispose();routine=null;
            if(installed){var loop=PlayerLoop.GetCurrentPlayerLoop();Remove(ref loop);PlayerLoop.SetPlayerLoop(loop);installed=false;}
            if(station){station.EndOperation();station.SetMode(0);station.ResetExercise();}station=null;collision=null;
            if(app){app.ClosePanel();app.enabled=appWasEnabled;}app=null;
            Directory.CreateDirectory("Documentation");File.WriteAllLines(ReportPath,report,new UTF8Encoding(false));File.WriteAllLines(TracePath,trace,new UTF8Encoding(false));
            Debug.Log("ROBOT_RING_GRIP_MOTION_VALIDATION_FINISHED "+string.Join(" | ",report));
        }
        static void Require(bool condition,string message)
        {report.Add((condition?"PASS: ":"FAIL: ")+message);if(!condition)throw new InvalidOperationException(message);}
        static string F(float value)=>value.ToString("F9",Invariant);
        static string Csv(string text)=>"\""+(text??"").Replace("\"","\"\"").Replace("\r"," ").Replace("\n"," ")+"\"";
        static float DirectionAngle(Vector3 a,Vector3 b)=>Mathf.Atan2(Vector3.Cross(a,b).magnitude,Mathf.Clamp(Vector3.Dot(a,b),-1,1))*Mathf.Rad2Deg;
        static void Capture(string label,bool wide)
        {
            var host=new GameObject("__RingGripMotionCamera"){hideFlags=HideFlags.HideAndDontSave};
            RenderTexture rt=null;Texture2D texture=null;var previous=RenderTexture.active;
            try
            {
                Camera camera=host.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=wide?40:37;camera.nearClipPlane=.005f;
                Transform tcp=station.ringGripTcp;
                Vector3 focus=station.ringRoot.position+tcp.up*.04f;
                camera.transform.position=wide?station.transform.TransformPoint(station.exerciseCenter)+new Vector3(1.25f,.75f,-1.1f)
                    :focus+tcp.right*.36f+tcp.forward*.17f+tcp.up*.055f;
                camera.transform.LookAt(focus,tcp.up);
                rt=new RenderTexture(1920,1080,24){antiAliasing=4};camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();
                Directory.CreateDirectory("Documentation/Screenshots");
                string path="Documentation/Screenshots/RingGripMotion_Station"+(station.stationIndex+1).ToString("00")+"_"+label+".png";
                File.WriteAllBytes(path,texture.EncodeToPNG());report.Add("SCREENSHOT: "+path);
            }
            finally
            {
                RenderTexture.active=previous;if(host)host.GetComponent<Camera>().targetTexture=null;
                if(texture)Object.DestroyImmediate(texture);if(rt){rt.Release();Object.DestroyImmediate(rt);}Object.DestroyImmediate(host);
            }
        }
    }
}
