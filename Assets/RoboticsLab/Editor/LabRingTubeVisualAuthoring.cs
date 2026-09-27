using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RoboticsLab.Editor
{
    /// <summary>Opaque, physically sized tube visuals; training geometry and root poses are unchanged.</summary>
    public static class LabRingTubeVisualAuthoring
    {
        const string Folder = "Assets/RoboticsLab/Generated/RingSolid";
        const int Sides = 12, RingSegments = 64;

        sealed class MeshData
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();
            public void Add(Vector3 vertex, Vector3 normal, Vector2 texcoord)
            { vertices.Add(vertex); normals.Add(normal.normalized); uv.Add(texcoord); }
            public void Tri(int a, int b, int c) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
        }

        [MenuItem("Robot Lab/Author Solid Ring And Wire")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before authoring solid tube visuals.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != LabSceneBuilder.ScenePath) throw new InvalidOperationException("Open RoboticsTrainingRoom first.");
            var stations = UnityEngine.Object.FindObjectsOfType<LabRobotStation>(true)
                .Where(s => s.gameObject.scene == scene).OrderBy(s => s.stationIndex).ToArray();
            if (stations.Length != 3) throw new InvalidOperationException("Expected three existing training stations.");
            var report = new StringBuilder("Solid ring and wire authoring / " + DateTime.UtcNow.ToString("O") + "\n");
            report.AppendLine("Opaque Standard meshes only; source wirePoints, radii, tool/root transforms and collision rules unchanged.");
            foreach (LabRobotStation station in stations)
            {
                BuildStation(station);
                Mesh wire = station.wireRoot.Find("SteelGuideSolid").GetComponent<MeshFilter>().sharedMesh;
                Mesh ring = station.ringRoot.Find("RingRimSolid").GetComponent<MeshFilter>().sharedMesh;
                report.AppendLine("STATION " + station.stationIndex + " sourcePoints=" + station.wirePoints.Length
                    + " wire radius=" + station.wireRadius.ToString("F7") + " m; vertices=" + wire.vertexCount
                    + "; triangles=" + wire.triangles.Length / 3 + "; cross-section sides=12; both ends capped");
                report.AppendLine(" ring R=" + station.ringRadius.ToString("F7") + " m; tube r=" + station.ringTubeRadius.ToString("F7")
                    + " m; vertices=" + ring.vertexCount + "; triangles=" + ring.triangles.Length / 3 + "; major/minor segments=64/12");
                report.AppendLine(" top grasp centreline local=(0," + station.ringRadius.ToString("F7") + ",0); real tube faces at z=+/-"
                    + station.ringTubeRadius.ToString("F7") + "; legacy LineRenderers retained and disabled");
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory("Documentation");
            report.AppendLine("PASS: finite meshes, outward winding, unit normals, unchanged physical radii, opaque depth-writing materials and renderer references authored. Review/save the scene and verify runtime colour updates separately.");
            File.WriteAllText("Documentation/SolidRingAuthoring.txt", report.ToString(), new UTF8Encoding(false));
            Debug.Log("ROBOT_SOLID_RING_WIRE_READY\n" + report);
        }

        /// <summary>Also called after LabRobotBuilder creates or replaces an individual station.</summary>
        public static void BuildStation(LabRobotStation station)
        {
            if (!station || !station.wireRoot || !station.ringRoot || station.wirePoints == null || station.wirePoints.Length < 2)
                throw new InvalidOperationException("Existing ring/wire roots and authored path are required.");
            if (station.wireRadius <= 0 || station.ringTubeRadius <= 0 || station.ringRadius <= station.ringTubeRadius)
                throw new InvalidOperationException("Invalid existing tube dimensions.");
            if ((station.ringRoot.lossyScale - Vector3.one).sqrMagnitude > .000001f)
                throw new InvalidOperationException("Ring root must retain the unit world scale expected by its rigid-grip and contact calculations.");
            EnsureFolder(Folder);
            var serialized = new SerializedObject(station);
            SerializedProperty reference = serialized.FindProperty("ringSolidRenderer");
            if (reference == null) throw new InvalidOperationException("Import LabRobotStation.ringSolidRenderer before authoring solid visuals.");
            Material steel = MaterialAsset("SteelGuide", new Color(.43f, .49f, .53f, 1), .62f, .44f);
            Material orange = MaterialAsset("OrangeRing", new Color(1, .7f, .15f, 1), .18f, .38f);
            Mesh wireMesh = Persist("Station" + (station.stationIndex + 1).ToString("D2") + "_SteelGuide", MakeWire(station));
            Mesh ringMesh = Persist("Station" + (station.stationIndex + 1).ToString("D2") + "_RingRim", MakeRing(station.ringRadius, station.ringTubeRadius));
            MeshChild(station.wireRoot, "SteelGuideSolid", wireMesh, steel);
            Renderer ringRenderer = MeshChild(station.ringRoot, "RingRimSolid", ringMesh, orange);
            if (station.wireRenderer) { station.wireRenderer.enabled = false; EditorUtility.SetDirty(station.wireRenderer); }
            if (station.ringRenderer) { station.ringRenderer.enabled = false; EditorUtility.SetDirty(station.ringRenderer); }
            reference.objectReferenceValue = ringRenderer;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(station);
            if (station.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(station.gameObject.scene);
        }

        static MeshData MakeWire(LabRobotStation station)
        {
            var points = new List<Vector3>();
            foreach (Vector3 point in station.wirePoints)
            {
                Vector3 world = station.transform.TransformPoint(point);
                if (!Finite(world)) throw new InvalidOperationException("The steel guide contains a non-finite point.");
                if (points.Count == 0 || (world - points[points.Count - 1]).sqrMagnitude > 1e-14f) points.Add(world);
            }
            if (points.Count < 2) throw new InvalidOperationException("The steel guide has no length.");
            var mesh = new MeshData();
            var tangents = new Vector3[points.Count];
            var normals = new Vector3[points.Count];
            var binormals = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 before = i > 0 ? (points[i] - points[i - 1]).normalized : (points[1] - points[0]).normalized;
                Vector3 after = i + 1 < points.Count ? (points[i + 1] - points[i]).normalized : before;
                tangents[i] = (before + after).normalized;
                if (tangents[i].sqrMagnitude < .5f) throw new InvalidOperationException("A 180-degree reversal cannot form a continuous finite-width guide.");
                if (i == 0)
                {
                    Vector3 seed = Mathf.Abs(Vector3.Dot(tangents[i], Vector3.up)) < .9f ? Vector3.up : Vector3.right;
                    normals[i] = Vector3.ProjectOnPlane(seed, tangents[i]).normalized;
                }
                else
                {
                    Vector3 transported = Quaternion.FromToRotation(tangents[i - 1], tangents[i]) * normals[i - 1];
                    normals[i] = Vector3.ProjectOnPlane(transported, tangents[i]).normalized;
                }
                binormals[i] = Vector3.Cross(tangents[i], normals[i]).normalized;
            }
            Matrix4x4 worldToLocal = station.wireRoot.worldToLocalMatrix;
            Matrix4x4 normalToLocal = station.wireRoot.localToWorldMatrix.transpose;
            float distance = 0;
            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0) distance += Vector3.Distance(points[i - 1], points[i]);
                for (int j = 0; j <= Sides; j++)
                {
                    float angle = j * (Mathf.PI * 2 / Sides);
                    Vector3 radial = normals[i] * Mathf.Cos(angle) + binormals[i] * Mathf.Sin(angle);
                    mesh.Add(worldToLocal.MultiplyPoint3x4(points[i] + radial * station.wireRadius),
                        normalToLocal.MultiplyVector(radial), new Vector2(j / (float)Sides, distance));
                }
            }
            for (int i = 0; i < points.Count - 1; i++) for (int j = 0; j < Sides; j++)
            {
                int a = i * (Sides + 1) + j, b = a + Sides + 1;
                mesh.Tri(a, a + 1, b + 1); mesh.Tri(a, b + 1, b);
            }
            AddCap(mesh, points[0], normals[0], binormals[0], -tangents[0], station.wireRadius, worldToLocal, normalToLocal, true);
            int last = points.Count - 1;
            AddCap(mesh, points[last], normals[last], binormals[last], tangents[last], station.wireRadius, worldToLocal, normalToLocal, false);
            return mesh;
        }

        static void AddCap(MeshData mesh, Vector3 point, Vector3 normal, Vector3 binormal, Vector3 outward,
            float radius, Matrix4x4 toLocal, Matrix4x4 normalToLocal, bool start)
        {
            int centre = mesh.vertices.Count;
            Vector3 n = normalToLocal.MultiplyVector(outward);
            mesh.Add(toLocal.MultiplyPoint3x4(point), n, Vector2.one * .5f);
            for (int j = 0; j <= Sides; j++)
            {
                float angle = j * (Mathf.PI * 2 / Sides), c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                mesh.Add(toLocal.MultiplyPoint3x4(point + radius * (normal * c + binormal * s)), n, new Vector2(.5f + .5f * c, .5f + .5f * s));
            }
            for (int j = 0; j < Sides; j++)
                if (start) mesh.Tri(centre, centre + j + 2, centre + j + 1);
                else mesh.Tri(centre, centre + j + 1, centre + j + 2);
        }

        static MeshData MakeRing(float radius, float tube)
        {
            var mesh = new MeshData();
            for (int i = 0; i <= RingSegments; i++)
            {
                float angle = i * (Mathf.PI * 2 / RingSegments);
                Vector3 radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                for (int j = 0; j <= Sides; j++)
                {
                    float minor = j * (Mathf.PI * 2 / Sides);
                    Vector3 normal = radial * Mathf.Cos(minor) + Vector3.forward * Mathf.Sin(minor);
                    mesh.Add(radial * radius + normal * tube, normal, new Vector2(i / (float)RingSegments, j / (float)Sides));
                }
            }
            for (int i = 0; i < RingSegments; i++) for (int j = 0; j < Sides; j++)
            {
                int a = i * (Sides + 1) + j, b = a + Sides + 1;
                mesh.Tri(a, b, b + 1); mesh.Tri(a, b + 1, a + 1);
            }
            return mesh;
        }

        static Mesh Persist(string name, MeshData data)
        {
            for (int i = 0; i < data.vertices.Count; i++)
                if (!Finite(data.vertices[i]) || !Finite(data.normals[i]) || Mathf.Abs(data.normals[i].sqrMagnitude - 1) > .001f)
                    throw new InvalidOperationException("Non-finite mesh vertex or invalid normal in " + name);
            for (int i = 0; i < data.triangles.Count; i += 3)
            {
                int a = data.triangles[i], b = data.triangles[i + 1], c = data.triangles[i + 2];
                Vector3 cross = Vector3.Cross(data.vertices[b] - data.vertices[a], data.vertices[c] - data.vertices[a]);
                if (Vector3.Dot(cross, data.normals[a] + data.normals[b] + data.normals[c]) <= 0)
                    throw new InvalidOperationException("Degenerate or inward tube face in " + name + " triangle " + i / 3);
            }
            string path = Folder + "/" + name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh) { mesh = new Mesh { name = name }; AssetDatabase.CreateAsset(mesh, path); }
            else mesh.Clear();
            mesh.indexFormat = data.vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(data.vertices); mesh.SetNormals(data.normals); mesh.SetUVs(0, data.uv);
            mesh.SetTriangles(data.triangles, 0); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh); return mesh;
        }

        static Renderer MeshChild(Transform root, string name, Mesh mesh, Material material)
        {
            Transform child = root.Find(name);
            if (!child) { child = new GameObject(name).transform; child.SetParent(root, false); }
            child.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity); child.localScale = Vector3.one;
            var filter = child.GetComponent<MeshFilter>();
            if (!filter) filter = child.gameObject.AddComponent<MeshFilter>();
            var renderer = child.GetComponent<MeshRenderer>();
            if (!renderer) renderer = child.gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh; renderer.sharedMaterial = material; renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            child.gameObject.SetActive(true); GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
            EditorUtility.SetDirty(filter); EditorUtility.SetDirty(renderer); return renderer;
        }

        static Material MaterialAsset(string name, Color color, float metallic, float smoothness)
        {
            string path = Folder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Standard");
            if (!shader) throw new InvalidOperationException("Built-in Standard shader is required.");
            if (!material) { material = new Material(shader) { name = name }; AssetDatabase.CreateAsset(material, path); }
            material.shader = shader; material.color = color;
            material.SetFloat("_Metallic", metallic); material.SetFloat("_Glossiness", smoothness); material.SetFloat("_Mode", 0);
            material.SetInt("_SrcBlend", (int)BlendMode.One); material.SetInt("_DstBlend", (int)BlendMode.Zero); material.SetInt("_ZWrite", 1);
            material.DisableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_ALPHABLEND_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_EMISSION"); material.SetOverrideTag("RenderType", "Opaque"); material.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(material); return material;
        }

        static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
            && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int split = path.LastIndexOf('/'); string parent = path.Substring(0, split);
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(split + 1));
        }
    }
}
