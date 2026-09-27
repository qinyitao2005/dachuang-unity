using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoboticsLab.Editor
{
    /// <summary>Compares the real equip/release sequence against a separate clean Animator.</summary>
    public static class LabHelmetReleaseValidation
    {
        static IEnumerator<int> routine;
        static List<string> report;
        static readonly HumanBodyBones[] BoneIds = new[]
        {
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand
        }.Concat(Enumerable.Range((int)HumanBodyBones.LeftThumbProximal,
            (int)HumanBodyBones.RightLittleDistal-(int)HumanBodyBones.LeftThumbProximal+1).Select(i=>(HumanBodyBones)i)).ToArray();

        [MenuItem("Robot Lab/Diagnostics/Validate Helmet Release")]
        public static void Run()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Start a fresh Play session before checking helmet release.");
            if(routine!=null)throw new InvalidOperationException("Helmet release validation is already running.");
            var app=Object.FindObjectOfType<LabTrainingApp>();
            if(!app || !app.player || !app.player.equipment || app.HelmetWorn || app.player.IsEquippingHelmet)
                throw new InvalidOperationException("The test requires a fresh, unequipped training character.");
            report=new List<string>{"Helmet release validation / "+DateTime.UtcNow.ToString("O"),
                "Independent baseline: a pre-equipment clone with the same Animator controller, Rebind/Idle/Walk evaluation, and no equipment behaviour.",
                "Local quaternion and anatomical-axis twist measurements cover upper/lower arms, hands and all mapped fingers including thumbs."};
            routine=Checks(app);EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }

        static void Tick()
        {
            try
            {
                if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play Mode stopped before validation completed.");
                if(routine!=null&&routine.MoveNext())return;
            }
            catch(Exception e){report.Add("FAIL: "+e);Debug.LogException(e);}
            EditorApplication.update-=Tick;
            if(routine!=null)routine.Dispose();routine=null;
            Directory.CreateDirectory("Documentation");
            File.WriteAllLines("Documentation/HelmetReleaseValidation.txt",report,new UTF8Encoding(false));
            Debug.Log("ROBOT_HELMET_RELEASE_VALIDATION_FINISHED "+string.Join(" | ",report.Where(s=>s.StartsWith("PASS")||s.StartsWith("FAIL"))));
        }

        static IEnumerator<int> Checks(LabTrainingApp app)
        {
            var player=app.player;
            var animator=player.animator;
            bool playerEnabled=player.enabled;
            float originalSpeed=animator.speed;
            GameObject clone=null;
            try
            {
                app.ClosePanel();
                Vector3 pickup=app.helmetPickup.position;
                player.Teleport(new Vector3(pickup.x-.85f,.08f,pickup.z),90);
                player.enabled=false;
                animator.speed=1;animator.SetFloat("Speed",0);animator.Play("Idle",0,0);animator.Update(0);
                int settled=Time.frameCount+3;
                while(Time.frameCount<settled)yield return 0;
                // Clone before Begin: its initial transforms cannot contain our tested IK pose.
                clone=Object.Instantiate(animator.gameObject);
                clone.name="__IndependentHelmetReleaseReference";clone.hideFlags=HideFlags.HideAndDontSave;
                clone.transform.SetParent(null,false);
                clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                clone.transform.localScale=animator.transform.lossyScale;
                foreach(var r in clone.GetComponentsInChildren<Renderer>(true))r.enabled=false;
                foreach(var c in clone.GetComponentsInChildren<Collider>(true))c.enabled=false;
                foreach(var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
                var reference=clone.GetComponent<Animator>();
                reference.runtimeAnimatorController=animator.runtimeAnimatorController;
                reference.applyRootMotion=false;reference.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                reference.enabled=true;reference.Rebind();
                Evaluate(reference,"Idle",0);
                var liveBones=BoneIds.Select(animator.GetBoneTransform).ToArray();
                var referenceBones=BoneIds.Select(reference.GetBoneTransform).ToArray();
                var idlePose=referenceBones.Select(t=>t?t.localRotation:Quaternion.identity).ToArray();
                var axes=referenceBones.Select(LongitudinalAxis).ToArray();
                Check(liveBones.Count(t=>t)>=30,"Mapped arms, hands and fingers available; count="+liveBones.Count(t=>t));
                Check(MaxDifference(liveBones,idlePose)<1,"Pre-equip live Idle matches the independent Animator baseline");
                var previous=Snapshot(liveBones);
                float start=Time.time,lastTime=Time.time;
                int lastFrame=-1,shot=0;
                float maxJump=0,maxReleaseJump=0,maxReleaseTwist=0;
                int abruptJumps=0;
                float[] shotTimes={2.4f,2.7f,3.1f,4f};
                string[] shotNames={"HelmetRelease240","HelmetRelease270","HelmetRelease310","HelmetRelease400"};
                app.EquipHelmet();
                Check(player.equipment.IsEquipping,"The actual training app started the equip sequence");
                double deadline=EditorApplication.timeSinceStartup+20;
                while(Time.time-start<4.2f || shot<shotTimes.Length)
                {
                    if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("The live helmet sequence stopped advancing.");
                    if(Time.frameCount==lastFrame){yield return 0;continue;}
                    lastFrame=Time.frameCount;
                    float elapsed=Time.time-start,dt=Time.time-lastTime;
                    float jump=MaxDifference(liveBones,previous);
                    maxJump=Mathf.Max(maxJump,jump);
                    if(dt>0&&dt<.1f&&jump>120)abruptJumps++;
                    if(elapsed>=2.25f)
                    {
                        float twist=MaxTwist(liveBones,idlePose,axes);
                        maxReleaseJump=Mathf.Max(maxReleaseJump,jump);maxReleaseTwist=Mathf.Max(maxReleaseTwist,twist);
                        report.Add("RELEASE_FRAME t="+elapsed.ToString("F5")+" dt="+dt.ToString("F5")+" frame="+Time.frameCount
                            +" armFromIdleDeg="+MaxDifference(liveBones,idlePose,0,6).ToString("F5")
                            +" fingersFromIdleDeg="+MaxDifference(liveBones,idlePose,6).ToString("F5")
                            +" longitudinalTwistFromIdleDeg="+twist.ToString("F5")+" largestFrameStepDeg="+jump.ToString("F5"));
                    }
                    previous=Snapshot(liveBones);lastTime=Time.time;
                    if(shot<shotTimes.Length&&elapsed>=shotTimes[shot])
                    {
                        Capture(player,shotNames[shot]);
                        report.Add("CAPTURE "+shotNames[shot]+" atSeconds="+elapsed.ToString("F5"));shot++;
                    }
                    yield return 0;
                }
                Check(app.HelmetWorn&&player.equipment.IsEquipped&&!player.equipment.IsEquipping,"Equip completed and the helmet remains fitted");
                Check(abruptJumps==0,"No >120 degree local-rotation jumps in a live frame shorter than 100ms; count="+abruptJumps);
                report.Add("INFO: maxSequenceFrameStepDeg="+maxJump.ToString("F6")+" maxReleaseFrameStepDeg="+maxReleaseJump.ToString("F6")
                    +" maxReleaseTwistFromIdleDeg="+maxReleaseTwist.ToString("F6")+" (absolute pose angles are reported, not treated as unnatural by themselves)");
                Evaluate(reference,"Idle",animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
                ReportPose("Post-release Idle",liveBones,referenceBones,1f);
                foreach(float phase in new[]{0f,.25f,.5f,.75f})
                {
                    animator.speed=1;animator.SetFloat("Speed",1);animator.Play("Walk",0,phase);animator.Update(0);animator.speed=0;
                    Evaluate(reference,"Walk",phase);
                    int target=Time.frameCount+3;
                    double until=EditorApplication.timeSinceStartup+8;
                    while(Time.frameCount<target)
                    {
                        if(EditorApplication.timeSinceStartup>until)throw new TimeoutException("No rendered frames for the post-equip walk pose.");
                        yield return 0;
                    }
                    ReportPose("Post-release Walk "+phase.ToString("F2"),liveBones,referenceBones,1f);
                }
                animator.speed=1;animator.SetFloat("Speed",0);animator.Play("Idle",0,0);animator.Update(0);
                Evaluate(reference,"Idle",0);
                settled=Time.frameCount+4;while(Time.frameCount<settled)yield return 0;
                ReportPose("Returned-to-Idle after Walk",liveBones,referenceBones,1f);
                report.Add("PASS: Completed independent release, finger recovery and post-equipment locomotion checks.");
            }
            finally
            {
                if(clone)Object.DestroyImmediate(clone);
                if(animator){animator.speed=originalSpeed;animator.SetFloat("Speed",0);animator.Play("Idle",0,0);animator.Update(0);}
                if(player)player.enabled=playerEnabled;
            }
        }

        static void Evaluate(Animator animator,string state,float phase)
        {
            animator.speed=1;animator.SetFloat("Speed",state=="Walk"?1:0);animator.Play(state,0,phase);animator.Update(0);animator.speed=0;
        }

        static void ReportPose(string context,Transform[] live,Transform[] reference,float tolerance)
        {
            var expected=Snapshot(reference);
            float max=MaxDifference(live,expected);
            report.Add("POSE "+context+" maxLocalErrorDeg="+max.ToString("F6"));
            for(int i=0;i<live.Length;i++)if(live[i]&&reference[i])
                report.Add("  "+BoneIds[i]+" localErrorDeg="+Quaternion.Angle(live[i].localRotation,expected[i]).ToString("F6"));
            Check(max<tolerance,context+" arms/hands/all fingers match the independent animation pose within "+tolerance+" degree");
        }

        static Quaternion[] Snapshot(Transform[] bones)=>bones.Select(t=>t?t.localRotation:Quaternion.identity).ToArray();
        static float MaxDifference(Transform[] bones,Quaternion[] rotations,int begin=0,int end=-1)
        {
            float result=0;if(end<0)end=bones.Length;
            for(int i=begin;i<end;i++)if(bones[i])result=Mathf.Max(result,Quaternion.Angle(bones[i].localRotation,rotations[i]));
            return result;
        }
        static Vector3 LongitudinalAxis(Transform bone)
        {
            if(!bone)return Vector3.up;
            foreach(Transform child in bone)if(child.localPosition.sqrMagnitude>.000001f)return child.localPosition.normalized;
            return Vector3.up;
        }
        static float MaxTwist(Transform[] bones,Quaternion[] reference,Vector3[] axes)
        {
            float result=0;
            for(int i=0;i<bones.Length;i++)if(bones[i])
            {
                Quaternion delta=Quaternion.Inverse(reference[i])*bones[i].localRotation;
                float projection=Vector3.Dot(new Vector3(delta.x,delta.y,delta.z),axes[i]);
                float angle=2*Mathf.Atan2(projection,delta.w)*Mathf.Rad2Deg;
                result=Mathf.Max(result,Mathf.Abs(Mathf.DeltaAngle(0,angle)));
            }
            return result;
        }

        static void Capture(LabThirdPerson player,string name)
        {
            var go=new GameObject("__HelmetReleaseCamera"){hideFlags=HideFlags.HideAndDontSave};
            RenderTexture rt=null;Texture2D texture=null;RenderTexture previous=RenderTexture.active;
            try
            {
                var camera=go.AddComponent<Camera>();camera.enabled=false;camera.nearClipPlane=.03f;camera.farClipPlane=60;camera.fieldOfView=40;
                camera.clearFlags=CameraClearFlags.Skybox;
                Vector3 target=player.transform.position+Vector3.up*1.22f;
                Vector3 forward=player.visual.forward,right=player.visual.right;
                camera.transform.position=target+forward*2.25f+right*1.05f+Vector3.up*.16f;
                // Near the east wall, stay in the room and view both hanging hands from the aisle side.
                if(player.transform.position.x>5.5f)camera.transform.position=target+forward*.5f-right*2.35f+Vector3.up*.1f;
                camera.transform.LookAt(target);
                rt=new RenderTexture(1600,1600,24){antiAliasing=4};camera.targetTexture=rt;camera.Render();
                RenderTexture.active=rt;texture=new Texture2D(1600,1600,TextureFormat.RGB24,false);
                texture.ReadPixels(new Rect(0,0,1600,1600),0,0);texture.Apply();
                Directory.CreateDirectory("Documentation/Screenshots");
                File.WriteAllBytes("Documentation/Screenshots/"+name+".png",texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;
                if(go)go.GetComponent<Camera>().targetTexture=null;
                if(texture)Object.DestroyImmediate(texture);
                if(rt){rt.Release();Object.DestroyImmediate(rt);}
                Object.DestroyImmediate(go);
            }
        }
        static void Check(bool condition,string message)
        {
            report.Add((condition?"PASS: ":"FAIL: ")+message);
            if(!condition)throw new InvalidOperationException(message);
        }
    }
}
