using UnityEngine;

namespace RoboticsLab
{
    /// <summary>Blends the authored locomotion floor baseline without altering the recorded pose.</summary>
    [DefaultExecutionOrder(100)]
    public class LabAvatarGrounding : MonoBehaviour
    {
        public Animator animator;
        public Transform visual;
        public float idleLocalY;
        public float walkLocalYOffset;
        public float sampledSoleRangeMeters;
        public bool configured;
        public float WalkWeight { get; private set; }
        public float AppliedLocalYOffset { get; private set; }
        static readonly int WalkState = Animator.StringToHash("Walk");

        void LateUpdate()
        {
            if (!configured || !visual || !animator || !animator.isInitialized || !animator.runtimeAnimatorController) return;
            bool currentWalk = animator.GetCurrentAnimatorStateInfo(0).shortNameHash == WalkState;
            WalkWeight = currentWalk ? 1 : 0;
            if (animator.IsInTransition(0))
            {
                bool nextWalk = animator.GetNextAnimatorStateInfo(0).shortNameHash == WalkState;
                float transition = Mathf.Clamp01(animator.GetAnimatorTransitionInfo(0).normalizedTime);
                WalkWeight = Mathf.Lerp(currentWalk ? 1 : 0, nextWalk ? 1 : 0, transition);
            }
            AppliedLocalYOffset = walkLocalYOffset * WalkWeight;
            Vector3 position = visual.localPosition;
            position.y = idleLocalY + AppliedLocalYOffset;
            visual.localPosition = position;
        }

        void OnDisable()
        {
            if (!configured || !visual) return;
            Vector3 position = visual.localPosition;
            position.y = idleLocalY;
            visual.localPosition = position;
            WalkWeight = 0; AppliedLocalYOffset = 0;
        }
    }
}
