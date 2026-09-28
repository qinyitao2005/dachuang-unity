using UnityEngine;

namespace RoboticsLab
{
    /// <summary>套圈几何：钢丝切线、圈内间隙、端点和实体圆环安全距离。</summary>
    internal sealed class LabRingGeometry
    {
        readonly Group0927Station station;
        const int RingSegments = 64;

        public LabRingGeometry(Group0927Station station) { this.station = station; }

        public float Skin => Mathf.Max(.00035f,.0015f*station.exerciseScale);
        // Leave room for the actual rigid ring's wire-sampling bound (<=0.5 mm),
        // polygon sagitta and IK tolerance so target contact can slide before safety rollback.
        public float TargetInset => Mathf.Max(.0007f,.001875f*station.exerciseScale);
        public float Clearance => Mathf.Max(.0002f,station.ringRadius-station.ringTubeRadius-station.wireRadius-Skin);
        public float Step
        {
            get
            {
                float shortest=float.PositiveInfinity;
                for(int i=0;i<station.wirePoints.Length-1;i++)
                {
                    float length=Vector3.Distance(station.wirePoints[i],station.wirePoints[i+1]);
                    if(length>.00001f)shortest=Mathf.Min(shortest,length);
                }
                return Mathf.Max(.0001f,Mathf.Min(Clearance*.16f,shortest*.45f));
            }
        }



        public Quaternion Frame(Vector3 localCenter,int hint)
        {
            if(station.wirePoints==null||station.wirePoints.Length<2)return station.transform.rotation;
            int segment;float raw,nearest;
            Vector3 tangent=station.transform.TransformDirection(Tangent(localCenter,hint,out segment,out raw,out nearest)).normalized;
            return Quaternion.LookRotation(tangent,Vector3.up)*Quaternion.AngleAxis(station.ringGripRoll,Vector3.forward);
        }

        public bool HeldRingClearance(out float clearance)
        {
            clearance=float.PositiveInfinity;
            if(!station.ringGripTcp||station.wirePoints==null||station.wirePoints.Length<2)return false;
            Vector3 center=station.ActualToolPosition;
            Vector3 normal=station.ringGripTcp.right;
            float tube=station.ringTubeRadius+station.wireRadius;
            // The visible 64-edge ring differs from a perfect circle by at most this sagitta.
            float sagitta=station.ringRadius*(1-Mathf.Cos(Mathf.PI/RingSegments));
            for(int i=0;i<station.wirePoints.Length-1;i++)
            {
                Vector3 a=station.transform.TransformPoint(station.wirePoints[i]),b=station.transform.TransformPoint(station.wirePoints[i+1]);
                float length=Vector3.Distance(a,b);
                if(Vector3.Distance((a+b)*.5f,center)-length*.5f-station.ringRadius-tube>clearance)continue;
                int count=Mathf.Max(1,Mathf.CeilToInt(length/.001f));
                float lowerBound=float.PositiveInfinity;
                for(int n=0;n<=count;n++)
                {
                    Vector3 q=Vector3.Lerp(a,b,n/(float)count)-center;
                    float axial=Vector3.Dot(q,normal);
                    float radial=Mathf.Sqrt(Mathf.Max(0,q.sqrMagnitude-axial*axial))-station.ringRadius;
                    lowerBound=Mathf.Min(lowerBound,Mathf.Sqrt(radial*radial+axial*axial)-tube);
                }
                // Distance to a set is 1-Lipschitz; half a sample spacing covers every
                // point between samples, including a tiny wire segment's interior.
                clearance=Mathf.Min(clearance,lowerBound-length/(2*count)-sagitta);
            }
            return !float.IsNaN(clearance)&&!float.IsInfinity(clearance);
        }

        public bool HeldRingThreadAllowed(int hint)
        {
            if(!station.ringGripTcp||station.wirePoints==null||station.wirePoints.Length<2)return false;
            Vector3 center=station.transform.InverseTransformPoint(station.ActualToolPosition);
            Vector3 normal=station.transform.InverseTransformDirection(station.ringGripTcp.right).normalized;
            int segment;float raw,nearest;Tangent(center,hint,out segment,out raw,out nearest);
            const float tolerance=.00018f;
            if(segment==0&&Vector3.Dot(center-station.wirePoints[0],(station.wirePoints[1]-station.wirePoints[0]).normalized)<-tolerance)return false;
            if(segment==station.wirePoints.Length-2&&Vector3.Dot(center-station.wirePoints[station.wirePoints.Length-1],(station.wirePoints[station.wirePoints.Length-1]-station.wirePoints[station.wirePoints.Length-2]).normalized)>tolerance)return false;
            float aperture=station.ringRadius-station.ringTubeRadius-station.wireRadius;
            for(int i=0;i<station.wirePoints.Length-1;i++)
            {
                Vector3 a=station.wirePoints[i]-center,b=station.wirePoints[i+1]-center;
                float da=Vector3.Dot(a,normal),db=Vector3.Dot(b,normal);
                float t=Mathf.Abs(da-db)>1e-8f?Mathf.Clamp01(da/(da-db)):0;
                Vector3 p=Vector3.Lerp(a,b,t);
                float axial=Vector3.Dot(p,normal);
                if(Mathf.Abs(axial)<=tolerance&&p.sqrMagnitude-axial*axial<aperture*aperture)return true;
            }
            return false;
        }

        public Vector3 InwardNormal(Vector3 center,int hint)
        {
            float epsilon=Mathf.Max(.000025f,.0001f*station.exerciseScale);
            int segment;LabRingLimit limit;
            float x=SafetyMargin(center+Vector3.right*epsilon,hint,out segment,out limit,TargetInset)-SafetyMargin(center-Vector3.right*epsilon,hint,out segment,out limit,TargetInset);
            float y=SafetyMargin(center+Vector3.up*epsilon,hint,out segment,out limit,TargetInset)-SafetyMargin(center-Vector3.up*epsilon,hint,out segment,out limit,TargetInset);
            float z=SafetyMargin(center+Vector3.forward*epsilon,hint,out segment,out limit,TargetInset)-SafetyMargin(center-Vector3.forward*epsilon,hint,out segment,out limit,TargetInset);
            Vector3 gradient=new Vector3(x,y,z);
            return gradient.sqrMagnitude>1e-14f?gradient.normalized:Vector3.zero;
        }

        public Vector3 Tangent(Vector3 center,int hint,out int segment,out float nearestRaw,out float nearest)
        {
            segment=Mathf.Clamp(hint,0,station.wirePoints.Length-2);
            nearest=float.PositiveInfinity;nearestRaw=0;
            int first=Mathf.Max(0,segment-3),last=Mathf.Min(station.wirePoints.Length-2,segment+3);
            for(int i=first;i<=last;i++)
            {
                Vector3 d=station.wirePoints[i+1]-station.wirePoints[i];
                float raw=Vector3.Dot(center-station.wirePoints[i],d)/Mathf.Max(1e-10f,d.sqrMagnitude);
                float distance=(center-(station.wirePoints[i]+Mathf.Clamp01(raw)*d)).sqrMagnitude;
                if(distance<nearest){nearest=distance;segment=i;nearestRaw=raw;}
            }
            Vector3 start=(station.wirePoints[segment+1]-station.wirePoints[Mathf.Max(0,segment-1)]).normalized;
            Vector3 finish=(station.wirePoints[Mathf.Min(station.wirePoints.Length-1,segment+2)]-station.wirePoints[segment]).normalized;
            return Vector3.Lerp(start,finish,Mathf.Clamp01(nearestRaw)).normalized;
        }

        public bool PoseAllowed(Vector3 center,int hint,out int segment,out LabRingLimit limit,float inset=0)
        {
            return SafetyMargin(center,hint,out segment,out limit,inset)>=0;
        }

        public float SafetyMargin(Vector3 center,int hint,out int segment,out LabRingLimit limit,float inset=0,Vector3? planeNormal=null)
        {
            float nearest,nearestRaw;
            Vector3 tangent=Tangent(center,hint,out segment,out nearestRaw,out nearest);
            if(planeNormal.HasValue)tangent=planeNormal.Value.normalized;
            const float endTolerance=.00018f;
            float margin=Clearance-inset-Mathf.Sqrt(nearest);limit=LabRingLimit.Rim;
            float cap=float.PositiveInfinity;
            float capInset=Mathf.Min(inset,.00013f);
            if(segment==0)cap=Vector3.Dot(center-station.wirePoints[0],(station.wirePoints[1]-station.wirePoints[0]).normalized)+endTolerance-capInset;
            if(segment==station.wirePoints.Length-2)cap=endTolerance-capInset-Vector3.Dot(center-station.wirePoints[station.wirePoints.Length-1],(station.wirePoints[station.wirePoints.Length-1]-station.wirePoints[station.wirePoints.Length-2]).normalized);
            if(cap<margin){margin=cap;limit=LabRingLimit.Endpoint;}

            // Measure a continuous signed clearance at the guide/plane intersection.
            // A constant negative result outside the hole has no gradient and can lock
            // a ring at a bend. Search by connected arc length, not just three tiny edges:
            // an offset ring's plane can intersect the adjacent guide farther along it.
            int first=segment,last=segment;float reach=station.ringRadius*2,walk=0;
            while(first>0&&walk<reach){walk+=Vector3.Distance(station.wirePoints[first],station.wirePoints[first-1]);first--;}
            walk=0;
            while(last<station.wirePoints.Length-2&&walk<reach){walk+=Vector3.Distance(station.wirePoints[last+1],station.wirePoints[last+2]);last++;}
            float threadMargin=float.NegativeInfinity;
            for(int i=first;i<=last;i++)
            {
                Vector3 a=station.wirePoints[i],b=station.wirePoints[i+1];
                float da=Vector3.Dot(a-center,tangent),db=Vector3.Dot(b-center,tangent);
                float t=Mathf.Abs(da-db)>.0000001f?Mathf.Clamp01(da/(da-db)):0;
                Vector3 q=Vector3.Lerp(a,b,t)-center;
                float axial=Vector3.Dot(q,tangent);
                float radial=Mathf.Sqrt(Mathf.Max(0,q.sqrMagnitude-axial*axial));
                float intersectionMargin=Mathf.Min(Clearance-inset-radial,endTolerance-Mathf.Abs(axial));
                threadMargin=Mathf.Max(threadMargin,intersectionMargin);
            }
            if(threadMargin<margin){margin=threadMargin;limit=LabRingLimit.Rim;}

            // Check the finite-width rim as well, including curved/nonlocal wire portions.
            float tube=station.ringTubeRadius+station.wireRadius+Skin+inset;
            for(int i=0;i<station.wirePoints.Length-1;i++)
            {
                Vector3 a=station.wirePoints[i],b=station.wirePoints[i+1];
                Vector3 midpoint=(a+b)*.5f;
                if((midpoint-center).magnitude>station.ringRadius+tube+Vector3.Distance(a,b)*.5f)continue;
                int samples=Mathf.Max(2,Mathf.CeilToInt(Vector3.Distance(a,b)/Mathf.Max(.0002f,tube*.45f)));
                for(int s=0;s<=samples;s++)
                {
                    Vector3 q=Vector3.Lerp(a,b,s/(float)samples)-center;
                    float axial=Vector3.Dot(q,tangent);
                    float radial=Mathf.Sqrt(Mathf.Max(0,q.sqrMagnitude-axial*axial))-station.ringRadius;
                    float gap=Mathf.Sqrt(radial*radial+axial*axial)-tube;
                    if(gap<margin){margin=gap;limit=LabRingLimit.Rim;}
                }
            }
            return margin;
        }
    }
}
