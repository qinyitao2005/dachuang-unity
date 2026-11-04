using System.Collections.Generic;
using UnityEngine;

namespace RoboticsLab
{
    // Palm placement is measured at the middle knuckle. Placing the wrist on the brim
    // would sink the fingertips into the shell as soon as the elbows bend.
    sealed class LabHelmetGripPose
    {
        readonly Transform body, spine, chest, neck;
        readonly Arm left, right;
        readonly List<Transform> bones = new List<Transform>();
        readonly Quaternion[] animated;
        bool captured;

        internal LabHelmetGripPose(Animator animator, Transform body)
        {
            this.body = body;
            if (animator && animator.isHuman)
            {
                spine = animator.GetBoneTransform(HumanBodyBones.Spine);
                chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                Add(spine); Add(chest); Add(neck);
                left = new Arm(animator, true, bones);
                right = new Arm(animator, false, bones);
            }
            animated = new Quaternion[bones.Count];
        }

        void Add(Transform bone) { if (bone) bones.Add(bone); }
        internal void Capture()
        {
            for (int i = 0; i < bones.Count; i++) animated[i] = bones[i].localRotation;
            captured = true;
        }
        internal void Restore()
        {
            if (!captured) return;
            for (int i = 0; i < bones.Count; i++) bones[i].localRotation = animated[i];
            captured = false;
        }
        internal void Lean(float lean, float nod)
        {
            Vector3 rightAxis = body.right;
            if (spine) spine.rotation = Quaternion.AngleAxis(17 * lean, rightAxis) * spine.rotation;
            if (chest) chest.rotation = Quaternion.AngleAxis(11 * lean, rightAxis) * chest.rotation;
            if (neck) neck.rotation = Quaternion.AngleAxis(9 * lean + 8 * nod, rightAxis) * neck.rotation;
        }

        internal void Hold(Transform shell, float halfWidth, float phase)
        {
            Vector3 center = shell.TransformPoint(new Vector3(0, .055f, -.015f));
            left?.Pose(body, center, halfWidth, phase);
            right?.Pose(body, center, halfWidth, phase);
        }

        sealed class Arm
        {
            readonly Transform upper, forearm, hand, knuckle, index, little;
            readonly Transform[] fingers = new Transform[12];
            readonly int side;

            internal Arm(Animator animator, bool isLeft, List<Transform> bones)
            {
                side = isLeft ? -1 : 1;
                upper = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                forearm = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hand = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                knuckle = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
                index = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
                little = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
                if (upper) bones.Add(upper);
                if (forearm) bones.Add(forearm);
                if (hand) bones.Add(hand);
                var first = isLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal;
                for (int i = 0; i < fingers.Length; i++)
                {
                    fingers[i] = animator.GetBoneTransform((HumanBodyBones)((int)first + i));
                    if (fingers[i]) bones.Add(fingers[i]);
                }
            }

            internal void Pose(Transform body, Vector3 center, float width, float phase)
            {
                if (!upper || !forearm || !hand) return;
                float offset = side < 0 ? .016f : 0;
                float reach = LabHelmetEquipment.Smooth(Mathf.InverseLerp(.015f + offset, .253f + offset, phase));
                float clearPhase = .84f + offset;
                float release = LabHelmetEquipment.Smooth(Mathf.InverseLerp(.767f + offset, .99f, Mathf.Min(phase, clearPhase)));
                // After the palm clears the hat, return the actual joint rotations to the
                // animated pose. A lowering wrist can cross the elbow pole of a two-bone solver.
                float lowerArms = LabHelmetEquipment.Smooth(Mathf.InverseLerp(clearPhase, 1f, phase));
                if (reach <= 0 || lowerArms >= 1) return;
                Vector3 outwards = body.right * side;
                Vector3 grip = center + outwards * width;
                float lift = LabHelmetEquipment.Smooth(Mathf.InverseLerp(.32f, .60f, phase));
                // Curl around the rim on the shelf, then slide the palms upright by the temples.
                Vector3 fingersForward = (body.forward * Mathf.Lerp(1f, .22f, lift)
                    + Vector3.up * Mathf.Lerp(.48f, 1f, lift)).normalized;
                Quaternion idleHand = hand.rotation, idleHandLocal = hand.localRotation;
                Quaternion holdingHand = idleHand;
                Vector3 wrist = grip - fingersForward * .065f;
                if (knuckle && index && little)
                {
                    Vector3 across = index.position - little.position;
                    Quaternion knuckles = Quaternion.LookRotation(knuckle.position - hand.position, across);
                    holdingHand = Quaternion.LookRotation(fingersForward, Vector3.up) * Quaternion.Inverse(knuckles) * idleHand;
                    wrist = grip - (holdingHand * Quaternion.Inverse(idleHand)) * (knuckle.position - hand.position);
                }
                Vector3 idleWrist = hand.position;
                Vector3 target = Vector3.Lerp(idleWrist, wrist, reach);
                if (release > 0)
                    target = LabHelmetEquipment.Bezier(wrist, wrist + outwards*.13f + body.forward*.09f - Vector3.up*.07f,
                        idleWrist + outwards*.07f + body.forward*.13f + Vector3.up*.21f, idleWrist, release);

                Quaternion idleUpper = upper.localRotation, idleForearm = forearm.localRotation;
                Vector3 axis = (target - upper.position).normalized;
                float a = Vector3.Distance(upper.position, forearm.position);
                float b = Vector3.Distance(forearm.position, hand.position);
                float distance = Mathf.Clamp(Vector3.Distance(upper.position,target), Mathf.Abs(a-b)+.0001f, a+b-.0001f);
                float along = (a*a - b*b + distance*distance)/(2*distance);
                // At the crown the elbows hang forward below the wrists; a rearward pole
                // makes the wearer flare both elbows level with their ears when releasing.
                Vector3 elbowDirection = outwards * Mathf.Lerp(.68f,.45f,lift) - Vector3.up*1.2f
                    + body.forward * Mathf.Lerp(-.24f,.82f,lift);
                Vector3 pole = Vector3.ProjectOnPlane(elbowDirection, axis).normalized;
                Vector3 elbow = upper.position + axis*along + pole*Mathf.Sqrt(Mathf.Max(0, a*a-along*along));
                upper.rotation = Quaternion.FromToRotation(forearm.position-upper.position, elbow-upper.position) * upper.rotation;
                forearm.rotation = Quaternion.FromToRotation(hand.position-forearm.position, target-forearm.position) * forearm.rotation;
                float weight = LabHelmetEquipment.Smooth(reach*2) * LabHelmetEquipment.Smooth((1-release)*2);
                upper.localRotation = Quaternion.Slerp(idleUpper, upper.localRotation, weight);
                forearm.localRotation = Quaternion.Slerp(idleForearm, forearm.localRotation, weight);
                hand.rotation = Quaternion.Slerp(idleHand, holdingHand, reach * (1-release));
                if (lowerArms > 0)
                {
                    upper.localRotation = Quaternion.Slerp(upper.localRotation, idleUpper, lowerArms);
                    forearm.localRotation = Quaternion.Slerp(forearm.localRotation, idleForearm, lowerArms);
                    hand.localRotation = Quaternion.Slerp(hand.localRotation, idleHandLocal, lowerArms);
                }

                float curl = LabHelmetEquipment.Smooth(Mathf.InverseLerp(.20f + offset, .29f + offset, phase))
                    * (1 - LabHelmetEquipment.Smooth(Mathf.InverseLerp(.755f + offset, .845f + offset, phase)));
                Vector3 curlAxis = Vector3.Cross(fingersForward,-outwards).normalized;
                for (int i = 0; i < fingers.Length; i++)
                    if (fingers[i]) fingers[i].rotation = Quaternion.AngleAxis(curl * (i%3 == 1 ? 31 : 18), curlAxis) * fingers[i].rotation;
            }
        }
    }
}
