using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoboticsLab.Editor
{
    public static class LabRobotBuilder
    {
        const string Generated="Assets/RoboticsLab/Generated";
        static readonly string[] Labels={"01  ·  轨迹绘制机械臂","02  ·  精密操作机械臂","03  ·  综合实训机械臂"};
        public static void ExtractOriginal()=>LabOriginalEquipmentAssets.Extract();

        [MenuItem("Robot Lab/Use Original Equipment")]
        public static void UpgradeExistingRoom()
        {
            if(EditorApplication.isPlaying)throw new Exception("Stop Play Mode before replacing equipment.");
            ExtractOriginal();
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!=LabSceneBuilder.ScenePath)scene=EditorSceneManager.OpenScene(LabSceneBuilder.ScenePath,OpenSceneMode.Single);
            var stations=Build();
            LabOriginalRackBuilder.Replace();
            var app=UnityEngine.Object.FindObjectOfType<LabTrainingApp>();
            if(!app)throw new Exception("Existing teaching session not found.");
            app.stations=stations;
            var pickup=GameObject.Find("HelmetPickup");if(pickup)app.helmetPickup=pickup.transform;
            EditorUtility.SetDirty(app);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("ROBOT_LAB_ORIGINAL_EQUIPMENT_READY full original workcells=3 original racks=2");
        }
        public static LabRobotStation[] Build()
        {
            var result=new LabRobotStation[3];
            for(int i=0;i<3;i++)
            {
                var go=GameObject.Find("Station0"+(i+1));
                if(!go){go=new GameObject("Station0"+(i+1));go.transform.position=new Vector3(-4.5f,0,5.5f+i*5.5f);}
                // Keep the room's wall services. All equipment comes from the original assembly.
                foreach(var child in go.transform.Cast<Transform>().ToArray())
                    if(child.name!="WallServiceOutlet"&&child.name!="PowerSocket"&&child.name!="CableConduit")UnityEngine.Object.DestroyImmediate(child.gameObject);
                var station=go.GetComponent<LabRobotStation>()??go.AddComponent<LabRobotStation>();
                station.stationIndex=i;station.displayName=Labels[i];
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(LabOriginalEquipmentAssets.CellPath(i));
                if(!prefab)throw new Exception("Missing complete original workcell "+i);
                var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab,go.transform);model.name="RobotModel";
                model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.Euler(0,90,0);
                model.transform.localScale=Vector3.one;
                // Original console is on local +Z. Rotate the complete cabinet toward the room aisle (+X).
                var solver=model.GetComponentsInChildren<IK>(true).FirstOrDefault(x=>x.endEffector);
                if(!solver)throw new Exception("Original workcell missing configured IK: "+i);
                var arm=solver.transform;
                while(arm.parent&&arm.name!="机械臂")arm=arm.parent;
                station.modelRoot=model.transform;station.endEffector=solver.endEffector;
                station.joints=solver.joints.Where(x=>x!=null&&x.joint).Select((x,n)=>new JointConfig
                {
                    joint=x.joint,axis=x.axis,
                    minAngle=n==0?-165:n==solver.joints.Length-1?-110:-100,
                    maxAngle=n==0?165:n==solver.joints.Length-1?110:100
                }).ToArray();
                station.tableHeight=LabOriginalEquipmentAssets.BoundsOf(arm.gameObject).min.y-go.transform.position.y;
                // Park the articulated arm facing the accessible working area; the cabinet stays fixed.
                var pivot=station.joints[0].joint.position;
                var from=Vector3.ProjectOnPlane(station.endEffector.position-pivot,Vector3.up);
                if(from.sqrMagnitude>.0001f)arm.RotateAround(pivot,Vector3.up,Vector3.SignedAngle(from,Vector3.right,Vector3.up));
                Vector3 tip=go.transform.InverseTransformPoint(station.endEffector.position);
                station.exerciseScale=.4f;
                station.exerciseCenter=tip;
                station.drawingHeight=Mathf.Max(station.tableHeight+.06f,tip.y-.12f*station.exerciseScale);
                station.exerciseCenter.y=station.drawingHeight+.12f*station.exerciseScale;
                station.workspaceHalfSize=new Vector3(.33f,.38f,.48f)*station.exerciseScale;
                station.moveSpeed=.22f*station.exerciseScale;
                station.ringRadius=.105f*station.exerciseScale;station.ringTubeRadius=.009f*station.exerciseScale;station.wireRadius=.007f*station.exerciseScale;
                station.toolTarget=new GameObject("ToolTarget").transform;station.toolTarget.SetParent(go.transform,false);station.toolTarget.localPosition=station.exerciseCenter;
                MakeExercises(station);
                // Cabinet collision stays below the work surface so tools are not blocked by an enclosing box.
                var bounds=LabOriginalEquipmentAssets.BoundsOf(model);
                var collision=new GameObject("CabinetCollision");collision.transform.SetParent(go.transform,false);
                var box=collision.AddComponent<BoxCollider>();
                box.center=new Vector3(0,station.tableHeight*.5f,0);box.size=new Vector3(bounds.size.x,station.tableHeight,bounds.size.z);
                result[i]=station;
                Debug.Log("ROBOT_LAB_STATION "+i+" meshes="+model.GetComponentsInChildren<MeshFilter>(true).Length+" model="+bounds+" table="+station.tableHeight+" tip="+tip+" exercise="+station.exerciseCenter+" joints="+station.joints.Length);
            }
            return result;
        }
        static void MakeExercises(LabRobotStation s)
        {
            Color cyan=new Color(.06f,.57f,.69f),dark=new Color(.11f,.18f,.23f);
            Vector3 c=s.exerciseCenter;float kScale=s.exerciseScale;
            var board=new GameObject("DrawingExercise");board.transform.SetParent(s.transform,false);s.drawingBoard=board.transform;
            var surface=Cube("DrawingPaper",board.transform,new Vector3(c.x,s.drawingHeight-.009f*kScale,c.z),new Vector3(.7f,.016f,1.02f)*kScale,Color.white);
            Cube("BoardBacking",board.transform,new Vector3(c.x,s.drawingHeight-.035f*kScale,c.z),new Vector3(.77f,.035f,1.09f)*kScale,dark);
            // Four adjustable supports connect the elevated drawing board to the physical worktop.
            float supportTop=s.drawingHeight-.0525f*kScale;
            float supportBottom=s.tableHeight;
            float supportHeight=Mathf.Max(.035f,supportTop-supportBottom);
            for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
            {
                Vector3 foot=new Vector3(c.x+x*.29f*kScale,supportBottom,c.z+z*.43f*kScale);
                Cube("BoardSupportFoot",board.transform,foot+Vector3.up*.006f*kScale,new Vector3(.105f,.012f,.105f)*kScale,dark);
                Cube("BoardSupportPost",board.transform,foot+Vector3.up*(supportHeight*.5f),new Vector3(.029f*kScale,supportHeight,.029f*kScale),new Color(.6f,.66f,.7f));
                Cube("BoardSupportSleeve",board.transform,foot+Vector3.up*(supportHeight*.23f),new Vector3(.041f*kScale,supportHeight*.44f,.041f*kScale),new Color(.22f,.29f,.34f));
            }
            for(int k=-4;k<=4;k++)
            {
                var a=new Vector3(c.x-.33f*kScale,s.drawingHeight+.001f,c.z+k*.1f*kScale);var b=a+Vector3.right*.66f*kScale;
                MakeLine("PaperGrid",board.transform,new[]{a,b},.0013f*kScale,new Color(.72f,.84f,.87f));
            }
            for(int k=-3;k<=3;k++)
            {
                var a=new Vector3(c.x+k*.1f*kScale,s.drawingHeight+.001f,c.z-.49f*kScale);var b=a+Vector3.forward*.98f*kScale;
                MakeLine("PaperGrid",board.transform,new[]{a,b},.0013f*kScale,new Color(.72f,.84f,.87f));
            }
            var pen=new GameObject("PenTool");pen.transform.SetParent(s.transform,false);s.penRoot=pen.transform;
            var nib=GameObject.CreatePrimitive(PrimitiveType.Cylinder);nib.name="PenNib";nib.transform.SetParent(pen.transform,false);nib.transform.localPosition=new Vector3(0,.028f,0)*kScale;nib.transform.localScale=new Vector3(.014f,.028f,.014f)*kScale;SetMaterial(nib,dark);UnityEngine.Object.DestroyImmediate(nib.GetComponent<Collider>());
            var wire=new GameObject("WireExercise");wire.transform.SetParent(s.transform,false);s.wireRoot=wire.transform;
            const int count=121;s.wirePoints=LabRingCourseAuthoring.MakePoints(c,s.stationIndex);
            s.wireRenderer=MakeLine("SteelGuide",wire.transform,s.wirePoints,s.wireRadius*2,new Color(.10f,.20f,.24f));
            for(int k=0;k<2;k++)
            {
                Vector3 p=s.wirePoints[k==0?0:count-1];float h=p.y-s.tableHeight;
                Cube(k==0?"StartSupport":"FinishSupport",wire.transform,new Vector3(p.x,s.tableHeight+h*.5f,p.z),new Vector3(.027f*kScale,h,.027f*kScale),dark);
                Cube(k==0?"StartMarker":"FinishMarker",wire.transform,new Vector3(p.x,s.tableHeight,p.z),new Vector3(.17f,.025f,.17f)*kScale,k==0?new Color(.15f,.72f,.5f):cyan);
            }
            var ring=new GameObject("SwivelRingTool");ring.transform.SetParent(s.transform,false);s.ringRoot=ring.transform;
            Vector3[] circle=new Vector3[65];for(int k=0;k<circle.Length;k++){float a=k/(float)(circle.Length-1)*Mathf.PI*2;circle[k]=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*s.ringRadius;}
            s.ringRenderer=MakeLine("RingRim",ring.transform,circle,s.ringTubeRadius*2,new Color(1,.7f,.15f));
            s.ringRenderer.numCornerVertices=3;
            LabRingTubeVisualAuthoring.BuildStation(s);
            board.SetActive(false);pen.SetActive(false);wire.SetActive(false);ring.SetActive(false);
        }
        static GameObject Cube(string name,Transform parent,Vector3 position,Vector3 scale,Color color)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;SetMaterial(go,color);return go;
        }
        static void SetMaterial(GameObject go,Color color)
        {
            string key=ColorUtility.ToHtmlStringRGB(color);string path=Generated+"/RobotMat_"+key+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(Shader.Find("Standard"));mat.color=color;mat.SetFloat("_Glossiness",.35f);AssetDatabase.CreateAsset(mat,path);}
            go.GetComponent<Renderer>().sharedMaterial=mat;
        }
        static LineRenderer MakeLine(string name,Transform parent,Vector3[] points,float width,Color color)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);var line=go.AddComponent<LineRenderer>();
            string path=Generated+"/RobotLine.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(Shader.Find("Sprites/Default"));AssetDatabase.CreateAsset(mat,path);}
            line.sharedMaterial=mat;line.useWorldSpace=false;line.positionCount=points.Length;line.SetPositions(points);line.widthMultiplier=width;line.startColor=line.endColor=color;line.numCapVertices=3;return line;
        }
        static Bounds GetBounds(GameObject go)
        {
            var renderers=go.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)return new Bounds(go.transform.position,Vector3.one);
            Bounds bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);return bounds;
        }
    }
}
