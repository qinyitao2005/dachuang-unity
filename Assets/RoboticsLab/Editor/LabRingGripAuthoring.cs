using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RoboticsLab.Editor
{
    /// <summary>Builds a ring-only copy of the supplied CAD gripper, retaining its real flange and jaws.</summary>
    public static class LabRingGripAuthoring
    {
        const string ToolName = "RingGrip_OriginalCAD";
        const string ArmPath = "OriginalAssembly/机械臂/基座/大臂/关节/小臂/手腕/小手腕";

        sealed class JawSurface
        {
            public Transform transform;
            public Vector3[] vertices;
            public int[] triangles;
            public Bounds bounds;
            public float innerX;
            public readonly List<int> faceTriangles = new List<int>();
            public Bounds faceBounds;
        }

        [MenuItem("Robot Lab/Author Ring Grip Tools")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before authoring ring grippers.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != LabSceneBuilder.ScenePath) throw new InvalidOperationException("Open RoboticsTrainingRoom first.");
            var stations = Object.FindObjectsOfType<LabRobotStation>(true)
                .Where(s => s.gameObject.scene == scene).OrderBy(s => s.stationIndex).ToArray();
            if (stations.Length != 3) throw new InvalidOperationException("Expected exactly three training stations.");
            var report = new StringBuilder("Ring gripper authoring / " + DateTime.UtcNow.ToString("O") + "\n");
            report.AppendLine("Static geometry checks only. Play-mode grasp, wire clearance and motion require separate validation.");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Author original CAD ring grippers");
            try
            {
                foreach (var station in stations) BuildStation(station, report);
            }
            catch (Exception exception)
            {
                report.AppendLine("FAIL: " + exception);
                Directory.CreateDirectory("Documentation");
                File.WriteAllText("Documentation/RingGripAuthoringFailure.txt", report.ToString(), new UTF8Encoding(false));
                Debug.LogError(report.ToString());
                // A failed authoring attempt must not leave a loose, visible tool in the scene.
                Undo.RevertAllDownToGroup(group);
                throw;
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory("Documentation");
            File.WriteAllText("Documentation/RingGripAuthoring.txt", report.ToString(), new UTF8Encoding(false));
            Debug.Log("ROBOT_RING_GRIP_AUTHORED original CAD tool copies=3; review and save scene.\n" + report);
        }

        static void BuildStation(LabRobotStation station, StringBuilder report)
        {
            if (!station.modelRoot || !station.endEffector) throw new InvalidOperationException("Station is missing its original model or TCP.");
            Transform wrist = station.modelRoot.Find(ArmPath);
            if (!wrist) throw new InvalidOperationException("Original small wrist not found at " + ArmPath);
            Transform original = station.endEffector;
            while (original.parent && original.parent != wrist) original = original.parent;
            if (original.parent != wrist || original.name == ToolName)
                throw new InvalidOperationException("Keep endEffector referencing the original drawing tool while authoring.");

            // Station 01 has the pointer tool. The complete workcell 02 has the matching
            // original CAD gripper/flange with the same 180-degree mounting roll. The other
            // stations retain their own original gripper prototype and exact flange pose.
            int prototypeIndex = station.stationIndex == 0 ? 1 : station.stationIndex;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LabOriginalEquipmentAssets.CellPath(prototypeIndex));
            Transform prototype = prefab ? prefab.transform.Find(ArmPath + "/机器人手爪") : null;
            if (!prototype) throw new InvalidOperationException("Original CAD gripper prototype is missing.");
            var serialized = new SerializedObject(station);
            foreach (string field in new[] { "ringGripTcp", "ringGripTool", "originalEndTool" })
                if (serialized.FindProperty(field) == null) throw new InvalidOperationException("Import the station's new " + field + " field first.");

            Transform existing = wrist.Find(ToolName);
            if (existing) Undo.DestroyObjectImmediate(existing.gameObject);
            GameObject copy = Object.Instantiate(prototype.gameObject, wrist, false);
            copy.name = ToolName;
            Transform tool = copy.transform;
            tool.localPosition = prototype.localPosition;
            tool.localRotation = prototype.localRotation;
            tool.localScale = prototype.localScale;
            Undo.RegisterCreatedObjectUndo(copy, "Create ring CAD gripper");
            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) if (behaviour) behaviour.enabled = false;
            foreach (var animator in copy.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

            float scaleX = tool.TransformVector(Vector3.right).magnitude;
            float scaleY = tool.TransformVector(Vector3.up).magnitude;
            float scaleZ = tool.TransformVector(Vector3.forward).magnitude;
            if (scaleX < .001f || Mathf.Abs(scaleX - scaleY) > .0001f || Mathf.Abs(scaleX - scaleZ) > .0001f)
                throw new InvalidOperationException("The CAD gripper requires the original uniform physical scale.");
            Transform first = tool.Find("机加手指1"), second = tool.Find("机加手指2");
            if (!first || !second) throw new InvalidOperationException("Original machined finger meshes are missing.");
            JawSurface left = ReadJaw(first, tool), right = ReadJaw(second, tool);
            if (left.bounds.center.x > right.bounds.center.x) { var swap = left; left = right; right = swap; }
            report.AppendLine("CONTACT GEOMETRY station=" + station.stationIndex + " scale=" + tool.lossyScale.ToString("F9"));
            try
            {
                FindInnerFace(left, true, scaleX);
                FindInnerFace(right, false, scaleX);
            }
            catch
            {
                DumpJaw(left, report); DumpJaw(right, report);
                throw;
            }
            report.AppendLine(" leftBounds=" + FormatBounds(left.bounds) + " rightBounds=" + FormatBounds(right.bounds));
            report.AppendLine(" leftFaceBounds=" + FormatBounds(left.faceBounds) + " rightFaceBounds=" + FormatBounds(right.faceBounds));
            float oldGap = (right.innerX - left.innerX) * scaleX;
            float desiredGap = 2 * station.ringTubeRadius;
            // These are stepped/V-profile CAD fingers: the narrowest faces belong to their
            // roots, while their distal legs are much farther apart. Closing the distal legs
            // to 7.2 mm would interpenetrate the roots and sliders. Retain a real open gap in
            // the original assembly and attach solid, visible distal jaw shoes instead.
            float cadRootGap = Mathf.Max(.0084f, desiredGap + .0012f);
            if (desiredGap <= 0 || cadRootGap > oldGap)
                throw new InvalidOperationException("Ring tube does not fit the existing CAD gripper opening.");
            float centreX = .5f * (left.innerX + right.innerX);
            float leftMove = centreX - cadRootGap / (2 * scaleX) - left.innerX;
            float rightMove = centreX + cadRootGap / (2 * scaleX) - right.innerX;
            float distalY = Mathf.Min(left.bounds.min.y, right.bounds.min.y);
            float centreZ = .5f * (Mathf.Max(left.bounds.min.z, right.bounds.min.z) + Mathf.Min(left.bounds.max.z, right.bounds.max.z));
            float seatY = distalY + .008f / scaleY;
            float leftSeatX = SurfaceAt(left, seatY, centreZ, true);
            float rightSeatX = SurfaceAt(right, seatY, centreZ, false);
            int originalMeshCount = copy.GetComponentsInChildren<MeshFilter>(true).Length;
            MoveJawAssemblies(tool, left.transform, right.transform, leftMove, rightMove, report);

            Vector3 gripPoint = new Vector3(centreX, distalY - .002f / scaleY, centreZ);
            AddDistalShoes(tool, gripPoint, distalY, leftSeatX + leftMove, rightSeatX + rightMove,
                desiredGap, scaleX, scaleY, scaleZ, report);

            var tcp = new GameObject("RingGripTCP").transform;
            tcp.SetParent(tool, false);
            tcp.localPosition = gripPoint;
            tcp.localRotation = Quaternion.identity;
            tcp.localScale = Vector3.one;
            // Non-rendered contact markers let later validation inspect both real inner faces.
            AddMarker(tool, "RingJawContact_Left", gripPoint - Vector3.right * desiredGap / (2 * scaleX));
            AddMarker(tool, "RingJawContact_Right", gripPoint + Vector3.right * desiredGap / (2 * scaleX));
            float measuredGap = Vector3.Distance(tool.Find("RingJawContact_Left").position, tool.Find("RingJawContact_Right").position);
            if (Mathf.Abs(measuredGap - desiredGap) > .00001f)
                throw new InvalidOperationException("Authored CAD jaw gap does not match the ring tube diameter.");
            Transform shoes = tool.Find("DistalJawShoes");
            int retainedCadMeshes = copy.GetComponentsInChildren<MeshFilter>(true).Count(m => !m.transform.IsChildOf(shoes));
            if (retainedCadMeshes != originalMeshCount || originalMeshCount != prototype.GetComponentsInChildren<MeshFilter>(true).Length)
                throw new InvalidOperationException("All original gripper mesh parts must be retained.");

            Undo.RecordObject(station, "Assign ring gripper references");
            serialized.Update();
            serialized.FindProperty("ringGripTcp").objectReferenceValue = tcp;
            serialized.FindProperty("ringGripTool").objectReferenceValue = tool;
            serialized.FindProperty("originalEndTool").objectReferenceValue = original;
            serialized.ApplyModifiedProperties();
            // Runtime owns mode visibility. Authoring leaves the original drawing tool visible.
            copy.SetActive(false);
            EditorUtility.SetDirty(station);
            report.AppendLine("STATION " + station.stationIndex + " prototype=" + LabOriginalEquipmentAssets.CellPath(prototypeIndex));
            report.AppendLine(" original=" + original.name + " mountLocal=" + tool.localPosition.ToString("F7")
                + " mountEuler=" + tool.localEulerAngles.ToString("F5") + " worldScale=" + tool.lossyScale.ToString("F7"));
            report.AppendLine(" leftFace=" + left.innerX.ToString("F7") + " rightFace=" + right.innerX.ToString("F7")
                + " contactLocal=" + tcp.localPosition.ToString("F7") + " face triangles=" + left.faceTriangles.Count + "/" + right.faceTriangles.Count);
            report.AppendLine(" oldGap=" + oldGap.ToString("F7") + " m; closedGap=" + measuredGap.ToString("F7")
                + " m; leftTravel=" + (leftMove * scaleX).ToString("F7") + " m; rightTravel=" + (rightMove * scaleX).ToString("F7") + " m");
            report.AppendLine(" CAD root gap=" + cadRootGap.ToString("F7") + " m; retained CAD meshes=" + retainedCadMeshes
                + "; additional distal-shoe meshes=" + shoes.GetComponentsInChildren<MeshFilter>(true).Length);
            report.AppendLine(" PASS: Real distal shoes attach to the original CAD legs; their rubber inner faces meet the 7.2 mm tube at a TCP 2 mm beyond the original fingertips. Original meshes/materials/scale retained.");
        }

        static float SurfaceAt(JawSurface jaw, float y, float z, bool left)
        {
            float result = left ? float.NegativeInfinity : float.PositiveInfinity;
            bool found = false;
            foreach (int i in Enumerable.Range(0, jaw.triangles.Length / 3).Select(i => i * 3))
            {
                Vector3 a = jaw.vertices[jaw.triangles[i]], b = jaw.vertices[jaw.triangles[i + 1]], c = jaw.vertices[jaw.triangles[i + 2]];
                var ab = new Vector2(b.y - a.y, b.z - a.z); var ac = new Vector2(c.y - a.y, c.z - a.z);
                var p = new Vector2(y - a.y, z - a.z);
                float denominator = Cross(ab, ac);
                if (Mathf.Abs(denominator) < 1e-12f) continue;
                float u = Cross(p, ac) / denominator, v = Cross(ab, p) / denominator;
                if (u < -.00001f || v < -.00001f || u + v > 1.00001f) continue;
                float x = a.x + u * (b.x - a.x) + v * (c.x - a.x);
                result = left ? Mathf.Max(result, x) : Mathf.Min(result, x); found = true;
            }
            if (!found) throw new InvalidOperationException("No real distal finger surface at shoe attachment y=" + y + ", z=" + z);
            return result;
        }

        static void AddDistalShoes(Transform tool, Vector3 grip, float tipY, float leftSeat, float rightSeat,
            float gap, float sx, float sy, float sz, StringBuilder report)
        {
            var root = new GameObject("DistalJawShoes").transform; root.SetParent(tool, false);
            Material metal = AssetDatabase.LoadAssetAtPath<Material>("Assets/RoboticsLab/Generated/Lab_Metal.mat");
            Material rubber = AssetDatabase.LoadAssetAtPath<Material>("Assets/RoboticsLab/Generated/Lab_Rubber.mat");
            if (!metal || !rubber) throw new InvalidOperationException("Existing room metal and rubber materials are required for the real jaw shoes.");
            float half = gap / (2 * sx), rubberThickness = .002f / sx, overlap = .0008f / sx;
            float height = .014f / sy, depth = .012f / sz, y = tipY + .004f / sy;
            float leftInner = grip.x - half, rightInner = grip.x + half;
            float leftOuter = leftSeat - overlap, rightOuter = rightSeat + overlap;
            float leftMetalEnd = leftInner - rubberThickness, rightMetalStart = rightInner + rubberThickness;
            if (leftOuter >= leftMetalEnd || rightOuter <= rightMetalStart)
                throw new InvalidOperationException("The distal metal shoes must extend from the real leg to the rubber pad.");
            ShoeBox(root, "Left_MetalShoe", new Vector3((leftOuter + leftMetalEnd) * .5f, y, grip.z),
                new Vector3(leftMetalEnd - leftOuter, height, depth), metal);
            ShoeBox(root, "Left_RubberContact", new Vector3((leftMetalEnd + leftInner) * .5f, y, grip.z),
                new Vector3(rubberThickness, height, depth), rubber);
            ShoeBox(root, "Right_MetalShoe", new Vector3((rightMetalStart + rightOuter) * .5f, y, grip.z),
                new Vector3(rightOuter - rightMetalStart, height, depth), metal);
            ShoeBox(root, "Right_RubberContact", new Vector3((rightInner + rightMetalStart) * .5f, y, grip.z),
                new Vector3(rubberThickness, height, depth), rubber);
            report.AppendLine("DISTAL SHOES contact=" + grip.ToString("F9") + " originalTipY=" + tipY.ToString("F9")
                + " attached left/right surfaceX=" + leftSeat.ToString("F9") + "/" + rightSeat.ToString("F9"));
            report.AppendLine(" shoes overlap original legs by 0.8 mm; rubber thickness=2 mm; shoe height/depth=14/12 mm; reach left/right="
                + ((leftInner - leftSeat) * sx * 1000).ToString("F4") + "/" + ((rightSeat - rightInner) * sx * 1000).ToString("F4") + " mm");
            report.AppendLine(" Ring orientation: tool +X=ring plane normal, tool +Y=ring local +Y/radial towards wrist, tube tangent=tool Z.");
        }

        static void ShoeBox(Transform root, string name, Vector3 position, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(root, false); go.transform.localPosition = position; go.transform.localScale = size;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, 0);
        }

        static void MoveJawAssemblies(Transform tool, Transform left, Transform right, float leftDelta, float rightDelta, StringBuilder report)
        {
            // Retain the connection from the machined finger to its original two screws and
            // pneumatic slider. Their meshes, local rotations and physical dimensions are unchanged.
            // Resolve BOTH groups before moving either: a slider's transform origin can cross
            // X=0 when closed, despite its mesh still belonging to the same physical side.
            List<Transform> leftParts = PickJawAssembly(tool, left, true);
            List<Transform> rightParts = PickJawAssembly(tool, right, false);
            foreach (Transform part in leftParts)
                if (rightParts.Contains(part)) throw new InvalidOperationException("A gripper part was selected for both jaws: " + PathFrom(tool, part));
            LogMovingParts(tool, "LEFT", leftParts, report);
            LogMovingParts(tool, "RIGHT", rightParts, report);
            Vector3 leftWorld = tool.TransformVector(new Vector3(leftDelta, 0, 0));
            Vector3 rightWorld = tool.TransformVector(new Vector3(rightDelta, 0, 0));
            foreach (Transform part in leftParts) part.position += leftWorld;
            foreach (Transform part in rightParts) part.position += rightWorld;
        }

        static List<Transform> PickJawAssembly(Transform tool, Transform jaw, bool negativeSide)
        {
            var moving = new List<Transform> { jaw };
            foreach (Transform part in tool.GetComponentsInChildren<Transform>(true))
            {
                if (part == jaw) continue;
                bool slider = part.name.StartsWith("复件 2_3^MHZL2-25D20221218", StringComparison.Ordinal);
                bool screw = part.name.StartsWith("内六角圆柱头螺钉_M5X20", StringComparison.Ordinal);
                Vector3 p = tool.InverseTransformPoint(part.position);
                if ((slider || (screw && p.y < -1f)) && (p.x < 0) == negativeSide) moving.Add(part);
            }
            // Moving a selected ancestor already moves its descendants; keep only top-level
            // selected nodes so nested CAD hierarchies cannot receive the displacement twice.
            return moving.Distinct().Where(part => !moving.Any(other => other != part && part.IsChildOf(other))).ToList();
        }

        static void LogMovingParts(Transform tool, string side, List<Transform> parts, StringBuilder report)
        {
            report.AppendLine("MOVING " + side + " count=" + parts.Count);
            foreach (Transform part in parts)
                report.AppendLine(" picked=" + PathFrom(tool, part) + " parent=" + PathFrom(tool, part.parent)
                    + " originalRootLocal=" + tool.InverseTransformPoint(part.position).ToString("F9"));
        }

        static string PathFrom(Transform root, Transform item)
        {
            if (!item || item == root) return root.name;
            return PathFrom(root, item.parent) + "/" + item.name;
        }

        static JawSurface ReadJaw(Transform jaw, Transform tool)
        {
            MeshFilter filter = jaw.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh) throw new InvalidOperationException("Finger requires its original static mesh.");
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices;
            int[] triangles;
            using (var arrays = MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                var data = arrays[0];
                using (var values = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                { data.GetVertices(values); vertices = values.ToArray(); }
                var indices = new List<int>();
                for (int i = 0; i < data.subMeshCount; i++)
                {
                    var submesh = data.GetSubMesh(i);
                    if (submesh.topology != MeshTopology.Triangles) continue;
                    using (var values = new NativeArray<int>(submesh.indexCount, Allocator.Temp))
                    { data.GetIndices(values, i, true); indices.AddRange(values.ToArray()); }
                }
                triangles = indices.ToArray();
            }
            Matrix4x4 matrix = tool.worldToLocalMatrix * jaw.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            if (vertices.Length == 0 || triangles.Length == 0) throw new InvalidOperationException("Empty finger mesh.");
            var result = new JawSurface { transform = jaw, vertices = vertices, triangles = triangles, bounds = new Bounds(vertices[0], Vector3.zero) };
            foreach (Vector3 p in vertices) result.bounds.Encapsulate(p);
            return result;
        }

        static void FindInnerFace(JawSurface jaw, bool left, float worldScale)
        {
            jaw.innerX = left ? jaw.bounds.max.x : jaw.bounds.min.x;
            float tolerance = .000005f / worldScale;
            bool hasBounds = false;
            for (int i = 0; i < jaw.triangles.Length; i += 3)
            {
                Vector3 a = jaw.vertices[jaw.triangles[i]], b = jaw.vertices[jaw.triangles[i + 1]], c = jaw.vertices[jaw.triangles[i + 2]];
                if (Mathf.Abs(a.x - jaw.innerX) > tolerance || Mathf.Abs(b.x - jaw.innerX) > tolerance || Mathf.Abs(c.x - jaw.innerX) > tolerance) continue;
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f) continue;
                jaw.faceTriangles.Add(i);
                if (!hasBounds) { jaw.faceBounds = new Bounds(a, Vector3.zero); hasBounds = true; }
                jaw.faceBounds.Encapsulate(a); jaw.faceBounds.Encapsulate(b); jaw.faceBounds.Encapsulate(c);
            }
            if (!hasBounds) throw new InvalidOperationException("No planar inner contact face found on " + jaw.transform.name + "; inspect the source mesh instead of guessing a TCP.");
        }

        static Vector2 CommonContactPoint(JawSurface left, JawSurface right)
        {
            float ymin = Mathf.Max(left.faceBounds.min.y, right.faceBounds.min.y), ymax = Mathf.Min(left.faceBounds.max.y, right.faceBounds.max.y);
            float zmin = Mathf.Max(left.faceBounds.min.z, right.faceBounds.min.z), zmax = Mathf.Min(left.faceBounds.max.z, right.faceBounds.max.z);
            if (ymax <= ymin || zmax <= zmin) throw new InvalidOperationException("The original finger contact faces do not overlap.");
            // The quick interior samples prefer the distal portion of the fingers. CAD faces
            // may contain pockets/holes, so failure here falls through to exact triangle overlap.
            foreach (float y in new[] { .18f, .28f, .40f, .55f, .72f })
                foreach (float z in new[] { .5f, .38f, .62f, .25f, .75f })
                {
                    var point = new Vector2(Mathf.Lerp(ymin, ymax, y), Mathf.Lerp(zmin, zmax, z));
                    if (FaceContains(left, point) && FaceContains(right, point)) return point;
                }
            Vector2 best = default;
            float bestScore = 0;
            foreach (int li in left.faceTriangles) foreach (int ri in right.faceTriangles)
            {
                var polygon = ProjectTriangle(left, li).ToList();
                Vector2[] clip = ProjectTriangle(right, ri);
                float orientation = Mathf.Sign(Cross(clip[1] - clip[0], clip[2] - clip[0]));
                if (orientation == 0) continue;
                for (int edge = 0; edge < 3 && polygon.Count >= 3; edge++)
                    polygon = ClipPolygon(polygon, clip[edge], clip[(edge + 1) % 3], orientation);
                if (polygon.Count < 3) continue;
                float twiceArea = 0;
                Vector2 sum = Vector2.zero;
                for (int i = 0; i < polygon.Count; i++)
                {
                    float weight = Cross(polygon[i], polygon[(i + 1) % polygon.Count]);
                    twiceArea += weight;
                    sum += (polygon[i] + polygon[(i + 1) % polygon.Count]) * weight;
                }
                float area = Mathf.Abs(twiceArea) * .5f;
                if (area < 1e-8f) continue;
                Vector2 centre = sum / (3 * twiceArea);
                float score = area * (1.25f - .25f * Mathf.InverseLerp(ymin, ymax, centre.x));
                if (score > bestScore && FaceContains(left, centre) && FaceContains(right, centre))
                { bestScore = score; best = centre; }
            }
            if (bestScore > 0) return best;
            throw new InvalidOperationException("No common solid contact patch was found after exact triangle intersection; see RingGripAuthoringFailure.txt for both mesh surfaces.");
        }

        static Vector2[] ProjectTriangle(JawSurface jaw, int index)
        {
            var result = new Vector2[3];
            for (int i = 0; i < 3; i++)
            { Vector3 v = jaw.vertices[jaw.triangles[index + i]]; result[i] = new Vector2(v.y, v.z); }
            return result;
        }

        static List<Vector2> ClipPolygon(List<Vector2> polygon, Vector2 a, Vector2 b, float orientation)
        {
            var result = new List<Vector2>();
            Vector2 previous = polygon[polygon.Count - 1];
            float previousSide = orientation * Cross(b - a, previous - a);
            foreach (Vector2 current in polygon)
            {
                float currentSide = orientation * Cross(b - a, current - a);
                if ((currentSide >= 0) != (previousSide >= 0))
                    result.Add(Vector2.LerpUnclamped(previous, current, previousSide / (previousSide - currentSide)));
                if (currentSide >= 0) result.Add(current);
                previous = current; previousSide = currentSide;
            }
            return result;
        }

        static void DumpJaw(JawSurface jaw, StringBuilder report)
        {
            report.AppendLine("JAW " + jaw.transform.name + " local=" + jaw.transform.localPosition.ToString("F9")
                + " rotation=" + jaw.transform.localRotation.ToString("F9") + " scale=" + jaw.transform.lossyScale.ToString("F9"));
            report.AppendLine(" meshRootLocalBounds=" + FormatBounds(jaw.bounds) + " innerX=" + jaw.innerX.ToString("F9")
                + " faceBounds=" + FormatBounds(jaw.faceBounds) + " faceTriangles=" + jaw.faceTriangles.Count);
            foreach (int i in jaw.faceTriangles.Take(100))
                report.AppendLine(" face=" + i + " " + jaw.vertices[jaw.triangles[i]].ToString("F9") + " "
                    + jaw.vertices[jaw.triangles[i + 1]].ToString("F9") + " " + jaw.vertices[jaw.triangles[i + 2]].ToString("F9"));
        }

        static string FormatBounds(Bounds bounds) => "min=" + bounds.min.ToString("F9") + " max=" + bounds.max.ToString("F9");

        static bool FaceContains(JawSurface jaw, Vector2 p)
        {
            foreach (int i in jaw.faceTriangles)
            {
                Vector3 av = jaw.vertices[jaw.triangles[i]], bv = jaw.vertices[jaw.triangles[i + 1]], cv = jaw.vertices[jaw.triangles[i + 2]];
                var a = new Vector2(av.y, av.z); var b = new Vector2(bv.y, bv.z); var c = new Vector2(cv.y, cv.z);
                float denominator = Cross(b - a, c - a);
                if (Mathf.Abs(denominator) < 1e-12f) continue;
                float u = Cross(p - a, c - a) / denominator, v = Cross(b - a, p - a) / denominator;
                if (u >= -.00001f && v >= -.00001f && u + v <= 1.00001f) return true;
            }
            return false;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static void AddMarker(Transform parent, string name, Vector3 local)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent, false); marker.localPosition = local;
        }
    }
}
