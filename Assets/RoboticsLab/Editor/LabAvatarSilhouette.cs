using System.Collections.Generic;
using UnityEngine;

namespace RoboticsLab.Editor
{
    /// <summary>Models the existing white training suit in its metre-scale Ch32 bind pose.</summary>
    internal static class LabAvatarSilhouette
    {
        internal static void Apply(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            var triangles = mesh.triangles;
            // Imported UV and tangent seams duplicate positions. Edit them as one point so the
            // suit stays closed, while keeping the original UVs and authored shading splits.
            var welded = new Dictionary<Vector3Int, int>(vertices.Length);
            var unique = new List<Vector3>(vertices.Length);
            var map = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var key = Vector3Int.RoundToInt(vertices[i] * 100000f);
                if (!welded.TryGetValue(key, out int index))
                {
                    index = unique.Count;
                    unique.Add(vertices[i]);
                    welded.Add(key, index);
                }
                map[i] = index;
            }
            var rest = unique.ToArray();
            var points = new Vector3[rest.Length];
            var neighbours = new HashSet<int>[rest.Length];
            var relax = new float[rest.Length];
            for (int i = 0; i < rest.Length; i++)
            {
                points[i] = Sculpt(rest[i]);
                relax[i] = FairingWeight(rest[i]);
                if (relax[i] > 0) neighbours[i] = new HashSet<int>();
            }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = map[triangles[i]], b = map[triangles[i + 1]], c = map[triangles[i + 2]];
                if (a == b || b == c || c == a) continue;
                AddEdge(neighbours, a, b);
                AddEdge(neighbours, b, c);
                AddEdge(neighbours, c, a);
            }

            var scratch = new Vector3[rest.Length];
            for (int pass = 0; pass < 5; pass++)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    scratch[i] = points[i];
                    if (neighbours[i] == null || neighbours[i].Count < 3) continue;
                    Vector3 average = Vector3.zero;
                    foreach (int j in neighbours[i]) average += points[j];
                    average /= neighbours[i].Count;
                    // Small reverse steps retain the rounded shoulder volume while softening
                    // the two grooves around the sleeve and the shelf-like rear waistband.
                    scratch[i] += (average - points[i]) * relax[i] * ((pass % 2 == 0) ? .63f : -.18f);
                }
                var previous = points;
                points = scratch;
                scratch = previous;
            }

            var before = SurfaceNormals(rest, triangles, map);
            var after = SurfaceNormals(points, triangles, map);
            for (int i = 0; i < vertices.Length; i++)
            {
                int group = map[i];
                Vector3 offset = points[group] - rest[group];
                if (offset.sqrMagnitude < 1e-16f) continue;
                vertices[i] += offset;
                if (before[group].sqrMagnitude < 1e-18f || after[group].sqrMagnitude < 1e-18f) continue;
                var turn = Quaternion.FromToRotation(before[group].normalized, after[group].normalized);
                if (normals.Length == vertices.Length) normals[i] = turn * normals[i];
                if (tangents.Length != vertices.Length) continue;
                var tangent = turn * (Vector3)tangents[i];
                tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w);
            }
            mesh.vertices = vertices;
            if (normals.Length == vertices.Length) mesh.normals = normals;
            if (tangents.Length == vertices.Length) mesh.tangents = tangents;
            mesh.RecalculateBounds();
        }

        static Vector3 Sculpt(Vector3 p)
        {
            float x = Mathf.Abs(p.x);
            float torso = (1 - Ramp(.18f, .25f, x)) * Ramp(.78f, .92f, p.y) * (1 - Ramp(1.39f, 1.46f, p.y));
            var offset = Vector3.zero;
            if (torso > 0)
            {
                // A full rib cage, a gentle break at the waist, and a softer transition to the pelvis.
                float ribs = Bell(p.y, 1.30f, .114f);
                float waist = Bell(p.y, 1.10f, .091f);
                float hips = Bell(p.y, .937f, .087f);
                float front = Ramp(.001f, .075f, p.z);
                float back = 1 - Ramp(-.078f, -.012f, p.z);
                offset.x += p.x * torso * (.085f * ribs - .085f * waist + .047f * hips);
                offset.z += torso * (front * (.018f * ribs * (.55f + .45f * Bell(x, .086f, .052f))
                                               - .013f * waist + .004f * hips)
                                     - back * (.013f * ribs - .008f * waist + .008f * hips));
            }

            float arm = Ramp(.18f, .265f, x) * (1 - Ramp(.58f, .663f, x)) * Ramp(1.22f, 1.34f, p.y);
            if (arm > 0)
            {
                float upper = Bell(x, .322f, .081f);
                float elbow = Bell(x, .443f, .046f);
                float forearm = Bell(x, .520f, .070f);
                float midY = Mathf.Lerp(1.416f, 1.407f, Ramp(.22f, .45f, x));
                float midZ = Mathf.Lerp(-.030f, -.044f, Ramp(.22f, .49f, x));
                float radius = arm * (.155f * upper - .085f * elbow + .10f * forearm);
                offset.y += (p.y - midY) * radius;
                offset.z += (p.z - midZ) * radius + arm * (.008f * upper - .0035f * elbow);
            }

            // Use a local axis for each leg so changes in width never move the knees or feet.
            float leg = Ramp(.18f, .25f, p.y) * (1 - Ramp(.82f, .905f, p.y));
            if (leg > 0)
            {
                float thigh = Bell(p.y, .73f, .10f);
                float knee = Bell(p.y, .572f, .056f);
                float calf = Bell(p.y, .395f, .092f);
                float ankle = Bell(p.y, .246f, .060f);
                float midX = Mathf.Lerp(.138f, .096f, Ramp(.23f, .85f, p.y));
                float midZ = Mathf.Lerp(-.030f, .010f, Ramp(.25f, .82f, p.y));
                float radius = leg * (.085f * thigh - .058f * knee + .077f * calf - .075f * ankle);
                offset.x += (p.x - Mathf.Sign(p.x) * midX) * radius * Ramp(0, .042f, x);
                offset.z += (p.z - midZ) * radius + leg * (.006f * thigh - .004f * calf);
            }
            return p + offset;
        }

        static float FairingWeight(Vector3 p)
        {
            float x = Mathf.Abs(p.x);
            float sleeve = Ramp(.132f, .162f, x) * (1 - Ramp(.263f, .298f, x))
                         * Ramp(1.285f, 1.35f, p.y);
            float waist = (1 - Ramp(.16f, .20f, x)) * Bell(p.y, 1.06f, .059f);
            return Mathf.Max(sleeve, waist * .82f);
        }

        static void AddEdge(HashSet<int>[] neighbours, int a, int b)
        {
            neighbours[a]?.Add(b);
            neighbours[b]?.Add(a);
        }

        static Vector3[] SurfaceNormals(Vector3[] points, int[] triangles, int[] map)
        {
            var normals = new Vector3[points.Length];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = map[triangles[i]], b = map[triangles[i + 1]], c = map[triangles[i + 2]];
                var n = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                normals[a] += n;
                normals[b] += n;
                normals[c] += n;
            }
            return normals;
        }

        static float Ramp(float a, float b, float value) =>
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, value));

        static float Bell(float value, float center, float width)
        {
            float t = (value - center) / width;
            return Mathf.Exp(-t*t);
        }
    }
}
