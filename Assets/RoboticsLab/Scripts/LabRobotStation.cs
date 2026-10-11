using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>Shared adapter for the original robot models and a future pendant UI.</summary>
    public class LabRobotStation : MonoBehaviour
    {
        public int stationIndex;
        public string displayName = "六轴机械臂";
        bool isOperating, emergencyStopped, penDown;
        int trainingMode, contacts;
        float progress, elapsed;
        Group0927Station group0927;
        bool UsesGroup0927 => stationIndex > 0 && (group0927 || (group0927 = GetComponent<Group0927Station>()));
        public bool IsOperating { get { return UsesGroup0927 ? group0927.IsOperating : isOperating; } private set { isOperating = value; } }
        public bool EmergencyStopped { get { return UsesGroup0927 ? group0927.EmergencyStopped : emergencyStopped; } private set { emergencyStopped = value; } }
        public bool PenDown { get { return UsesGroup0927 ? group0927.PenDown : penDown; } private set { penDown = value; } }
        public int TrainingMode { get { return UsesGroup0927 ? group0927.TrainingMode : trainingMode; } private set { trainingMode = value; } }
        public float Progress { get { return UsesGroup0927 ? group0927.Progress : progress; } private set { progress = value; } }
        public int Contacts { get { return UsesGroup0927 ? group0927.Contacts : contacts; } private set { contacts = value; } }
        public float Elapsed { get { return UsesGroup0927 ? group0927.Elapsed : elapsed; } private set { elapsed = value; } }
        public Transform modelRoot, toolTarget, endEffector;
        public Transform ringGripTcp, ringGripTool, originalEndTool;
        public float ringGripRoll;
        public bool ringUsePoseSeed;
        public JointConfig[] joints;
        public Transform drawingBoard, wireRoot, ringRoot, penRoot;
        public LineRenderer wireRenderer, ringRenderer;
        public Renderer ringSolidRenderer;
        MaterialPropertyBlock ringColorProperties;
        public Vector3 exerciseCenter = new Vector3(.65f, 1.25f, 0);
        public Vector3 workspaceHalfSize = new Vector3(.38f,.36f,.53f);
        public float moveSpeed = .22f;
        public float drawingHeight = 1.055f;
        public float exerciseScale = 1f, tableHeight = .93f;
        public float ringRadius = .105f, ringTubeRadius = .009f, wireRadius = .007f;
        public Vector3[] wirePoints;
        bool toolInContact, ringMovementBlocked, robotWireBlocked;
        string robotCollisionReason = "";
        public bool ToolInContact { get { return UsesGroup0927 ? group0927.ToolInContact : toolInContact; } private set { toolInContact = value; } }
        public bool RingMovementBlocked { get { return UsesGroup0927 ? group0927.RingMovementBlocked : ringMovementBlocked; } private set { ringMovementBlocked = value; } }
        public bool RobotWireBlocked { get { return UsesGroup0927 ? group0927.RobotWireBlocked : robotWireBlocked; } private set { robotWireBlocked = value; } }
        public string RobotCollisionReason { get { return UsesGroup0927 ? group0927.RobotCollisionReason : robotCollisionReason; } private set { robotCollisionReason = value; } }
        public string DrawingBlockReason => UsesGroup0927 ? group0927.DrawingBlockReason : "";
        public Vector3 RingGripPosition => UsesGroup0927 ? group0927.RingGripPosition : ringGripTcp ? ringGripTcp.position : endEffector ? endEffector.position : transform.position;
        public Vector3 ActualToolPosition
        {
            get { if(UsesGroup0927)return group0927.ActualToolPosition;
                if(TrainingMode!=1||!ringGripTcp)return endEffector?endEffector.position:transform.position;
                Vector3 center;Quaternion rotation;GetActualRingPose(out center,out rotation);return center; }
        }
        public float PositionError => UsesGroup0927 ? group0927.PositionError : toolTarget && endEffector ? Vector3.Distance(toolTarget.position,ActualToolPosition) : 0;
        LabRobotWireCollision robotWireCollision;
        readonly List<LineRenderer> strokes = new List<LineRenderer>();
        LineRenderer currentStroke;
        Material lineMaterial;
        Quaternion[] homeRotations;
        float[] jointAngles;
        Vector3 lastInk;
        float inkLength, contactCooldown;
        bool previousContact, initialized, ringStarted;
        int furthestSegment;
        armProperty legacyState;
        Transform originalTrajectoryModule;
        bool originalTrajectoryVisible;
        Vector3 requestedMotion;
        int currentRingSegment;
        Vector3 acceptedRingTarget;
        bool ringRimBlocked;
        Quaternion[] stepRotations;
        float[] stepAngles;
        string ringStopStage="none";
        Vector3 ringRejectedPose,ringLastNormal;
        const int RingSegments = 64;

        void Start() { if(UsesGroup0927)return; Initialize(); ResetExercise(); }
        void Initialize()
        {
            if(UsesGroup0927)return;
            if(initialized) return;
            initialized=true;
            if(joints==null)joints=new JointConfig[0];
            homeRotations=new Quaternion[joints.Length]; jointAngles=new float[joints.Length];
            stepRotations=new Quaternion[joints.Length];stepAngles=new float[joints.Length];
            for(int i=0;i<joints.Length;i++)if(joints[i]!=null&&joints[i].joint) homeRotations[i]=joints[i].joint.localRotation;
            if(modelRoot)legacyState=modelRoot.GetComponentInChildren<armProperty>(true);
            if(modelRoot)foreach(var child in modelRoot.GetComponentsInChildren<Transform>(true))
                if(child.name=="轨迹模块"){originalTrajectoryModule=child;originalTrajectoryVisible=child.gameObject.activeSelf;break;}
            lineMaterial=new Material(Shader.Find("Sprites/Default"));
            robotWireCollision=GetComponent<LabRobotWireCollision>()??gameObject.AddComponent<LabRobotWireCollision>();
            robotWireCollision.station=this;
            if(!toolTarget) {toolTarget=new GameObject("ToolTarget").transform;toolTarget.SetParent(transform,false);}
            SetMode(TrainingMode);
        }
        public void BeginOperation() {if(UsesGroup0927){group0927.BeginOperation();return;}Initialize();IsOperating=true;requestedMotion=Vector3.zero;ApplyExerciseVisibility();}
        public void EndOperation() {if(UsesGroup0927){group0927.EndOperation();return;}IsOperating=false;PenDown=false;requestedMotion=Vector3.zero;currentStroke=null;ApplyExerciseVisibility();if(legacyState)legacyState.armIsMoving=false;}
        public void SetEmergencyStop(bool stopped) {if(UsesGroup0927){group0927.SetEmergencyStop(stopped);return;}EmergencyStopped=stopped;requestedMotion=Vector3.zero;if(stopped){PenDown=false;currentStroke=null;}}
        public void SetMode(int mode)
        {
            if(UsesGroup0927){group0927.SetMode(mode);return;}
            TrainingMode=Mathf.Clamp(mode,0,1);
            ApplyExerciseVisibility();
            if(initialized)ResetExercise();
        }
        void ApplyExerciseVisibility()
        {
            if(UsesGroup0927)return;
            // Exchange the original trajectory jig for the selected exercise attachment.
            // Keep the preserved jig in the assembly and restore it when leaving the station.
            if(originalTrajectoryModule)originalTrajectoryModule.gameObject.SetActive(!IsOperating&&originalTrajectoryVisible);
            if(drawingBoard)drawingBoard.gameObject.SetActive(IsOperating&&TrainingMode==0);
            if(wireRoot)wireRoot.gameObject.SetActive(IsOperating&&TrainingMode==1);
            if(ringRoot)ringRoot.gameObject.SetActive(IsOperating&&TrainingMode==1);
            if(penRoot)penRoot.gameObject.SetActive(IsOperating&&TrainingMode==0);
            if(ringGripTool)ringGripTool.gameObject.SetActive(IsOperating&&TrainingMode==1);
            if(originalEndTool)originalEndTool.gameObject.SetActive(!(IsOperating&&TrainingMode==1));
            if(robotWireCollision)robotWireCollision.RebuildCache();
            foreach(var stroke in strokes)if(stroke)stroke.gameObject.SetActive(IsOperating&&TrainingMode==0);
        }
        public void SetPen(bool down)
        {
            if(UsesGroup0927){group0927.SetPen(down);return;}
            PenDown=down&&IsOperating&&!EmergencyStopped&&TrainingMode==0;
            if(PenDown&&toolTarget){var p=toolTarget.localPosition;p.y=drawingHeight;toolTarget.localPosition=p;}
            else currentStroke=null;
        }
        public void ClearDrawing()
        {
            if(UsesGroup0927){group0927.ClearDrawing();return;}
            foreach(var stroke in strokes)if(stroke)Destroy(stroke.gameObject);
            strokes.Clear(); currentStroke=null; inkLength=0;if(TrainingMode==0)Progress=0;
        }
        public void ResetExercise()
        {
            if(UsesGroup0927){group0927.ResetExercise();return;}
            if(!initialized){Initialize();return;}
            PenDown=false; EmergencyStopped=false;Elapsed=0;Progress=0;Contacts=0;ToolInContact=false;
            previousContact=false;contactCooldown=0;ringStarted=false;furthestSegment=0;requestedMotion=Vector3.zero;
            currentRingSegment=0;RingMovementBlocked=false;ringRimBlocked=false;
            RobotWireBlocked=false;RobotCollisionReason="";
            ringStopStage="reset";ringRejectedPose=ringLastNormal=Vector3.zero;
            ClearDrawing();
            for(int i=0;i<joints.Length;i++)if(joints[i]!=null&&joints[i].joint){joints[i].joint.localRotation=homeRotations[i];jointAngles[i]=0;}
            var start=exerciseCenter;
            if(TrainingMode==0)start.y=drawingHeight+.12f*exerciseScale;
            else if(wirePoints!=null&&wirePoints.Length>1)start=wirePoints[0];
            toolTarget.localPosition=start;
            float settled=TrainingMode==1?Mathf.Max(.0001f,.0004f*exerciseScale):.003f;
            for(int i=0;i<60;i++)
            {
                ringInitializePose=TrainingMode==1&&ringUsePoseSeed&&i<10;
                SolveIK(.18f);
                if(!ringInitializePose&&endEffector&&PositionError<settled)break;
            }
            ringInitializePose=false;
            acceptedRingTarget=toolTarget.localPosition;
            UpdateToolVisuals();
        }
        /// <summary>Call once per frame with world XYZ direction. Time integration happens here once.</summary>
        public void MoveTool(Vector3 input)
        {
            if(UsesGroup0927){group0927.MoveTool(input);return;}
            if(!IsOperating||EmergencyStopped||!toolTarget)return;
            RingMovementBlocked=false;
            RobotWireBlocked=false;RobotCollisionReason="";
            requestedMotion=Vector3.ClampMagnitude(input,1);
            Vector3 local=toolTarget.localPosition+transform.InverseTransformDirection(requestedMotion)*moveSpeed*Time.deltaTime;
            local.x=Mathf.Clamp(local.x,exerciseCenter.x-workspaceHalfSize.x,exerciseCenter.x+workspaceHalfSize.x);
            float minimumY=exerciseCenter.y-workspaceHalfSize.y;
            if(TrainingMode==0)minimumY=Mathf.Max(drawingHeight,minimumY);
            local.y=Mathf.Clamp(local.y,minimumY,exerciseCenter.y+workspaceHalfSize.y);
            local.z=Mathf.Clamp(local.z,exerciseCenter.z-workspaceHalfSize.z,exerciseCenter.z+workspaceHalfSize.z);
            if(TrainingMode==0&&PenDown)local.y=drawingHeight;
            if(TrainingMode==1&&wirePoints!=null&&wirePoints.Length>1)MoveRingTo(local,Time.deltaTime);
            else toolTarget.localPosition=local;
        }
        void LateUpdate()
        {
            if(UsesGroup0927)return;
            if(!initialized)return;
            if(IsOperating&&!EmergencyStopped)
            {
                Elapsed+=Time.deltaTime;
                if(TrainingMode==1&&wirePoints!=null&&wirePoints.Length>1)MoveRingTo(toolTarget.localPosition,Time.deltaTime);
                else SolveIK(Time.deltaTime);
            }
            if(legacyState)legacyState.armIsMoving=IsOperating&&!EmergencyStopped&&requestedMotion.sqrMagnitude>.001f;
            UpdateToolVisuals();
            if(IsOperating&&!EmergencyStopped)
            {
                if(TrainingMode==0)UpdateDrawing();else UpdateRing();
            }
            requestedMotion=Vector3.zero;
            ringRimBlocked=false;
        }
        void SolveIK(float dt)
        {
            if(!endEffector||!toolTarget||joints.Length==0)return;
            if(TrainingMode==1&&ringGripTcp){SolveRingPoseIK(dt);return;}
            Transform effector=endEffector;
            Vector3 desired=toolTarget.position;
            // CCD walks from wrist to shoulder. Each joint uses its original local hinge axis.
            for(int pass=0;pass<5;pass++)
            {
                float tolerance=TrainingMode==1?Mathf.Max(.00006f,.00025f*exerciseScale):.002f;
                if((effector.position-desired).sqrMagnitude<tolerance*tolerance)break;
                for(int i=joints.Length-1;i>=0;i--)
                {
                    var c=joints[i]; if(c==null||!c.joint||!effector.IsChildOf(c.joint))continue;
                    Vector3 localAxis=c.axis==JointAxis.X?Vector3.right:c.axis==JointAxis.Z?Vector3.forward:Vector3.up;
                    Vector3 axis=c.axis==JointAxis.WorldY?Vector3.up:c.joint.TransformDirection(localAxis);
                    Vector3 a=Vector3.ProjectOnPlane(effector.position-c.joint.position,axis);
                    Vector3 b=Vector3.ProjectOnPlane(desired-c.joint.position,axis);
                    if(a.sqrMagnitude<1e-8f||b.sqrMagnitude<1e-8f)continue;
                    float step=Mathf.Clamp(Vector3.SignedAngle(a,b,axis),-Mathf.Max(.1f,dt*80),Mathf.Max(.1f,dt*80));
                    float next=Mathf.Clamp(jointAngles[i]+step,c.minAngle,c.maxAngle);
                    step=next-jointAngles[i];jointAngles[i]=next;
                    c.joint.rotation=Quaternion.AngleAxis(step,axis)*c.joint.rotation;
                }
            }
        }
        // Reused by all ring substeps. These buffers are separate from the collision
        // transaction's stepRotations/stepAngles, which must survive the whole solve.
        sealed class RingPoseIkBuffers
        {
            public readonly int count;
            public readonly double[,] jacobian,system;
            public readonly double[] error,delta;
            public readonly float[] angles;
            public readonly Quaternion[] rotations;
            public readonly bool[] usable;
            public RingPoseIkBuffers(int count)
            {
                this.count=count;jacobian=new double[6,count];system=new double[count,count+1];
                error=new double[6];delta=new double[count];angles=new float[count];
                rotations=new Quaternion[count];usable=new bool[count];
            }
        }
        RingPoseIkBuffers ringPoseIk;
        internal bool ringInitializePose;

        void SolveRingPoseIK(float dt)
        {
            if(ringPoseIk==null||ringPoseIk.count!=joints.Length)ringPoseIk=new RingPoseIkBuffers(joints.Length);
            var work=ringPoseIk;
            Quaternion frame=RingFrame(toolTarget.localPosition);
            bool initializePose=ringInitializePose;
            Vector3 desiredPosition=toolTarget.position+(initializePose?frame*Vector3.up*ringRadius:Vector3.zero);
            Vector3 desiredNormal=frame*Vector3.forward;
            Quaternion desiredRotation=Quaternion.LookRotation(-(frame*Vector3.right),frame*Vector3.up);
            // TCP local X is the closing axis; local Y points back toward the wrist.
            // Solve the rigid ring centre and normal (five DOF), leaving roll around
            // the wire tangent free. The roll in RingFrame guides the ambiguous
            // antiparallel starting normal; it is not a world-up posture constraint.
            const float orientationLength=.10f,positionTolerance=.00008f;
            const float angleTolerance=.5f*Mathf.Deg2Rad;
            float maximumStep=Mathf.Max(.1f,dt*80)*Mathf.Deg2Rad;
            double damping=.0015;
            for(int pass=0;pass<32;pass++)
            {
                Vector3 actualCenter=ringGripTcp.position-(initializePose?Vector3.zero:ringGripTcp.up*ringRadius);
                Vector3 actualNormal=ringGripTcp.right;
                Vector3 positionError=desiredPosition-actualCenter;
                Vector3 rotationError=initializePose?RingPoseRotationError(desiredRotation,ringGripTcp.rotation)
                    :RingPoseNormalError(desiredNormal,actualNormal,frame*Vector3.up);
                if(positionError.sqrMagnitude<positionTolerance*positionTolerance&&rotationError.sqrMagnitude<angleTolerance*angleTolerance)return;
                work.error[0]=positionError.x;work.error[1]=positionError.y;work.error[2]=positionError.z;
                work.error[3]=rotationError.x*orientationLength;work.error[4]=rotationError.y*orientationLength;work.error[5]=rotationError.z*orientationLength;
                float previousCost=positionError.sqrMagnitude+rotationError.sqrMagnitude*orientationLength*orientationLength;
                for(int i=0;i<joints.Length;i++)
                {
                    var c=joints[i];
                    bool usable=c!=null&&c.joint&&ringGripTcp.IsChildOf(c.joint);
                    work.usable[i]=usable;
                    for(int row=0;row<6;row++)work.jacobian[row,i]=0;
                    if(!usable)continue;
                    work.angles[i]=jointAngles[i];work.rotations[i]=c.joint.localRotation;
                    Vector3 axis=RingPoseJointAxis(c);
                    // The point Jacobian includes the rigid rim-to-centre offset.
                    // Rotation about the plane normal changes roll but not that normal.
                    Vector3 linear=Vector3.Cross(axis,actualCenter-c.joint.position);
                    Vector3 angular=initializePose?axis:axis-actualNormal*Vector3.Dot(axis,actualNormal);
                    work.jacobian[0,i]=linear.x;work.jacobian[1,i]=linear.y;work.jacobian[2,i]=linear.z;
                    work.jacobian[3,i]=angular.x*orientationLength;work.jacobian[4,i]=angular.y*orientationLength;work.jacobian[5,i]=angular.z*orientationLength;
                    // Do not keep asking a saturated hinge to move farther outside its limits.
                    double gradient=0;for(int row=0;row<6;row++)gradient+=work.jacobian[row,i]*work.error[row];
                    if((jointAngles[i]<=c.minAngle+.0001f&&gradient<0)||(jointAngles[i]>=c.maxAngle-.0001f&&gradient>0))
                        for(int row=0;row<6;row++)work.jacobian[row,i]=0;
                }
                for(int row=0;row<joints.Length;row++)
                {
                    for(int col=0;col<joints.Length;col++)
                    {
                        double value=0;for(int k=0;k<6;k++)value+=work.jacobian[k,row]*work.jacobian[k,col];
                        work.system[row,col]=value+(row==col?damping*damping:0);
                    }
                    double right=0;for(int k=0;k<6;k++)right+=work.jacobian[k,row]*work.error[k];
                    work.system[row,joints.Length]=right;
                }
                if(!SolveRingPoseLinearSystem(work))return;
                double largest=0;for(int i=0;i<joints.Length;i++)largest=Math.Max(largest,Math.Abs(work.delta[i]));
                if(largest<1e-10)return;
                double scale=largest>maximumStep?maximumStep/largest:1;
                bool accepted=false;
                for(int trial=0;trial<7;trial++)
                {
                    RestoreRingPoseTrial(work);
                    double fraction=scale/Math.Pow(2,trial);
                    for(int i=0;i<joints.Length;i++)
                    {
                        if(!work.usable[i])continue;
                        var c=joints[i];
                        float next=Mathf.Clamp(work.angles[i]+(float)(work.delta[i]*fraction)*Mathf.Rad2Deg,c.minAngle,c.maxAngle);
                        float step=next-jointAngles[i];jointAngles[i]=next;
                        c.joint.rotation=Quaternion.AngleAxis(step,RingPoseJointAxis(c))*c.joint.rotation;
                    }
                    Vector3 trialPosition=desiredPosition-(ringGripTcp.position-(initializePose?Vector3.zero:ringGripTcp.up*ringRadius));
                    Vector3 trialRotation=initializePose?RingPoseRotationError(desiredRotation,ringGripTcp.rotation)
                        :RingPoseNormalError(desiredNormal,ringGripTcp.right,frame*Vector3.up);
                    float cost=trialPosition.sqrMagnitude+trialRotation.sqrMagnitude*orientationLength*orientationLength;
                    if(cost<previousCost-1e-14f)
                    {accepted=true;damping=Math.Max(.00005,damping*.6);break;}
                }
                if(!accepted)
                {
                    RestoreRingPoseTrial(work);damping*=4;
                    if(damping>.08)return;
                }
            }
        }
        static Vector3 RingPoseJointAxis(JointConfig joint)
        {
            if(joint.axis==JointAxis.WorldY)return Vector3.up;
            Vector3 local=joint.axis==JointAxis.X?Vector3.right:joint.axis==JointAxis.Z?Vector3.forward:Vector3.up;
            return joint.joint.TransformDirection(local).normalized;
        }
        static Vector3 RingPoseNormalError(Vector3 desired,Vector3 actual,Vector3 startingGuide)
        {
            Vector3 cross=Vector3.Cross(actual,desired);
            float sine=cross.magnitude,cosine=Mathf.Clamp(Vector3.Dot(actual,desired),-1,1);
            if(sine>1e-7f)return cross*(Mathf.Atan2(sine,cosine)/sine);
            if(cosine>=0)return Vector3.zero;
            Vector3 guide=Vector3.ProjectOnPlane(startingGuide,actual);
            if(guide.sqrMagnitude<1e-10f)guide=Vector3.ProjectOnPlane(Vector3.right,actual);
            if(guide.sqrMagnitude<1e-10f)guide=Vector3.ProjectOnPlane(Vector3.forward,actual);
            return guide.normalized*Mathf.PI;
        }
        static Vector3 RingPoseRotationError(Quaternion desired,Quaternion actual)
        {
            Quaternion difference=desired*Quaternion.Inverse(actual);
            if(difference.w<0)difference=new Quaternion(-difference.x,-difference.y,-difference.z,-difference.w);
            Vector3 vector=new Vector3(difference.x,difference.y,difference.z);
            float length=vector.magnitude;
            return length<1e-7f?vector*2:vector*(2*Mathf.Atan2(length,Mathf.Clamp(difference.w,-1,1))/length);
        }
        void RestoreRingPoseTrial(RingPoseIkBuffers work)
        {
            for(int i=0;i<joints.Length;i++)if(work.usable[i])
            {joints[i].joint.localRotation=work.rotations[i];jointAngles[i]=work.angles[i];}
        }
        static bool SolveRingPoseLinearSystem(RingPoseIkBuffers work)
        {
            int count=work.count;var matrix=work.system;
            // Partial pivoting in double precision keeps near-singular wrist poses bounded.
            for(int col=0;col<count;col++)
            {
                int pivot=col;for(int row=col+1;row<count;row++)if(Math.Abs(matrix[row,col])>Math.Abs(matrix[pivot,col]))pivot=row;
                if(Math.Abs(matrix[pivot,col])<1e-16)return false;
                if(pivot!=col)for(int k=col;k<=count;k++){double swap=matrix[col,k];matrix[col,k]=matrix[pivot,k];matrix[pivot,k]=swap;}
                double diagonal=matrix[col,col];
                for(int row=col+1;row<count;row++)
                {
                    double factor=matrix[row,col]/diagonal;
                    matrix[row,col]=0;for(int k=col+1;k<=count;k++)matrix[row,k]-=factor*matrix[col,k];
                }
            }
            for(int row=count-1;row>=0;row--)
            {
                double value=matrix[row,count];for(int col=row+1;col<count;col++)value-=matrix[row,col]*work.delta[col];
                work.delta[row]=value/matrix[row,row];
                if(double.IsNaN(work.delta[row])||double.IsInfinity(work.delta[row]))return false;
            }
            return true;
        }
        void UpdateToolVisuals()
        {
            if(!endEffector)return;
            if(penRoot){penRoot.position=endEffector.position;penRoot.rotation=Quaternion.identity;}
            if(ringRoot)
            {
                Vector3 center;Quaternion rotation;GetActualRingPose(out center,out rotation);
                ringRoot.SetPositionAndRotation(center,rotation);
            }
        }
        Quaternion RingFrame(Vector3 localCenter)
        {
            if(wirePoints==null||wirePoints.Length<2)return transform.rotation;
            int segment;float raw,nearest;
            Vector3 tangent=transform.TransformDirection(RingTangent(localCenter,currentRingSegment,out segment,out raw,out nearest)).normalized;
            return Quaternion.LookRotation(tangent,Vector3.up)*Quaternion.AngleAxis(ringGripRoll,Vector3.forward);
        }
        void GetActualRingPose(out Vector3 center,out Quaternion rotation)
        {
            Vector3 grip=RingGripPosition;
            Vector3 local=toolTarget?toolTarget.localPosition:transform.InverseTransformPoint(grip);
            if(!ringGripTcp){center=grip;rotation=RingFrame(local);return;}
            // The ring is a rigid payload: jaw closing axis is its plane normal,
            // while the distal contact holds its top rim one radius above its centre.
            rotation=Quaternion.LookRotation(ringGripTcp.right,ringGripTcp.up);
            center=grip-ringGripTcp.up*ringRadius;
        }
        void UpdateDrawing()
        {
            if(!PenDown||!endEffector){currentStroke=null;return;}
            Vector3 point=transform.InverseTransformPoint(endEffector.position);
            if(Mathf.Abs(point.y-drawingHeight)>.03f*exerciseScale){currentStroke=null;return;}
            point.y=drawingHeight+.003f*exerciseScale;
            if(currentStroke&&Vector3.Distance(lastInk,point)<.004f*exerciseScale)return;
            if(!currentStroke)
            {
                var go=new GameObject("DrawingStroke");go.transform.SetParent(transform,false);
                currentStroke=go.AddComponent<LineRenderer>();currentStroke.sharedMaterial=lineMaterial;
                currentStroke.useWorldSpace=false;currentStroke.widthMultiplier=.007f*exerciseScale;
                currentStroke.startColor=currentStroke.endColor=new Color(.02f,.24f,.7f);
                currentStroke.numCapVertices=4;currentStroke.positionCount=1;currentStroke.SetPosition(0,point);
                strokes.Add(currentStroke);lastInk=point;return;
            }
            inkLength+=Vector3.Distance(lastInk,point);lastInk=point;
            int count=currentStroke.positionCount;currentStroke.positionCount=count+1;currentStroke.SetPosition(count,point);
            Progress=Mathf.Clamp01(inkLength/(2f*exerciseScale));
        }

        enum RingLimit { None, Endpoint, Rim }
        float RingSkin => Mathf.Max(.00035f,.0015f*exerciseScale);
        // Leave room for the actual rigid ring's wire-sampling bound (<=0.5 mm),
        // polygon sagitta and IK tolerance so target contact can slide before safety rollback.
        float RingTargetInset => Mathf.Max(.0007f,.001875f*exerciseScale);
        float RingClearance => Mathf.Max(.0002f,ringRadius-ringTubeRadius-wireRadius-RingSkin);
        float RingStep
        {
            get
            {
                float shortest=float.PositiveInfinity;
                for(int i=0;i<wirePoints.Length-1;i++)
                {
                    float length=Vector3.Distance(wirePoints[i],wirePoints[i+1]);
                    if(length>.00001f)shortest=Mathf.Min(shortest,length);
                }
                return Mathf.Max(.0001f,Mathf.Min(RingClearance*.16f,shortest*.45f));
            }
        }

        // Free XYZ movement is retained inside the aperture. At contact, remove only the
        // outward component and slide along the local collision surface. No path snapping.
        void MoveRingTo(Vector3 destination,float dt)
        {
            if(!endEffector)return;
            Vector3 origin=acceptedRingTarget;
            toolTarget.localPosition=origin;
            float step=RingStep;
            int count=Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(origin,destination)/step),1,64);
            Vector3 displacement=(destination-origin)/count;
            for(int n=1;n<=count;n++)
            {
                Vector3 before=toolTarget.localPosition;
                Vector3 candidate=before+displacement;
                int segment;
                RingLimit limit;
                bool stopped=!RingPoseAllowed(candidate,currentRingSegment,out segment,out limit,RingTargetInset);
                float fraction=1;
                if(stopped)
                {
                    MarkRingLimit(limit);
                    float lo=0,hi=1;
                    for(int iteration=0;iteration<12;iteration++)
                    {
                        float mid=(lo+hi)*.5f;int ignoredSegment;RingLimit ignoredLimit;
                        if(RingPoseAllowed(Vector3.Lerp(before,candidate,mid),currentRingSegment,out ignoredSegment,out ignoredLimit,RingTargetInset))lo=mid;
                        else hi=mid;
                    }
                    fraction=lo;candidate=Vector3.Lerp(before,candidate,lo);
                }
                if(!CommitRingPose(candidate,dt,step))return;
                if(stopped)
                {
                    Vector3 remaining=displacement*(1-fraction);
                    Vector3 contact=toolTarget.localPosition;
                    Vector3 inward=RingInwardNormal(contact,currentRingSegment);
                    ringLastNormal=inward;
                    if(inward.sqrMagnitude<.5f){ringStopStage="no-surface-normal";ringRejectedPose=contact+remaining*.05f;return;}
                    float outward=Vector3.Dot(remaining,inward);
                    if(outward<0)remaining-=inward*outward;
                    if(remaining.sqrMagnitude<.0000000001f){ringStopStage="no-tangential-component";return;}

                    // Start the tangent step just inside the contact band. Correcting only
                    // its endpoint can leave the middle of the chord outside a curved or
                    // changing active surface. The retreat is at most 0.06 mm in this rig,
                    // occurs only during contact, and is charged to the input distance.
                    float retreat=Mathf.Min(.00006f,Mathf.Min(RingTargetInset*.3f,remaining.magnitude*.08f));
                    Vector3 inside=contact+inward*retreat;
                    if(!RingSweepAllowed(contact,inside,currentRingSegment,step*.3f,out segment,out limit,RingTargetInset)){ringStopStage="contact-band-retreat";return;}
                    if(!CommitRingPose(inside,dt,step*.3f))return;
                    remaining*=Mathf.Max(0,1-retreat/remaining.magnitude);
                    contact=toolTarget.localPosition;

                    // A tangent chord on a curved surface sits slightly outside it. A tiny
                    // normal correction retracts that chord into the contact band, without
                    // attracting a freely moving ring towards the centre of the wire.
                    Vector3 slide=contact+remaining;
                    float correctionBudget=remaining.magnitude*.6f;
                    float slideMargin=Mathf.Min(RingTargetInset*.15f,remaining.magnitude*.04f);
                    for(int correction=0;correction<5;correction++)
                    {
                        float margin=RingSafetyMargin(slide,currentRingSegment,out segment,out limit,RingTargetInset);
                        if(margin>=slideMargin)break;
                        Vector3 normal=RingInwardNormal(slide,currentRingSegment);
                        float amount=slideMargin-margin;
                        if(normal.sqrMagnitude<.5f||amount>correctionBudget)break;
                        slide+=normal*amount;correctionBudget-=amount;
                    }
                    slide=contact+Vector3.ClampMagnitude(slide-contact,remaining.magnitude);
                    if(!RingSweepAllowed(contact,slide,currentRingSegment,step*.3f,out segment,out limit,RingTargetInset)){ringStopStage="target-slide-sweep";return;}
                    if(!CommitRingPose(slide,dt,step*.3f))return;
                    ringStopStage="sliding";
                }
            }
        }

        bool CommitRingPose(Vector3 candidate,float dt,float step)
        {
            Vector3 before=toolTarget.localPosition;
            SaveJointStep();
            Vector3 actualBefore=transform.InverseTransformPoint(ActualToolPosition);
            toolTarget.localPosition=candidate;
            for(int solve=0;solve<3;solve++)SolveIK(Mathf.Max(dt,.012f));
            Vector3 actualAfter=transform.InverseTransformPoint(ActualToolPosition);
            int segment;RingLimit limit;
            string collision;float clearance;
            if(robotWireCollision&&!robotWireCollision.CheckSweep(stepRotations,out collision,out clearance))
            {
                RestoreJointStep();toolTarget.localPosition=before;acceptedRingTarget=before;
                if(ringGripTcp&&robotWireCollision.LastHitTransform==ringGripTcp)
                {
                    if(robotWireCollision.LastHitWasRingRim)MarkRingLimit(RingLimit.Rim);
                    else RingMovementBlocked=true;
                    ringStopStage="held-ring-sweep";return false;
                }
                RobotWireBlocked=true;RobotCollisionReason=collision;RingMovementBlocked=true;
                ringStopStage="robot-wire-sweep";return false;
            }
            segment=currentRingSegment;
            if(!ringGripTcp&&!RingSweepAllowed(actualBefore,actualAfter,currentRingSegment,step,out segment,out limit))
            {
                RestoreJointStep();toolTarget.localPosition=before;
                ringStopStage="actual-IK-sweep";
                MarkRingLimit(limit);acceptedRingTarget=before;return false;
            }
            float raw,nearest;RingTangent(actualAfter,segment,out segment,out raw,out nearest);
            currentRingSegment=segment;acceptedRingTarget=candidate;return true;
        }

        internal float HeldRingSafetyMargin => RingSkin;
        internal bool HeldRingClearance(out float clearance)
        {
            clearance=float.PositiveInfinity;
            if(!ringGripTcp||wirePoints==null||wirePoints.Length<2)return false;
            Vector3 center;Quaternion rotation;GetActualRingPose(out center,out rotation);
            Vector3 normal=rotation*Vector3.forward;
            float tube=ringTubeRadius+wireRadius;
            // The visible 64-edge ring differs from a perfect circle by at most this sagitta.
            float sagitta=ringRadius*(1-Mathf.Cos(Mathf.PI/RingSegments));
            for(int i=0;i<wirePoints.Length-1;i++)
            {
                Vector3 a=transform.TransformPoint(wirePoints[i]),b=transform.TransformPoint(wirePoints[i+1]);
                float length=Vector3.Distance(a,b);
                if(Vector3.Distance((a+b)*.5f,center)-length*.5f-ringRadius-tube>clearance)continue;
                int count=Mathf.Max(1,Mathf.CeilToInt(length/.001f));
                float lowerBound=float.PositiveInfinity;
                for(int n=0;n<=count;n++)
                {
                    Vector3 q=Vector3.Lerp(a,b,n/(float)count)-center;
                    float axial=Vector3.Dot(q,normal);
                    float radial=Mathf.Sqrt(Mathf.Max(0,q.sqrMagnitude-axial*axial))-ringRadius;
                    lowerBound=Mathf.Min(lowerBound,Mathf.Sqrt(radial*radial+axial*axial)-tube);
                }
                // Distance to a set is 1-Lipschitz; half a sample spacing covers every
                // point between samples, including a tiny wire segment's interior.
                clearance=Mathf.Min(clearance,lowerBound-length/(2*count)-sagitta);
            }
            return !float.IsNaN(clearance)&&!float.IsInfinity(clearance);
        }
        internal bool HeldRingThreadAllowed()
        {
            if(!ringGripTcp||wirePoints==null||wirePoints.Length<2)return false;
            Vector3 center=transform.InverseTransformPoint(ActualToolPosition);
            Vector3 normal=transform.InverseTransformDirection(ringGripTcp.right).normalized;
            int segment;float raw,nearest;RingTangent(center,currentRingSegment,out segment,out raw,out nearest);
            const float tolerance=.00018f;
            if(segment==0&&Vector3.Dot(center-wirePoints[0],(wirePoints[1]-wirePoints[0]).normalized)<-tolerance)return false;
            if(segment==wirePoints.Length-2&&Vector3.Dot(center-wirePoints[wirePoints.Length-1],(wirePoints[wirePoints.Length-1]-wirePoints[wirePoints.Length-2]).normalized)>tolerance)return false;
            float aperture=ringRadius-ringTubeRadius-wireRadius;
            for(int i=0;i<wirePoints.Length-1;i++)
            {
                Vector3 a=wirePoints[i]-center,b=wirePoints[i+1]-center;
                float da=Vector3.Dot(a,normal),db=Vector3.Dot(b,normal);
                float t=Mathf.Abs(da-db)>1e-8f?Mathf.Clamp01(da/(da-db)):0;
                Vector3 p=Vector3.Lerp(a,b,t);
                float axial=Vector3.Dot(p,normal);
                if(Mathf.Abs(axial)<=tolerance&&p.sqrMagnitude-axial*axial<aperture*aperture)return true;
            }
            return false;
        }

        Vector3 RingInwardNormal(Vector3 center,int hint)
        {
            float epsilon=Mathf.Max(.000025f,.0001f*exerciseScale);
            int segment;RingLimit limit;
            float x=RingSafetyMargin(center+Vector3.right*epsilon,hint,out segment,out limit,RingTargetInset)-RingSafetyMargin(center-Vector3.right*epsilon,hint,out segment,out limit,RingTargetInset);
            float y=RingSafetyMargin(center+Vector3.up*epsilon,hint,out segment,out limit,RingTargetInset)-RingSafetyMargin(center-Vector3.up*epsilon,hint,out segment,out limit,RingTargetInset);
            float z=RingSafetyMargin(center+Vector3.forward*epsilon,hint,out segment,out limit,RingTargetInset)-RingSafetyMargin(center-Vector3.forward*epsilon,hint,out segment,out limit,RingTargetInset);
            Vector3 gradient=new Vector3(x,y,z);
            return gradient.sqrMagnitude>1e-14f?gradient.normalized:Vector3.zero;
        }

        void MarkRingLimit(RingLimit limit)
        {
            RingMovementBlocked=true;
            if(limit==RingLimit.Rim)ringRimBlocked=true;
        }

        void SaveJointStep()
        {
            for(int i=0;i<joints.Length;i++)
                if(joints[i]!=null&&joints[i].joint)
                {stepRotations[i]=joints[i].joint.localRotation;stepAngles[i]=jointAngles[i];}
        }
        void RestoreJointStep()
        {
            for(int i=0;i<joints.Length;i++)
                if(joints[i]!=null&&joints[i].joint)
                {joints[i].joint.localRotation=stepRotations[i];jointAngles[i]=stepAngles[i];}
        }

        bool RingSweepAllowed(Vector3 from,Vector3 to,int hint,float maxStep,out int segment,out RingLimit limit,float inset=0)
        {
            segment=hint;limit=RingLimit.None;
            int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(from,to)/maxStep));
            for(int i=1;i<=count;i++)
                if(!RingPoseAllowed(Vector3.Lerp(from,to,i/(float)count),segment,out segment,out limit,inset)){ringRejectedPose=Vector3.Lerp(from,to,i/(float)count);return false;}
            return true;
        }

        public string DescribeRingConstraint()
        {
            int segment;RingLimit limit;
            Vector3 actual=transform.InverseTransformPoint(ActualToolPosition);
            float targetMargin=RingSafetyMargin(toolTarget.localPosition,currentRingSegment,out segment,out limit,RingTargetInset);
            string targetLimit=limit.ToString();
            float actualMargin=RingSafetyMargin(actual,currentRingSegment,out segment,out limit,0,
                ringGripTcp?(Vector3?)transform.InverseTransformDirection(ringGripTcp.right):null);
            float rejectedMargin=RingSafetyMargin(ringRejectedPose,currentRingSegment,out segment,out limit,RingTargetInset);
            return "stage="+ringStopStage+" segment="+currentRingSegment+" target="+toolTarget.localPosition.ToString("F7")+" actual="+actual.ToString("F7")+" targetMargin="+targetMargin.ToString("F8")+" targetLimit="+targetLimit+" actualMargin="+actualMargin.ToString("F8")+" rejected="+ringRejectedPose.ToString("F7")+" rejectedMargin="+rejectedMargin.ToString("F8")+" rejectedLimit="+limit+" normal="+ringLastNormal.ToString("F6")+" IKerror="+PositionError.ToString("F8");
        }

        Vector3 RingTangent(Vector3 center,int hint,out int segment,out float nearestRaw,out float nearest)
        {
            segment=Mathf.Clamp(hint,0,wirePoints.Length-2);
            nearest=float.PositiveInfinity;nearestRaw=0;
            int first=Mathf.Max(0,segment-3),last=Mathf.Min(wirePoints.Length-2,segment+3);
            for(int i=first;i<=last;i++)
            {
                Vector3 d=wirePoints[i+1]-wirePoints[i];
                float raw=Vector3.Dot(center-wirePoints[i],d)/Mathf.Max(1e-10f,d.sqrMagnitude);
                float distance=(center-(wirePoints[i]+Mathf.Clamp01(raw)*d)).sqrMagnitude;
                if(distance<nearest){nearest=distance;segment=i;nearestRaw=raw;}
            }
            Vector3 start=(wirePoints[segment+1]-wirePoints[Mathf.Max(0,segment-1)]).normalized;
            Vector3 finish=(wirePoints[Mathf.Min(wirePoints.Length-1,segment+2)]-wirePoints[segment]).normalized;
            return Vector3.Lerp(start,finish,Mathf.Clamp01(nearestRaw)).normalized;
        }

        bool RingPoseAllowed(Vector3 center,int hint,out int segment,out RingLimit limit,float inset=0)
        {
            return RingSafetyMargin(center,hint,out segment,out limit,inset)>=0;
        }

        float RingSafetyMargin(Vector3 center,int hint,out int segment,out RingLimit limit,float inset=0,Vector3? planeNormal=null)
        {
            float nearest,nearestRaw;
            Vector3 tangent=RingTangent(center,hint,out segment,out nearestRaw,out nearest);
            if(planeNormal.HasValue)tangent=planeNormal.Value.normalized;
            const float endTolerance=.00018f;
            float margin=RingClearance-inset-Mathf.Sqrt(nearest);limit=RingLimit.Rim;
            float cap=float.PositiveInfinity;
            float capInset=Mathf.Min(inset,.00013f);
            if(segment==0)cap=Vector3.Dot(center-wirePoints[0],(wirePoints[1]-wirePoints[0]).normalized)+endTolerance-capInset;
            if(segment==wirePoints.Length-2)cap=endTolerance-capInset-Vector3.Dot(center-wirePoints[wirePoints.Length-1],(wirePoints[wirePoints.Length-1]-wirePoints[wirePoints.Length-2]).normalized);
            if(cap<margin){margin=cap;limit=RingLimit.Endpoint;}

            // Measure a continuous signed clearance at the guide/plane intersection.
            // A constant negative result outside the hole has no gradient and can lock
            // a ring at a bend. Search by connected arc length, not just three tiny edges:
            // an offset ring's plane can intersect the adjacent guide farther along it.
            int first=segment,last=segment;float reach=ringRadius*2,walk=0;
            while(first>0&&walk<reach){walk+=Vector3.Distance(wirePoints[first],wirePoints[first-1]);first--;}
            walk=0;
            while(last<wirePoints.Length-2&&walk<reach){walk+=Vector3.Distance(wirePoints[last+1],wirePoints[last+2]);last++;}
            float threadMargin=float.NegativeInfinity;
            for(int i=first;i<=last;i++)
            {
                Vector3 a=wirePoints[i],b=wirePoints[i+1];
                float da=Vector3.Dot(a-center,tangent),db=Vector3.Dot(b-center,tangent);
                float t=Mathf.Abs(da-db)>.0000001f?Mathf.Clamp01(da/(da-db)):0;
                Vector3 q=Vector3.Lerp(a,b,t)-center;
                float axial=Vector3.Dot(q,tangent);
                float radial=Mathf.Sqrt(Mathf.Max(0,q.sqrMagnitude-axial*axial));
                float intersectionMargin=Mathf.Min(RingClearance-inset-radial,endTolerance-Mathf.Abs(axial));
                threadMargin=Mathf.Max(threadMargin,intersectionMargin);
            }
            if(threadMargin<margin){margin=threadMargin;limit=RingLimit.Rim;}

            // Check the finite-width rim as well, including curved/nonlocal wire portions.
            float tube=ringTubeRadius+wireRadius+RingSkin+inset;
            for(int i=0;i<wirePoints.Length-1;i++)
            {
                Vector3 a=wirePoints[i],b=wirePoints[i+1];
                Vector3 midpoint=(a+b)*.5f;
                if((midpoint-center).magnitude>ringRadius+tube+Vector3.Distance(a,b)*.5f)continue;
                int samples=Mathf.Max(2,Mathf.CeilToInt(Vector3.Distance(a,b)/Mathf.Max(.0002f,tube*.45f)));
                for(int s=0;s<=samples;s++)
                {
                    Vector3 q=Vector3.Lerp(a,b,s/(float)samples)-center;
                    float axial=Vector3.Dot(q,tangent);
                    float radial=Mathf.Sqrt(Mathf.Max(0,q.sqrMagnitude-axial*axial))-ringRadius;
                    float gap=Mathf.Sqrt(radial*radial+axial*axial)-tube;
                    if(gap<margin){margin=gap;limit=RingLimit.Rim;}
                }
            }
            return margin;
        }
        void UpdateRing()
        {
            if(!ringRoot||wirePoints==null||wirePoints.Length<2)return;
            contactCooldown=Mathf.Max(0,contactCooldown-Time.deltaTime);
            Vector3 center=ringRoot.position;float nearest=float.PositiveInfinity;int nearestIndex=0;float nearestT=0;bool contact=ringRimBlocked;
            // Sphere-to-circle distance on densely subdivided wire segments approximates tube contact,
            // including both axial separation from the ring plane and radial separation from its rim.
            for(int i=0;i<wirePoints.Length-1;i++)
            {
                Vector3 a=transform.TransformPoint(wirePoints[i]),b=transform.TransformPoint(wirePoints[i+1]);
                Vector3 segment=b-a;float t=Mathf.Clamp01(Vector3.Dot(center-a,segment)/Mathf.Max(segment.sqrMagnitude,1e-8f));
                float d=(a+t*segment-center).sqrMagnitude;if(d<nearest){nearest=d;nearestIndex=i;nearestT=t;}
                for(int s=0;s<=3;s++)
                {
                    Vector3 q=ringRoot.InverseTransformPoint(Vector3.Lerp(a,b,s/3f));
                    float radial=Mathf.Sqrt(q.x*q.x+q.y*q.y)-ringRadius;
                    if(radial*radial+q.z*q.z<=Mathf.Pow(ringTubeRadius+wireRadius,2))contact=true;
                }
            }
            ToolInContact=contact;
            if(contact&&!previousContact&&contactCooldown<=0){Contacts++;contactCooldown=.25f;}
            previousContact=contact;
            Color ringColor=contact?new Color(1,.22f,.14f):new Color(1,.7f,.15f);
            if(ringRenderer)ringRenderer.startColor=ringRenderer.endColor=ringColor;
            if(ringSolidRenderer)
            {
                if(ringColorProperties==null)ringColorProperties=new MaterialPropertyBlock();
                ringSolidRenderer.GetPropertyBlock(ringColorProperties);
                ringColorProperties.SetColor("_Color",ringColor);
                ringSolidRenderer.SetPropertyBlock(ringColorProperties);
            }
            if(!ringStarted&&nearestIndex<4&&nearest<Mathf.Pow(ringRadius-wireRadius-ringTubeRadius,2))ringStarted=true;
            if(ringStarted&&nearest<Mathf.Pow(ringRadius-wireRadius-ringTubeRadius,2)&&nearestIndex<=furthestSegment+8)
            {
                furthestSegment=Mathf.Max(furthestSegment,nearestIndex);
                float achieved=(nearestIndex+nearestT)/(wirePoints.Length-1);
                if(nearestIndex==wirePoints.Length-2&&Vector3.Distance(center,transform.TransformPoint(wirePoints[wirePoints.Length-1]))<.0004f)achieved=1;
                Progress=Mathf.Max(Progress,Mathf.Clamp01(achieved));
            }
        }
        void OnDestroy(){if(lineMaterial)Destroy(lineMaterial);}
    }
}
