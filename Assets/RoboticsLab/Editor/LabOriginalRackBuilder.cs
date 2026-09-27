#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoboticsLab.Editor
{
    /// <summary>Places the preserved original orange rack without replacing pickup references.</summary>
    public static class LabOriginalRackBuilder
    {
        const string PrefabPath = "Assets/RoboticsLab/Generated/OriginalStorageRack.prefab";
        const string FrontName = "OriginalSafetyStorageRack";
        const string RearName = "OriginalRearStorageRack";

        public static void Replace()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Stop Play Mode before replacing the storage racks.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab)
                throw new InvalidOperationException("Export the original rack first: " + PrefabPath);
            var environment = GameObject.Find("RoboticsLab_Environment");
            if (!environment)
                throw new InvalidOperationException("The robotics lab environment must exist before replacing its racks.");
            Transform safety = environment.transform.Find("SafetyHelmetRack");
            Transform storage = environment.transform.Find("RearStorage");
            if (!safety || !storage)
                throw new InvalidOperationException("Expected SafetyHelmetRack and RearStorage under RoboticsLab_Environment.");

            // Keep these exact objects: TrainingSession and helmet authoring retain their references.
            var helmets = new List<Transform>();
            foreach (Transform child in safety)
                if (child.name == "HelmetPickup" || child.name.StartsWith("SpareHelmet",StringComparison.Ordinal))
                    helmets.Add(child);
            if (!helmets.Exists(h => h.name == "HelmetPickup"))
                throw new InvalidOperationException("HelmetPickup is missing; the equipment interaction must be preserved.");
            helmets.Sort((a,b) =>
            {
                if (a == b) return 0;
                if (a.name == "HelmetPickup") return -1;
                if (b.name == "HelmetPickup") return 1;
                return string.CompareOrdinal(a.name,b.name);
            });

            var obsolete = new List<GameObject>();
            foreach (Transform child in safety)
                if (!helmets.Contains(child)) obsolete.Add(child.gameObject);
            foreach (var child in obsolete) Object.DestroyImmediate(child);

            // The source is 5.83 x 2.88 x 1.152 m. A uniform scale preserves every original detail.
            // Long edge follows Z and the open face points into the room toward -X.
            GameObject front = Place(prefab,safety,FrontName,new Vector3(7.3f,0,3.2f),90f,3.8f);
            Bounds frontBounds = RenderBounds(front);
            if (frontBounds.max.x > 7.8f)
            {
                front.transform.position += Vector3.left*(frontBounds.max.x-7.8f);
                frontBounds = RenderBounds(front);
            }
            float beamTop = AccessibleBeamTop(front,frontBounds);
            const float trayThickness = .018f;
            float trayWidth = Mathf.Max(.35f,frontBounds.size.x-.085f);
            float trayLength = frontBounds.size.z-.14f;
            var tray = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tray.name = "HelmetSupportTray";
            tray.transform.SetParent(safety,false);
            tray.transform.position = new Vector3(frontBounds.center.x,beamTop+trayThickness*.5f,frontBounds.center.z);
            tray.transform.localScale = new Vector3(trayWidth,trayThickness,trayLength);
            tray.GetComponent<Renderer>().sharedMaterial = TrayMaterial();
            tray.isStatic = true;

            // All four helmets share an accessible shelf. The first remains nearest the doorway.
            float availableLength = frontBounds.size.z-.95f;
            for (int i=0;i<helmets.Count;i++)
            {
                Transform helmet = helmets[i];
                float u = helmets.Count == 1 ? .5f : i/(float)(helmets.Count-1);
                float z = frontBounds.center.z + Mathf.Lerp(-availableLength*.5f,availableLength*.5f,u);
                helmet.rotation = Quaternion.Euler(0,90,0);
                helmet.position = new Vector3(frontBounds.center.x,beamTop+trayThickness,z);
                Bounds b = RenderBounds(helmet.gameObject);
                helmet.position += new Vector3(frontBounds.center.x-b.center.x,beamTop+trayThickness-b.min.y,z-b.center.z);
                EditorUtility.SetDirty(helmet);
            }

            // Only the three generic rear cabinets are replaced; classroom furniture and wall text stay.
            foreach (string name in new[]{"ToolCabinet_0","ToolCabinet_1","ToolCabinet_2",RearName})
            {
                Transform old = storage.Find(name);
                if (old) Object.DestroyImmediate(old.gameObject);
            }
            GameObject rear = Place(prefab,storage,RearName,new Vector3(-3.5f,0,20.9f),0f,6f);
            Bounds rearBounds = RenderBounds(rear);
            EditorSceneManager.MarkSceneDirty(environment.scene);
            Debug.Log("ROBOT_ORIGINAL_RACKS_READY front="+frontBounds+" shelfTop="+(beamTop+trayThickness).ToString("F3")+
                " rear="+rearBounds+" preservedHelmetObjects="+helmets.Count);
        }

        static GameObject Place(GameObject prefab,Transform parent,string name,Vector3 bottomCentre,float yaw,float length)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);
            instance.name = name;
            instance.transform.SetPositionAndRotation(bottomCentre,Quaternion.Euler(0,yaw,0));
            Bounds original = RenderBounds(instance);
            float longest = Mathf.Max(original.size.x,original.size.z);
            if (longest < .001f) throw new InvalidOperationException("The exported original rack has no usable render bounds.");
            instance.transform.localScale *= length/longest;
            Bounds scaled = RenderBounds(instance);
            instance.transform.position += bottomCentre-new Vector3(scaled.center.x,scaled.min.y,scaled.center.z);
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            EnsureRackCollider(instance);
            return instance;
        }

        static float AccessibleBeamTop(GameObject rack,Bounds bounds)
        {
            float best = float.NaN;
            float error = float.PositiveInfinity;
            // The preserved source rack has merged renderers, so inspect real top faces first.
            // Transform all three vertices before the cross product: the imported hierarchy
            // includes non-uniform scales, which make local face areas/normals unsuitable here.
            foreach (var filter in rack.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (!mesh) continue;
                Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
                // The Editor API intentionally bypasses the imported mesh's read/write flag.
                // Dispose snapshots and temporary arrays without changing the source FBX settings.
                using (var snapshots = MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    var data = snapshots[0];
                    if(data.vertexCount==0)continue;
                    using (var vertices = new NativeArray<Vector3>(data.vertexCount,Allocator.Temp))
                    {
                        data.GetVertices(vertices);
                        for (int subMesh=0;subMesh<data.subMeshCount;subMesh++)
                        {
                            var sub = data.GetSubMesh(subMesh);
                            if (sub.topology != MeshTopology.Triangles) continue;
                            using (var triangles = new NativeArray<int>(sub.indexCount,Allocator.Temp))
                            {
                                data.GetIndices(triangles,subMesh,true);
                                for (int i=0;i+2<triangles.Length;i+=3)
                                {
                                    Vector3 a = toWorld.MultiplyPoint3x4(vertices[triangles[i]]);
                                    Vector3 b = toWorld.MultiplyPoint3x4(vertices[triangles[i+1]]);
                                    Vector3 c = toWorld.MultiplyPoint3x4(vertices[triangles[i+2]]);
                                    float ymin = Mathf.Min(a.y,Mathf.Min(b.y,c.y));
                                    float ymax = Mathf.Max(a.y,Mathf.Max(b.y,c.y));
                                    float height = (a.y+b.y+c.y)/3f;
                                    if (height < .72f || height > 1.4f || ymax-ymin > .003f) continue;
                                    Vector3 cross = Vector3.Cross(b-a,c-a);
                                    float doubleArea = cross.magnitude;
                                    // Large, upward horizontal faces identify beams, not tiny post end caps.
                                    if (doubleArea < .06f || cross.y/doubleArea < .999f) continue;
                                    float difference = Mathf.Abs(height-1.08f);
                                    if (difference < error) { error=difference; best=height; }
                                }
                            }
                        }
                    }
                }
            }
            if (!float.IsNaN(best))
            {
                Debug.Log("ROBOT_RACK_BEAM_GEOMETRY top="+best.ToString("F4"));
                return best;
            }
            foreach (var renderer in rack.GetComponentsInChildren<Renderer>(true))
            {
                Bounds b = renderer.bounds;
                // Reject posts and footings; prefer original long horizontal members at hand height.
                if (b.size.y > .18f || b.max.y < .72f || b.max.y > 1.4f) continue;
                if (b.size.z < bounds.size.z*.15f && b.size.x < bounds.size.x*.6f) continue;
                float difference = Mathf.Abs(b.max.y-1.08f);
                if (difference < error) { error=difference; best=b.max.y; }
            }
            if (!float.IsNaN(best)) return best;
            // Heavily segmented meshes may expose neither sufficiently large top faces nor
            // separate beam bounds. Keep the existing explicit fallback for that exceptional case.
            Debug.LogWarning("ROBOT_RACK_SHELF_FALLBACK: no large horizontal face or separate beam found; inspect the support tray at y=1.05.");
            return Mathf.Min(1.05f,bounds.max.y-.35f);
        }

        static void EnsureRackCollider(GameObject rack)
        {
            if (rack.GetComponentInChildren<Collider>(true)) return;
            Bounds world = RenderBounds(rack);
            Bounds local = new Bounds(rack.transform.InverseTransformPoint(world.center),Vector3.zero);
            for (int x=-1;x<=1;x+=2) for (int y=-1;y<=1;y+=2) for (int z=-1;z<=1;z+=2)
                local.Encapsulate(rack.transform.InverseTransformPoint(world.center+Vector3.Scale(world.extents,new Vector3(x,y,z))));
            var collider = rack.AddComponent<BoxCollider>();
            collider.center=local.center; collider.size=local.size;
        }

        static Material TrayMaterial()
        {
            const string path = "Assets/RoboticsLab/Generated/Lab_RackSupportTray.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Standard"));
                material.name="Lab_RackSupportTray";
                material.color=new Color(.79f,.82f,.83f);
                material.SetFloat("_Metallic",.35f);material.SetFloat("_Glossiness",.25f);
                AssetDatabase.CreateAsset(material,path);
            }
            return material;
        }

        static Bounds RenderBounds(GameObject gameObject)
        {
            var renderers = gameObject.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("No renderers found on " + gameObject.name);
            Bounds result = renderers[0].bounds;
            for (int i=1;i<renderers.Length;i++) result.Encapsulate(renderers[i].bounds);
            return result;
        }
    }
}
#endif
