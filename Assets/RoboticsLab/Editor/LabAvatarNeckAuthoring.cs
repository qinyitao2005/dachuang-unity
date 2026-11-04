using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoboticsLab.Editor
{
    // Sculpt in the Ch32 bind pose (metres). The neck and the head still follow the
    // original Humanoid bones. The scene uses the same single skinned renderer.
    internal static class LabAvatarNeckAuthoring
    {
        const int Sectors = 48;
        const int Rings = 19;

        internal static void Apply(Mesh source, Mesh mesh, Transform[] bones)
        {
            Vector3[] rest = source.vertices;
            int[] triangles = source.triangles;
            var islands = Islands(rest, triangles);
            var bounds = new Dictionary<int, Bounds>();
            for (int i = 0; i < rest.Length; i++)
            {
                int id = islands[i];
                if (!bounds.TryGetValue(id, out Bounds box)) box = new Bounds(rest[i], Vector3.zero);
                box.Encapsulate(rest[i]); bounds[id] = box;
            }
            var remove = new HashSet<int>();
            int face = -1, torso = -1;
            foreach (var pair in bounds)
            {
                Bounds box = pair.Value;
                if (box.min.y > 1.40f && box.max.y < 1.62f && box.size.x < .19f) remove.Add(pair.Key);
                if (box.min.y > 1.52f && box.max.y > 1.74f) face = pair.Key;
                if (box.min.x < -.5f && box.max.x > .5f) torso = pair.Key;
            }
            if (remove.Count != 4 || torso < 0 || face < 0 || mesh.subMeshCount != 1 || mesh.blendShapeCount != 0)
                throw new InvalidOperationException("Unexpected Ch32 geometry; cannot identify the collar, bellows and two neck cables.");
            int neckBone = FindBone(bones, "Neck"), headBone = FindBone(bones, "Head");
            int chestBone = FindBone(bones, "Spine2");

            var positions = new List<Vector3>(); var normals = new List<Vector3>();
            var tangents = new List<Vector4>(); var uv = new List<Vector2>();
            var weights = new List<BoneWeight>(); var output = new List<int>();
            var torsoTriangles = new List<int>();
            var skull = new List<int>();
            var v = mesh.vertices; var n = mesh.normals; var t = mesh.tangents;
            var tex = mesh.uv; var w = mesh.boneWeights; var map = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                if (remove.Contains(islands[i])) { map[i] = -1; continue; }
                if (islands[i] == face) SculptLowerHead(ref v[i], ref n[i], ref t[i]);
                map[i] = positions.Count;
                positions.Add(v[i]); normals.Add(n[i]); tangents.Add(t[i]); uv.Add(tex[i]); weights.Add(w[i]);
            }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i+1], c = triangles[i+2];
                if (map[a] < 0) continue;
                output.Add(map[a]); output.Add(map[b]); output.Add(map[c]);
                if (islands[a] == face) { skull.Add(a); skull.Add(b); skull.Add(c); }
                if (islands[a] == torso && Mathf.Min(v[a].y, Mathf.Min(v[b].y, v[c].y)) > 1.32f
                    && Mathf.Min(Mathf.Abs(v[a].x), Mathf.Min(Mathf.Abs(v[b].x), Mathf.Abs(v[c].x))) < .17f)
                { torsoTriangles.Add(a); torsoTriangles.Add(b); torsoTriangles.Add(c); }
            }

            int start = positions.Count;
            var shoulderNormals = new Vector3[Rings * Sectors];
            var scalpNormals = new Vector3[Rings * Sectors];
            var scalpBlend = new float[Rings * Sectors];
            for (int row = 0; row < Rings; row++)
            {
                float h = row / (float)(Rings - 1);
                for (int col = 0; col < Sectors; col++)
                {
                    float a = 2 * Mathf.PI * col / Sectors, side = Mathf.Sin(a), front = Mathf.Cos(a);
                    // A broad saddle crosses the collarbones and the upper trapezius.
                    // At the throat this narrows and tips gently forward beneath the jaw.
                    float flare = Mathf.Pow(1-h, 3.7f);
                    float x = side * (.046f + .063f*flare + .003f*h*h);
                    float z = -.007f + .020f*h + front*(.052f + .024f*flare + .006f*h*h);
                    float baseY = SurfaceHeight(.109f*side, -.007f + .076f*front, v, torsoTriangles);
                    float topY = 1.625f - .025f*front;
                    float y = Mathf.Lerp(baseY-.006f, topY, h);
                    if (h < .48f)
                    {
                        float shoulder = SurfaceHeight(x,z,v,torsoTriangles,n,out Vector3 shoulderNormal);
                        shoulderNormals[row*Sectors+col] = shoulderNormal;
                        y = Mathf.Max(y, shoulder + .002f - .008f*(1-Smooth(0,.16f,h)));
                    }
                    // Tuck the nape forward under the occiput and taper both sides.
                    // Keep the throat and the existing collarbone attachment in place.
                    float upperNeck = Smooth(1.505f, 1.561f, y);
                    float back = 1 - Smooth(-.93f, .62f, front);
                    x -= .0075f * side * upperNeck * (1 - Smooth(.08f, .86f, front));
                    z += .018f * upperNeck * back;
                    int sample = row*Sectors+col;
                    float headFollow = BlendNapeIntoSkull(ref x,y,ref z,front,v,n,skull,out scalpNormals[sample],out scalpBlend[sample]);
                    positions.Add(new Vector3(x,y,z)); normals.Add(Vector3.zero); tangents.Add(Vector4.zero);
                    uv.Add(new Vector2(col/(float)Sectors,h));
                    float fromChest = 1-Smooth(.10f,.70f,h);
                    float fromHead = Mathf.Lerp(Smooth(.45f,.94f,h),1f,headFollow);
                    weights.Add(new BoneWeight {boneIndex0=neckBone,weight0=1-fromChest-fromHead,
                        boneIndex1=chestBone,weight1=fromChest,boneIndex2=headBone,weight2=fromHead});
                }
            }
            // The zipper has a folded top face right under the throat. Exclude its
            // outlier normal when smoothing the surrounding cloth, to avoid a glint.
            for (int row=0; row<5; row++)
            {
                int first = row*Sectors;
                Vector3 left = shoulderNormals[first+Sectors-3], right = shoulderNormals[first+3];
                for (int col=-2; col<=2; col++)
                    shoulderNormals[first+(col+Sectors)%Sectors] = Vector3.Slerp(left,right,(col+3)/6f);
            }
            for (int row = 0; row < Rings; row++) for (int col = 0; col < Sectors; col++)
            {
                int here=start+row*Sectors+col, next=start+row*Sectors+(col+1)%Sectors;
                int previous=start+row*Sectors+(col+Sectors-1)%Sectors;
                int above=here+(row==Rings-1?0:Sectors), below=here-(row==0?0:Sectors);
                Vector3 along = (positions[next]-positions[previous]).normalized;
                Vector3 normal = Vector3.Cross(along, positions[above]-positions[below]).normalized;
                float h = row / (float)(Rings - 1);
                Vector3 shoulderNormal = shoulderNormals[row*Sectors+col];
                if (shoulderNormal.sqrMagnitude > .5f && h < .24f)
                {
                    normal = Vector3.Slerp(shoulderNormal, normal, Smooth(.04f,.24f,h)).normalized;
                    along = Vector3.ProjectOnPlane(along, normal).normalized;
                }
                int sample=here-start;
                if(scalpBlend[sample]>0 && scalpNormals[sample].sqrMagnitude>.5f)
                {
                    normal=Vector3.Slerp(normal,scalpNormals[sample],scalpBlend[sample]).normalized;
                    along=Vector3.ProjectOnPlane(along,normal).normalized;
                }
                normals[here] = normal; tangents[here] = new Vector4(along.x,along.y,along.z,1);
                if(row==Rings-1)continue;
                output.Add(here);output.Add(next);output.Add(above);
                output.Add(next);output.Add(next+Sectors);output.Add(above);
            }
            if (positions.Count < 16000 || output.Count < 90000) throw new InvalidOperationException("Unexpected number of assembled trainee vertices.");
            foreach (Vector3 normal in normals)
                if (!(normal.sqrMagnitude > .99f && normal.sqrMagnitude < 1.01f))
                    throw new InvalidOperationException("Invalid normal on assembled white trainee.");
            var bindposes=mesh.bindposes;
            mesh.Clear();mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetTangents(tangents);
            mesh.SetUVs(0,uv);mesh.bindposes=bindposes;mesh.boneWeights=weights.ToArray();
            mesh.SetTriangles(output,0);mesh.RecalculateBounds();
        }

        static float Smooth(float a,float b,float x) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,x));

        // Taper the flat underside of the chin into the mandibular angle. Carry
        // the original normal and tangent through the local shape deformation.
        static Vector3 LowerHead(Vector3 p)
        {
            float lower = 1-Smooth(1.547f,1.602f,p.y);
            float front = Smooth(.043f,.097f,p.z);
            float jaw = lower * Smooth(.025f,.058f,p.z);
            float occiput = (1 - Smooth(.012f,.05f,p.z)) * Smooth(1.556f,1.581f,p.y)
                          * (1 - Smooth(1.587f,1.619f,p.y));
            return new Vector3(p.x*(1-.065f*jaw-.25f*occiput),
                p.y+lower*(.008f*front+.005f*Smooth(.024f,.056f,p.z)*Smooth(.014f,.059f,Mathf.Abs(p.x)))+.0025f*occiput,
                p.z-.003f*front*lower+(.021f-p.z)*.36f*occiput);
        }

        static void SculptLowerHead(ref Vector3 p, ref Vector3 n, ref Vector4 tangent)
        {
            if(p.y>1.62f || (p.y>1.603f && p.z>=.04f)) return;
            Vector3 a = Vector3.ProjectOnPlane((Vector3)tangent,n).normalized;
            Vector3 b = Vector3.Cross(n,a);
            const float e=.0001f;
            Vector3 da=LowerHead(p+a*e)-LowerHead(p-a*e), db=LowerHead(p+b*e)-LowerHead(p-b*e);
            if(da.sqrMagnitude>1e-12f && db.sqrMagnitude>1e-12f)
            {a=da.normalized;n=Vector3.Cross(a,db.normalized).normalized;tangent=new Vector4(a.x,a.y,a.z,tangent.w);}
            p=LowerHead(p);
        }

        // Sample the existing sloped clavicles in bind space, including their
        // raised rear edge. The bottom row sinks into that surface by 6 mm.
        static float SurfaceHeight(float x,float z,Vector3[] v,List<int> triangles)
        {
            return SurfaceHeight(x,z,v,triangles,null,out _);
        }
        static float SurfaceHeight(float x,float z,Vector3[] v,List<int> triangles,Vector3[] normals,out Vector3 normal)
        {
            normal=Vector3.up;
            float height=1.37f;
            for(int i=0;i<triangles.Count;i+=3)
            {
                Vector3 a=v[triangles[i]], b=v[triangles[i+1]]-a, c=v[triangles[i+2]]-a;
                float area=b.x*c.z-b.z*c.x;
                if(Mathf.Abs(area)<1e-10f) continue;
                float s=((x-a.x)*c.z-(z-a.z)*c.x)/area, t=(b.x*(z-a.z)-b.z*(x-a.x))/area;
                float y=a.y+b.y*s+c.y*t;
                if(s<0 || t<0 || s+t>1 || y<height)continue;
                height=y;
                if(normals!=null)normal=(normals[triangles[i]]*(1-s-t)+normals[triangles[i+1]]*s+normals[triangles[i+2]]*t).normalized;
            }
            return height;
        }

        // Join the nape to the lower skull with matching position, slope and shading.
        // Both meshes use the head bone in the contact band, including while bowing.
        static float BlendNapeIntoSkull(ref float x, float y, ref float z, float front, Vector3[] vertices,
            Vector3[] normals, List<int> skull, out Vector3 scalpNormal, out float scalpMix)
        {
            scalpNormal = Vector3.zero;
            scalpMix = 0;
            float influence = 1 - Smooth(.10f, .80f, front);
            const float startY = 1.556f, centerZ = .011f;
            if (influence <= 0 || y < startY) return 0;

            Vector3 radial = new Vector3(x, 0, z-centerZ);
            float stem = radial.magnitude;
            Vector3 direction = radial / stem;
            float joinY = 1.62f - .006f*front;
            float blend = Mathf.Clamp01((y-startY)/(joinY-startY));
            if (!TrySampleHeadSurface(joinY, centerZ, direction, vertices, normals, skull,
                out float rim, out Vector3 rimNormal)) return 0;

            // A cubic Hermite profile starts vertically at the neck and meets the
            // measured tangent below the occiput. The last rows tuck inside the scalp.
            float slope = Mathf.Clamp(-rimNormal.y / Mathf.Max(.25f, Vector3.Dot(rimNormal,direction)), 0, 1);
            float u = blend*blend*(3-2*blend);
            float radius = Mathf.Lerp(stem,rim,u) + (blend-1)*blend*blend*(joinY-startY)*slope;
            bool touchesHead = TrySampleHeadSurface(y, centerZ, direction, vertices, normals, skull,
                out float surface, out scalpNormal);
            if (y > joinY && touchesHead) radius = surface - .012f*Smooth(joinY,joinY+.021f,y);
            float target = Mathf.Lerp(stem,radius,influence);
            if (touchesHead)
            {
                float contact = 1 - Smooth(.002f,.016f,Mathf.Abs(target-surface));
                scalpMix = Smooth(1.565f,1.591f,y) * contact;
            }
            x = direction.x*target;
            z = centerZ + direction.z*target;
            return Mathf.Max(scalpMix, influence*Smooth(.4f,.96f,blend));
        }

        // The closed head mesh also has inward-facing surfaces under the jaw.
        // Keep the outermost triangle hit on each horizontal ray through the skull.
        static bool TrySampleHeadSurface(float y, float z, Vector3 direction, Vector3[] vertices,
            Vector3[] normals, List<int> triangles, out float radius, out Vector3 normal)
        {
            var origin = new Vector3(0,y,z);
            radius = 0;
            normal = Vector3.zero;
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int ia = triangles[i], ib = triangles[i+1], ic = triangles[i+2];
                Vector3 a = vertices[ia], b = vertices[ib], c = vertices[ic];
                if ((a.y < y && b.y < y && c.y < y) || (a.y > y && b.y > y && c.y > y)) continue;
                Vector3 e1 = b-a, e2 = c-a, cross = Vector3.Cross(direction,e2);
                float det = Vector3.Dot(e1,cross);
                if (Mathf.Abs(det) < 1e-10f) continue;
                Vector3 delta = origin-a;
                float u = Vector3.Dot(delta,cross)/det;
                if (u < 0 || u > 1) continue;
                Vector3 q = Vector3.Cross(delta,e1);
                float v = Vector3.Dot(direction,q)/det;
                if (v < 0 || v+u > 1) continue;
                float distance = Vector3.Dot(e2,q)/det;
                if (distance <= radius) continue;
                radius = distance;
                normal = (normals[ia]*(1-u-v) + normals[ib]*u + normals[ic]*v).normalized;
            }
            return radius > 0;
        }

        static int FindBone(Transform[] bones,string suffix)
        {
            int i=Array.FindIndex(bones,b=>b && (b.name.EndsWith(":"+suffix,StringComparison.Ordinal)||b.name==suffix));
            if(i<0)throw new InvalidOperationException("Missing trainee bone: "+suffix);
            return i;
        }

        // FBX duplicates UV-seam vertices. Join coincident seam positions before
        // finding connected pieces so no CAD detail is selected by a vertex ID.
        static int[] Islands(Vector3[] p,int[] triangles)
        {
            var parent=new int[p.Length];var seams=new Dictionary<Vector3Int,int>();
            for(int i=0;i<p.Length;i++)
            {
                parent[i]=i; var key=Vector3Int.RoundToInt(p[i]*100000f);
                if(seams.TryGetValue(key,out int previous))parent[i]=previous;else seams.Add(key,i);
            }
            for(int i=0;i<triangles.Length;i+=3)
            {
                int root=Root(parent,triangles[i]);parent[Root(parent,triangles[i+1])]=root;parent[Root(parent,triangles[i+2])]=root;
            }
            for(int i=0;i<parent.Length;i++)parent[i]=Root(parent,i);
            return parent;
        }

        static int Root(int[] parent,int i)
        {
            while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}
            return i;
        }
    }
}
