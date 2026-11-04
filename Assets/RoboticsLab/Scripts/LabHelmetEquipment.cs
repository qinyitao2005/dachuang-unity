using System;
using UnityEngine;

namespace RoboticsLab
{
    // Keep foot placement with the locomotion controller and pose the upper body after the Animator.
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

        float elapsed, approachTime;
        Action completed;
        LabHelmetGripPose pose;
        CharacterController controller;
        Vector3 startPosition, stance, facing, shelfPosition;
        Quaternion startFacing, shelfRotation;
        bool handedOff;

        public bool Begin(Action onCompleted)
        {
            if (IsEquipping || IsEquipped || !player || !player.visual || !player.wornHelmet || !player.head
                || !pickupVisual || !pickupVisual.activeInHierarchy) return false;
            controller = player.GetComponent<CharacterController>();
            pose = new LabHelmetGripPose(player.animator, player.visual);
            shelfPosition = pickupVisual.transform.position;
            shelfRotation = pickupVisual.transform.rotation;
            // The brim points along -Z on this piece of safety equipment.
            facing = Vector3.ProjectOnPlane(-(shelfRotation * Vector3.forward), Vector3.up).normalized;
            startPosition = transform.position;
            stance = shelfPosition - facing * .405f;
            // The visual is grounded with a small lateral offset from its capsule.
            stance -= Vector3.Project(player.visual.position - startPosition, Vector3.Cross(Vector3.up, facing));
            stance.y = startPosition.y;
            startFacing = player.visual.rotation;
            float turn = Quaternion.Angle(startFacing, Quaternion.LookRotation(facing));
            approachTime = Mathf.Clamp(Mathf.Max(Vector3.Distance(startPosition, stance)/1.45f, turn/250f), .18f, 1.08f);
            elapsed = 0;
            handedOff = false;
            completed = onCompleted;
            IsEquipping = true;
            player.wornHelmet.SetActive(false);
            return true;
        }

        void Update()
        {
            if (!IsEquipping) return;
            pose.Restore();
            float phase = Mathf.Clamp01((elapsed + Time.deltaTime) / approachTime);
            Vector3 destination = Vector3.Lerp(startPosition, stance, Smooth(phase));
            Vector3 step = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up);
            Vector3 before = transform.position;
            if (phase < 1 || elapsed < approachTime)
            {
                if (controller && controller.enabled) controller.Move(step);
                player.visual.rotation = Quaternion.Slerp(startFacing, Quaternion.LookRotation(facing), Smooth(phase));
            }
            float speed = Time.deltaTime > 0 ? Vector3.Distance(before, transform.position) / Time.deltaTime : 0;
            if (player.animator) player.animator.SetFloat("Speed", speed / 1.6f, .08f, Time.deltaTime);
        }

        void LateUpdate()
        {
            if (!IsEquipping) return;
            elapsed += Time.deltaTime;
            // The hands can begin to reach during the last part of the step.
            float gestureStart = approachTime * .65f;
            float phase = Mathf.InverseLerp(gestureStart, duration, elapsed);
            pose.Capture();
            float lean = Smooth(phase / .25f) * (1 - Smooth((phase - .36f) / .28f));
            float nod = Smooth((phase - .47f) / .16f) * (1 - Smooth((phase - .77f) / .16f));
            pose.Lean(lean, nod);

            Transform helmet = player.wornHelmet.transform;
            Vector3 fitted = player.head.TransformPoint(fittedLocalPosition);
            Quaternion seatedRotation = player.head.rotation * fittedLocalRotation;
            Vector3 position = shelfPosition;
            Quaternion rotation = shelfRotation;
            const float grasp = .27f, overhead = .64f, seated = .77f;
            if (phase > grasp && phase < overhead)
            {
                float t = Smooth(Mathf.InverseLerp(grasp, overhead, phase));
                Vector3 apex = fitted + Vector3.up * .12f + facing * .055f;
                position = Bezier(shelfPosition, shelfPosition + Vector3.up * .18f - facing * .095f,
                    fitted + facing * .40f + Vector3.up * .17f, apex, t);
                rotation = Quaternion.AngleAxis(-8 * Mathf.Sin(t * Mathf.PI), player.visual.right)
                    * Quaternion.Slerp(shelfRotation, seatedRotation, t);
            }
            else if (phase >= overhead)
            {
                float t = Smooth(Mathf.InverseLerp(overhead, seated, phase));
                position = fitted + (Vector3.up * .12f + facing * .055f) * (1 - t);
                rotation = seatedRotation;
            }
            // Solve the hands at the actual shelf pose even before the shell changes owners.
            helmet.SetPositionAndRotation(position, rotation);
            pose.Hold(helmet, gripHalfWidth, phase);
            if (!handedOff && phase >= grasp)
            {
                // A blocked approach must not pull a distant prop through a rack or a wall.
                if (Vector3.ProjectOnPlane(transform.position - stance, Vector3.up).magnitude > .17f)
                {
                    Cancel();
                    return;
                }
                pickupVisual.SetActive(false);
                helmet.gameObject.SetActive(true);
                handedOff = true;
            }
            if (elapsed < duration) return;
            pose.Restore();
            FitToHead();
            IsEquipping = false;
            IsEquipped = true;
            var callback = completed;
            completed = null;
            callback?.Invoke();
        }

        void Cancel()
        {
            pose?.Restore();
            if (handedOff && pickupVisual) pickupVisual.SetActive(true);
            player.wornHelmet.SetActive(false);
            FitToHead();
            IsEquipping = false;
            handedOff = false;
            completed = null;
        }

        void OnDisable()
        {
            if (IsEquipping && player && player.wornHelmet) Cancel();
        }

        public void FitToHead()
        {
            Transform helmet = player.wornHelmet.transform;
            helmet.SetParent(player.head, false);
            helmet.localPosition = fittedLocalPosition;
            helmet.localRotation = fittedLocalRotation;
            helmet.localScale = fittedLocalScale;
        }

        internal static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3 - 2 * value);
        }

        internal static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1 - t;
            return u*u*u*a + 3*u*u*t*b + 3*u*t*t*c + t*t*t*d;
        }
    }
}
