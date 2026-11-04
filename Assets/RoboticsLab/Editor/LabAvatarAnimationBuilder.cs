using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoboticsLab.Editor
{
    /// <summary>Preserves the source motion capture for locomotion and authors a separate standing idle.</summary>
    public static class LabAvatarAnimationBuilder
    {
        const string Folder = "Assets/RoboticsLab/Generated";
        const string IdlePath = Folder + "/TraineeIdle.anim";
        const string WalkPath = Folder + "/TraineeNeutralWalk.anim";
        const string ControllerPath = Folder + "/TraineeLocomotion.controller";
        const float IdleLength = 2f;
        const float WalkReferenceSpeed = 1.6f;

        public static RuntimeAnimatorController Build(Animator animator, AnimationClip walkClip)
        {
            if (!animator) throw new ArgumentNullException(nameof(animator));
            if (!walkClip) throw new ArgumentNullException(nameof(walkClip));
            if (!Directory.Exists(Folder))
            {
                Directory.CreateDirectory(Folder);
                AssetDatabase.Refresh();
            }

            AnimationClip idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdlePath);
            if (!idle)
            {
                idle = new AnimationClip { name = "TraineeIdle", frameRate = 30, legacy = false };
                AssetDatabase.CreateAsset(idle, IdlePath);
            }
            idle.ClearCurves();

            bool humanoid = animator.avatar && animator.avatar.isValid && animator.avatar.isHuman;
            if (humanoid) WriteHumanoidIdle(idle, animator, walkClip);
            else
            {
                WriteFixedFrame(idle, walkClip);
                Debug.LogWarning("ROBOT_AVATAR_IDLE_FALLBACK: no valid Humanoid avatar; using a deterministic first-frame hold.");
            }

            var settings = AnimationUtility.GetAnimationClipSettings(idle);
            settings.loopTime = true;
            settings.loopBlend = false;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = true;
            settings.loopBlendOrientation = true;
            settings.loopBlendPositionY = true;
            settings.loopBlendPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(idle, settings);
            idle.EnsureQuaternionContinuity();
            EditorUtility.SetDirty(idle);

            // Keep the imported natural body motion, including pelvis, chest and arm timing.
            // Copy the clip asset without resampling or synthesizing any muscle curves.
            AnimationClip locomotion = AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkPath);
            if (!locomotion)
            {
                locomotion = new AnimationClip();
                AssetDatabase.CreateAsset(locomotion, WalkPath);
            }
            EditorUtility.CopySerialized(walkClip, locomotion);
            locomotion.name = Path.GetFileNameWithoutExtension(WalkPath);
            locomotion.hideFlags = HideFlags.None;
            var walkSettings = AnimationUtility.GetAnimationClipSettings(locomotion);
            walkSettings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(locomotion, walkSettings);
            EditorUtility.SetDirty(locomotion);
            float nativeSpeed = EstimateSourceSpeed(animator, walkClip);
            // Speed supplied by LabThirdPerson is actual movement / 1.6, so only this
            // constant cadence calibration is needed. Limits preserve the source rhythm.
            float playbackRate = nativeSpeed > .1f ? Mathf.Clamp(WalkReferenceSpeed / nativeSpeed, .72f, 1.15f) : 1f;

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.parameters = new[]
            {
                new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float, defaultFloat = 0 }
            };
            if (controller.layers.Length == 0) controller.AddLayer("Base Layer");
            var machine = controller.layers[0].stateMachine;
            foreach (var old in machine.states) machine.RemoveState(old.state);
            var idleState = machine.AddState("Idle", new Vector3(250, 80));
            idleState.motion = idle;
            idleState.writeDefaultValues = true;
            var walkState = machine.AddState("Walk", new Vector3(480, 80));
            walkState.motion = locomotion;
            walkState.speed = playbackRate;
            walkState.writeDefaultValues = true;
            walkState.speedParameter = "Speed";
            walkState.speedParameterActive = true;
            machine.defaultState = idleState;
            ConfigureTransition(idleState.AddTransition(walkState), AnimatorConditionMode.Greater, .08f);
            ConfigureTransition(walkState.AddTransition(idleState), AnimatorConditionMode.Less, .08f);
            animator.applyRootMotion = false;
            EditorUtility.SetDirty(controller);
            WriteMotionEvidence(animator, walkClip, locomotion, nativeSpeed, playbackRate);
            AssetDatabase.SaveAssets();
            Debug.Log("ROBOT_AVATAR_LOCOMOTION_READY humanoid=" + humanoid + " idleHumanMotion=" + idle.humanMotion + " walkHumanMotion=" + locomotion.humanMotion + " walk=" + AssetDatabase.GetAssetPath(locomotion));
            return controller;
        }

        static void ConfigureTransition(AnimatorStateTransition transition, AnimatorConditionMode condition, float threshold)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = .18f;
            transition.offset = 0;
            transition.canTransitionToSelf = false;
            transition.AddCondition(condition, threshold, "Speed");
        }

        static void WriteHumanoidIdle(AnimationClip clip, Animator source, AnimationClip walkClip)
        {
            GameObject temporary = Object.Instantiate(source.gameObject);
            temporary.name = "__RobotLab_IdleAuthoringPreview";
            temporary.hideFlags = HideFlags.HideAndDontSave;
            temporary.transform.SetParent(null, false);
            temporary.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            temporary.transform.localScale = Vector3.one;
            HumanPoseHandler handler = null;
            try
            {
                var preview = temporary.GetComponent<Animator>();
                preview.enabled = false;
                preview.runtimeAnimatorController = null;
                ApplyStandingPose(preview);

                handler = new HumanPoseHandler(preview.avatar, preview.transform);
                HumanPose pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                if (pose.muscles == null || pose.muscles.Length != HumanTrait.MuscleCount)
                    throw new InvalidOperationException("Unity could not extract the temporary idle HumanPose.");

                var knownBindings = new Dictionary<string, string>();
                foreach (var binding in AnimationUtility.GetCurveBindings(walkClip))
                    if (binding.type == typeof(Animator)) knownBindings[Normalize(binding.propertyName)] = binding.propertyName;
                for (int i = 0; i < HumanTrait.MuscleCount; i++)
                {
                    string muscle = HumanTrait.MuscleName[i];
                    string property;
                    if (!knownBindings.TryGetValue(Normalize(muscle), out property)) property = MuscleProperty(muscle);
                    Constant(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property), Mathf.Clamp(pose.muscles[i], -1f, 1f));
                }
                Constant(clip, "RootT.x", pose.bodyPosition.x);
                Constant(clip, "RootT.y", pose.bodyPosition.y);
                Constant(clip, "RootT.z", pose.bodyPosition.z);
                Constant(clip, "RootQ.x", pose.bodyRotation.x);
                Constant(clip, "RootQ.y", pose.bodyRotation.y);
                Constant(clip, "RootQ.z", pose.bodyRotation.z);
                Constant(clip, "RootQ.w", pose.bodyRotation.w);
            }
            finally
            {
                if (handler != null) handler.Dispose();
                Object.DestroyImmediate(temporary);
            }
        }

        static void ApplyStandingPose(Animator preview)
        {
            RestoreAvatarReference(preview);
            var head = preview.GetBoneTransform(HumanBodyBones.Head);
            Quaternion referenceHeadRotation = head ? head.rotation : Quaternion.identity;
            float referenceFeet = LowestFoot(preview);
            // Pose actual mapped bones first, then convert to this Avatar's muscles.
            Aim(preview, HumanBodyBones.Hips, HumanBodyBones.Spine, Vector3.up);
            Aim(preview, HumanBodyBones.Spine, HumanBodyBones.Chest, Vector3.up);
            Aim(preview, HumanBodyBones.Chest, HumanBodyBones.UpperChest, Vector3.up);
            Aim(preview, HumanBodyBones.UpperChest, HumanBodyBones.Neck, Vector3.up);
            Aim(preview, HumanBodyBones.Neck, HumanBodyBones.Head, Vector3.up);
            // The anatomical neck-to-head joint vector is not the face's viewing direction.
            // Retain the Avatar's reference head orientation after straightening the neck.
            if (head) head.rotation = referenceHeadRotation;
            PoseLeg(preview, true);
            PoseLeg(preview, false);
            PoseArm(preview, true);
            PoseArm(preview, false);
            var hips = preview.GetBoneTransform(HumanBodyBones.Hips);
            if (hips) hips.position += Vector3.up * (referenceFeet - LowestFoot(preview));
        }

        static float EstimateSourceSpeed(Animator source, AnimationClip clip)
        {
            float scale = Mathf.Abs(source.transform.lossyScale.y);
            float importedSpeed = Vector3.ProjectOnPlane(clip.averageSpeed, Vector3.up).magnitude * scale;
            if (importedSpeed > .1f && importedSpeed < 8f) return importedSpeed;
            // In-place Mixamo clips have zero root velocity. Estimate their native
            // cadence from backward foot travel near the ground, without changing poses.
            var temporary = Object.Instantiate(source.gameObject);
            temporary.name = "__OriginalMotionCadencePreview";
            temporary.hideFlags = HideFlags.HideAndDontSave;
            temporary.transform.SetParent(null, false);
            temporary.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            temporary.transform.localScale = source.transform.lossyScale;
            try
            {
                var animator = temporary.GetComponent<Animator>();
                animator.runtimeAnimatorController = null; animator.enabled = false;
                if (!animator.isHuman || clip.length <= .01f) return 0;
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                var feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
                if (!hips || feet.Any(f => !f)) return 0;
                const int samples = 96;
                var heights = new float[2, samples + 1];
                var positions = new float[2, samples + 1];
                var minima = new[] { float.PositiveInfinity, float.PositiveInfinity };
                for (int i = 0; i <= samples; i++)
                {
                    clip.SampleAnimation(temporary, clip.length * i / samples);
                    for (int foot = 0; foot < 2; foot++)
                    {
                        heights[foot, i] = feet[foot].position.y;
                        positions[foot, i] = feet[foot].position.z - hips.position.z;
                        minima[foot] = Mathf.Min(minima[foot], heights[foot, i]);
                    }
                }
                var velocities = new List<float>();
                float dt = clip.length / samples;
                for (int foot = 0; foot < 2; foot++) for (int i = 1; i <= samples; i++)
                {
                    float velocity = (positions[foot, i - 1] - positions[foot, i]) / dt;
                    if (Mathf.Max(heights[foot, i - 1], heights[foot, i]) < minima[foot] + .04f && velocity > .2f && velocity < 8f)
                        velocities.Add(velocity);
                }
                velocities.Sort();
                return velocities.Count >= 4 ? velocities[velocities.Count / 2] : 0;
            }
            finally { Object.DestroyImmediate(temporary); }
        }

        static void WriteMotionEvidence(Animator animator, AnimationClip source, AnimationClip output, float nativeSpeed, float playback)
        {
            var report = new List<string>
            {
                "Natural locomotion authoring / " + DateTime.UtcNow.ToString("O"),
                "Source: " + AssetDatabase.GetAssetPath(source) + " / " + source.name,
                "Output: " + WalkPath + " (existing asset path retained for diagnostics and references)",
                "Original humanoid clip copied directly; no generated foot targets or resampled muscle curves.",
                "Source seconds=" + source.length.ToString("F5") + " frameRate=" + source.frameRate + " humanoid=" + source.humanMotion,
                "Movement reference m/s=" + WalkReferenceSpeed + "; estimated source m/s=" + nativeSpeed.ToString("F5") + "; base playback multiplier=" + playback.ToString("F5"),
                "Animator Speed is movement/reference; it multiplies the base playback once. Root motion remains disabled.",
                "Cadence calibration is bounded to 0.72..1.15 to retain natural recorded rhythm; it is not a measured no-slip guarantee."
            };
            var sourceBindings = AnimationUtility.GetCurveBindings(source);
            var outputBindings = AnimationUtility.GetCurveBindings(output);
            int compared = 0, differing = 0, animated = 0;
            foreach (var binding in sourceBindings)
            {
                var a = AnimationUtility.GetEditorCurve(source, binding);
                var b = AnimationUtility.GetEditorCurve(output, binding);
                if (a == null) continue;
                compared++;
                bool same = b != null && a.length == b.length;
                if (same) for (int i = 0; i < a.length; i++)
                {
                    Keyframe x = a.keys[i], y = b.keys[i];
                    if (!x.Equals(y)) { same = false; break; }
                }
                if (!same) differing++;
                float range = a.length > 0 ? a.keys.Max(k => k.value) - a.keys.Min(k => k.value) : 0;
                if (range > .0001f) animated++;
                if (binding.type == typeof(Animator) && (binding.propertyName.Contains("Spine") || binding.propertyName.Contains("Chest") || binding.propertyName.Contains("Leg") || binding.propertyName.StartsWith("Root")))
                    report.Add("CURVE " + binding.propertyName + " keys=" + a.length + " range=" + range.ToString("F6") + " preserved=" + same);
            }
            bool preserved = sourceBindings.Length == outputBindings.Length && differing == 0 && compared > 0;
            report.Add((preserved ? "PASS" : "FAIL") + ": source keyframes preserved; compared=" + compared + " differing=" + differing + " animated=" + animated);
            Directory.CreateDirectory("Documentation");
            File.WriteAllLines("Documentation/NaturalLocomotion.txt", report);
            if (!preserved) throw new InvalidOperationException("The source animation copy did not preserve its curve data; inspect Documentation/NaturalLocomotion.txt.");
        }

        static void RestoreAvatarReference(Animator animator)
        {
            var bones = animator.avatar.humanDescription.skeleton;
            if (bones == null || bones.Length == 0) return;
            var transforms = animator.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            foreach (var bone in bones)
            {
                Transform transform;
                if (!transforms.TryGetValue(bone.name, out transform) || transform == animator.transform) continue;
                transform.localPosition = bone.position;
                transform.localRotation = bone.rotation;
                transform.localScale = bone.scale;
            }
        }

        static void Aim(Animator animator, HumanBodyBones bone, HumanBodyBones child, Vector3 direction)
        {
            Transform a = animator.GetBoneTransform(bone), b = animator.GetBoneTransform(child);
            if (!a || !b || (b.position - a.position).sqrMagnitude < .0000001f) return;
            a.rotation = Quaternion.FromToRotation(b.position - a.position, direction.normalized) * a.rotation;
        }

        static void PoseLeg(Animator animator, bool left)
        {
            var upper = left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
            var lower = left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
            var foot = left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
            var toes = left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes;
            Aim(animator, upper, lower, Vector3.down + Vector3.forward * .025f);
            Aim(animator, lower, foot, Vector3.down - Vector3.forward * .025f);
            // The toes sit forward and below the ankle when the white trainee stands flat.
            Aim(animator, foot, toes, Vector3.forward - Vector3.up * .6f);
        }

        static void PoseArm(Animator animator, bool left)
        {
            float side = left ? -1 : 1;
            var shoulder = left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder;
            var upper = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            var lower = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            var hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            Aim(animator, shoulder, upper, Vector3.right * side + Vector3.down * .04f);
            Aim(animator, upper, lower, Vector3.down + Vector3.right * (side * .12f));
            Aim(animator, lower, hand, Vector3.down + Vector3.forward * .12f);
        }

        static float LowestFoot(Animator animator)
        {
            float y = float.PositiveInfinity;
            foreach (var bone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
            {
                var transform = animator.GetBoneTransform(bone);
                if (transform) y = Mathf.Min(y, transform.position.y);
            }
            return float.IsPositiveInfinity(y) ? 0 : y;
        }

        static string Normalize(string name)
        {
            return name.Replace("Hand.", "").Replace(" ", "").Replace(".", "");
        }

        static string MuscleProperty(string name)
        {
            // Unity's finger curve bindings use e.g. LeftHand.Thumb.1 Stretched.
            var finger = Regex.Match(name, "^(Left|Right) (Thumb|Index|Middle|Ring|Little) (.+)$");
            return finger.Success ? finger.Groups[1].Value + "Hand." + finger.Groups[2].Value + "." + finger.Groups[3].Value : name;
        }

        static void Constant(AnimationClip clip, string property, float value)
        {
            Constant(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property), value);
        }

        static void Constant(AnimationClip clip, EditorCurveBinding binding, float value)
        {
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, IdleLength, value));
        }

        static void WriteFixedFrame(AnimationClip idle, AnimationClip source)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve != null) Constant(idle, binding, curve.Evaluate(0));
            }
        }
    }
}
