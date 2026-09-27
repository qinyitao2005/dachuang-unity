using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoboticsLab.Editor
{
    /// <summary>Creates a reversible, independent body mesh while retaining the original rig.</summary>
    public static class LabAvatarBodyAuthoring
    {
        const string SourcePath = "Assets/Jogging.fbx";
        const string Folder = "Assets/RoboticsLab/Generated";
        const float Reduction = .11f;

        sealed class BoneRule
        {
            public string region;
            public Vector3 origin, axis;
            public bool protect;
            public bool active => !string.IsNullOrEmpty(region);
        }

        sealed class Metric
        {
            public int count;
            public double before, after;
        }

        [MenuItem("Robot Lab/Update Natural Walk And Slim Body")]
        public static void UpdateAvatar() => LabAvatarEquipmentAuthoring.UpdateScene();

        public static void Apply(LabThirdPerson player)
        {
            if (!player || !player.animator || !player.animator.isHuman)
                throw new InvalidOperationException("A valid Humanoid trainee is required for body authoring.");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            if (!source) throw new InvalidOperationException("The preserved Jogging.fbx source is missing.");
            string originalHash = Hash(SourcePath);
            var report = new List<string>
            {
                "Avatar body authoring / " + DateTime.UtcNow.ToString("O"),
                "Source: " + SourcePath,
                "Body radial reduction: 11 percent around bind-pose torso and limb axes; blended by the original skin weights.",
                "No skeleton, bone lengths, bind poses, skin weights, materials, head, hands or footwear are scaled.",
                "Protected head/neck/hand/foot weights taper the reduction smoothly to zero; their dominant vertices remain identical.",
                "Every update rebuilds from the original FBX mesh, preventing cumulative slimming."
            };
            var originals = source.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .ToDictionary(r => AnimationUtility.CalculateTransformPath(r.transform, source.transform));
            int processed = 0;
            Directory.CreateDirectory(Folder);
            foreach (var renderer in player.animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, player.animator.transform);
                SkinnedMeshRenderer originalRenderer;
                if (!originals.TryGetValue(path, out originalRenderer))
                    throw new InvalidOperationException("Cannot safely map trainee renderer to original FBX: " + path);
                Mesh original = originalRenderer.sharedMesh;
                if (!original) continue;
                Vector3[] vertices = original.vertices;
                BoneWeight[] weights = original.boneWeights;
                Matrix4x4[] bindposes = original.bindposes;
                if (vertices.Length != weights.Length || bindposes.Length != originalRenderer.bones.Length)
                    throw new InvalidOperationException("Unexpected skinning layout on " + original.name);
                var rules = MakeRules(player.animator, originalRenderer, bindposes);
                Vector3[] output = (Vector3[])vertices.Clone();
                Vector3[] normals = original.normals;
                Vector4[] tangents = original.tangents;
                var metrics = new Dictionary<string, Metric>();
                int modified = 0, protectedVertices = 0;
                float maxWorldDelta = 0, maxProtectedDelta = 0;
                for (int v = 0; v < vertices.Length; v++)
                {
                    BoneWeight weight = weights[v];
                    int[] indices = { weight.boneIndex0, weight.boneIndex1, weight.boneIndex2, weight.boneIndex3 };
                    float[] values = { weight.weight0, weight.weight1, weight.weight2, weight.weight3 };
                    float protectedWeight = 0;
                    for (int w = 0; w < 4; w++) if (values[w] > 0 && rules[indices[w]].protect) protectedWeight += values[w];
                    float attenuation = 1f - Mathf.SmoothStep(0, 1, Mathf.Clamp01(protectedWeight / .35f));
                    Vector3 delta = Vector3.zero, normalDelta = Vector3.zero, tangentDelta = Vector3.zero;
                    for (int w = 0; w < 4; w++)
                    {
                        var rule = rules[indices[w]];
                        if (values[w] <= 0 || !rule.active || attenuation <= 0) continue;
                        float amount = Reduction * attenuation;
                        delta -= Vector3.ProjectOnPlane(vertices[v] - rule.origin, rule.axis) * (amount * values[w]);
                        // Keep the imported seam normals smooth. RecalculateNormals would split
                        // the original FBX's UV seams; use each radial transform's inverse transpose.
                        if (normals.Length == vertices.Length)
                            normalDelta += Vector3.ProjectOnPlane(normals[v], rule.axis) * (amount / (1 - amount) * values[w]);
                        if (tangents.Length == vertices.Length)
                            tangentDelta -= Vector3.ProjectOnPlane((Vector3)tangents[v], rule.axis) * (amount * values[w]);
                    }
                    output[v] += delta;
                    if (normals.Length == vertices.Length) normals[v] = (normals[v] + normalDelta).normalized;
                    if (tangents.Length == vertices.Length)
                    {
                        Vector3 tangent = (Vector3)tangents[v] + tangentDelta;
                        if (normals.Length == vertices.Length) tangent = Vector3.ProjectOnPlane(tangent, normals[v]);
                        tangent.Normalize();
                        tangents[v] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[v].w);
                    }
                    if (delta.sqrMagnitude > 1e-12f) modified++;
                    float worldDelta = renderer.transform.TransformVector(delta).magnitude;
                    maxWorldDelta = Mathf.Max(maxWorldDelta, worldDelta);
                    if (protectedWeight >= .35f)
                    {
                        protectedVertices++;
                        maxProtectedDelta = Mathf.Max(maxProtectedDelta, worldDelta);
                    }
                    int dominant = 0;
                    for (int w = 1; w < 4; w++) if (values[w] > values[dominant]) dominant = w;
                    var primary = rules[indices[dominant]];
                    if (primary.active && values[dominant] >= .75f && protectedWeight < .001f)
                    {
                        Metric metric;
                        if (!metrics.TryGetValue(primary.region, out metric)) metrics[primary.region] = metric = new Metric();
                        metric.count++;
                        metric.before += Vector3.ProjectOnPlane(vertices[v] - primary.origin, primary.axis).magnitude;
                        metric.after += Vector3.ProjectOnPlane(output[v] - primary.origin, primary.axis).magnitude;
                    }
                }
                Mesh clone = Object.Instantiate(original);
                clone.name = "TraineeSlim_" + original.name;
                clone.hideFlags = HideFlags.None;
                clone.vertices = output;
                if (normals.Length == vertices.Length) clone.normals = normals;
                if (tangents.Length == vertices.Length) clone.tangents = tangents;
                clone.RecalculateBounds();
                string guid; long fileId;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out guid, out fileId);
                string meshPath = Folder + "/TraineeSlim_" + Regex.Replace(original.name, "[^A-Za-z0-9_-]", "_") + "_" + fileId + ".asset";
                clone.name = Path.GetFileNameWithoutExtension(meshPath);
                Mesh asset = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (asset) { EditorUtility.CopySerialized(clone, asset); Object.DestroyImmediate(clone); }
                else { asset = clone; AssetDatabase.CreateAsset(asset, meshPath); }
                Undo.RecordObject(renderer, "Use independently authored slim body mesh");
                renderer.sharedMesh = asset;
                // The original animation bounds remain a conservative culling volume.
                renderer.localBounds = originalRenderer.localBounds;
                EditorUtility.SetDirty(asset); EditorUtility.SetDirty(renderer);
                bool weightsPreserved = asset.boneWeights.SequenceEqual(original.boneWeights);
                bool bindposesPreserved = asset.bindposes.SequenceEqual(original.bindposes);
                report.Add("MESH " + path + " => " + meshPath + " vertices=" + vertices.Length + " modified=" + modified + " maxDeltaMeters=" + maxWorldDelta.ToString("F6"));
                report.Add((weightsPreserved && bindposesPreserved ? "PASS" : "FAIL") + ": original skin weights and bind poses preserved");
                report.Add((maxProtectedDelta <= 1e-7f ? "PASS" : "FAIL") + ": protected dominant head/neck/hand/foot vertices unchanged; count=" + protectedVertices + " maxDeltaMeters=" + maxProtectedDelta.ToString("F8"));
                foreach (var metric in metrics.OrderBy(m => m.Key))
                    report.Add("RADIAL " + metric.Key + " highInfluenceVertices=" + metric.Value.count + " meanRatio=" + (metric.Value.after / Math.Max(1e-12, metric.Value.before)).ToString("F6"));
                if (!weightsPreserved || !bindposesPreserved || maxProtectedDelta > 1e-7f)
                    throw new InvalidOperationException("Slim-body invariants failed for " + path);
                processed++;
            }
            if (processed == 0) throw new InvalidOperationException("No trainee skinned meshes were updated.");
            string afterHash = Hash(SourcePath);
            report.Add((originalHash == afterHash ? "PASS" : "FAIL") + ": source FBX SHA256 unchanged: " + afterHash);
            Directory.CreateDirectory("Documentation");
            File.WriteAllLines("Documentation/AvatarBodyFit.txt", report);
            if (originalHash != afterHash) throw new InvalidOperationException("The original avatar file changed unexpectedly.");
            Debug.Log("ROBOT_AVATAR_SLIM_BODY_READY independentMeshes=" + processed + " radialReduction=" + Reduction);
        }

        static BoneRule[] MakeRules(Animator animator, SkinnedMeshRenderer renderer, Matrix4x4[] bindposes)
        {
            var human = new Dictionary<string, HumanBodyBones>();
            var current = animator.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform t = animator.GetBoneTransform((HumanBodyBones)i);
                if (t) human[t.name] = (HumanBodyBones)i;
            }
            Func<HumanBodyBones, int> indexOf = bone =>
            {
                var t = animator.GetBoneTransform(bone);
                return t ? Array.FindIndex(renderer.bones, b => b && b.name == t.name) : -1;
            };
            Func<int, Vector3> origin = index => bindposes[index].inverse.MultiplyPoint3x4(Vector3.zero);
            var protectedRoots = new[] { HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }.Select(animator.GetBoneTransform).Where(t => t).ToArray();
            int hips = indexOf(HumanBodyBones.Hips), neck = indexOf(HumanBodyBones.Neck);
            if (neck < 0) neck = indexOf(HumanBodyBones.Chest);
            var rules = new BoneRule[renderer.bones.Length];
            for (int i = 0; i < rules.Length; i++)
            {
                var rule = rules[i] = new BoneRule();
                if (!renderer.bones[i]) continue;
                Transform mapped;
                if (current.TryGetValue(renderer.bones[i].name, out mapped)) rule.protect = protectedRoots.Any(p => mapped == p || mapped.IsChildOf(p));
                HumanBodyBones bone;
                if (rule.protect || !human.TryGetValue(renderer.bones[i].name, out bone)) continue;
                if (bone == HumanBodyBones.Hips || bone == HumanBodyBones.Spine || bone == HumanBodyBones.Chest || bone == HumanBodyBones.UpperChest)
                {
                    if (hips < 0 || neck < 0) continue;
                    rule.region = "Torso"; rule.origin = origin(hips); rule.axis = (origin(neck) - rule.origin).normalized;
                }
                else
                {
                    HumanBodyBones child;
                    switch (bone)
                    {
                        case HumanBodyBones.LeftUpperArm: child = HumanBodyBones.LeftLowerArm; break;
                        case HumanBodyBones.RightUpperArm: child = HumanBodyBones.RightLowerArm; break;
                        case HumanBodyBones.LeftLowerArm: child = HumanBodyBones.LeftHand; break;
                        case HumanBodyBones.RightLowerArm: child = HumanBodyBones.RightHand; break;
                        case HumanBodyBones.LeftUpperLeg: child = HumanBodyBones.LeftLowerLeg; break;
                        case HumanBodyBones.RightUpperLeg: child = HumanBodyBones.RightLowerLeg; break;
                        case HumanBodyBones.LeftLowerLeg: child = HumanBodyBones.LeftFoot; break;
                        case HumanBodyBones.RightLowerLeg: child = HumanBodyBones.RightFoot; break;
                        default: continue;
                    }
                    int end = indexOf(child);
                    if (end < 0) continue;
                    rule.region = bone.ToString(); rule.origin = origin(i); rule.axis = (origin(end) - rule.origin).normalized;
                }
                if (rule.axis.sqrMagnitude < .9f) rule.region = null;
            }
            return rules;
        }

        static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
