using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoboticsLab
{
    // Arm posing runs after the Animator. The imported rig and bind poses stay untouched.
    [DefaultExecutionOrder(200)]
    public class LabHelmetEquipment : MonoBehaviour
    {
        public LabThirdPerson player;
        public Vector3 fittedLocalPosition;
        public Quaternion fittedLocalRotation = Quaternion.identity;
        public Vector3 fittedLocalScale = Vector3.one;
        public float duration = 3.1f;
        public float gripHalfWidth = .12f;
        public GameObject pickupVisual;
        public bool IsEquipping { get; private set; }
        public bool IsEquipped { get; private set; }
        public float EquipProgress => IsEquipping ? Mathf.Clamp01(elapsed / duration) : (IsEquipped ? 1 : 0);
        float elapsed;
        Action completed;
        Transform[] posedBones = new Transform[0];
        Quaternion[] animatedLocalPose = new Quaternion[0];
        bool hasPoseOverride;

        public bool Begin(Action onCompleted)
        {
            if (IsEquipping || IsEquipped || !player || !player.wornHelmet || !player.head) return false;
            completed = onCompleted;
            elapsed = 0;
            IsEquipping = true;
            CachePosedBones();
            if (player.animator)
            {
                player.animator.SetFloat("Speed", 0);
                player.animator.Play("Idle", 0, 0);
            }
            player.wornHelmet.SetActive(false);
            return true;
        }

        void Update()
        {
            // Animation is evaluated after Update and before LateUpdate. Remove only
            // our previous temporary pose first, so constant or unkeyed channels do
            // not feed last frame's finger/arm overrides back into the next solve.
            RestoreAnimatedPose();
        }

        void LateUpdate()
        {
            if (!IsEquipping) return;
            for (int i = 0; i < posedBones.Length; i++)
                if (posedBones[i]) animatedLocalPose[i] = posedBones[i].localRotation;
            elapsed += Time.deltaTime;
            Pose(elapsed);
            if (elapsed < duration) return;
            RestoreAnimatedPose();
            FitToHead();
            IsEquipping = false;
            IsEquipped = true;
            var callback = completed;
            completed = null;
            callback?.Invoke();
        }

        void CachePosedBones()
        {
            RestoreAnimatedPose();
            var bones = new List<Transform>();
            var animator = player.animator;
            if (animator && animator.isHuman)
            {
                foreach (var id in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                    HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand })
                {
                    var bone = animator.GetBoneTransform(id);
                    if (bone) bones.Add(bone);
                }
                // The complete finger range includes thumbs, which we preserve even
                // though the gripping pose only curls index through little fingers.
                for (int i = (int)HumanBodyBones.LeftThumbProximal; i <= (int)HumanBodyBones.RightLittleDistal; i++)
                {
                    var bone = animator.GetBoneTransform((HumanBodyBones)i);
                    if (bone) bones.Add(bone);
                }
            }
            posedBones = bones.ToArray();
            animatedLocalPose = new Quaternion[posedBones.Length];
        }

        void RestoreAnimatedPose()
        {
            if (!hasPoseOverride) return;
            for (int i = 0; i < posedBones.Length; i++)
                if (posedBones[i]) posedBones[i].localRotation = animatedLocalPose[i];
            hasPoseOverride = false;
        }

        void OnDisable()
        {
            RestoreAnimatedPose();
            if (IsEquipping) { IsEquipping = false; completed = null; }
        }

        public void FitToHead()
        {
            var helmet = player.wornHelmet.transform;
            helmet.SetParent(player.head, false);
            helmet.localPosition = fittedLocalPosition;
            helmet.localRotation = fittedLocalRotation;
            helmet.localScale = fittedLocalScale;
        }

        void Pose(float time)
        {
            var helmet = player.wornHelmet.transform;
            var basis = player.visual;
            Vector3 forward = basis.forward, right = basis.right;
            Vector3 fitted = player.head.TransformPoint(fittedLocalPosition);
            Quaternion fittedRotation = player.head.rotation * fittedLocalRotation;
            Vector3 carried = fitted - Vector3.up * .36f + forward * .30f;
            Vector3 raised = fitted + Vector3.up * .16f + forward * .045f;
            Vector3 position;
            if (time < .45f) position = carried;
            else if (time < 1.5f) position = Vector3.Lerp(carried, raised, Ease((time - .45f) / 1.05f));
            else position = Vector3.Lerp(raised, fitted, Ease((time - 1.5f) / .55f));
            helmet.SetPositionAndRotation(position, fittedRotation);
            helmet.localScale = fittedLocalScale;
            if(time >= .42f && !helmet.gameObject.activeSelf)
            {
                if(pickupVisual)pickupVisual.SetActive(false);
                helmet.gameObject.SetActive(true);
            }

            float weight = Ease(time / .42f) * (1 - Ease((time - 2.3f) / (duration - 2.3f)));
            if (weight <= 0) return;
            // Solve the complete arm and finger targets first. Blend local rotations
            // as one hierarchy, rather than blending each child's world rotation
            // against a parent that has already been partially blended.
            PoseArm(true, position - right * gripHalfWidth + Vector3.up * .025f);
            PoseArm(false, position + right * gripHalfWidth + Vector3.up * .025f);
            for (int i = 0; i < posedBones.Length; i++)
                if (posedBones[i]) posedBones[i].localRotation = Quaternion.Slerp(animatedLocalPose[i], posedBones[i].localRotation, weight);
            hasPoseOverride = true;
        }

        void PoseArm(bool left, Vector3 target)
        {
            var animator = player.animator;
            if (!animator || !animator.isHuman) return;
            Transform upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            Transform lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Transform hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (!upper || !lower || !hand) return;
            float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, hand.position);
            Vector3 delta = target - upper.position;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a-b)+.001f, a+b-.001f);
            Vector3 axis = delta.normalized;
            Vector3 pole = player.visual.right * (left ? -1 : 1) + player.visual.forward * .25f;
            pole = Vector3.ProjectOnPlane(pole, axis).normalized;
            float along = (a*a-b*b+distance*distance)/(2*distance);
            Vector3 elbow = upper.position + axis*along + pole*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            upper.rotation = Quaternion.FromToRotation(lower.position-upper.position, elbow-upper.position)*upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position-lower.position, target-lower.position)*lower.rotation;
            Transform fingers = animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            if (fingers) hand.rotation = Quaternion.FromToRotation(fingers.position-hand.position, Vector3.up)*hand.rotation;
            Transform index = animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            Transform little = animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            Vector3 inward = player.visual.right * (left ? 1 : -1);
            if(fingers && index && little)
            {
                Vector3 palm = Vector3.Cross(fingers.position-hand.position,index.position-little.position).normalized*(left?-1:1);
                hand.rotation=Quaternion.AngleAxis(Vector3.SignedAngle(palm,inward,Vector3.up),Vector3.up)*hand.rotation;
            }
            CurlFingers(animator,left,Vector3.Cross(Vector3.up,inward).normalized);
        }

        static void CurlFingers(Animator animator,bool left,Vector3 axis)
        {
            var first=left?HumanBodyBones.LeftIndexProximal:HumanBodyBones.RightIndexProximal;
            // Index, middle, ring and little each have three consecutive humanoid bones.
            for(int finger=0;finger<4;finger++)for(int joint=0;joint<3;joint++)
            {
                var bone=animator.GetBoneTransform((HumanBodyBones)((int)first+finger*3+joint));
                if(bone)bone.rotation=Quaternion.AngleAxis(joint==0?18:joint==1?32:20,axis)*bone.rotation;
            }
        }

        static float Ease(float value) { value=Mathf.Clamp01(value); return value*value*(3-2*value); }
    }
}
