using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>
    /// 绘图碰撞查询：逐个 CAD 网格检查画板、桌面及原工作台框架。
    /// 不求解 IK、不修改训练状态；扫掠时临时采样关节，finally 恢复调用时姿态。
    /// </summary>
    public sealed class LabDrawingCollision
    {
        readonly Group0927Station station;
        public float safetyMargin = .00015f;
        public float maxSweepPadding = .0006f;
        public int maxSweepIntervals = 192;
        public float penContactTolerance = .0006f;
        public int ProxyCount => moving.Count;
        public int StaticProxyCount => obstacles.Count;
        public int LastSweepIntervals { get; private set; }
        public Transform LastHitTransform { get; private set; }
        public Transform LastObstacleTransform { get; private set; }
        public string CacheError { get; private set; } = "";

        sealed class Proxy
        {
            public MeshFilter mesh;
            public Transform transform;
            public Matrix4x4 lastMatrix;
            public Bounds lastBatchBounds;
            public bool wasActive, wasBatched;
            public Renderer renderer;
            public Bounds localBounds;
            public float[] radii;
            public int deepestJoint;
            public bool nib, paper, board, baseMount;
            public string path;
        }
        struct Box
        {
            public Vector3 center, x, y, z, extents, min, max;
        }
        readonly List<Proxy> moving = new List<Proxy>();
        readonly List<Proxy> obstacles = new List<Proxy>();
        JointConfig[] joints;
        Transform cachedModel, cachedPen, cachedBoard;
        Box[] obstacleBoxes;
        int[] activeObstacles;
        int activeObstacleCount;
        // Static CAD is indexed in world space. Rebuild the tree when a fixture moves or
        // changes visibility; intermediate joint sweep samples only query nearby leaves.
        struct Node
        {
            public Vector3 min, max;
            public int start, count, left, right;
        }
        Node[] nodes;
        int nodeCount;
        bool indexReady;
        sealed class ByCenter : IComparer<int>
        {
            readonly Box[] boxes;
            readonly int axis;
            public ByCenter(Box[] boxes, int axis) { this.boxes = boxes; this.axis = axis; }
            public int Compare(int a, int b) => boxes[a].center[axis].CompareTo(boxes[b].center[axis]);
        }
        ByCenter[] centerOrder;
        Vector3 tableNormal, tableOrigin;
        Quaternion[] finalRotations;
        float[] jointAngles, paddings;
        bool cached;

        public LabDrawingCollision(Group0927Station station) { this.station = station; }

        /// <summary>更换模型/画板/笔或调整其尺寸、固定层级后重建；不需要每帧重建。</summary>
        public void RebuildCache()
        {
            cached = true;
            CacheError = "";
            moving.Clear(); obstacles.Clear();
            cachedModel = station ? station.modelRoot : null;
            cachedBoard = station ? station.drawingBoard : null;
            cachedPen = station ? station.penRoot : null;
            joints = station ? station.joints : null;
            if (!station || !cachedModel || joints == null || joints.Length == 0)
            { CacheError = "绘图碰撞缺少模型或关节配置。"; return; }

            foreach (var filter in cachedModel.GetComponentsInChildren<MeshFilter>(true))
            {
                if (Under(filter.transform, cachedPen)) continue;
                var proxy = MakeProxy(filter);
                if (proxy == null) continue;
                proxy.deepestJoint = DeepestJoint(filter.transform);
                if (proxy.deepestJoint >= 0)
                {
                    proxy.radii = new float[joints.Length];
                    if (!CacheRadii(filter.transform, proxy.localBounds, proxy.radii))
                    { CacheError = "无效运动网格变换：" + proxy.path; return; }
                    moving.Add(proxy);
                }
                else
                {
                    Transform arm = joints[0] != null ? joints[0].joint : null;
                    while (arm && arm != cachedModel && arm.name != "机械臂") arm = arm.parent;
                    proxy.baseMount = arm && arm != cachedModel && Under(filter.transform, arm);
                    obstacles.Add(proxy);
                }
            }
            if (cachedBoard)
                foreach (var filter in cachedBoard.GetComponentsInChildren<MeshFilter>(true))
                {
                    // Printed reference paths are visual ink on the paper, not raised obstacles.
                    Transform print = cachedBoard.Find("TracingPrintedGuides");
                    if (print && filter.transform.IsChildOf(print)) continue;
                    var proxy = MakeProxy(filter);
                    if (proxy == null) continue;
                    proxy.paper = filter.name == "DrawingPaper";
                    proxy.board = true;
                    obstacles.Add(proxy);
                }
            if (cachedPen)
                foreach (var filter in cachedPen.GetComponentsInChildren<MeshFilter>(true))
                {
                    var proxy = MakeProxy(filter);
                    if (proxy == null) continue;
                    proxy.nib = filter.name == "PenNib" || filter.name == "PenTip"
                        || (station.stationIndex == 0 && filter.transform == cachedPen && filter.name == "针尖-230314");
                    proxy.deepestJoint = joints.Length;
                    proxy.radii = new float[joints.Length];
                    if (!CacheRadii(filter.transform, proxy.localBounds, proxy.radii))
                    { CacheError = "笔尖未连接到配置的关节链。"; return; }
                    moving.Add(proxy);
                }
            moving.Sort((a, b) => b.deepestJoint.CompareTo(a.deepestJoint));
            obstacleBoxes = new Box[obstacles.Count];
            activeObstacles = new int[obstacles.Count];
            nodes = new Node[2 * obstacles.Count + 1];
            centerOrder = new[] { new ByCenter(obstacleBoxes, 0), new ByCenter(obstacleBoxes, 1), new ByCenter(obstacleBoxes, 2) };
            indexReady = false;
            finalRotations = new Quaternion[joints.Length];
            jointAngles = new float[joints.Length];
            paddings = new float[moving.Count];
            if (moving.Count == 0) CacheError = "未找到机械臂运动网格。";
        }

        public bool Check(out string reason, out float minClearance)
        {
            LastSweepIntervals = 0;
            if (!Prepare(out reason)) return Unverified(reason, out reason, out minClearance);
            return CheckPose(null, safetyMargin, out reason, out minClearance);
        }

        /// <summary>检查之前到当前关节局部旋转之间的完整运动；返回后始终保持当前姿态。</summary>
        public bool CheckSweep(Quaternion[] previousLocalRotations, out string reason, out float minClearance)
        {
            LastSweepIntervals = 0;
            if (!Prepare(out reason)) return Unverified(reason, out reason, out minClearance);
            if (previousLocalRotations == null || previousLocalRotations.Length != joints.Length)
                return Unverified("绘图扫掠的关节数量不匹配。", out reason, out minClearance);
            if (!Finite(maxSweepPadding) || maxSweepPadding <= 0 || maxSweepIntervals < 1)
                return Unverified("绘图扫掠采样设置无效。", out reason, out minClearance);
            for (int j = 0; j < joints.Length; j++)
            {
                Transform joint = joints[j] != null ? joints[j].joint : null;
                finalRotations[j] = joint ? joint.localRotation : Quaternion.identity;
                if (!joint) { jointAngles[j] = 0; continue; }
                if (!Finite(previousLocalRotations[j]) || !Finite(finalRotations[j]))
                    return Unverified("绘图关节旋转无效。", out reason, out minClearance);
                jointAngles[j] = RotationAngleRadians(previousLocalRotations[j], finalRotations[j]);
            }
            float maximumMotion = 0;
            for (int p = 0; p < moving.Count; p++)
            {
                float motion = 0;
                if (Active(moving[p]))
                    for (int j = 0; j < joints.Length; j++) motion += jointAngles[j] * moving[p].radii[j];
                paddings[p] = motion;
                maximumMotion = Mathf.Max(maximumMotion, motion);
            }
            float clearanceCap = Mathf.Max(safetyMargin, 2 * maxSweepPadding);
            if (!CheckPose(null, safetyMargin, out reason, out minClearance, clearanceCap)) return false;
            float finalClearance = minClearance;
            if (maximumMotion < 1e-9f) return true;
            try
            {
                SetPose(previousLocalRotations);
                if (!CheckPose(null, safetyMargin, out reason, out minClearance, clearanceCap))
                { reason = "起始姿态：" + reason; return false; }
                float endpointClearance = Mathf.Min(finalClearance, minClearance);
                if (!Finite(maximumMotion) || endpointClearance <= 0)
                    return Unverified("绘图扫掠缺少可验证的间隙。", out reason, out minClearance);
                float paddingLimit = Mathf.Min(maxSweepPadding, endpointClearance * .5f);
                double required = Math.Ceiling(maximumMotion / (2.0 * paddingLimit));
                if (required > maxSweepIntervals)
                    return Unverified("绘图运动过大，需要拆成更小步长（" + required + " 次采样）。", out reason, out minClearance);
                int intervals = Mathf.Max(1, (int)required);
                LastSweepIntervals = intervals;
                float largestPadding = 0;
                for (int p = 0; p < paddings.Length; p++)
                { paddings[p] /= 2 * intervals; largestPadding = Mathf.Max(largestPadding, paddings[p]); }
                minClearance = endpointClearance - largestPadding;
                for (int sample = 1; sample < intervals; sample++)
                {
                    float t = sample / (float)intervals;
                    for (int j = 0; j < joints.Length; j++)
                        if (joints[j] != null && joints[j].joint)
                            joints[j].joint.localRotation = Quaternion.Slerp(previousLocalRotations[j], finalRotations[j], t);
                    float clearance;
                    if (!CheckPose(paddings, 0, out reason, out clearance, clearanceCap))
                    { minClearance = Mathf.Min(minClearance, clearance); reason = "运动路径：" + reason; return false; }
                    minClearance = Mathf.Min(minClearance, clearance);
                }
                reason = "";
                return true;
            }
            finally { SetPose(finalRotations); }
        }

        bool Prepare(out string reason)
        {
            if (!cached || cachedModel != (station ? station.modelRoot : null) ||
                cachedPen != (station ? station.penRoot : null) || cachedBoard != (station ? station.drawingBoard : null) ||
                joints != (station ? station.joints : null)) RebuildCache();
            reason = CacheError;
            if (!string.IsNullOrEmpty(reason)) return false;
            if (!Finite(safetyMargin) || safetyMargin < 0 || !Finite(penContactTolerance) || penContactTolerance < 0)
            { reason = "绘图碰撞间隙设置无效。"; return false; }
            Vector3 normal = station.transform.up;
            Vector3 origin = station.transform.TransformPoint(Vector3.up * station.tableHeight);
            bool dirty = !indexReady || !normal.Equals(tableNormal) || !origin.Equals(tableOrigin);
            tableNormal = normal;
            tableOrigin = origin;
            for (int i = 0; i < obstacles.Count; i++)
            {
                Proxy proxy = obstacles[i];
                bool active = Active(proxy);
                if (active != proxy.wasActive) dirty = true;
                bool wasActive = proxy.wasActive;
                proxy.wasActive = active;
                if (!active) continue;
                bool batched = proxy.renderer.isPartOfStaticBatch;
                if (batched)
                {
                    Bounds bounds = proxy.renderer.bounds;
                    if (wasActive && proxy.wasBatched && bounds.Equals(proxy.lastBatchBounds)) continue;
                    proxy.lastBatchBounds = bounds;
                    obstacleBoxes[i] = AxisAligned(bounds);
                }
                else
                {
                    Matrix4x4 matrix = proxy.transform.localToWorldMatrix;
                    if (wasActive && !proxy.wasBatched && matrix.Equals(proxy.lastMatrix)) continue;
                    proxy.lastMatrix = matrix;
                    obstacleBoxes[i] = WorldBox(proxy, matrix);
                }
                proxy.wasBatched = batched;
                if (!Finite(obstacleBoxes[i].center) || !Finite(obstacleBoxes[i].extents))
                {
                    proxy.wasActive = false;
                    reason = "无效静态网格变换：" + proxy.path;
                    return false;
                }
                dirty = true;
            }
            if (dirty)
            {
                activeObstacleCount = 0;
                for (int i = 0; i < obstacles.Count; ++i)
                {
                    if (!obstacles[i].wasActive) continue;
                    Box box = obstacleBoxes[i];
                    if (!obstacles[i].board && Vector3.Dot(box.center - tableOrigin, tableNormal)
                        + Radius(box, tableNormal) <= .001f) continue;
                    activeObstacles[activeObstacleCount++] = i;
                }
                nodeCount = 0;
                if (activeObstacleCount > 0) BuildNode(0, activeObstacleCount);
                indexReady = true;
            }
            return true;
        }

        int BuildNode(int start, int count)
        {
            int index = nodeCount++;
            Box first = obstacleBoxes[activeObstacles[start]];
            Node node = new Node { min = first.min, max = first.max, start = start, count = count };
            for (int i = start + 1; i < start + count; i++)
            {
                Box box = obstacleBoxes[activeObstacles[i]];
                node.min = Vector3.Min(node.min, box.min);
                node.max = Vector3.Max(node.max, box.max);
            }
            if (count > 8)
            {
                Vector3 size = node.max - node.min;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Array.Sort(activeObstacles, start, count, centerOrder[axis]);
                int half = count / 2;
                node.left = BuildNode(start, half);
                node.right = BuildNode(start + half, count - half);
                node.count = 0;
            }
            nodes[index] = node;
            return index;
        }

        // The AABB gap is a lower bound on separation. Include the swept-motion skin
        // in the search distance before discarding an entire subtree.
        bool CheckNode(int index, Proxy proxy, ref Box box, float skin, float margin,
            ref float minClearance, ref string reason)
        {
            ref Node node = ref nodes[index];
            float search = Mathf.Max(margin, minClearance) + skin;
            if (box.min.x > node.max.x + search || box.max.x < node.min.x - search ||
                box.min.y > node.max.y + search || box.max.y < node.min.y - search ||
                box.min.z > node.max.z + search || box.max.z < node.min.z - search)
                return true;
            if (node.count == 0)
                return CheckNode(node.left, proxy, ref box, skin, margin, ref minClearance, ref reason)
                    && CheckNode(node.right, proxy, ref box, skin, margin, ref minClearance, ref reason);
            for (int j = node.start; j < node.start + node.count; j++)
            {
                int o = activeObstacles[j];
                Proxy obstacle = obstacles[o];
                if (proxy.deepestJoint == 0 && obstacle.baseMount) continue;
                Box other = obstacleBoxes[o];
                float allowance = proxy.nib && obstacle.paper ? penContactTolerance : 0;
                float distanceSquared = BoundsDistanceSquared(box, other);
                float threshold = Mathf.Max(margin, minClearance) + skin - allowance;
                if (distanceSquared > 0 && (threshold <= 0 || distanceSquared >= threshold * threshold)) continue;
                float gap = Separation(box, other) - skin + allowance;
                if (gap < minClearance) minClearance = gap;
                if (gap < margin) return Hit(proxy, obstacle, gap, obstacle.path, out reason);
            }
            return true;
        }

        bool CheckPose(float[] padding, float margin, out string reason, out float minClearance,
            float clearanceCap = float.PositiveInfinity)
        {
            minClearance = clearanceCap;
            reason = "";
            LastHitTransform = LastObstacleTransform = null;
            int activeMoving = 0;
            for (int p = 0; p < moving.Count; p++)
            {
                Proxy proxy = moving[p];
                if (!Active(proxy)) continue;
                activeMoving++;
                Box box = WorldBox(proxy);
                if (!Finite(box.center) || !Finite(box.extents))
                    return Unverified("绘图网格变换无效：" + proxy.path, out reason, out minClearance);
                float skin = padding != null ? padding[p] : 0;
                // 第一轴座本来就安装在桌面上；桌面平面只约束肩部之后及笔。
                if (proxy.deepestJoint >= 1)
                {
                    float gap = Vector3.Dot(box.center - tableOrigin, tableNormal) - Radius(box, tableNormal) - skin;
                    if (gap < minClearance) minClearance = gap;
                    if (gap < margin) return Hit(proxy, null, gap, "工作台面", out reason);
                }
                if (nodeCount > 0 && !CheckNode(0, proxy, ref box, skin, margin, ref minClearance, ref reason))
                    return false;
            }
            if (activeMoving == 0)
                return Unverified("没有可验证的活动机械臂网格。", out reason, out minClearance);
            return true;
        }

        bool Hit(Proxy proxy, Proxy obstacle, float gap, string name, out string reason)
        {
            LastHitTransform = proxy.mesh ? proxy.mesh.transform : null;
            LastObstacleTransform = obstacle != null && obstacle.mesh ? obstacle.mesh.transform : null;
            reason = proxy.path + " 接近 " + name + "，保守间隙 " + (gap * 1000).ToString("F3") + " mm。";
            return false;
        }

        Proxy MakeProxy(MeshFilter filter)
        {
            if (!filter || !filter.sharedMesh) return null;
            var renderer = filter.GetComponent<Renderer>();
            if (!renderer) return null;
            return new Proxy { mesh = filter, transform = filter.transform, renderer = renderer, localBounds = filter.sharedMesh.bounds,
                path = PathOf(filter.transform, station.transform) };
        }
        int DeepestJoint(Transform child)
        {
            int result = -1;
            for (int j = 0; j < joints.Length; j++)
                if (joints[j] != null && Under(child, joints[j].joint)) result = j;
            return result;
        }
        void SetPose(Quaternion[] rotations)
        {
            for (int j = 0; j < joints.Length; j++)
                if (joints[j] != null && joints[j].joint) joints[j].joint.localRotation = rotations[j];
        }
        Box WorldBox(Proxy proxy)
        {
            // Unity 静态合批后 sharedMesh 可能是整个合批网格，已不再属于本零件的
            // 局部坐标。Renderer.bounds 仍是当前零件的正确世界包围盒，直接使用。
            return proxy.renderer.isPartOfStaticBatch ? AxisAligned(proxy.renderer.bounds)
                : WorldBox(proxy, proxy.transform.localToWorldMatrix);
        }
        static Box AxisAligned(Bounds bounds) =>
            new Box { center = bounds.center, x = Vector3.right, y = Vector3.up, z = Vector3.forward,
                extents = bounds.extents, min = bounds.min, max = bounds.max };
        static Box WorldBox(Proxy proxy, Matrix4x4 matrix)
        {
            Quaternion rotation = proxy.transform.rotation;
            Vector3 extent = proxy.localBounds.extents;
            Vector3 vx = matrix.MultiplyVector(Vector3.right * extent.x);
            Vector3 vy = matrix.MultiplyVector(Vector3.up * extent.y);
            Vector3 vz = matrix.MultiplyVector(Vector3.forward * extent.z);
            var box = new Box { center = matrix.MultiplyPoint3x4(proxy.localBounds.center),
                x = rotation * Vector3.right, y = rotation * Vector3.up, z = rotation * Vector3.forward };
            box.extents = new Vector3(Project(box.x, vx, vy, vz), Project(box.y, vx, vy, vz), Project(box.z, vx, vy, vz));
            Vector3 worldExtent = Abs(box.x) * box.extents.x + Abs(box.y) * box.extents.y + Abs(box.z) * box.extents.z;
            box.min = box.center - worldExtent; box.max = box.center + worldExtent;
            return box;
        }

        // 15 轴分离轴定理；正值是保守分离间隙，负值是盒体相交。
        static float Separation(Box a, Box b)
        {
            Vector3 delta = b.center - a.center;
            float result = float.NegativeInfinity;
            result = Mathf.Max(result, AxisGap(a, b, delta, a.x));
            result = Mathf.Max(result, AxisGap(a, b, delta, a.y));
            result = Mathf.Max(result, AxisGap(a, b, delta, a.z));
            result = Mathf.Max(result, AxisGap(a, b, delta, b.x));
            result = Mathf.Max(result, AxisGap(a, b, delta, b.y));
            result = Mathf.Max(result, AxisGap(a, b, delta, b.z));
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++)
            {
                Vector3 axis = Vector3.Cross(Axis(a, i), Axis(b, j));
                if (axis.sqrMagnitude > 1e-10f)
                    result = Mathf.Max(result, AxisGap(a, b, delta, axis.normalized));
            }
            return result;
        }
        static float AxisGap(Box a, Box b, Vector3 delta, Vector3 axis) =>
            Mathf.Abs(Vector3.Dot(delta, axis)) - Radius(a, axis) - Radius(b, axis);
        static Vector3 Axis(Box b, int i) => i == 0 ? b.x : i == 1 ? b.y : b.z;
        static float Radius(Box b, Vector3 axis) => Mathf.Abs(Vector3.Dot(b.x, axis)) * b.extents.x +
            Mathf.Abs(Vector3.Dot(b.y, axis)) * b.extents.y + Mathf.Abs(Vector3.Dot(b.z, axis)) * b.extents.z;
        static float BoundsDistanceSquared(Box a, Box b) =>
            Vector3.Max(Vector3.zero, Vector3.Max(a.min - b.max, b.min - a.max)).sqrMagnitude;

        bool CacheRadii(Transform item, Bounds bounds, float[] radii)
        {
            var chain = new List<Transform>();
            for (Transform t = item; t; t = t.parent)
                for (int j = 0; j < joints.Length; j++)
                    if (joints[j] != null && joints[j].joint == t) { chain.Add(t); break; }
            if (chain.Count == 0) return false;
            Matrix4x4 relative = RelativeMatrix(item, chain[0]);
            float radius = 0;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                radius = Mathf.Max(radius, relative.MultiplyPoint3x4(bounds.center +
                    Vector3.Scale(bounds.extents, new Vector3(x, y, z))).magnitude);
            for (int k = 0; k < chain.Count; k++)
            {
                Transform joint = chain[k];
                float worldRadius = radius * ScaleUpperBound(joint);
                if (!Finite(worldRadius)) return false;
                for (int j = 0; j < joints.Length; j++)
                    if (joints[j] != null && joints[j].joint == joint) radii[j] = worldRadius;
                if (k + 1 == chain.Count) break;
                Transform parent = chain[k + 1];
                Vector3 pivot = RelativeMatrix(joint.parent, parent).MultiplyPoint3x4(joint.localPosition);
                float scale = 1;
                for (Transform t = joint; t && t != parent; t = t.parent)
                    scale *= MaximumScale(t.localScale);
                radius = pivot.magnitude + radius * scale;
            }
            return true;
        }
        static Matrix4x4 RelativeMatrix(Transform child, Transform ancestor)
        {
            Matrix4x4 result = Matrix4x4.identity;
            while (child && child != ancestor)
            { result = Matrix4x4.TRS(child.localPosition, child.localRotation, child.localScale) * result; child = child.parent; }
            return result;
        }
        static bool Under(Transform child, Transform parent) => child && parent && (child == parent || child.IsChildOf(parent));
        static bool Active(Proxy p) => p.mesh && p.renderer && p.mesh.gameObject.activeInHierarchy && p.renderer.enabled;
        static float ScaleUpperBound(Transform t)
        { float result = 1; while (t) { result *= MaximumScale(t.localScale); t = t.parent; } return result; }
        static float MaximumScale(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        static float Project(Vector3 axis, Vector3 a, Vector3 b, Vector3 c) =>
            Mathf.Abs(Vector3.Dot(axis, a)) + Mathf.Abs(Vector3.Dot(axis, b)) + Mathf.Abs(Vector3.Dot(axis, c));
        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        static string PathOf(Transform t, Transform root)
        { string result = t.name; while (t.parent && t.parent != root) { t = t.parent; result = t.name + "/" + result; } return result; }
        bool Unverified(string message, out string reason, out float minClearance)
        { reason = message; minClearance = float.NegativeInfinity; LastHitTransform = LastObstacleTransform = null; return false; }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        static bool Finite(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w) &&
            q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w > .000001f;
        static float RotationAngleRadians(Quaternion a, Quaternion b)
        {
            double an = Math.Sqrt((double)a.x*a.x + (double)a.y*a.y + (double)a.z*a.z + (double)a.w*a.w);
            double bn = Math.Sqrt((double)b.x*b.x + (double)b.y*b.y + (double)b.z*b.z + (double)b.w*b.w);
            double sign = (double)a.x*b.x + (double)a.y*b.y + (double)a.z*b.z + (double)a.w*b.w < 0 ? -1 : 1;
            double ax = a.x/an, ay = a.y/an, az = a.z/an, aw = a.w/an;
            double bx = sign*b.x/bn, by = sign*b.y/bn, bz = sign*b.z/bn, bw = sign*b.w/bn;
            double difference = (ax-bx)*(ax-bx) + (ay-by)*(ay-by) + (az-bz)*(az-bz) + (aw-bw)*(aw-bw);
            double sum = (ax+bx)*(ax+bx) + (ay+by)*(ay+by) + (az+bz)*(az+bz) + (aw+bw)*(aw+bw);
            return (float)(4 * Math.Atan2(Math.Sqrt(difference), Math.Sqrt(sum)));
        }
    }
}
