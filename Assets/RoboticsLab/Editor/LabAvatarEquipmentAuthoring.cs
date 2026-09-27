using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoboticsLab.Editor
{
    public static class LabAvatarEquipmentAuthoring
    {
        [MenuItem("Robot Lab/Match Shelf And Worn Helmet Sizes")]
        public static void MatchHelmetSizesInScene()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode first.");
            var player=Object.FindObjectOfType<LabThirdPerson>();
            MatchShelfHelmetSizes(player);
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            EditorSceneManager.SaveScene(player.gameObject.scene);
            Debug.Log("ROBOT_HELMET_SIZES_MATCHED");
        }

        public static void MatchShelfHelmetSizes(LabThirdPerson player)
        {
            if(!player || !player.head || !player.equipment || !player.wornHelmet)
                throw new InvalidOperationException("The fitted character helmet is required before sizing shelf helmets.");
            var rack=GameObject.Find("SafetyHelmetRack");
            if(!rack)throw new InvalidOperationException("SafetyHelmetRack was not found.");
            // The shelf and worn copies share the same mesh hierarchy. Match their world scale,
            // accounting for the avatar's scaled skeleton instead of copying its local scale.
            Vector3 size=Vector3.Scale(player.head.lossyScale,player.equipment.fittedLocalScale);
            foreach(Transform helmet in rack.transform)
            {
                if(helmet.name!="HelmetPickup"&&!helmet.name.StartsWith("SpareHelmet",StringComparison.Ordinal))continue;
                // Small interchangeable props must keep their own geometry instead of being
                // folded into a room-scale static batch when their fitted size is updated.
                foreach(var part in helmet.GetComponentsInChildren<Transform>(true))
                {
                    Undo.RecordObject(part.gameObject,"Keep safety helmet geometry independent");
                    GameObjectUtility.SetStaticEditorFlags(part.gameObject,0);
                }
                var renderers=helmet.GetComponentsInChildren<Renderer>(true);
                Bounds before=renderers[0].bounds;foreach(var r in renderers)before.Encapsulate(r.bounds);
                Undo.RecordObject(helmet,"Match safety helmet size");
                Vector3 parent=helmet.parent.lossyScale;
                helmet.localScale=new Vector3(size.x/parent.x,size.y/parent.y,size.z/parent.z);
                Bounds after=renderers[0].bounds;foreach(var r in renderers)after.Encapsulate(r.bounds);
                helmet.position+=new Vector3(before.center.x-after.center.x,before.min.y-after.min.y,before.center.z-after.center.z);
                EditorUtility.SetDirty(helmet);
                Debug.Log("ROBOT_SHELF_HELMET_MATCHED "+helmet.name+" worldScale="+helmet.lossyScale+" bottom="+before.min.y);
            }
        }

        [MenuItem("Robot Lab/Update Avatar Equipment And Walk")]
        public static void UpdateScene()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var player=Object.FindObjectOfType<LabThirdPerson>();
            if(!player) throw new InvalidOperationException("Open the robotics training room first.");
            var source=AssetDatabase.LoadAllAssetsAtPath("Assets/Jogging.fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
            player.animator.runtimeAnimatorController=LabAvatarAnimationBuilder.Build(player.animator,source);
            Fit(player);
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            EditorSceneManager.SaveScene(player.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("ROBOT_AVATAR_UPDATE_SAVED");
        }

        public static void Fit(LabThirdPerson player)
        {
            if(!player.animator || !player.animator.isHuman || !player.wornHelmet) throw new InvalidOperationException("A humanoid avatar and helmet are required.");
            LabAvatarBodyAuthoring.Apply(player);
            var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/RoboticsLab/Generated/TraineeIdle.anim");
            var preview=Object.Instantiate(player.animator.gameObject);
            preview.name="__HelmetFitPreview";
            preview.hideFlags=HideFlags.HideAndDontSave;
            preview.transform.SetParent(null,false);
            preview.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            preview.transform.localScale=player.animator.transform.lossyScale;
            try
            {
                var animator=preview.GetComponent<Animator>();
                animator.runtimeAnimatorController=null;
                idle.SampleAnimation(preview,0);
                var bodyPoints=HeadVertices(animator,null);
                Bounds body=new Bounds(bodyPoints[0],Vector3.zero);
                foreach(var p in bodyPoints)body.Encapsulate(p);
                float heightFactor=1.72f/body.size.y;
                player.visual.localScale*=heightFactor;
                preview.transform.localScale*=heightFactor;
                bodyPoints=HeadVertices(animator,null);
                body=new Bounds(bodyPoints[0],Vector3.zero);
                foreach(var p in bodyPoints)body.Encapsulate(p);
                var controller=player.GetComponent<CharacterController>();
                float restRoot=controller.skinWidth-(controller.center.y-controller.height*.5f);
                player.visual.localPosition=new Vector3(-body.center.x,.005f-restRoot-body.min.y,-body.center.z);
                var head=animator.GetBoneTransform(HumanBodyBones.Head);
                var points=HeadVertices(animator,head);
                if(points.Count<10)throw new InvalidOperationException("Could not measure the skinned head vertices.");
                Bounds all=new Bounds(points[0],Vector3.zero);
                foreach(var p in points)all.Encapsulate(p);
                var crown=points.Where(p=>p.y>all.min.y+all.size.y*.58f).ToList();
                Bounds skull=new Bounds(crown[0],Vector3.zero);
                foreach(var p in crown)skull.Encapsulate(p);
                float scale=Mathf.Clamp(skull.size.x*1.18f/.464f,.38f,.65f);
                Vector3 root=new Vector3(skull.center.x,all.max.y-all.size.y*.29f-.05f*scale,skull.center.z);
                var equipment=player.GetComponent<LabHelmetEquipment>();
                if(!equipment)equipment=player.gameObject.AddComponent<LabHelmetEquipment>();
                equipment.player=player;
                equipment.fittedLocalPosition=head.InverseTransformPoint(root);
                // Shelf geometry has its brim towards -Z; the wearer faces +Z.
                equipment.fittedLocalRotation=Quaternion.Inverse(head.rotation)*Quaternion.Euler(0,180,0);
                equipment.fittedLocalScale=new Vector3(scale/head.lossyScale.x,scale/head.lossyScale.y,scale/head.lossyScale.z);
                equipment.gripHalfWidth=.27f*scale;
                player.head=player.animator.GetBoneTransform(HumanBodyBones.Head);
                player.equipment=equipment;
                player.walkSpeed=1.6f;
                var helmet=player.wornHelmet.transform;
                var previousAnchor=helmet.parent;
                equipment.FitToHead();
                if(previousAnchor && previousAnchor.name=="HelmetAnchor" && previousAnchor.childCount==0)Object.DestroyImmediate(previousAnchor.gameObject);
                foreach(var renderer in player.wornHelmet.GetComponentsInChildren<Renderer>())renderer.gameObject.isStatic=false;
                player.wornHelmet.SetActive(false);
                MatchShelfHelmetSizes(player);
                EditorUtility.SetDirty(player);EditorUtility.SetDirty(equipment);
                if(Mathf.Abs(heightFactor-1)>.0001f)
                {
                    var source=AssetDatabase.LoadAllAssetsAtPath("Assets/Jogging.fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
                    player.animator.runtimeAnimatorController=LabAvatarAnimationBuilder.Build(player.animator,source);
                }
                ConfigureWalkGrounding(player,animator);
                Directory.CreateDirectory("Documentation");
                File.WriteAllText("Documentation/HelmetFit.txt","Head bounds: "+all+"\nCrown bounds: "+skull+"\nHelmet world scale: "+scale+"\nHead local offset: "+equipment.fittedLocalPosition+"\nHead local rotation: "+equipment.fittedLocalRotation.eulerAngles+"\nStanding body height: "+body.size.y+"\nVisual local offset: "+player.visual.localPosition);
                Debug.Log("ROBOT_HELMET_FITTED scale="+scale+" head="+all);
            }
            finally { Object.DestroyImmediate(preview); }
        }

        static void ConfigureWalkGrounding(LabThirdPerson player,Animator preview)
        {
            // Evaluate the same Humanoid state machine used in Play Mode. Direct
            // SampleAnimation can handle Humanoid root channels differently.
            var root=preview.transform;
            Vector3 scale=player.animator.transform.lossyScale;
            preview.runtimeAnimatorController=player.animator.runtimeAnimatorController;
            preview.applyRootMotion=false;
            preview.enabled=true;
            preview.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            preview.Rebind();
            Action restoreRoot=()=>
            {
                root.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                root.localScale=scale;
            };
            Func<string,float,float> minimumAt=(state,phase)=>
            {
                restoreRoot();
                preview.SetFloat("Speed",state=="Walk"?1:0);
                preview.Play(state,0,phase);
                preview.Update(0);
                // No root-motion displacement is allowed to accumulate between samples.
                restoreRoot();
                var points=HeadVertices(preview,null);
                if(points.Count==0)throw new InvalidOperationException("No CPU-skinned body vertices for grounding calibration.");
                return points.Min(p=>p.y);
            };
            float idleMinimum=minimumAt("Idle",0);
            const int intervals=64;
            var minima=new float[intervals+1];
            for(int i=0;i<=intervals;i++)minima[i]=minimumAt("Walk",i/(float)intervals);
            float walkMinimum=minima.Min(),walkMaximum=minima.Max();
            float worldOffset=idleMinimum-walkMinimum;
            if(float.IsNaN(worldOffset)||float.IsInfinity(worldOffset)||Mathf.Abs(worldOffset)>.35f)
                throw new InvalidOperationException("Unexpected walk-to-idle floor difference: "+worldOffset+" m. Inspect the source pose before applying an offset.");
            float worldPerLocalY=player.visual.parent?player.visual.parent.TransformVector(Vector3.up).y:1;
            if(Mathf.Abs(worldPerLocalY)<.01f)throw new InvalidOperationException("The visual parent must have an upright vertical axis.");
            var grounding=player.GetComponent<LabAvatarGrounding>();
            if(!grounding)grounding=player.gameObject.AddComponent<LabAvatarGrounding>();
            grounding.animator=player.animator;
            grounding.visual=player.visual;
            grounding.idleLocalY=player.visual.localPosition.y;
            grounding.walkLocalYOffset=worldOffset/worldPerLocalY;
            grounding.sampledSoleRangeMeters=walkMaximum-walkMinimum;
            grounding.configured=true;
            EditorUtility.SetDirty(grounding);
            var report=new List<string>
            {
                "Walk floor baseline calibration / "+DateTime.UtcNow.ToString("O"),
                "Sampling: same runtime Animator controller, root motion disabled, Animator.Play + Update(0), restored visual root before/after every sample.",
                "Measurement: full-body standard CPU skinning (all vertices); the lowest body surface is the sole in this avatar.",
                "Samples="+(intervals+1)+" over a complete Walk cycle, including the loop boundary.",
                "Idle sole in normalized preview world="+idleMinimum.ToString("F6"),
                "Walk sole range before fixed offset="+walkMinimum.ToString("F6")+" .. "+walkMaximum.ToString("F6"),
                "Fixed walk world Y offset="+worldOffset.ToString("F6")+"; local Y offset="+grounding.walkLocalYOffset.ToString("F6"),
                "Idle visual local Y="+grounding.idleLocalY.ToString("F6"),
                "Predicted settled-world sole range after offset="+(.005f).ToString("F6")+" .. "+(.005f+walkMaximum-walkMinimum).ToString("F6"),
                "The prediction assumes the existing Idle fit rests at y=0.005 m. Confirm it with live CPU skinning and screenshots.",
                "The original clip and every joint curve are untouched; one constant visual-root correction blends with the Walk state transition weight.",
                "No per-frame foot snapping or bone correction is applied. Natural sole-height variation, including any flight phase, is preserved."
            };
            for(int i=0;i<=intervals;i++)report.Add("PHASE "+(i/(float)intervals).ToString("F5")+" rawSoleY="+minima[i].ToString("F6")
                +" predictedSettledWorldY="+(.005f+minima[i]-idleMinimum+worldOffset).ToString("F6"));
            Directory.CreateDirectory("Documentation");
            File.WriteAllLines("Documentation/WalkGroundingCalibration.txt",report);
            Debug.Log("ROBOT_WALK_GROUNDING_READY worldOffset="+worldOffset.ToString("F6")+" predictedSoleRange=.005000.."+(.005f+walkMaximum-walkMinimum).ToString("F6"));
        }

        public static List<Vector3> HeadVertices(Animator animator,Transform head)
        {
            var points=new List<Vector3>();
            foreach(var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if(!renderer.sharedMesh)continue;
                var weights=renderer.sharedMesh.boneWeights;
                var bones=renderer.bones;
                var bindposes=renderer.sharedMesh.bindposes;
                var matrices=new Matrix4x4[bones.Length];
                for(int i=0;i<bones.Length;i++)if(bones[i]&&i<bindposes.Length)matrices[i]=bones[i].localToWorldMatrix*bindposes[i];
                {
                    var vertices=renderer.sharedMesh.vertices;
                    for(int i=0;i<Mathf.Min(vertices.Length,weights.Length);i++)
                    {
                        var w=weights[i];float total=0;
                        if(IsHead(bones,w.boneIndex0,head))total+=w.weight0;
                        if(IsHead(bones,w.boneIndex1,head))total+=w.weight1;
                        if(IsHead(bones,w.boneIndex2,head))total+=w.weight2;
                        if(IsHead(bones,w.boneIndex3,head))total+=w.weight3;
                        if(head==null || total>.5f)
                        {
                            var v=vertices[i];
                            points.Add(matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+
                                matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+
                                matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+
                                matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3);
                        }
                    }
                }
            }
            return points;
        }
        static bool IsHead(Transform[] bones,int index,Transform head){return head&&index>=0&&index<bones.Length&&bones[index]&&(bones[index]==head||bones[index].IsChildOf(head));}
    }
}
