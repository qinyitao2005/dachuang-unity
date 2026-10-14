using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RoboticsLab
{
    // Explicit standalone acceptance run only; normal players never see this component.
    public class LabBuildSmoke : MonoBehaviour
    {
        [Serializable]
        public class TraceResult
        {
            public string shape, status;
            public bool completed, timedOut, rigidTool, noInk;
            public float coverage, accuracy, score, meanErrorMm, maxPenTilt, maxGripTilt, maxNeedleTilt, seconds;
            public int inkVertices;
        }

        [Serializable]
        public class SmokeReport
        {
            public bool passed, scene, bundledChineseFont, videoShader, videoDecoded, helmet, wallLearning;
            public bool cameraRendered, trajectoryProgramming, roomDetails, stationRoles, stationEntry;
            public bool tracingBoard, tracingCad, tracingRigid, tracingAutomatic, tracingNoInk, tracingScreenshotRendered;
            public bool ringRimGrip, robotWireClearance, drawingInk, drawingHorizontal;
            public string unityVersion, project, robotWireReason, exception;
            public float wallSeconds, drawingInkLengthMm, drawingEndpointErrorMm;
            public int drawingInkVertices;
            public List<TraceResult> traces = new List<TraceResult>();
            public List<string> failures = new List<string>();
        }

        readonly SmokeReport report = new SmokeReport();
        string folder;
        float started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if(Array.IndexOf(arguments,"-robot-smoke")>=0 || Array.IndexOf(arguments,"--smoketest")>=0)
                new GameObject("BuildSmokeValidation").AddComponent<LabBuildSmoke>();
        }
        IEnumerator Start()
        {
            folder=Path.GetDirectoryName(Application.dataPath);
            started=Time.realtimeSinceStartup;
            report.unityVersion=Application.unityVersion;
            report.project=Application.productName;
            Application.runInBackground=true;
            IEnumerator checks=Checks();
            while(true)
            {
                bool next=false;
                object waiting=null;
                try
                {
                    if(Time.realtimeSinceStartup-started>480)throw new TimeoutException("Standalone smoke timed out.");
                    next=checks.MoveNext();
                    if(next)waiting=checks.Current;
                }
                catch(Exception ex)
                {
                    report.exception=ex.ToString();
                    report.failures.Add("Exception: "+ex.Message);
                    Debug.LogException(ex);
                }
                if(!next)break;
                yield return waiting;
            }
            try{(checks as IDisposable)?.Dispose();}
            catch(Exception ex){report.exception=ex.ToString();report.failures.Add("Cleanup: "+ex.Message);}
            report.passed=string.IsNullOrEmpty(report.exception)&&report.scene&&report.bundledChineseFont&&report.videoShader
                &&report.videoDecoded&&report.helmet&&report.wallLearning&&report.cameraRendered&&report.trajectoryProgramming
                &&report.roomDetails&&report.stationRoles&&report.stationEntry&&report.tracingBoard&&report.tracingCad
                &&report.tracingRigid&&report.tracingAutomatic&&report.tracingNoInk&&report.tracingScreenshotRendered&&report.ringRimGrip
                &&report.robotWireClearance&&report.drawingInk&&report.drawingHorizontal;
            report.wallSeconds=Time.realtimeSinceStartup-started;
            string json=JsonUtility.ToJson(report,true);
            File.WriteAllText(Path.Combine(folder,"StandaloneSmoke.json"),json);
            Debug.Log("ROBOT_STANDALONE_SMOKE "+json);
            Application.Quit(report.passed?0:2);
        }

        IEnumerator Checks()
        {
            yield return new WaitForSeconds(2);
            var app=FindObjectOfType<LabTrainingApp>();
            bool scene=app && app.player && app.stations!=null && app.stations.Length==3;
            report.scene=Check(scene,"Scene contains player and exactly three stations");
            report.bundledChineseFont=Check(app && app.uiFont,"Bundled Chinese font");
            report.videoShader=Check(app && app.videoScreenMaterial && app.videoScreenMaterial.shader.isSupported,"Supported video shader");
            if(scene&&app.helmetPickup){var p=app.helmetPickup.position;app.player.Teleport(new Vector3(p.x-.85f,.08f,p.z),90);app.EquipHelmet();}
            yield return new WaitForSeconds(3.5f);
            if(scene){app.player.Teleport(new Vector3(4.9f,.08f,10.5f),90);app.OpenLesson();}
            yield return new WaitForSeconds(4);
            report.videoDecoded=Check(app && app.lessonVideo && app.lessonVideo.isPrepared && app.lessonVideo.frame>0,"Teaching video decoded actual frames");
            report.helmet=Check(app && app.player && app.player.wornHelmet && app.player.wornHelmet.activeInHierarchy,"Safety helmet equipped");
            // A hidden Windows swap chain may return a black ScreenCapture despite a
            // running scene. Render the actual player camera into an explicit target.
            report.cameraRendered=Check(scene&&CaptureCamera(app.player.viewCamera,Path.Combine(folder,"StandaloneSmoke.png")),"Player camera renders scene");
            yield return new WaitForSeconds(1);
            report.wallLearning=Check(app&&app.wallLearningDisplay&&app.wallLearningDisplay.GetComponentInChildren<Canvas>()&&app.wallLearningDisplay.GetComponentInChildren<Canvas>().renderMode==RenderMode.WorldSpace,"Learning remains on wall canvas");
            List<LabTrajectoryStep> steps;string parseError;
            report.trajectoryProgramming=Check(app&&app.trajectoryProgram&&app.trajectoryPanel
                &&LabTrajectoryParser.TryParse("SPEED 60\nPEN UP\nMOVE 10 20 30\nWAIT 0.2",Vector3.zero,out steps,out parseError)&&steps.Count==4,"Trajectory programming parser and panel");
            var details=GameObject.Find("RoomPolish");
            report.roomDetails=Check(details&&details.transform.Find("EntranceLobby")&&details.GetComponentsInChildren<MeshRenderer>().Length>40,"Detailed room assets present");
            if(!scene)yield break;

            app.ClosePanel();
            bool appEnabled=app.enabled;
            app.enabled=false; // Keep user input from competing with the explicit acceptance-run driver.
            LabTracingExercise tracing=null;
            LabRobotStation[] stations=app.stations.OrderBy(s=>s.stationIndex).ToArray();
            try
            {
                report.stationRoles=stations[0].IsTracing&&stations[1].IsRing&&stations[2].IsDrawing;
                foreach(var station in stations)
                {
                    station.EndOperation();
                    var group=station.GetComponent<Group0927Station>();
                    report.stationRoles&=group!=null;
                    if(!group)continue;
                    for(int requested=0;requested<=1;requested++)
                    {
                        station.SetMode(requested);
                        report.stationRoles&=station.TrainingMode==station.RequiredMode;
                        group.SetMode(requested);
                        report.stationRoles&=group.TrainingMode==station.RequiredMode;
                    }
                }
                Check(report.stationRoles,"Station identities and both API mode guards are fixed");
                report.stationEntry=true;

                var traceStation=stations[0];
                var traceRig=traceStation.GetComponent<Group0927Station>();
                var rigid=new RigidSnapshot(traceRig?traceRig.DrawingToolRoot:null);
                tracing=traceStation.GetComponent<LabTracingExercise>();
                traceStation.BeginOperation();
                yield return null;
                report.stationEntry&=Check(traceStation.IsOperating&&!traceStation.EmergencyStopped,"Tracing station entry: "+traceStation.DrawingBlockReason);
                var print=traceStation.drawingBoard?traceStation.drawingBoard.Find("TracingPrintedGuides"):null;
                report.tracingBoard=Check(tracing&&print&&print.gameObject.activeInHierarchy
                    &&Enumerable.Range(0,5).All(i=>print.Find("Guide_"+i)!=null),"Tracing board contains manual J and four printed automatic figures");
                int meshes=traceRig&&traceRig.DrawingToolRoot?traceRig.DrawingToolRoot.GetComponentsInChildren<MeshFilter>(true)
                    .Count(m=>m.sharedMesh):0;
                report.tracingCad=Check(meshes==28&&ProbeBindingValid(traceStation,traceRig),
                    "Complete original Pointer CAD probe has 28 meshes and the actual needle owns TracingProbeTCP; meshes="+meshes);
                report.tracingRigid=report.tracingCad&&Check(rigid.Unchanged(),"Original probe CAD local transforms survive initial IK unchanged");
                report.tracingAutomatic=tracing&&traceStation.IsOperating;
                report.tracingNoInk=Check(NoInk(traceStation),"Tracing probe starts without DrawingStroke objects or ink");
                if(tracing&&traceRig&&traceStation.IsOperating)
                {
                    for(int shape=1;shape<=4;shape++)
                    {
                        tracing.SelectShape(shape);tracing.StartAutomatic();
                        var result=new TraceResult{shape=tracing.CurrentShapeName,rigidTool=true,noInk=NoInk(traceStation)};
                        float shapeStart=Time.realtimeSinceStartup;
                        bool launched=tracing.IsAutomatic;
                        while(tracing.IsAutomatic&&Time.realtimeSinceStartup-shapeStart<90)
                        {
                            yield return null;
                            result.maxPenTilt=Mathf.Max(result.maxPenTilt,traceRig.DrawingTiltDegrees);
                            result.maxGripTilt=Mathf.Max(result.maxGripTilt,traceRig.DrawingGripTiltDegrees);
                            result.maxNeedleTilt=Mathf.Max(result.maxNeedleTilt,ProbeTilt(traceRig));
                            result.rigidTool&=rigid.Unchanged();
                            result.noInk&=NoInk(traceStation);
                        }
                        // Sample after the final robot LateUpdate; the automatic runner finishes on its next Update.
                        result.timedOut=tracing.IsAutomatic;
                        if(result.timedOut)tracing.StopAutomatic();
                        result.completed=tracing.Completed;
                        result.coverage=tracing.Coverage;result.accuracy=tracing.Accuracy;result.score=tracing.Score;
                        result.meanErrorMm=tracing.MeanErrorMm;result.status=tracing.Status;
                        result.seconds=Time.realtimeSinceStartup-shapeStart;result.inkVertices=InkVertices(traceStation);
                        result.noInk&=NoInk(traceStation);
                        report.traces.Add(result);
                        report.tracingRigid&=Check(result.rigidTool&&result.maxPenTilt<=1.05f&&result.maxGripTilt<=1.05f&&result.maxNeedleTilt<=1.05f,
                            "Tracing original probe CAD stays rigid with vertical needle axis and TCP: "+result.shape);
                        report.tracingNoInk&=Check(result.noInk&&result.inkVertices==0,
                            "Tracing follows printed guides without DrawingStroke objects or ink: "+result.shape);
                        report.tracingAutomatic&=Check(launched&&!result.timedOut&&result.completed&&result.coverage>=99
                            &&result.accuracy>=80&&result.score>=80,
                            "Actual automatic "+result.shape+" coverage="+result.coverage.ToString("F2")+" score="+result.score.ToString("F2")+" status="+result.status);
                    }
                    report.tracingScreenshotRendered=Check(CaptureTracing(app.player.viewCamera,tracing),"Standalone tracing paper and workcell screenshots rendered");
                    tracing.StopAutomatic();
                }
                traceStation.EndOperation();

                var ring=stations[1];
                ring.BeginOperation();yield return null;
                report.stationEntry&=Check(ring.IsOperating&&ring.TrainingMode==1,"Ring station entry");
                var ringRig=ring.GetComponent<Group0927Station>();
                string reason="Missing active Group0927WireCollision";float gap=0;
                var collision=ring.GetComponent<Group0927WireCollision>();
                report.ringRimGrip=Check(ringRig&&ringRig.ringGripTcp&&ringRig.ringGripTool&&ringRig.ringGripTool.gameObject.activeInHierarchy
                    &&ringRig.ringSolidRenderer&&ringRig.ringSolidRenderer.enabled&&ringRig.ringRenderer&&!ringRig.ringRenderer.enabled
                    &&ringRig.wireRenderer&&!ringRig.wireRenderer.enabled&&ringRig.wireRoot&&ringRig.wireRoot.Find("SteelGuideSolid")
                    &&Mathf.Abs(Vector3.Distance(ring.ActualToolPosition,ring.RingGripPosition)-ring.ringRadius)<.00005f
                    &&ring.ringRoot&&Vector3.Distance(ring.ringRoot.TransformPoint(Vector3.up*ring.ringRadius),ring.RingGripPosition)<.00005f,"Only station two uses the original CAD rim grip");
                report.robotWireClearance=Check(collision&&collision.station==ringRig&&collision.Check(out reason,out gap),"Active Group0927 wire collision validates ring pose: "+reason);
                report.robotWireReason=reason;
                ring.EndOperation();

                var drawing=stations[2];
                drawing.BeginOperation();yield return null;
                report.stationEntry&=Check(drawing.IsOperating&&drawing.TrainingMode==0,"Free drawing station entry: "+drawing.DrawingBlockReason);
                if(drawing.IsOperating)
                {
                    drawing.SetPen(true);
                    Vector3 start=drawing.transform.TransformPoint(new Vector3(drawing.exerciseCenter.x,drawing.drawingHeight,drawing.exerciseCenter.z));
                    foreach(int frame in MoveTo(drawing,start,8))yield return null;
                    Vector3 finish=start+drawing.transform.TransformDirection(new Vector3(.025f,0,0));
                    foreach(int frame in MoveTo(drawing,finish,8))yield return null;
                    yield return null;
                    drawing.SetPen(false);
                    report.drawingInkVertices=InkVertices(drawing);
                    report.drawingInkLengthMm=InkLengthWorld(drawing)*1000;
                    report.drawingEndpointErrorMm=Vector3.Distance(drawing.ActualToolPosition,finish)*1000;
                    report.drawingInk=Check(report.drawingInkVertices>=2&&report.drawingInkLengthMm>=20&&report.drawingEndpointErrorMm<=1,
                        "Third station draws requested 25 mm stroke through MoveTool; vertices="+report.drawingInkVertices
                        +" inkLengthMm="+report.drawingInkLengthMm.ToString("F3")+" endpointErrorMm="+report.drawingEndpointErrorMm.ToString("F3"));
                    var drawingRig=drawing.GetComponent<Group0927Station>();
                    report.drawingHorizontal=Check(drawingRig&&drawingRig.DrawingTiltDegrees<=1.05f&&drawingRig.DrawingGripTiltDegrees<=1.05f,"Third station keeps original gripper horizontal");
                    drawing.EndOperation();
                }
            }
            finally
            {
                if(tracing)tracing.StopAutomatic();
                foreach(var station in stations)if(station)station.EndOperation();
                if(app)app.enabled=appEnabled;
            }
        }

        bool Check(bool passed,string message)
        {
            if(!passed)report.failures.Add(message);
            Debug.Log("ROBOT_SMOKE "+(passed?"PASS ":"FAIL ")+message);
            return passed;
        }

        static IEnumerable<int> MoveTo(LabRobotStation station,Vector3 destination,float seconds)
        {
            float until=Time.realtimeSinceStartup+seconds;
            while(station.IsOperating&&Vector3.Distance(station.ActualToolPosition,destination)>.0007f&&Time.realtimeSinceStartup<until)
            {
                Vector3 requested=station.toolTarget?station.toolTarget.position:station.ActualToolPosition;
                Vector3 velocity=Vector3.ClampMagnitude((destination-requested)/Mathf.Max(.0001f,Time.deltaTime),.02f);
                station.MoveTool(velocity/Mathf.Max(.0001f,station.moveSpeed));
                yield return 0;
            }
        }

        static bool ProbeBindingValid(LabRobotStation station,Group0927Station rig)
        {
            return rig&&rig.DrawingToolRoot&&rig.DrawingToolRoot.name=="TracingProbe_OriginalCAD"
                &&rig.drawingGripTool==rig.DrawingToolRoot&&rig.penRoot&&rig.penRoot.name=="针尖-230314"
                &&rig.penRoot.GetComponent<MeshFilter>()&&rig.penRoot.IsChildOf(rig.DrawingToolRoot)
                &&rig.drawingTcp&&rig.drawingTcp.name=="TracingProbeTCP"&&rig.drawingTcp.parent==rig.penRoot
                &&station.penRoot==rig.penRoot;
        }
        static float ProbeTilt(Group0927Station rig)=>rig.penRoot?Vector3.Angle(rig.penRoot.up,Vector3.up):180;
        // Hidden or empty strokes still violate the tracing station's no-ink contract.
        static bool NoInk(LabRobotStation station)=>!station.GetComponentsInChildren<Transform>(true)
            .Any(node=>node.name=="DrawingStroke")&&InkVertices(station)==0;
        static int InkVertices(LabRobotStation station)=>station.GetComponentsInChildren<LineRenderer>(true)
            .Where(line=>line.name=="DrawingStroke").Sum(line=>line.positionCount);

        static float InkLengthWorld(LabRobotStation station)
        {
            float length=0;
            foreach(var line in station.GetComponentsInChildren<LineRenderer>(true))
            {
                if(line.name!="DrawingStroke")continue;
                for(int point=1;point<line.positionCount;point++)
                {
                    Vector3 a=line.GetPosition(point-1),b=line.GetPosition(point);
                    if(!line.useWorldSpace){a=line.transform.TransformPoint(a);b=line.transform.TransformPoint(b);}
                    length+=Vector3.Distance(a,b);
                }
            }
            return length;
        }

        bool CaptureTracing(Camera source,LabTracingExercise tracing)
        {
            if(!source||!tracing)return false;
            var go=new GameObject("SmokeTracingCaptureCamera");
            var camera=go.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
            try
            {
                Vector3 center=tracing.PaperToWorld(Vector2.zero);
                camera.orthographic=true;camera.orthographicSize=.245f;
                camera.transform.position=center+tracing.station.transform.up*.75f;
                camera.transform.rotation=Quaternion.LookRotation(center-camera.transform.position,tracing.station.transform.forward);
                bool paper=CaptureCamera(camera,Path.Combine(folder,"StandaloneTracingPaper.png"));
                camera.orthographic=false;camera.fieldOfView=40;
                camera.transform.position=center+tracing.station.transform.TransformDirection(new Vector3(.45f,.6f,-.52f));
                camera.transform.LookAt(center+Vector3.up*.06f);
                return CaptureCamera(camera,Path.Combine(folder,"StandaloneTracingWorkcell.png"))&&paper;
            }
            finally{Destroy(go);}
        }

        sealed class RigidSnapshot
        {
            readonly Transform[] nodes;
            readonly Vector3[] positions,scales;
            readonly Quaternion[] rotations;
            public RigidSnapshot(Transform root)
            {
                nodes=root?root.GetComponentsInChildren<Transform>(true):new Transform[0];
                positions=nodes.Select(t=>t.localPosition).ToArray();scales=nodes.Select(t=>t.localScale).ToArray();
                rotations=nodes.Select(t=>t.localRotation).ToArray();
            }
            public bool Unchanged()
            {
                if(nodes.Length==0)return false;
                for(int i=0;i<nodes.Length;i++)
                {
                    if(!nodes[i]||(nodes[i].localPosition-positions[i]).sqrMagnitude>1e-12f||(nodes[i].localScale-scales[i]).sqrMagnitude>1e-12f)return false;
                    Quaternion a=rotations[i],b=nodes[i].localRotation;
                    float same=Mathf.Abs(a.x-b.x)+Mathf.Abs(a.y-b.y)+Mathf.Abs(a.z-b.z)+Mathf.Abs(a.w-b.w);
                    float opposite=Mathf.Abs(a.x+b.x)+Mathf.Abs(a.y+b.y)+Mathf.Abs(a.z+b.z)+Mathf.Abs(a.w+b.w);
                    if(Mathf.Min(same,opposite)>.00001f)return false;
                }
                return true;
            }
        }

        static bool CaptureCamera(Camera camera,string path)
        {
            if(!camera)return false;
            var target=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);
            var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
            var previousTarget=camera.targetTexture;
            var previousActive=RenderTexture.active;
            try
            {
                camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());
                var pixels=image.GetPixels32();int visible=0;
                for(int i=0;i<pixels.Length;i+=257)
                    if(pixels[i].r>16||pixels[i].g>16||pixels[i].b>16)visible++;
                return visible>pixels.Length/257/10;
            }
            finally
            {
                camera.targetTexture=previousTarget;RenderTexture.active=previousActive;
                target.Release();Destroy(target);Destroy(image);
            }
        }
    }
}
