using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoboticsLab
{
    /// <summary>Replaceable printed guides; their transforms never drive the robot.</summary>
    public sealed class LabTracingBoardView
    {
        readonly LabTracingExercise owner;
        GameObject root;
        Material material;
        readonly MeshRenderer[] guides = new MeshRenderer[5];
        readonly MeshFilter[] guideMeshes = new MeshFilter[5];
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly Color Guide = new Color(.14f, .20f, .25f);
        const float PrintHeight = .0018f;
        public LabTracingBoardView(LabTracingExercise exercise) { owner = exercise; }

        public void Rebuild()
        {
            Dispose();
            if (!owner.station || !owner.station.drawingBoard) return;
            root = new GameObject("TracingPrintedGuides");
            root.transform.SetParent(owner.station.drawingBoard, false);
            material = new Material(Shader.Find("Sprites/Default"));
            material.name = "TracingGuide_Runtime";
            material.mainTexture = Texture2D.whiteTexture;
            for (int i = 0; i < 5; i++)
            {
                Vector2[] path = owner.GetPath(i);
                guides[i] = MakePrint("Guide_" + i, Ribbon(path, .0012f), Guide);
                guideMeshes[i] = guides[i].GetComponent<MeshFilter>();
                Vector2 start = path[0];
                MakePrint("Start_" + i, Dot(start, .0022f), new Color(.02f, .62f, .26f));
                if (i == 0) MakePrint("J_End", Dot(path[path.Length - 1], .0022f), new Color(.83f, .13f, .13f));
            }
            Label("J / MANUAL", new Vector2(-.31f, .45f));
            Label("1 CIRCLE", new Vector2(.025f, .395f));
            Label("2 TRIANGLE", new Vector2(.30f, .395f));
            Label("3 SQUARE", new Vector2(.025f, .095f));
            Label("4 CURVE", new Vector2(.27f, -.15f));
            Highlight(owner.CurrentShape);
        }

        MeshRenderer MakePrint(string name, Mesh mesh, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Tint(renderer, color);
            return renderer;
        }

        Mesh Ribbon(Vector2[] points, float width)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float radius = width * .5f;
            for (int i = 1; i < points.Length; i++)
            {
                Vector2 direction = points[i] - points[i - 1];
                if (direction.sqrMagnitude < 1e-12f) continue;
                Vector2 normal = new Vector2(-direction.y, direction.x).normalized * radius;
                int b = vertices.Count;
                vertices.Add(LocalVertex(points[i - 1] + normal));
                vertices.Add(LocalVertex(points[i - 1] - normal));
                vertices.Add(LocalVertex(points[i] + normal));
                vertices.Add(LocalVertex(points[i] - normal));
                triangles.Add(b); triangles.Add(b + 2); triangles.Add(b + 1);
                triangles.Add(b + 2); triangles.Add(b + 3); triangles.Add(b + 1);
            }
            // Round joins cover gaps at sharp turns. All triangles are physically flat
            // on the paper, so no segment can turn edge-on like a billboarded line.
            for (int i = 0; i < points.Length; i++) AddDisc(points[i], radius, 8, vertices, triangles);
            return BuildMesh("PrintedTracingRibbon", vertices, triangles);
        }

        Mesh Dot(Vector2 point, float radius)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            AddDisc(point, radius, 24, vertices, triangles);
            return BuildMesh("PrintedTracingMarker", vertices, triangles);
        }

        void AddDisc(Vector2 point, float radius, int segments, List<Vector3> vertices, List<int> triangles)
        {
            int start = vertices.Count;
            vertices.Add(LocalVertex(point));
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                vertices.Add(LocalVertex(point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius));
                if (i == 0) continue;
                triangles.Add(start); triangles.Add(start + i + 1); triangles.Add(start + i);
            }
        }

        Vector3 LocalVertex(Vector2 point)
        {
            return root.transform.InverseTransformPoint(owner.PaperToWorld(point, PrintHeight));
        }

        Mesh BuildMesh(string name, List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            var normals = new Vector3[vertices.Count];
            var colors = new Color[vertices.Count];
            var uv = new Vector2[vertices.Count];
            Vector3 normal = root.transform.InverseTransformDirection(owner.station.transform.up).normalized;
            for (int i = 0; i < vertices.Count; i++) { normals[i] = normal; colors[i] = Color.white; }
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.uv = uv;
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            return mesh;
        }

        void Tint(MeshRenderer renderer, Color color)
        {
            properties.Clear();
            properties.SetColor(ColorId, color);
            renderer.SetPropertyBlock(properties);
        }

        void Label(string text, Vector2 normalised)
        {
            var go = new GameObject(text);
            go.transform.SetParent(root.transform, false);
            go.transform.position = owner.PaperToWorld(Vector2.Scale(normalised, owner.layoutSize) + owner.layoutOffset, .0018f);
            go.transform.rotation = owner.station.transform.rotation * Quaternion.Euler(90, 0, 0);
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.fontSize = 48;
            mesh.characterSize = .00165f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.color = Guide;
        }

        public void Highlight(int selected)
        {
            for (int i = 0; i < guides.Length; i++) if (guides[i])
            {
                Tint(guides[i], i == selected ? new Color(.03f, .32f, .66f) : Guide);
                Mesh old = guideMeshes[i].sharedMesh;
                guideMeshes[i].sharedMesh = Ribbon(owner.GetPath(i), i == selected ? .0015f : .0012f);
                meshes.Remove(old);
                DestroyAsset(old);
            }
        }

        public void Dispose()
        {
            DestroyAsset(root);
            DestroyAsset(material);
            foreach (Mesh mesh in meshes) DestroyAsset(mesh);
            meshes.Clear();
        }

        static void DestroyAsset(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
        }
    }
}
