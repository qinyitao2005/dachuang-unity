using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace RoboticsLab.Editor
{
    public static class LabOriginalEquipmentAssets
    {
        const string Folder="Assets/RoboticsLab/Generated";
        static readonly string[] Sources={"Draw","多工位实训平台 1","多工位实训平台 2"};
        public static string CellPath(int i)=>Folder+"/OriginalWorkcell"+(i+1)+".prefab";
        public const string RackPath=Folder+"/OriginalStorageRack.prefab";

        [MenuItem("Robot Lab/Extract Complete Original Workcells")]
        public static void Extract()
        {
            if(Enumerable.Range(0,3).All(i=>AssetDatabase.LoadAssetAtPath<GameObject>(CellPath(i)))&&AssetDatabase.LoadAssetAtPath<GameObject>(RackPath))return;
            Directory.CreateDirectory(Folder);
            var active=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/SampleScene.unity");
            bool opened=!scene.IsValid()||!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity",OpenSceneMode.Additive);
            if(active.IsValid())UnityEngine.SceneManagement.SceneManager.SetActiveScene(active);
            var preview=EditorSceneManager.NewPreviewScene();
            try
            {
                var group=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).First(t=>t.name=="多工位机器人集合");
                for(int i=0;i<3;i++)
                {
                    var source=group.Cast<Transform>().First(t=>t.name.Trim()==Sources[i]);
                    var container=new GameObject("OriginalWorkcell"+(i+1));
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(container,preview);
                    try
                    {
                        var clone=Object.Instantiate(source.gameObject);
                        clone.name="OriginalAssembly";
                        clone.transform.SetParent(container.transform,true);
                        clone.transform.SetPositionAndRotation(source.position,source.rotation);
                        clone.transform.localScale=source.lossyScale;
                        foreach(var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))if(behaviour)behaviour.enabled=false;
                        foreach(var animator in clone.GetComponentsInChildren<Animator>(true))animator.enabled=false;
                        foreach(var camera in clone.GetComponentsInChildren<Camera>(true))camera.enabled=false;
                        foreach(var listener in clone.GetComponentsInChildren<AudioListener>(true))listener.enabled=false;
                        foreach(var body in clone.GetComponentsInChildren<Rigidbody>(true)){body.isKinematic=true;body.useGravity=false;}
                        foreach(var collider in clone.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
                        Bounds bounds=BoundsOf(clone);
                        clone.transform.position-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
                        var arm=clone.GetComponentsInChildren<Transform>(true).First(t=>t.name=="机械臂");
                        foreach(var renderer in clone.GetComponentsInChildren<Renderer>(true))
                            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,renderer.transform.IsChildOf(arm)?0:StaticEditorFlags.BatchingStatic);
                        PrefabUtility.SaveAsPrefabAsset(container,CellPath(i));
                        Debug.Log("ROBOT_ORIGINAL_WORKCELL_EXTRACTED "+Sources[i]+" meshes="+clone.GetComponentsInChildren<MeshFilter>(true).Length+" bounds="+BoundsOf(clone));
                    }
                    finally{Object.DestroyImmediate(container);}
                }
                var rackGroup=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).First(t=>t.name=="架子");
                var rackSource=rackGroup.Cast<Transform>().First(t=>t.GetComponentsInChildren<MeshFilter>(true).Any(m=>m.sharedMesh&&AssetDatabase.GetAssetPath(m.sharedMesh).EndsWith("架子2.fbx",StringComparison.OrdinalIgnoreCase)));
                var rackContainer=new GameObject("OriginalStorageRack");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rackContainer,preview);
                try
                {
                    var rack=Object.Instantiate(rackSource.gameObject);rack.name="OriginalShelfAssembly";
                    rack.transform.SetParent(rackContainer.transform,true);
                    rack.transform.SetPositionAndRotation(rackSource.position,rackSource.rotation);
                    rack.transform.localScale=rackSource.lossyScale;
                    foreach(var c in rack.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
                    var rb=BoundsOf(rack);
                    rack.transform.position-=new Vector3(rb.center.x,rb.min.y,rb.center.z);
                    foreach(var r in rack.GetComponentsInChildren<Renderer>(true))GameObjectUtility.SetStaticEditorFlags(r.gameObject,StaticEditorFlags.BatchingStatic);
                    PrefabUtility.SaveAsPrefabAsset(rackContainer,RackPath);
                    Debug.Log("ROBOT_ORIGINAL_RACK_EXTRACTED meshes="+rack.GetComponentsInChildren<MeshFilter>(true).Length+" bounds="+BoundsOf(rack));
                }
                finally{Object.DestroyImmediate(rackContainer);}
                AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
                if(opened)EditorSceneManager.CloseScene(scene,true);
                if(active.IsValid())UnityEngine.SceneManagement.SceneManager.SetActiveScene(active);
            }
        }

        public static Bounds BoundsOf(GameObject go)
        {
            var renderers=go.GetComponentsInChildren<Renderer>(true).Where(r=>r is MeshRenderer||r is SkinnedMeshRenderer).ToArray();
            if(renderers.Length==0)throw new InvalidOperationException("No source mesh renderers in "+go.name);
            Bounds b=renderers[0].bounds;
            foreach(var r in renderers)b.Encapsulate(r.bounds);
            return b;
        }
    }
}
