using UnityEngine;

namespace RoboticsLab
{
    [RequireComponent(typeof(CharacterController))]
    public class LabThirdPerson : MonoBehaviour
    {
        public Camera viewCamera;
        public Transform visual;
        public Animator animator;
        public Transform head;
        public GameObject wornHelmet;
        public LabHelmetEquipment equipment;
        public bool IsEquippingHelmet => equipment && equipment.IsEquipping;
        public bool inputEnabled = true;
        public float walkSpeed = 1.6f;
        public Vector3 focusPoint;
        public float focusScale=1f;
        public Vector3 focusCameraOffset = new Vector3(3.1f, 2.2f, -2.5f);
        public bool focusMode;
        public bool learningFocus;
        public bool focusZoomEnabled=true;
        public float focusHorizontalScreenOffset;
        public bool pointerReleased;
        public bool LookCaptured => Cursor.lockState==CursorLockMode.Locked;
        public Vector3 learningCameraPosition, learningLookAt;
        CharacterController controller;
        float yaw, pitch = 18, vertical;
        Vector3 cameraVelocity;
        Vector3 planarVelocity, moveDamping;
        RuntimeAnimatorController animationController;
        bool hasSpeedParameter;
        float operationZoom=1;
        static readonly int SpeedParameter = Animator.StringToHash("Speed");
        public float distance = 3.5f;
        void Awake() { controller = GetComponent<CharacterController>(); yaw = transform.eulerAngles.y; }
        void Update()
        {
            if (!viewCamera) return;
            bool canMove = inputEnabled && !IsEquippingHelmet;
            if(canMove&&Input.GetKeyDown(KeyCode.Tab))pointerReleased=!pointerReleased;
            if(canMove&&Input.GetKeyDown(KeyCode.Escape))pointerReleased=true;
            UpdatePointerCapture();
            if (canMove && LookCaptured)
            {
                yaw += Input.GetAxis("Mouse X") * 3f;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2.1f, 5, 65);
            }
            if (inputEnabled) distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * .25f, 2.0f, 5f);
            if (focusMode&&focusZoomEnabled) operationZoom=Mathf.Clamp(operationZoom-Input.mouseScrollDelta.y*.06f,.65f,1.3f);
            Vector3 direction = Vector3.zero;
            if (canMove)
            {
                direction = Quaternion.Euler(0, yaw, 0) * new Vector3(Input.GetAxisRaw("Horizontal"),0,Input.GetAxisRaw("Vertical"));
                direction = Vector3.ClampMagnitude(direction,1);
            }
            if (controller.isGrounded && vertical < 0) vertical = -2;
            vertical += Physics.gravity.y * Time.deltaTime;
            float speed = walkSpeed * (Input.GetKey(KeyCode.LeftShift) ? 1.5f : 1);
            Vector3 desired=direction*speed;
            if(!canMove){planarVelocity=Vector3.zero;moveDamping=Vector3.zero;}
            else planarVelocity=Vector3.SmoothDamp(planarVelocity,desired,ref moveDamping,.12f);
            controller.Move((planarVelocity + Vector3.up * vertical) * Time.deltaTime);
            if (direction.sqrMagnitude > .01f && visual)
                visual.rotation = Quaternion.Slerp(visual.rotation, Quaternion.LookRotation(direction), 12 * Time.deltaTime);
            UpdateAnimation(planarVelocity.magnitude / 1.6f);
            if (transform.position.y < -4) Teleport(new Vector3(0,.1f,1.8f),0);
        }
        void UpdateAnimation(float locomotionSpeed)
        {
            if (!animator || !animator.runtimeAnimatorController) return;
            if (animationController != animator.runtimeAnimatorController)
            {
                animationController = animator.runtimeAnimatorController;
                hasSpeedParameter = false;
                foreach (var parameter in animator.parameters)
                    if (parameter.nameHash == SpeedParameter && parameter.type == AnimatorControllerParameterType.Float)
                        hasSpeedParameter = true;
            }
            if (hasSpeedParameter)
            {
                animator.speed = 1;
                animator.SetFloat(SpeedParameter, locomotionSpeed, .09f, Time.deltaTime);
            }
            else animator.speed = locomotionSpeed > .1f ? locomotionSpeed : 0;
        }
        void LateUpdate()
        {
            if (!viewCamera) return;
            Vector3 target = transform.position + Vector3.up * 1.35f;
            Vector3 wanted;
            if(learningFocus)
            {
                target=learningLookAt;
                wanted=learningCameraPosition;
            }
            else if (focusMode)
            {
                target = focusPoint;
                wanted = focusPoint + focusCameraOffset*operationZoom*focusScale;
                // Keep the work surface visible between the program and jog panels.
                Vector3 forward=(target-wanted).normalized;
                Vector3 right=Vector3.Cross(Vector3.up,forward).normalized;
                float halfWidth=Vector3.Distance(target,wanted)*Mathf.Tan(viewCamera.fieldOfView*.5f*Mathf.Deg2Rad)*viewCamera.aspect;
                Vector3 shift=-right*(focusHorizontalScreenOffset*2*halfWidth);
                target+=shift;wanted+=shift;
            }
            else wanted = target - Quaternion.Euler(pitch,yaw,0) * Vector3.forward * distance;
            if (!focusMode && !learningFocus)
            {
                var delta = wanted-target;
                var hits = Physics.SphereCastAll(target,.14f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
                float nearest = delta.magnitude;
                foreach(var hit in hits)
                    if (!hit.transform.IsChildOf(transform) && hit.distance > .15f) nearest = Mathf.Min(nearest,hit.distance-.08f);
                wanted = target + delta.normalized * Mathf.Max(.45f,nearest);
            }
            viewCamera.transform.position = Vector3.SmoothDamp(viewCamera.transform.position,wanted,ref cameraVelocity,.12f);
            viewCamera.transform.rotation = Quaternion.LookRotation(target-viewCamera.transform.position);
        }
        public bool WearHelmet(System.Action completed) { return equipment && equipment.Begin(completed); }
        public void UpdatePointerCapture()
        {
            bool capture=inputEnabled&&!IsEquippingHelmet&&!pointerReleased&&Application.isFocused;
            var wanted=capture?CursorLockMode.Locked:CursorLockMode.None;
            if(Cursor.lockState!=wanted)Cursor.lockState=wanted;
            Cursor.visible=!capture;
        }
        void OnDisable(){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        void OnApplicationFocus(bool focused){if(!focused){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}}
        public void Teleport(Vector3 pos,float heading)
        {
            if(!controller) controller=GetComponent<CharacterController>();
            controller.enabled=false; transform.position=pos; controller.enabled=true; yaw=heading; vertical=0;
            planarVelocity=Vector3.zero;moveDamping=Vector3.zero;
            if(visual) visual.rotation=Quaternion.Euler(0,heading,0);
        }
    }
}
