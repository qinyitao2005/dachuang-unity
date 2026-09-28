using UnityEngine;

namespace RoboticsLab
{
    /// <summary>套圈成绩：有效进度、碰线状态和去抖后的碰线次数。</summary>
    internal sealed class LabRingExercise
    {
        readonly Group0927Station station;
        float contactCooldown;
        bool previousContact, ringStarted;
        int furthestSegment;
        public float Progress { get; private set; }
        public int Contacts { get; private set; }
        public bool ToolInContact { get; private set; }

        public LabRingExercise(Group0927Station station) { this.station = station; }
        public void Reset()
        {
            Progress = 0;
            Contacts = furthestSegment = 0;
            ToolInContact = previousContact = ringStarted = false;
            contactCooldown = 0;
        }

        public void Tick(bool ringRimBlocked, float deltaTime)
        {
            if(!station.ringRoot||station.wirePoints==null||station.wirePoints.Length<2)return;
            contactCooldown=Mathf.Max(0,contactCooldown-deltaTime);
            Vector3 center=station.ringRoot.position;float nearest=float.PositiveInfinity;int nearestIndex=0;float nearestT=0;bool contact=ringRimBlocked;
            // Sphere-to-circle distance on densely subdivided wire segments approximates tube contact,
            // including both axial separation from the ring plane and radial separation from its rim.
            for(int i=0;i<station.wirePoints.Length-1;i++)
            {
                Vector3 a=station.transform.TransformPoint(station.wirePoints[i]),b=station.transform.TransformPoint(station.wirePoints[i+1]);
                Vector3 segment=b-a;float t=Mathf.Clamp01(Vector3.Dot(center-a,segment)/Mathf.Max(segment.sqrMagnitude,1e-8f));
                float d=(a+t*segment-center).sqrMagnitude;if(d<nearest){nearest=d;nearestIndex=i;nearestT=t;}
                for(int s=0;s<=3;s++)
                {
                    Vector3 q=station.ringRoot.InverseTransformPoint(Vector3.Lerp(a,b,s/3f));
                    float radial=Mathf.Sqrt(q.x*q.x+q.y*q.y)-station.ringRadius;
                    if(radial*radial+q.z*q.z<=Mathf.Pow(station.ringTubeRadius+station.wireRadius,2))contact=true;
                }
            }
            ToolInContact=contact;
            if(contact&&!previousContact&&contactCooldown<=0){Contacts++;contactCooldown=.25f;}
            previousContact=contact;
            if(!ringStarted&&nearestIndex<4&&nearest<Mathf.Pow(station.ringRadius-station.wireRadius-station.ringTubeRadius,2))ringStarted=true;
            if(ringStarted&&nearest<Mathf.Pow(station.ringRadius-station.wireRadius-station.ringTubeRadius,2)&&nearestIndex<=furthestSegment+8)
            {
                furthestSegment=Mathf.Max(furthestSegment,nearestIndex);
                float achieved=(nearestIndex+nearestT)/(station.wirePoints.Length-1);
                if(nearestIndex==station.wirePoints.Length-2&&Vector3.Distance(center,station.transform.TransformPoint(station.wirePoints[station.wirePoints.Length-1]))<.0004f)achieved=1;
                Progress=Mathf.Max(Progress,Mathf.Clamp01(achieved));
            }
        }
    }
}
