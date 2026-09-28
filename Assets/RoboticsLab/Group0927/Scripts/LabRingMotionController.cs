using UnityEngine;

namespace RoboticsLab
{
    /// <summary>套圈运动事务：细分输入、接触滑动、碰撞检查和受阻姿态回退。</summary>
    internal sealed class LabRingMotionController
    {
        readonly Group0927Station station;
        readonly LabRobotJointRig rig;
        readonly LabRingGeometry geometry;
        readonly Group0927WireCollision robotWireCollision;
        readonly System.Action<float> solveIk;
        int currentRingSegment;
        Vector3 acceptedRingTarget;
        bool ringRimBlocked;
        string ringStopStage = "none";
        Vector3 ringRejectedPose, ringLastNormal;

        public int Segment => currentRingSegment;
        public bool RimBlocked => ringRimBlocked;
        public bool RingMovementBlocked { get; private set; }
        public bool RobotWireBlocked { get; private set; }
        public string RobotCollisionReason { get; private set; } = "";

        public LabRingMotionController(Group0927Station station, LabRobotJointRig rig,
            LabRingGeometry geometry, Group0927WireCollision collision, System.Action<float> solveIk)
        {
            this.station = station;
            this.rig = rig;
            this.geometry = geometry;
            robotWireCollision = collision;
            this.solveIk = solveIk;
        }

        public void Reset()
        {
            currentRingSegment = 0;
            ringRimBlocked = false;
            ClearInputBlock();
            ringStopStage = "reset";
            ringRejectedPose = ringLastNormal = Vector3.zero;
        }

        public void AcceptCurrentTarget() { acceptedRingTarget = station.toolTarget.localPosition; }
        public void EndFrame() { ringRimBlocked = false; }
        public void ClearInputBlock()
        {
            RingMovementBlocked = RobotWireBlocked = false;
            RobotCollisionReason = "";
        }

        public void MoveTo(Vector3 destination,float dt)
        {
            if(!station.endEffector)return;
            Vector3 origin=acceptedRingTarget;
            station.toolTarget.localPosition=origin;
            float step=geometry.Step;
            int count=Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(origin,destination)/step),1,512);
            Vector3 displacement=(destination-origin)/count;
            for(int n=1;n<=count;n++)
            {
                Vector3 before=station.toolTarget.localPosition;
                Vector3 candidate=before+displacement;
                int segment;
                LabRingLimit limit;
                bool stopped=!geometry.PoseAllowed(candidate,currentRingSegment,out segment,out limit,geometry.TargetInset);
                float fraction=1;
                if(stopped)
                {
                    MarkRingLimit(limit);
                    float lo=0,hi=1;
                    for(int iteration=0;iteration<12;iteration++)
                    {
                        float mid=(lo+hi)*.5f;int ignoredSegment;LabRingLimit ignoredLimit;
                        if(geometry.PoseAllowed(Vector3.Lerp(before,candidate,mid),currentRingSegment,out ignoredSegment,out ignoredLimit,geometry.TargetInset))lo=mid;
                        else hi=mid;
                    }
                    fraction=lo;candidate=Vector3.Lerp(before,candidate,lo);
                }
                if(!CommitRingPose(candidate,dt,step))return;
                if(stopped)
                {
                    Vector3 remaining=displacement*(1-fraction);
                    Vector3 contact=station.toolTarget.localPosition;
                    Vector3 inward=geometry.InwardNormal(contact,currentRingSegment);
                    ringLastNormal=inward;
                    if(inward.sqrMagnitude<.5f){ringStopStage="no-surface-normal";ringRejectedPose=contact+remaining*.05f;return;}
                    float outward=Vector3.Dot(remaining,inward);
                    if(outward<0)remaining-=inward*outward;
                    if(remaining.sqrMagnitude<.0000000001f){ringStopStage="no-tangential-component";return;}

                    // Start the tangent step just inside the contact band. Correcting only
                    // its endpoint can leave the middle of the chord outside a curved or
                    // changing active surface. The retreat is at most 0.06 mm in this rig,
                    // occurs only during contact, and is charged to the input distance.
                    float retreat=Mathf.Min(.00006f,Mathf.Min(geometry.TargetInset*.3f,remaining.magnitude*.08f));
                    Vector3 inside=contact+inward*retreat;
                    if(!RingSweepAllowed(contact,inside,currentRingSegment,step*.3f,out segment,out limit,geometry.TargetInset)){ringStopStage="contact-band-retreat";return;}
                    if(!CommitRingPose(inside,dt,step*.3f))return;
                    remaining*=Mathf.Max(0,1-retreat/remaining.magnitude);
                    contact=station.toolTarget.localPosition;

                    // A tangent chord on a curved surface sits slightly outside it. A tiny
                    // normal correction retracts that chord into the contact band, without
                    // attracting a freely moving ring towards the centre of the wire.
                    Vector3 slide=contact+remaining;
                    float correctionBudget=remaining.magnitude*.6f;
                    float slideMargin=Mathf.Min(geometry.TargetInset*.15f,remaining.magnitude*.04f);
                    for(int correction=0;correction<5;correction++)
                    {
                        float margin=geometry.SafetyMargin(slide,currentRingSegment,out segment,out limit,geometry.TargetInset);
                        if(margin>=slideMargin)break;
                        Vector3 normal=geometry.InwardNormal(slide,currentRingSegment);
                        float amount=slideMargin-margin;
                        if(normal.sqrMagnitude<.5f||amount>correctionBudget)break;
                        slide+=normal*amount;correctionBudget-=amount;
                    }
                    slide=contact+Vector3.ClampMagnitude(slide-contact,remaining.magnitude);
                    if(!RingSweepAllowed(contact,slide,currentRingSegment,step*.3f,out segment,out limit,geometry.TargetInset)){ringStopStage="target-slide-sweep";return;}
                    if(!CommitRingPose(slide,dt,step*.3f))return;
                    ringStopStage="sliding";
                }
            }
        }

        bool CommitRingPose(Vector3 candidate,float dt,float step)
        {
            Vector3 before=station.toolTarget.localPosition;
            rig.CaptureCollisionPose();
            Vector3 actualBefore=station.transform.InverseTransformPoint(station.ActualToolPosition);
            station.toolTarget.localPosition=candidate;
            for(int solve=0;solve<3;solve++)solveIk(Mathf.Max(dt,.012f));
            Vector3 actualAfter=station.transform.InverseTransformPoint(station.ActualToolPosition);
            int segment;LabRingLimit limit;
            string collision;float clearance;
            if(robotWireCollision&&!robotWireCollision.CheckSweep(rig.CollisionRotations,out collision,out clearance))
            {
                rig.RestoreCollisionPose();station.toolTarget.localPosition=before;acceptedRingTarget=before;
                if(station.ringGripTcp&&robotWireCollision.LastHitTransform==station.ringGripTcp)
                {
                    if(robotWireCollision.LastHitWasRingRim)MarkRingLimit(LabRingLimit.Rim);
                    else RingMovementBlocked=true;
                    ringStopStage="held-ring-sweep";return false;
                }
                RobotWireBlocked=true;RobotCollisionReason=collision;RingMovementBlocked=true;
                ringStopStage="robot-wire-sweep";return false;
            }
            segment=currentRingSegment;
            if(!station.ringGripTcp&&!RingSweepAllowed(actualBefore,actualAfter,currentRingSegment,step,out segment,out limit))
            {
                rig.RestoreCollisionPose();station.toolTarget.localPosition=before;
                ringStopStage="actual-IK-sweep";
                MarkRingLimit(limit);acceptedRingTarget=before;return false;
            }
            float raw,nearest;geometry.Tangent(actualAfter,segment,out segment,out raw,out nearest);
            currentRingSegment=segment;acceptedRingTarget=candidate;return true;
        }

        void MarkRingLimit(LabRingLimit limit)
        {
            RingMovementBlocked=true;
            if(limit==LabRingLimit.Rim)ringRimBlocked=true;
        }

        bool RingSweepAllowed(Vector3 from,Vector3 to,int hint,float maxStep,out int segment,out LabRingLimit limit,float inset=0)
        {
            segment=hint;limit=LabRingLimit.None;
            int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(from,to)/maxStep));
            for(int i=1;i<=count;i++)
                if(!geometry.PoseAllowed(Vector3.Lerp(from,to,i/(float)count),segment,out segment,out limit,inset)){ringRejectedPose=Vector3.Lerp(from,to,i/(float)count);return false;}
            return true;
        }

        public string DescribeConstraint()
        {
            int segment;LabRingLimit limit;
            Vector3 actual=station.transform.InverseTransformPoint(station.ActualToolPosition);
            float targetMargin=geometry.SafetyMargin(station.toolTarget.localPosition,currentRingSegment,out segment,out limit,geometry.TargetInset);
            string targetLimit=limit.ToString();
            float actualMargin=geometry.SafetyMargin(actual,currentRingSegment,out segment,out limit,0,
                station.ringGripTcp?(Vector3?)station.transform.InverseTransformDirection(station.ringGripTcp.right):null);
            float rejectedMargin=geometry.SafetyMargin(ringRejectedPose,currentRingSegment,out segment,out limit,geometry.TargetInset);
            return "stage="+ringStopStage+" segment="+currentRingSegment+" target="+station.toolTarget.localPosition.ToString("F7")+" actual="+actual.ToString("F7")+" targetMargin="+targetMargin.ToString("F8")+" targetLimit="+targetLimit+" actualMargin="+actualMargin.ToString("F8")+" rejected="+ringRejectedPose.ToString("F7")+" rejectedMargin="+rejectedMargin.ToString("F8")+" rejectedLimit="+limit+" normal="+ringLastNormal.ToString("F6")+" IKerror="+station.PositionError.ToString("F8");
        }
    }
}
