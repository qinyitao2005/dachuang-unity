using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>
    /// Conservative wire-capsule checks against individual moving CAD mesh bounds.
    /// This component does not move the robot, except for temporary joint poses inside
    /// CheckSweep, whose finally block always restores the supplied current pose.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Group0927WireCollision : MonoBehaviour
    {
        public Group0927Station station;
        [Min(0)] public float safetyMargin=.00035f;
        [Min(.0001f)] public float maxSweepPadding=.00075f;
        [Range(4,256)] public int maxSweepIntervals=96;

        public int ProxyCount => proxies.Count;
        public int LastActiveProxyCount { get; private set; }
        public int LastSweepIntervals { get; private set; }
        public bool LastHeldRingChecked { get; private set; }
        public float LastHeldRingClearance { get; private set; } = float.PositiveInfinity;
        public float LastHeldRingPadding { get; private set; }
        public bool LastHitWasRingRim { get; private set; }
        public Transform LastHitTransform { get; private set; }
        public int LastHitWireSegment { get; private set; } = -1;
        public string CacheError { get; private set; } = "";

        sealed class Proxy
        {
            public MeshFilter filter;
            public Renderer renderer;
            public Bounds localBounds;
            public float[] rotationRadii;
            public string path;
            public int deepestJoint;
        }
        struct Segment
        {
            public Vector3 a,b,min,max;
        }
        struct Box
        {
            public Vector3 center,x,y,z,extents,min,max;
        }
        readonly List<Proxy> proxies=new List<Proxy>();
        readonly float[] cuts=new float[8];
        JointConfig[] cachedJoints;
        Group0927Station cachedStation;
        Transform cachedModel;
        Transform cachedGrip;
        float cachedRingRadius,cachedRingTubeRadius;
        Quaternion[] finalRotations;
        float[] jointAngles,paddings,ringRotationRadii;
        Segment[] wireSegments;
        int wireSegmentCount;
        float wireWorldRadius;
        Vector3 wireMin,wireMax;
        bool cached;

        /// <summary>
        /// Rebuild after adding/replacing the gripper or changing mesh geometry/scale.
        /// Inactive original tools stay cached but are skipped by every live pose check.
        /// Static cabinet/console meshes outside configured joint descendants are excluded.
        /// </summary>
        public void RebuildCache()
        {
            if(!station)station=GetComponent<Group0927Station>();
            proxies.Clear();CacheError="";cached=true;
            cachedStation=station;cachedJoints=station?station.joints:null;cachedModel=station?station.modelRoot:null;
            cachedGrip=station?station.ringGripTcp:null;
            cachedRingRadius=station?station.ringRadius:0;cachedRingTubeRadius=station?station.ringTubeRadius:0;
            LastHitTransform=null;LastHitWireSegment=-1;LastActiveProxyCount=0;
            if(!station||!cachedModel||cachedJoints==null||cachedJoints.Length==0)
            {CacheError="Robot model and configured joints are required.";return;}

            var filters=cachedModel.GetComponentsInChildren<MeshFilter>(true);
            foreach(var filter in filters)
            {
                if(!filter.sharedMesh)continue;
                var renderer=filter.GetComponent<Renderer>();
                if(!renderer)continue;
                bool moving=false;
                for(int j=0;j<cachedJoints.Length;j++)
                    if(cachedJoints[j]!=null&&cachedJoints[j].joint&&IsUnder(filter.transform,cachedJoints[j].joint)){moving=true;break;}
                if(!moving)continue;

                Bounds local=filter.sharedMesh.bounds; // Available even when Read/Write is disabled.
                if(!Finite(local.center)||!Finite(local.extents)||local.extents.x<0||local.extents.y<0||local.extents.z<0)
                {CacheError="Invalid local mesh bounds: "+filter.name;return;}
                var proxy=new Proxy{filter=filter,renderer=renderer,localBounds=local,rotationRadii=new float[cachedJoints.Length],path=PathOf(filter.transform,cachedModel)};
                if(!CacheRotationRadii(filter.transform,local,proxy.rotationRadii,out proxy.deepestJoint)){CacheError="Invalid transform scale: "+proxy.path;return;}
                proxies.Add(proxy);
            }
            ringRotationRadii=new float[cachedJoints.Length];
            if(cachedGrip)
            {
                int ignored;
                if(!Finite(cachedRingRadius)||!Finite(cachedRingTubeRadius)||cachedRingRadius<=0||cachedRingTubeRadius<0||
                    !CacheRotationRadii(cachedGrip,new Bounds(Vector3.zero,Vector3.zero),ringRotationRadii,out ignored))
                {CacheError="Invalid rigid ring attachment or dimensions.";return;}
                // Ring dimensions and TCP.up/right are world-rigid, even when the CAD
                // hierarchy contains scaled transforms. Its farthest point lies two
                // radii plus the tube away from the rim grip. Add this rotational term
                // independently of the TCP point's conservative translation envelope.
                float payloadRadius=2*cachedRingRadius+cachedRingTubeRadius;
                for(int j=0;j<cachedJoints.Length;j++)
                    if(cachedJoints[j]!=null&&cachedJoints[j].joint&&IsUnder(cachedGrip,cachedJoints[j].joint))ringRotationRadii[j]+=payloadRadius;
            }
            // Tool/wrist candidates usually establish a tight minimum first, allowing
            // the whole-wire broad phase to reject distant shoulder/base meshes cheaply.
            proxies.Sort((a,b)=>b.deepestJoint.CompareTo(a.deepestJoint));
            finalRotations=new Quaternion[cachedJoints.Length];jointAngles=new float[cachedJoints.Length];
            paddings=new float[proxies.Count];
            if(proxies.Count==0)CacheError="No rendered CAD meshes were found under the configured moving joints.";
        }

        /// <summary>
        /// True means the current pose has at least safetyMargin clearance.
        /// minClearance is signed OBB-to-wire-surface separation in WORLD metres:
        /// zero means tangency; negative means overlap with the conservative mesh box.
        /// Missing/unverifiable geometry returns false and negative infinity.
        /// </summary>
        public bool Check(out string reason,out float minClearance)
        {
            LastSweepIntervals=0;
            if(!EnsureCache(out reason)||!PrepareProductionWire(out reason))return Unverified(reason,out reason,out minClearance);
            return CheckPose(null,safetyMargin,out reason,out minClearance,HasHeldRing,0,HasHeldRing?station.HeldRingSafetyMargin:0);
        }

        /// <summary>Check a world-space test capsule against the same live CAD mesh proxies.</summary>
        public bool CheckWireSegment(Vector3 aWorld,Vector3 bWorld,float radius,out string reason,out float minClearance)
        {
            LastSweepIntervals=0;
            if(!EnsureCache(out reason))return Unverified(reason,out reason,out minClearance);
            if(!Finite(aWorld)||!Finite(bWorld)||!Finite(radius)||radius<0)
                return Unverified("Invalid test wire capsule.",out reason,out minClearance);
            EnsureWireStorage(1);wireSegments[0]=MakeSegment(aWorld,bWorld);wireSegmentCount=1;wireWorldRadius=radius;
            wireMin=wireSegments[0].min;wireMax=wireSegments[0].max;
            return CheckPose(null,safetyMargin,out reason,out minClearance);
        }

        /// <summary>
        /// Validate Slerp(previous local joint rotations, current local joint rotations).
        /// The current/final pose is restored on success, rejection and exceptions.
        /// Positions, scales and ancestors outside station.joints must stay fixed during
        /// the move. RebuildCache after changing those authored transforms.
        /// Swept minClearance subtracts the conservative between-sample motion envelope.
        /// Excessive required sampling rejects the move instead of dropping samples.
        /// </summary>
        public bool CheckSweep(Quaternion[] previousLocalRotations,out string reason,out float minClearance)
        {
            LastSweepIntervals=0;
            if(!EnsureCache(out reason)||!PrepareProductionWire(out reason))return Unverified(reason,out reason,out minClearance);
            if(previousLocalRotations==null||previousLocalRotations.Length!=cachedJoints.Length)
                return Unverified("Previous joint rotation array does not match station.joints.",out reason,out minClearance);
            if(!Finite(maxSweepPadding)||maxSweepPadding<=0||maxSweepIntervals<1)
                return Unverified("Invalid sweep sampling settings.",out reason,out minClearance);

            for(int j=0;j<cachedJoints.Length;j++)
            {
                Transform joint=cachedJoints[j]!=null?cachedJoints[j].joint:null;
                finalRotations[j]=joint?joint.localRotation:Quaternion.identity;
                if(!joint){jointAngles[j]=0;continue;}
                if(!Finite(previousLocalRotations[j])||!Finite(finalRotations[j]))
                    return Unverified("Invalid joint quaternion at index "+j+".",out reason,out minClearance);
                jointAngles[j]=RotationAngleRadians(previousLocalRotations[j],finalRotations[j]);
            }
            float maximumMotion=0;
            for(int p=0;p<proxies.Count;p++)
            {
                float motion=0;
                if(IsActive(proxies[p]))
                    for(int j=0;j<jointAngles.Length;j++)motion+=jointAngles[j]*proxies[p].rotationRadii[j];
                paddings[p]=motion;maximumMotion=Mathf.Max(maximumMotion,motion);
            }
            bool heldRing=HasHeldRing;
            float ringMotion=0,ringStaticMargin=heldRing?station.HeldRingSafetyMargin:0;
            if(heldRing)
                for(int j=0;j<jointAngles.Length;j++)ringMotion+=jointAngles[j]*ringRotationRadii[j];
            maximumMotion=Mathf.Max(maximumMotion,ringMotion);
            if(maximumMotion<=1e-9f)return CheckPose(null,safetyMargin,out reason,out minClearance,heldRing,0,ringStaticMargin);
            if(!CheckPose(null,safetyMargin,out reason,out minClearance,heldRing,0,ringStaticMargin)){reason="Final pose: "+reason;return false;}
            float finalClearance=minClearance;
            try
            {
                for(int j=0;j<cachedJoints.Length;j++)
                    if(cachedJoints[j]!=null&&cachedJoints[j].joint)cachedJoints[j].joint.localRotation=previousLocalRotations[j];
                if(!CheckPose(null,safetyMargin,out reason,out minClearance,heldRing,0,ringStaticMargin)){reason="Initial pose: "+reason;return false;}
                float endpointClearance=Mathf.Min(finalClearance,minClearance);
                if(endpointClearance<=0)
                    return Unverified("A moving sweep needs positive endpoint surface clearance.",out reason,out minClearance);

                // Endpoint poses retain the static safety margin. Between samples we
                // prove physical nonintersection, without charging that static buffer a
                // second time. Padding <= half the actual endpoint gap guarantees that
                // an already legal contact pose can start a retreat instead of locking.
                float paddingLimit=Mathf.Min(maxSweepPadding,endpointClearance*.5f);
                double required=Math.Ceiling(maximumMotion/(2.0*paddingLimit));
                if(!Finite(maximumMotion)||required>maxSweepIntervals)
                    return Unverified("Wire sweep needs "+required+" intervals; budget is "+maxSweepIntervals+". Subdivide this joint move.",out reason,out minClearance);
                int intervals=Mathf.Max(1,(int)required);LastSweepIntervals=intervals;
                float largestPadding=0;
                for(int p=0;p<paddings.Length;p++){paddings[p]/=2*intervals;largestPadding=Mathf.Max(largestPadding,paddings[p]);}
                float ringPadding=ringMotion/(2*intervals);largestPadding=Mathf.Max(largestPadding,ringPadding);
                minClearance=endpointClearance-largestPadding;
                LastHeldRingPadding=ringPadding;
                // Both endpoint samples are already proven safe with their padding.
                for(int sample=1;sample<intervals;sample++)
                {
                    float t=sample/(float)intervals;
                    for(int j=0;j<cachedJoints.Length;j++)
                        if(cachedJoints[j]!=null&&cachedJoints[j].joint)
                            cachedJoints[j].joint.localRotation=Quaternion.Slerp(previousLocalRotations[j],finalRotations[j],t);
                    string poseReason;float clearance;
                    bool safe=CheckPose(paddings,0,out poseReason,out clearance,heldRing,ringPadding,0);
                    minClearance=Mathf.Min(minClearance,clearance);
                    if(!safe)
                    {
                        reason="Sweep sample "+sample+"/"+intervals+": "+poseReason;
                        return false;
                    }
                }
                return true;
            }
            finally
            {
                for(int j=0;j<cachedJoints.Length;j++)
                    if(cachedJoints[j]!=null&&cachedJoints[j].joint)cachedJoints[j].joint.localRotation=finalRotations[j];
            }
        }

        public Transform GetProxyTransform(int index)=>index>=0&&index<proxies.Count&&proxies[index].filter?proxies[index].filter.transform:null;
        public Bounds GetProxyLocalBounds(int index)=>proxies[index].localBounds;

        bool HasHeldRing => station&&station.TrainingMode==1&&station.ringGripTcp;

        bool CacheRotationRadii(Transform item,Bounds localBounds,float[] rotationRadii,out int deepestJoint)
        {
            deepestJoint=0;
            var chain=new List<Transform>();
            for(Transform t=item;t;t=t.parent)
            {
                bool configured=false;
                for(int j=0;j<cachedJoints.Length;j++)
                    if(cachedJoints[j]!=null&&cachedJoints[j].joint==t){configured=true;deepestJoint=Mathf.Max(deepestJoint,j);}
                if(configured)chain.Add(t);
            }
            if(chain.Count==0)return false;
            // Collapse all fixed CAD transforms before taking a length. Imported CAD
            // often has large opposite parent/child offsets that cancel exactly; summing
            // their separate magnitudes would needlessly inflate every sweep interval.
            Matrix4x4 relative=RelativeMatrix(item,chain[0]);
            float radius=0;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {
                Vector3 corner=localBounds.center+Vector3.Scale(localBounds.extents,new Vector3(x,y,z));
                radius=Mathf.Max(radius,relative.MultiplyPoint3x4(corner).magnitude);
            }
            for(int k=0;k<chain.Count;k++)
            {
                Transform joint=chain[k];float worldRadius=radius*ScaleUpperBound(joint);
                if(!Finite(worldRadius))return false;
                for(int j=0;j<cachedJoints.Length;j++)
                    if(cachedJoints[j]!=null&&cachedJoints[j].joint==joint)rotationRadii[j]=worldRadius;
                if(k+1>=chain.Count)break;
                Transform parentJoint=chain[k+1];
                Vector3 pivot=RelativeMatrix(joint.parent,parentJoint).MultiplyPoint3x4(joint.localPosition);
                float scale=1;
                for(Transform t=joint;t&&t!=parentJoint;t=t.parent)
                {Vector3 s=t.localScale;scale*=Mathf.Max(Mathf.Abs(s.x),Mathf.Abs(s.y),Mathf.Abs(s.z));}
                // Only configured joints can now change the directions of these rigid
                // chunks. The enclosing sphere remains valid throughout all rotations.
                radius=pivot.magnitude+radius*scale;
            }
            return true;
        }
        static Matrix4x4 RelativeMatrix(Transform child,Transform ancestor)
        {
            Matrix4x4 result=Matrix4x4.identity;
            while(child&&child!=ancestor)
            {
                result=Matrix4x4.TRS(child.localPosition,child.localRotation,child.localScale)*result;
                child=child.parent;
            }
            return result;
        }

        bool EnsureCache(out string reason)
        {
            if(!station)station=GetComponent<Group0927Station>();
            if(!cached||cachedStation!=station||cachedModel!=(station?station.modelRoot:null)||cachedJoints!=(station?station.joints:null)||
                cachedGrip!=(station?station.ringGripTcp:null)||cachedRingRadius!=(station?station.ringRadius:0)||cachedRingTubeRadius!=(station?station.ringTubeRadius:0))RebuildCache();
            reason=CacheError;return string.IsNullOrEmpty(reason)&&proxies.Count>0;
        }
        bool PrepareProductionWire(out string reason)
        {
            if(station.wirePoints==null||station.wirePoints.Length<2||!Finite(station.wireRadius)||station.wireRadius<0)
            {reason="Steel wire points and radius are not configured.";return false;}
            wireWorldRadius=station.wireRadius*ScaleUpperBound(station.transform);
            if(!Finite(wireWorldRadius)){reason="Invalid wire scale.";return false;}
            wireSegmentCount=station.wirePoints.Length-1;EnsureWireStorage(wireSegmentCount);
            Vector3 a=station.transform.TransformPoint(station.wirePoints[0]);
            wireMin=wireMax=a;
            for(int i=0;i<wireSegmentCount;i++)
            {
                Vector3 b=station.transform.TransformPoint(station.wirePoints[i+1]);
                if(!Finite(a)||!Finite(b)){reason="Invalid wire point at "+i+".";return false;}
                wireSegments[i]=MakeSegment(a,b);a=b;
                wireMin=Vector3.Min(wireMin,b);wireMax=Vector3.Max(wireMax,b);
            }
            reason="";return true;
        }
        void EnsureWireStorage(int count){if(wireSegments==null||wireSegments.Length<count)wireSegments=new Segment[count];}
        static Segment MakeSegment(Vector3 a,Vector3 b)=>new Segment{a=a,b=b,min=Vector3.Min(a,b),max=Vector3.Max(a,b)};

        bool CheckPose(float[] temporalPadding,float requiredMargin,out string reason,out float minClearance,bool heldRing=false,float ringPadding=0,float ringRequiredMargin=0)
        {
            minClearance=float.PositiveInfinity;int closestProxy=-1,closestWire=-1;
            LastHitTransform=null;LastHitWireSegment=-1;LastActiveProxyCount=0;
            LastHeldRingChecked=false;LastHeldRingClearance=float.PositiveInfinity;LastHeldRingPadding=ringPadding;
            LastHitWasRingRim=false;
            for(int p=0;p<proxies.Count;p++)
            {
                Proxy proxy=proxies[p];if(!IsActive(proxy))continue;
                LastActiveProxyCount++;
                Box box=WorldBox(proxy);
                if(!Finite(box.center)||!Finite(box.extents))return Unverified("Invalid mesh transform: "+proxy.path,out reason,out minClearance);
                float radius=wireWorldRadius+(temporalPadding!=null?temporalPadding[p]:0);
                if(Mathf.Sqrt(BoundsDistanceSquared(box.min,box.max,wireMin,wireMax))-radius>=minClearance)continue;
                for(int i=0;i<wireSegmentCount;i++)
                {
                    Segment segment=wireSegments[i];
                    float lowerBound=Mathf.Sqrt(BoundsDistanceSquared(box.min,box.max,segment.min,segment.max))-radius;
                    if(lowerBound>=minClearance)continue;
                    Vector3 a=ToBox(segment.a,box),b=ToBox(segment.b,box);
                    float clearance=Mathf.Sqrt(SegmentBoxDistanceSquared(a,b,box.extents))-radius;
                    if(clearance<minClearance){minClearance=clearance;closestProxy=p;closestWire=i;}
                }
            }
            if(LastActiveProxyCount==0)return Unverified("No active rendered meshes on the moving chain.",out reason,out minClearance);
            if(!Finite(requiredMargin)||requiredMargin<0)return Unverified("Invalid safety margin.",out reason,out minClearance);
            if(minClearance<requiredMargin)
            {
                LastHitTransform=closestProxy>=0?proxies[closestProxy].filter.transform:null;LastHitWireSegment=closestWire;
                reason=(closestProxy>=0?proxies[closestProxy].path:"Moving mesh")+" near wire segment "+closestWire+"; conservative clearance="+(minClearance*1000).ToString("F3")+" mm (required "+(requiredMargin*1000).ToString("F3")+" mm).";
                return false;
            }
            if(heldRing)
            {
                LastHeldRingChecked=true;
                float ringGap;
                if(!station.HeldRingClearance(out ringGap)||!Finite(ringGap)||!Finite(ringRequiredMargin)||ringRequiredMargin<0)
                    return Unverified("Held ring clearance cannot be verified.",out reason,out minClearance);
                LastHeldRingClearance=ringGap;
                float sweptRingGap=ringGap-ringPadding;
                minClearance=Mathf.Min(minClearance,sweptRingGap);
                if(sweptRingGap<ringRequiredMargin)
                {
                    LastHitWasRingRim=true;
                    LastHitTransform=station.ringGripTcp;LastHitWireSegment=-1;
                    reason="Held ring near wire; conservative clearance="+(sweptRingGap*1000).ToString("F3")+" mm (required "+(ringRequiredMargin*1000).ToString("F3")+" mm).";
                    return false;
                }
                if(!station.HeldRingThreadAllowed())
                {
                    LastHitTransform=station.ringGripTcp;LastHitWireSegment=-1;
                    reason="Held ring leaves the threaded guide or crosses a wire endpoint.";return false;
                }
            }
            reason="";return true;
        }

        static bool IsActive(Proxy proxy)=>proxy.filter&&proxy.renderer&&proxy.filter.gameObject.activeInHierarchy&&proxy.renderer.enabled;
        static bool IsUnder(Transform child,Transform parent)=>child==parent||child.IsChildOf(parent);
        static float ScaleUpperBound(Transform t)
        {
            float scale=1;
            while(t){Vector3 s=t.localScale;scale*=Mathf.Max(Mathf.Abs(s.x),Mathf.Abs(s.y),Mathf.Abs(s.z));t=t.parent;}
            return scale;
        }
        static string PathOf(Transform t,Transform root)
        {
            string path=t.name;
            while(t.parent&&t.parent!=root){t=t.parent;path=t.name+"/"+path;}
            return path;
        }
        static Box WorldBox(Proxy proxy)
        {
            Transform t=proxy.filter.transform;Matrix4x4 matrix=t.localToWorldMatrix;
            Vector3 extent=proxy.localBounds.extents;
            Vector3 vx=matrix.MultiplyVector(new Vector3(extent.x,0,0));
            Vector3 vy=matrix.MultiplyVector(new Vector3(0,extent.y,0));
            Vector3 vz=matrix.MultiplyVector(new Vector3(0,0,extent.z));
            var box=new Box{center=matrix.MultiplyPoint3x4(proxy.localBounds.center),x=t.rotation*Vector3.right,y=t.rotation*Vector3.up,z=t.rotation*Vector3.forward};
            box.extents=new Vector3(ProjectExtent(box.x,vx,vy,vz),ProjectExtent(box.y,vx,vy,vz),ProjectExtent(box.z,vx,vy,vz));
            Vector3 worldExtent=Abs(box.x)*box.extents.x+Abs(box.y)*box.extents.y+Abs(box.z)*box.extents.z;
            box.min=box.center-worldExtent;box.max=box.center+worldExtent;return box;
        }
        static float ProjectExtent(Vector3 axis,Vector3 a,Vector3 b,Vector3 c)=>Mathf.Abs(Vector3.Dot(axis,a))+Mathf.Abs(Vector3.Dot(axis,b))+Mathf.Abs(Vector3.Dot(axis,c));
        static Vector3 Abs(Vector3 v)=>new Vector3(Mathf.Abs(v.x),Mathf.Abs(v.y),Mathf.Abs(v.z));
        static Vector3 ToBox(Vector3 point,Box box)
        {
            Vector3 q=point-box.center;return new Vector3(Vector3.Dot(q,box.x),Vector3.Dot(q,box.y),Vector3.Dot(q,box.z));
        }
        static float BoundsDistanceSquared(Vector3 minA,Vector3 maxA,Vector3 minB,Vector3 maxB)
        {
            Vector3 delta=Vector3.Max(Vector3.zero,Vector3.Max(minA-maxB,minB-maxA));return delta.sqrMagnitude;
        }

        // Distance along a segment to an AABB is a convex piecewise quadratic. Split at
        // the six slab crossings, then minimize each quadratic interval exactly. This
        // also handles a point segment and boxes collapsed by a zero scale.
        float SegmentBoxDistanceSquared(Vector3 a,Vector3 b,Vector3 extents)
        {
            Vector3 d=b-a;int count=2;cuts[0]=0;cuts[1]=1;
            for(int axis=0;axis<3;axis++)
            {
                if(Mathf.Abs(d[axis])<1e-12f)continue;
                float first=(-extents[axis]-a[axis])/d[axis],second=(extents[axis]-a[axis])/d[axis];
                if(first>0&&first<1)cuts[count++]=first;
                if(second>0&&second<1)cuts[count++]=second;
            }
            for(int i=1;i<count;i++){float value=cuts[i];int j=i-1;while(j>=0&&cuts[j]>value){cuts[j+1]=cuts[j];j--;}cuts[j+1]=value;}
            float minimum=float.PositiveInfinity;
            for(int i=0;i<count-1;i++)
            {
                float lo=cuts[i],hi=cuts[i+1],mid=(lo+hi)*.5f,A=0,B=0;
                for(int axis=0;axis<3;axis++)
                {
                    float value=a[axis]+d[axis]*mid;
                    if(value>=-extents[axis]&&value<=extents[axis])continue;
                    float offset=a[axis]-(value<0?-extents[axis]:extents[axis]);
                    A+=d[axis]*d[axis];B+=d[axis]*offset;
                }
                float t=A>1e-20f?Mathf.Clamp(-B/A,lo,hi):lo;
                Vector3 q=Abs(a+d*t)-extents;q=Vector3.Max(q,Vector3.zero);
                minimum=Mathf.Min(minimum,q.sqrMagnitude);
                if(minimum<=0)return 0;
            }
            return minimum;
        }
        bool Unverified(string message,out string reason,out float minClearance)
        {
            LastHitTransform=null;LastHitWireSegment=-1;reason=message;minClearance=float.NegativeInfinity;return false;
        }
        static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
        static bool Finite(Quaternion q)=>Finite(q.x)&&Finite(q.y)&&Finite(q.z)&&Finite(q.w)&&q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w>.000001f;
        static float RotationAngleRadians(Quaternion a,Quaternion b)
        {
            // Quaternion.Angle deliberately treats very small rotations as equal. A
            // swept-motion bound must retain those rotations too, especially when small
            // joint changes cancel at the TCP while a remote link still moves.
            double an=Math.Sqrt((double)a.x*a.x+(double)a.y*a.y+(double)a.z*a.z+(double)a.w*a.w);
            double bn=Math.Sqrt((double)b.x*b.x+(double)b.y*b.y+(double)b.z*b.z+(double)b.w*b.w);
            double sign=(double)a.x*b.x+(double)a.y*b.y+(double)a.z*b.z+(double)a.w*b.w<0?-1:1;
            double ax=a.x/an,ay=a.y/an,az=a.z/an,aw=a.w/an;
            double bx=sign*b.x/bn,by=sign*b.y/bn,bz=sign*b.z/bn,bw=sign*b.w/bn;
            double difference=(ax-bx)*(ax-bx)+(ay-by)*(ay-by)+(az-bz)*(az-bz)+(aw-bw)*(aw-bw);
            double sum=(ax+bx)*(ax+bx)+(ay+by)*(ay+by)+(az+bz)*(az+bz)+(aw+bw)*(aw+bw);
            return (float)(4*Math.Atan2(Math.Sqrt(difference),Math.Sqrt(sum)));
        }
    }
}
