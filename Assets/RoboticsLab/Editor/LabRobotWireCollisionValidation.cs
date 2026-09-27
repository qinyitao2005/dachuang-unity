using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace RoboticsLab.Editor
{
    /// <summary>Independent collision probes; production path driving is tested separately.</summary>
    public static class LabRobotWireCollisionValidation
    {
        const string ReportPath="Documentation/RobotWireCollisionValidation.txt";
        static readonly List<string> report=new List<string>();
        static Scene preview;

        [MenuItem("Robot Lab/Diagnostics/Validate Robot Wire Collision")]
        public static void Run()
        {
            if(!EditorApplication.isPlaying)throw new Exception("Play Mode required; probes use temporary preview-scene objects only.");
            report.Clear();report.Add("Robot/wire collision validation: "+DateTime.Now.ToString("s"));
            report.Add("Actual CAD model copies plus independent geometric fixtures. No source assets or live robot poses are changed.");
            var sources=Object.FindObjectsOfType<LabRobotStation>().Where(s=>s.modelRoot&&s.stationIndex>=0&&s.stationIndex<3).OrderBy(s=>s.stationIndex).ToArray();
            preview=EditorSceneManager.NewPreviewScene();
            try
            {
                SyntheticChecks();
                RigidRingChecks();
                Require(sources.Length==3,"three authored robot stations are available");
                foreach(var source in sources)RealModelChecks(source);
                report.Add("PASS: robot/wire OBB capsule and swept-motion validation completed.");
            }
            catch(Exception e)
            {
                if(report.Count==0||!report[report.Count-1].StartsWith("FAIL:"))report.Add("FAIL: "+e.Message);
                Debug.LogException(e);
            }
            finally
            {
                if(preview.IsValid())EditorSceneManager.ClosePreviewScene(preview);
                Directory.CreateDirectory("Documentation");File.WriteAllLines(ReportPath,report);
                Debug.Log("ROBOT_LAB_ROBOT_WIRE_COLLISION_VALIDATION "+string.Join(" | ",report));
            }
        }

        static void SyntheticChecks()
        {
            var root=new GameObject("TemporaryWireCollisionFixture");SceneManager.MoveGameObjectToScene(root,preview);
            try
            {
                var station=root.AddComponent<LabRobotStation>();station.enabled=false;
                var model=new GameObject("Model").transform;model.SetParent(root.transform,false);station.modelRoot=model;
                var joint=new GameObject("MovingJoint").transform;joint.SetParent(model,false);
                var body=GameObject.CreatePrimitive(PrimitiveType.Cube);body.name="KnownRigidLink";body.transform.SetParent(joint,false);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.localPosition=Vector3.right;body.transform.localScale=Vector3.one*.08f;
                station.joints=new[]{new JointConfig{joint=joint,axis=JointAxis.Y,minAngle=-180,maxAngle=180}};
                station.wireRadius=.002f;
                station.wirePoints=new[]{new Vector3(1,-.1f,0),new Vector3(1,.1f,0)};
                var collision=root.AddComponent<LabRobotWireCollision>();collision.station=station;collision.RebuildCache();
                string reason;float gap;
                Require(collision.ProxyCount==1&&string.IsNullOrEmpty(collision.CacheError),"fixture caches exactly its actual rigid-link mesh");
                Require(!collision.CheckWireSegment(new Vector3(1,-.1f,0),new Vector3(1,.1f,0),.002f,out reason,out gap)&&gap<0,"capsule through a mesh box is rejected; gap="+gap.ToString("F6"));
                Require(collision.CheckWireSegment(new Vector3(4,-.1f,0),new Vector3(4,.1f,0),.002f,out reason,out gap)&&gap>2,"remote capsule is clear; gap="+gap.ToString("F6"));

                // The same segment distance implementation must not divide by mesh scale.
                model.localScale=new Vector3(1.2f,.8f,1.1f);model.localRotation=Quaternion.Euler(15,27,18);
                joint.localRotation=Quaternion.Euler(10,31,8);body.transform.localScale=new Vector3(-.08f,.04f,.02f);
                collision.RebuildCache();Vector3 center=body.transform.TransformPoint(body.GetComponent<MeshFilter>().sharedMesh.bounds.center);
                Require(!collision.CheckWireSegment(center-Vector3.up*.1f,center+Vector3.up*.1f,.002f,out reason,out gap)&&gap<0,"mirrored/nonuniform/sheared mesh bounds retain collision coverage");
                body.transform.localScale=Vector3.zero;collision.RebuildCache();center=body.transform.position;
                Require(!collision.CheckWireSegment(center,center,.002f,out reason,out gap)&&Finite(gap)&&gap<0,"zero-scale box and zero-length capsule are handled without NaN");
                Require(collision.CheckWireSegment(center+Vector3.one,center+Vector3.one,.002f,out reason,out gap)&&Finite(gap)&&gap>0,"collapsed box does not become an infinite obstacle");
                model.localScale=Vector3.one;model.localRotation=Quaternion.identity;body.transform.localScale=Vector3.one*.08f;

                // Endpoints are clear, but the real rotating link crosses the wire midway.
                collision.maxSweepPadding=.01f;collision.maxSweepIntervals=256;
                joint.localRotation=Quaternion.Euler(0,-30,0);collision.RebuildCache();
                Quaternion[] previous={joint.localRotation};
                Require(collision.Check(out reason,out gap),"swept-collision fixture initial pose is statically clear");
                joint.localRotation=Quaternion.identity;
                Require(!collision.Check(out reason,out gap),"independent midpoint pose actually intersects the wire");
                joint.localRotation=Quaternion.Euler(0,30,0);Quaternion final=joint.localRotation;
                Require(collision.Check(out reason,out gap),"swept-collision fixture final pose is statically clear");
                bool safe=collision.CheckSweep(previous,out reason,out gap);
                Require(!safe&&reason.StartsWith("Sweep sample ")&&collision.LastSweepIntervals>1,"sweep detects an intermediate hit with safe endpoints; "+reason);
                Require(SameRotation(joint.localRotation,final),"rejected sweep restores the original final joint pose");

                // A legal pose close to a wire must be able to move away. This caught the
                // earlier error of charging safetyMargin again after temporal inflation.
                joint.localRotation=Quaternion.identity;collision.maxSweepPadding=.00075f;collision.maxSweepIntervals=96;
                float startGap=collision.safetyMargin+.00001f;
                station.wirePoints=new[]{new Vector3(1,-.1f,-.04f-station.wireRadius-startGap),new Vector3(1,.1f,-.04f-station.wireRadius-startGap)};
                previous[0]=joint.localRotation;collision.RebuildCache();
                Require(collision.Check(out reason,out gap)&&gap>=collision.safetyMargin&&gap<collision.safetyMargin+.00003f,"retreat fixture starts legally within 0.03 mm of its safety boundary; gap="+gap.ToString("F6"));
                joint.localRotation=Quaternion.Euler(0,-.7f,0);final=joint.localRotation;
                Require(collision.Check(out reason,out gap)&&gap>startGap+.005f,"retreat endpoint moves the real link away from the wire");
                safe=collision.CheckSweep(previous,out reason,out gap);
                Require(safe&&gap>=0,"near-boundary outward retreat is permitted with proven nonintersection; intervals="+collision.LastSweepIntervals+" lowerGap="+gap.ToString("F6")+" "+reason);
                Require(SameRotation(joint.localRotation,final),"accepted retreat sweep preserves the final pose");
                previous[0]=final;
                Require(collision.CheckSweep(previous,out reason,out gap)&&collision.LastSweepIntervals==0,"unchanged joint pose skips temporal resampling");
                joint.localRotation=Quaternion.AngleAxis(.001f,Vector3.up)*previous[0];
                Require(collision.CheckSweep(previous,out reason,out gap)&&collision.LastSweepIntervals>0,"tiny joint rotations retain a nonzero motion envelope");

                // Fixed CAD offsets cancel before computing motion radii, rather than
                // generating thousands of unnecessary samples from a distant local origin.
                var cadFrame=new GameObject("LargeCancelledCADOffset").transform;cadFrame.SetParent(joint,false);cadFrame.localPosition=new Vector3(1000,0,0);
                body.transform.SetParent(cadFrame,false);body.transform.localPosition=new Vector3(-999,0,0);
                station.wirePoints=new[]{new Vector3(5,-.1f,0),new Vector3(5,.1f,0)};
                joint.localRotation=Quaternion.identity;previous[0]=joint.localRotation;collision.RebuildCache();
                joint.localRotation=Quaternion.Euler(0,-.7f,0);final=joint.localRotation;
                Require(collision.CheckSweep(previous,out reason,out gap)&&collision.LastSweepIntervals<=16,"fixed CAD translation cancellation keeps sweep bounds useful; intervals="+collision.LastSweepIntervals+" "+reason);

                collision.maxSweepIntervals=1;collision.maxSweepPadding=.000001f;
                Require(!collision.CheckSweep(previous,out reason,out gap)&&reason.Contains("budget"),"insufficient sampling budget rejects the move instead of dropping collision coverage");
                Require(SameRotation(joint.localRotation,final),"budget rejection restores the final joint pose");
            }
            finally{Object.DestroyImmediate(root);}
        }

        static void RealModelChecks(LabRobotStation source)
        {
            GameObject copy=Object.Instantiate(source.gameObject);copy.name="TemporaryCADProbe_"+source.stationIndex;SceneManager.MoveGameObjectToScene(copy,preview);
            try
            {
                foreach(var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
                var station=copy.GetComponent<LabRobotStation>();
                Require(station.ringGripTool&&station.originalEndTool,"station "+source.stationIndex+" has authored ring gripper and retained original tool");
                station.originalEndTool.gameObject.SetActive(false);station.ringGripTool.gameObject.SetActive(true);
                var collision=copy.GetComponent<LabRobotWireCollision>()??copy.AddComponent<LabRobotWireCollision>();collision.station=station;collision.RebuildCache();
                var expected=MovingMeshes(station).ToArray();
                var actual=new HashSet<Transform>();for(int i=0;i<collision.ProxyCount;i++)actual.Add(collision.GetProxyTransform(i));
                Require(expected.Length>=84&&collision.ProxyCount==expected.Length&&expected.All(m=>actual.Contains(m.transform)),"station "+source.stationIndex+" caches every actual moving CAD mesh; count="+collision.ProxyCount);
                Require(station.modelRoot.GetComponentsInChildren<MeshFilter>(true).Length>collision.ProxyCount+1000,"station "+source.stationIndex+" excludes the static cabinet and training-cell frame");
                string reason;float gap;
                Vector3 far=copy.transform.position+Vector3.one*100;
                Require(collision.CheckWireSegment(far,far+Vector3.up,.002f,out reason,out gap),"station "+source.stationIndex+" remote probe remains clear");
                Require(collision.LastActiveProxyCount==ActiveCount(expected),"station "+source.stationIndex+" hidden original tool is skipped while new CAD gripper is active; active="+collision.LastActiveProxyCount);
                var newMeshes=station.ringGripTool.GetComponentsInChildren<MeshFilter>(true).Where(m=>m.sharedMesh&&m.GetComponent<Renderer>()).ToArray();
                Require(newMeshes.Length>=3&&newMeshes.All(m=>actual.Contains(m.transform)),"station "+source.stationIndex+" includes the gripper body and both CAD fingers; meshes="+newMeshes.Length);

                // Probe a real gripper mesh, wrist mesh and upper moving link, rather
                // than validating only a sphere around the tool endpoint.
                MeshFilter gripper=newMeshes.FirstOrDefault(m=>m.name.Contains("手指")||m.name.IndexOf("finger",StringComparison.OrdinalIgnoreCase)>=0)??newMeshes[0];
                ProbeMesh(collision,gripper,"station "+source.stationIndex+" gripper");
                ProbeMesh(collision,JointMesh(station,4),"station "+source.stationIndex+" wrist");
                ProbeMesh(collision,JointMesh(station,1),"station "+source.stationIndex+" upper arm");

                var renderer=gripper.GetComponent<Renderer>();bool rendererEnabled=renderer.enabled;renderer.enabled=false;
                collision.CheckWireSegment(far,far+Vector3.up,.002f,out reason,out gap);
                Require(collision.LastActiveProxyCount==ActiveCount(expected),"station "+source.stationIndex+" renderer-disabled mesh is skipped without stale collision geometry");
                renderer.enabled=rendererEnabled;
                station.ringGripTool.gameObject.SetActive(false);station.originalEndTool.gameObject.SetActive(true);collision.RebuildCache();
                collision.CheckWireSegment(far,far+Vector3.up,.002f,out reason,out gap);
                Require(collision.LastActiveProxyCount==ActiveCount(expected),"station "+source.stationIndex+" cache handles returning to the retained original tool");
                station.originalEndTool.gameObject.SetActive(false);station.ringGripTool.gameObject.SetActive(true);
                RealRingChecks(station,collision);
            }
            finally{Object.DestroyImmediate(copy);}
        }

        static void RigidRingChecks()
        {
            var root=new GameObject("TemporaryRigidRingSweepFixture");SceneManager.MoveGameObjectToScene(root,preview);
            try
            {
                var station=root.AddComponent<LabRobotStation>();station.enabled=false;
                var model=new GameObject("Model").transform;model.SetParent(root.transform,false);station.modelRoot=model;
                var joint=new GameObject("WristRoll").transform;joint.SetParent(model,false);
                var body=GameObject.CreatePrimitive(PrimitiveType.Cube);body.name="RemoteCADBody";body.transform.SetParent(joint,false);
                Object.DestroyImmediate(body.GetComponent<Collider>());body.transform.localPosition=Vector3.up*.3f;body.transform.localScale=Vector3.one*.008f;
                station.joints=new[]{new JointConfig{joint=joint,axis=JointAxis.X,minAngle=-180,maxAngle=180}};
                station.exerciseScale=.4f;station.ringRadius=.042f;station.ringTubeRadius=.0036f;station.wireRadius=.0028f;
                var tcp=new GameObject("TrueRimGripTCP").transform;tcp.SetParent(joint,false);tcp.localPosition=Vector3.up*(station.ringRadius+.025f);station.ringGripTcp=tcp;
                station.SetMode(1);
                var collision=root.AddComponent<LabRobotWireCollision>();collision.station=station;
                collision.maxSweepPadding=.002f;collision.maxSweepIntervals=256;
                // Both endpoint rings are threaded and clear. The actual TCP rotation
                // bends the centre's path outward by 12.5 mm; interpolating endpoint
                // centres would stay well inside the hole and miss the midpoint hit.
                float wireY=.025f-station.ringRadius+station.ringTubeRadius+station.wireRadius-.001f;
                station.wirePoints=new[]{new Vector3(-.025f,wireY,0),new Vector3(.025f,wireY,0)};
                collision.RebuildCache();string reason;float gap;
                joint.localRotation=Quaternion.Euler(-60,0,0);Quaternion[] previous={joint.localRotation};
                Require(collision.Check(out reason,out gap)&&collision.LastHeldRingChecked,"rigid ring sweep initial pose is threaded and statically clear; "+reason);
                Vector3 from=station.ActualToolPosition;
                joint.localRotation=Quaternion.Euler(60,0,0);Quaternion final=joint.localRotation;
                Require(collision.Check(out reason,out gap)&&collision.LastHeldRingChecked,"rigid ring sweep final pose is threaded and statically clear; "+reason);
                Vector3 to=station.ActualToolPosition;
                joint.localRotation=Quaternion.identity;
                Require(Vector3.Distance(station.ActualToolPosition,(from+to)*.5f)>.012f,"actual rigid ring midpoint differs from fake endpoint-centre interpolation by more than 12 mm");
                Require(!collision.Check(out reason,out gap)&&collision.LastHeldRingChecked&&reason.StartsWith("Held ring near wire"),"only the actual rotating ring hits the wire at the independent midpoint; "+reason);
                joint.localRotation=final;
                Require(!collision.CheckSweep(previous,out reason,out gap)&&reason.StartsWith("Sweep sample ")&&reason.Contains("Held ring")&&collision.LastHeldRingPadding>0,
                    "shared true-joint sweep catches the ring-only intermediate collision; intervals="+collision.LastSweepIntervals+" "+reason);
                Require(SameRotation(joint.localRotation,final),"ring-only sweep rejection restores the final joint pose");

                joint.localRotation=Quaternion.identity;previous[0]=joint.localRotation;
                float sagitta=station.ringRadius*(1-Mathf.Cos(Mathf.PI/64));
                float physicalGap=.00063f+.0005f+sagitta;
                wireY=.025f-(station.ringRadius-station.ringTubeRadius-station.wireRadius-physicalGap);
                station.wirePoints=new[]{new Vector3(-.025f,wireY,0),new Vector3(.025f,wireY,0)};
                collision.maxSweepPadding=.00075f;collision.maxSweepIntervals=96;
                Require(collision.Check(out reason,out gap)&&collision.LastHeldRingClearance>=.0006f&&collision.LastHeldRingClearance<.00069f,
                    "rigid ring starts legally close to its 0.6 mm static boundary; gap="+collision.LastHeldRingClearance.ToString("F6")+" "+reason);
                float initialGap=collision.LastHeldRingClearance;
                joint.localRotation=Quaternion.Euler(5,0,0);final=joint.localRotation;
                Require(collision.Check(out reason,out gap)&&collision.LastHeldRingClearance>initialGap+.00001f,"rigid ring retreat endpoint improves true ring surface clearance");
                Require(collision.CheckSweep(previous,out reason,out gap)&&gap>=0&&collision.LastHeldRingChecked&&collision.LastHeldRingPadding>0,
                    "near-boundary rigid ring retreat passes without a second safety-margin charge; intervals="+collision.LastSweepIntervals+" "+reason);
                Require(SameRotation(joint.localRotation,final),"accepted rigid ring retreat restores the final joint pose");
                previous[0]=final;
                Require(collision.CheckSweep(previous,out reason,out gap)&&collision.LastSweepIntervals==0&&collision.LastHeldRingChecked,"no-motion sweep still validates the actual held ring");

                Vector3 center=station.ActualToolPosition;
                station.wirePoints=new[]{center+tcp.right*.03f,center+tcp.right*.05f};
                Require(!collision.Check(out reason,out gap)&&collision.LastHeldRingClearance>.0006f&&reason.Contains("endpoint"),"thread/endcap check rejects a physically clear ring beyond the finite wire");
            }
            finally{Object.DestroyImmediate(root);}
        }

        static void RealRingChecks(LabRobotStation station,LabRobotWireCollision collision)
        {
            station.SetMode(1);collision.RebuildCache();
            Vector3 center=station.ActualToolPosition,normal=station.ringGripTcp.right;
            station.wirePoints=new[]{station.transform.InverseTransformPoint(center-normal*.008f),station.transform.InverseTransformPoint(center+normal*.008f)};
            string reason;float gap;
            Require(collision.Check(out reason,out gap)&&collision.LastHeldRingChecked,
                "station "+station.stationIndex+" actual CAD gripper and rigid ring permit a wire through the aperture; ringGap="+collision.LastHeldRingClearance.ToString("F6")+" "+reason);
            var previous=station.joints.Select(j=>j!=null&&j.joint?j.joint.localRotation:Quaternion.identity).ToArray();
            Transform wrist=station.joints.Where(j=>j!=null&&j.joint&&(station.ringGripTcp==j.joint||station.ringGripTcp.IsChildOf(j.joint))).Last().joint;
            wrist.localRotation=Quaternion.AngleAxis(.05f,Vector3.forward)*wrist.localRotation;Quaternion final=wrist.localRotation;
            Require(collision.CheckSweep(previous,out reason,out gap)&&collision.LastHeldRingChecked&&collision.LastHeldRingPadding>0,
                "station "+station.stationIndex+" shared CAD/ring sweep validates a real wrist motion; intervals="+collision.LastSweepIntervals+" "+reason);
            Require(SameRotation(wrist.localRotation,final),"station "+station.stationIndex+" shared sweep preserves its actual final wrist pose");
            center=station.ActualToolPosition;normal=station.ringGripTcp.right;
            Vector3 rim=center-station.ringGripTcp.up*station.ringRadius;
            station.wirePoints=new[]{station.transform.InverseTransformPoint(rim-normal*.003f),station.transform.InverseTransformPoint(rim+normal*.003f)};
            Require(!collision.Check(out reason,out gap),"station "+station.stationIndex+" a wire through the actual opposite ring rim is rejected; "+reason);
        }

        static IEnumerable<MeshFilter> MovingMeshes(LabRobotStation station)
        {
            return station.modelRoot.GetComponentsInChildren<MeshFilter>(true).Where(m=>m.sharedMesh&&m.GetComponent<Renderer>()&&station.joints.Any(j=>j!=null&&j.joint&&(m.transform==j.joint||m.transform.IsChildOf(j.joint))));
        }
        static int ActiveCount(IEnumerable<MeshFilter> meshes)=>meshes.Count(m=>m.gameObject.activeInHierarchy&&m.GetComponent<Renderer>().enabled);
        static MeshFilter JointMesh(LabRobotStation station,int index)
        {
            Transform joint=station.joints[Mathf.Min(index,station.joints.Length-1)].joint;
            var filter=joint.GetComponent<MeshFilter>();
            if(!filter)filter=joint.GetComponentsInChildren<MeshFilter>().FirstOrDefault(m=>m.sharedMesh&&m.GetComponent<Renderer>()&&m.GetComponent<Renderer>().enabled);
            if(!filter)throw new Exception("No CAD mesh under configured joint "+joint.name);
            return filter;
        }
        static void ProbeMesh(LabRobotWireCollision collision,MeshFilter mesh,string label)
        {
            Vector3 center=mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center);
            string reason;float gap;
            bool safe=collision.CheckWireSegment(center-Vector3.up*.003f,center+Vector3.up*.003f,.001f,out reason,out gap);
            Require(!safe&&gap<0&&collision.LastHitTransform,label+" internal capsule is blocked by an actual mesh proxy; "+mesh.name+" gap="+gap.ToString("F6"));
        }
        static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        static bool SameRotation(Quaternion a,Quaternion b)
        {
            float difference=(a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z)+(a.w-b.w)*(a.w-b.w);
            float sum=(a.x+b.x)*(a.x+b.x)+(a.y+b.y)*(a.y+b.y)+(a.z+b.z)*(a.z+b.z)+(a.w+b.w)*(a.w+b.w);
            return Mathf.Min(difference,sum)<1e-14f;
        }
        static void Require(bool condition,string text)
        {
            report.Add((condition?"PASS: ":"FAIL: ")+text);
            if(!condition)throw new Exception(text);
        }
    }
}
