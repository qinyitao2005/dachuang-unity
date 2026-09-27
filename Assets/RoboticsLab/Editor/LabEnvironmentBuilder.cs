#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoboticsLab.Editor
{
    /// <summary>Reusable white industrial room, furniture and safety equipment for Unity 2022.</summary>
    public static class LabEnvironmentBuilder
    {
        const string Generated = "Assets/RoboticsLab/Generated";
        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        static Transform root;
        static Font font;
        static Material labelMaterial;
        static readonly Dictionary<char, CharacterInfo> LabelGlyphs = new Dictionary<char, CharacterInfo>();
        static float labelCapHeight;
        static int labelMeshIndex;

        public static GameObject Build()
        {
            Folder("Assets/RoboticsLab"); Folder(Generated);
            Materials();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            PrepareLabelAtlas();
            GameObject previous = GameObject.Find("RoboticsLab_Environment");
            if (previous != null) Object.DestroyImmediate(previous);
            GameObject environment = new GameObject("RoboticsLab_Environment");
            root = environment.transform;
            Architecture(); Ceiling(); Entry(); HelmetRack(); TeachingWall();
            for (int i = 1; i <= 3; ++i) Station(i, 5.5f * i);
            RearStorage();
            LabOriginalRackBuilder.Replace();
            AssetDatabase.SaveAssets();
            return environment;
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            Folder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        static void Materials()
        {
            Mats.Clear();
            Mat("Wall", new Color(.965f,.975f,.980f), .015f,.23f);
            Mat("White", new Color(.98f,.984f,.987f), .015f,.30f);
            Mat("Floor", new Color(.80f,.844f,.864f), .02f,.28f);
            Mat("FloorAlternate", new Color(.825f,.86f,.875f), .02f,.27f);
            Mat("Grout", new Color(.72f,.765f,.782f), .02f,.18f);
            Mat("Metal", new Color(.47f,.55f,.59f), .72f,.55f);
            Mat("DarkMetal", new Color(.15f,.22f,.27f), .58f,.37f);
            Mat("Graphite", new Color(.055f,.092f,.125f), .22f,.28f);
            Mat("Navy", new Color(.027f,.075f,.115f), .08f,.13f);
            Mat("Blue", new Color(.018f,.36f,.49f), .12f,.32f);
            Mat("BlueLight", new Color(.23f,.59f,.67f), .08f,.30f);
            Mat("PaleBlue", new Color(.63f,.81f,.845f), .02f,.18f);
            Mat("SafetyYellow", new Color(.99f,.72f,.10f), .05f,.36f);
            Mat("Red", new Color(.79f,.065f,.047f), .18f,.37f);
            Mat("Green", new Color(.055f,.47f,.33f), .05f,.26f);
            Mat("Rubber", new Color(.072f,.094f,.11f), .03f,.12f);
            Mat("Paper", new Color(.91f,.935f,.92f), 0,.08f);
            Mat("WindowSky", new Color(.62f,.76f,.81f), 0,.05f, new Color(.36f,.48f,.53f));
            Mat("Light", new Color(.93f,.97f,1), .02f,.30f, new Color(1.2f,1.32f,1.42f));
            // The luminous diffuser must stay white even before any light baking.
            Mats["Light"].shader = Shader.Find("Unlit/Color");
            Mats["Light"].color = new Color(.975f,.99f,1);
            EditorUtility.SetDirty(Mats["Light"]);
            Mat("Indicator", new Color(.18f,.73f,.47f), .05f,.35f, new Color(.10f,.48f,.26f));
            Material glass = Mat("Glass", new Color(.63f,.82f,.89f,.17f), .15f,.86f);
            glass.SetFloat("_Mode", 3);
            glass.SetInt("_SrcBlend", (int)BlendMode.One);
            glass.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            glass.SetInt("_ZWrite", 0);
            glass.DisableKeyword("_ALPHATEST_ON"); glass.DisableKeyword("_ALPHABLEND_ON");
            glass.EnableKeyword("_ALPHAPREMULTIPLY_ON"); glass.renderQueue = 3000;
            EditorUtility.SetDirty(glass);
        }

        static Material Mat(string name, Color color, float metallic, float smoothness, Color? emission = null)
        {
            string path = Generated + "/Lab_" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("Standard");
            m.name = "Lab_" + name; m.color = color;
            m.SetFloat("_Metallic", metallic); m.SetFloat("_Glossiness", smoothness);
            if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); }
            Mats[name] = m; EditorUtility.SetDirty(m); return m;
        }

        static Transform Group(string name, Transform parent, Vector3 pos, Quaternion? rot = null)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(parent, false); t.localPosition = pos; t.localRotation = rot ?? Quaternion.identity;
            return t;
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 scale, string mat, bool solid = true)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name;
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            g.GetComponent<MeshRenderer>().sharedMaterial = Mats[mat];
            if (!solid) Object.DestroyImmediate(g.GetComponent<Collider>());
            g.isStatic = true; return g;
        }

        static GameObject Cyl(string name, Transform parent, Vector3 pos, float radius, float height, string mat)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); g.name = name;
            g.transform.SetParent(parent, false); g.transform.localPosition = pos;
            g.transform.localScale = new Vector3(radius * 2, height * .5f, radius * 2);
            g.GetComponent<MeshRenderer>().sharedMaterial = Mats[mat];
            Object.DestroyImmediate(g.GetComponent<Collider>()); g.isStatic = true; return g;
        }

        static GameObject Orb(string name, Transform parent, Vector3 pos, Vector3 scale, string mat)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere); g.name = name;
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            g.GetComponent<MeshRenderer>().sharedMaterial = Mats[mat];
            Object.DestroyImmediate(g.GetComponent<Collider>()); g.isStatic = true; return g;
        }

        static void Rod(string name, Transform parent, Vector3 a, Vector3 b, float radius, string mat)
        {
            GameObject g = Cyl(name,parent,(a+b)*.5f,radius,Vector3.Distance(a,b),mat);
            g.transform.localRotation = Quaternion.FromToRotation(Vector3.up,b-a);
        }

        static void PrepareLabelAtlas()
        {
            // Bake the small ASCII sign alphabet once. Static glyph meshes and an asset-backed
            // atlas survive reloads/builds without the dynamic font replacing or resizing its atlas.
            string alphabet = "";
            for (int i = 32; i <= 126; ++i) alphabet += (char)i;
            font.RequestCharactersInTexture(alphabet,80,FontStyle.Normal);
            LabelGlyphs.Clear();
            foreach (char c in alphabet)
            {
                CharacterInfo info;
                if (font.GetCharacterInfo(c,out info,80,FontStyle.Normal)) LabelGlyphs[c] = info;
            }
            CharacterInfo capital = LabelGlyphs['H'];
            labelCapHeight = Mathf.Max(1,capital.maxY-capital.minY);
            labelMeshIndex = 0;
            Texture source = font.material.mainTexture;
            RenderTexture copy = RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source,copy);
            RenderTexture.active = copy;
            var pixels = new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,true);
            pixels.ReadPixels(new Rect(0,0,source.width,source.height),0,0);
            pixels.Apply(false,false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(copy);
            const string atlasPath = Generated + "/Lab_LabelAtlas.asset";
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            if (atlas == null)
            {
                atlas = pixels;
                atlas.name = "Lab_LabelAtlas";
                AssetDatabase.CreateAsset(atlas,atlasPath);
            }
            else
            {
                EditorUtility.CopySerialized(pixels,atlas);
                Object.DestroyImmediate(pixels);
            }
            atlas.wrapMode = TextureWrapMode.Clamp;
            atlas.filterMode = FilterMode.Bilinear;
            EditorUtility.SetDirty(atlas);
            const string materialPath = Generated + "/Lab_DepthText.mat";
            labelMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Shader shader = Shader.Find("RoboticsLab/DepthTestedText");
            if (shader == null) throw new System.InvalidOperationException("Import RoboticsLab/Shaders/DepthTestedText.shader before building the room.");
            if (labelMaterial == null)
            {
                labelMaterial = new Material(shader);
                AssetDatabase.CreateAsset(labelMaterial,materialPath);
            }
            labelMaterial.shader = shader;
            labelMaterial.mainTexture = atlas;
            labelMaterial.color = Color.white;
            EditorUtility.SetDirty(labelMaterial);
        }

        static void Label(string name, Transform parent, string text, Vector3 pos, float size, Color color,
            Quaternion? rot = null, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            Transform t = Group(name,parent,pos,rot);
            // size is the actual capital letter height in metres, independent of font pixels.
            float scale = size/labelCapHeight;
            string[] lines = text.Split('\n');
            float lineAdvance = labelCapHeight*1.65f;
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            var colors = new List<Color>();
            float ymin = float.PositiveInfinity,ymax = float.NegativeInfinity;
            for (int line = 0; line < lines.Length; ++line)
            {
                float width = 0;
                foreach (char c in lines[line]) if (LabelGlyphs.ContainsKey(c)) width += LabelGlyphs[c].advance;
                float x = anchor == TextAnchor.MiddleLeft ? 0 : -width*.5f;
                float baseline = -line*lineAdvance;
                foreach (char c in lines[line])
                {
                    CharacterInfo glyph;
                    if (!LabelGlyphs.TryGetValue(c,out glyph)) continue;
                    if (c != ' ')
                    {
                        int v = vertices.Count;
                        vertices.Add(new Vector3(x+glyph.minX,baseline+glyph.minY,0)*scale);
                        vertices.Add(new Vector3(x+glyph.minX,baseline+glyph.maxY,0)*scale);
                        vertices.Add(new Vector3(x+glyph.maxX,baseline+glyph.minY,0)*scale);
                        vertices.Add(new Vector3(x+glyph.maxX,baseline+glyph.maxY,0)*scale);
                        uv.Add(glyph.uvBottomLeft);uv.Add(glyph.uvTopLeft);uv.Add(glyph.uvBottomRight);uv.Add(glyph.uvTopRight);
                        for(int j=0;j<4;++j) colors.Add(color);
                        triangles.Add(v);triangles.Add(v+1);triangles.Add(v+2);
                        triangles.Add(v+2);triangles.Add(v+1);triangles.Add(v+3);
                        ymin=Mathf.Min(ymin,(baseline+glyph.minY)*scale);
                        ymax=Mathf.Max(ymax,(baseline+glyph.maxY)*scale);
                    }
                    x += glyph.advance;
                }
            }
            float centreY = vertices.Count>0 ? (ymin+ymax)*.5f : 0;
            for(int i=0;i<vertices.Count;++i) vertices[i] -= new Vector3(0,centreY,0);
            string path = Generated + "/Lab_Label_" + (labelMeshIndex++).ToString("D3") + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}else mesh.Clear();
            mesh.name=System.IO.Path.GetFileNameWithoutExtension(path);
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.SetColors(colors);
            mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            MeshRenderer renderer=t.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=labelMaterial;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            t.gameObject.isStatic = true;
        }

        static void Architecture()
        {
            Transform p = Group("Architecture",root,Vector3.zero);
            Box("FloorFoundation",p,new Vector3(0,-.14f,11),new Vector3(16.5f,.26f,22.5f),"Grout");
            for(int x=0;x<8;x++) for(int z=0;z<11;z++)
                Box("EpoxyFloorPanel_"+x+"_"+z,p,new Vector3(-7+x*2,-.015f,1+z*2),new Vector3(1.985f,.03f,1.985f),
                    (x+z)%3==0 ? "FloorAlternate":"Floor");
            Box("WestWallBase",p,new Vector3(-8.12f,1.05f,11),new Vector3(.24f,2.1f,22.25f),"Wall");
            Box("WestWallUpper",p,new Vector3(-8.12f,4.3f,11),new Vector3(.24f,1.05f,22.25f),"Wall");
            float[] edges={0,3.8f,7.2f,9.3f,12.7f,14.8f,18.2f,22};
            for(int i=0;i<edges.Length-1;i+=2)
                Box("WestWallPier",p,new Vector3(-8.12f,2.95f,(edges[i]+edges[i+1])*.5f),new Vector3(.24f,1.7f,edges[i+1]-edges[i]),"Wall");
            foreach(float z in new[]{5.5f,11f,16.5f}) Window(p,z);
            Box("EastWall",p,new Vector3(8.12f,2.4f,11),new Vector3(.24f,4.8f,22.25f),"Wall");
            Box("NorthWall",p,new Vector3(0,2.4f,22.12f),new Vector3(16.25f,4.8f,.24f),"Wall");
            foreach(float x in new[]{-4.8f,4.8f})
                Box("EntranceWall",p,new Vector3(x,2.4f,-.12f),new Vector3(6.4f,4.8f,.24f),"Wall");
            Box("EntranceLintel",p,new Vector3(0,4.02f,-.12f),new Vector3(3.2f,1.56f,.24f),"Wall");
            Box("Ceiling",p,new Vector3(0,4.92f,11),new Vector3(16.35f,.24f,22.35f),"White");
            foreach(float x in new[]{-7.985f,7.985f})
                Box("WallSkirting",p,new Vector3(x,.12f,11),new Vector3(.045f,.24f,22),"Metal",false);
            Box("NorthSkirting",p,new Vector3(0,.12f,21.985f),new Vector3(16,.24f,.045f),"Metal",false);
            for(int i=1;i<11;i++)
            {
                Box("EastPanelJoint",p,new Vector3(7.985f,2.45f,i*2),new Vector3(.014f,4.58f,.017f),"Grout",false);
                Box("WestPanelJoint",p,new Vector3(-7.985f,1.1f,i*2),new Vector3(.014f,1.8f,.017f),"Grout",false);
            }
            for(int i=-3;i<=3;i++)
                Box("NorthPanelJoint",p,new Vector3(i*2,2.45f,21.985f),new Vector3(.017f,4.58f,.014f),"Grout",false);
            Box("EastServiceRail",p,new Vector3(7.935f,3.73f,11),new Vector3(.1f,.065f,21.9f),"Metal",false);
            Box("WestServiceRail",p,new Vector3(-7.935f,3.88f,11),new Vector3(.1f,.065f,21.9f),"Metal",false);
            Box("PedestrianGuide",p,new Vector3(.88f,.004f,10.6f),new Vector3(.052f,.006f,18.1f),"Blue",false);
            Box("PedestrianGuideFine",p,new Vector3(1.015f,.004f,10.6f),new Vector3(.014f,.006f,18.1f),"White",false);
            for(int i=0;i<3;i++)
            {
                float z=5.5f+i*5.5f;
                Box("OperatorCellBoundary",p,new Vector3(-2.45f,.006f,z),new Vector3(.055f,.008f,3.5f),"SafetyYellow",false);
                foreach(float dz in new[]{-1.74f,1.74f})
                    Box("CellBoundaryEnd",p,new Vector3(-4.52f,.006f,z+dz),new Vector3(4.18f,.008f,.055f),"SafetyYellow",false);
                Box("CellNumberTile",p,new Vector3(-1.83f,.01f,z),new Vector3(.8f,.01f,.8f),"Blue",false);
                Label("FloorCellNumber",p,"0"+(i+1),new Vector3(-1.83f,.023f,z),.2f,Color.white,Quaternion.Euler(90,90,0));
                Box("ApproachLine",p,new Vector3(-.08f,.005f,z),new Vector3(1.85f,.007f,.035f),"Blue",false);
            }
            for(int i=0;i<4;i++)
            {
                float z=3.1f+i*4.95f;
                Box("ArrowLeft",p,new Vector3(1.7f,.008f,z),new Vector3(.07f,.009f,.38f),"Blue",false).transform.localRotation=Quaternion.Euler(0,45,0);
                Box("ArrowRight",p,new Vector3(1.91f,.008f,z),new Vector3(.07f,.009f,.38f),"Blue",false).transform.localRotation=Quaternion.Euler(0,-45,0);
            }
            Label("FloorStart",p,"START / SAFETY FIRST",new Vector3(3.85f,.018f,1.42f),.115f,Mats["Blue"].color,Quaternion.Euler(90,0,0));
        }

        static void Window(Transform parent,float z)
        {
            Transform p=Group("ObservationWindow_"+z,parent,new Vector3(-8.02f,2.96f,z));
            Box("ExteriorDaylight",p,new Vector3(-.17f,0,0),new Vector3(.025f,1.66f,3.36f),"WindowSky",false);
            Box("Glass",p,Vector3.zero,new Vector3(.025f,1.63f,3.32f),"Glass");
            foreach(float h in new[]{-.85f,.85f}) Box("WindowFrame",p,new Vector3(.018f,h,0),new Vector3(.14f,.075f,3.51f),"Metal",false);
            foreach(float side in new[]{-1.72f,0,1.72f}) Box("Mullion",p,new Vector3(.027f,0,side),new Vector3(.16f,1.71f,.064f),"White",false);
            Box("InteriorSill",p,new Vector3(.15f,-.87f,0),new Vector3(.41f,.075f,3.59f),"White",false);
            Box("ExteriorHorizon",p,new Vector3(-.147f,-.35f,0),new Vector3(.012f,.16f,3.3f),"PaleBlue",false);
            foreach(float dz in new[]{-.95f,-.48f,.55f,1.15f})
                Box("ExteriorStructure",p,new Vector3(-.137f,-.05f,dz),new Vector3(.014f,1.5f,.07f),"PaleBlue",false);
        }

        static void Ceiling()
        {
            Transform p=Group("CeilingServices",root,Vector3.zero);
            for(int x=-6;x<=6;x+=4) Box("CeilingSeam",p,new Vector3(x,4.785f,11),new Vector3(.027f,.025f,22),"Grout",false);
            for(int z=2;z<22;z+=4) Box("CeilingCrossSeam",p,new Vector3(0,4.785f,z),new Vector3(16,.025f,.025f),"Grout",false);
            foreach(float x in new[]{-3.4f,3.4f}) foreach(float z in new[]{3f,8.3f,13.6f,19f})
            {
                Transform f=Group("LinearLight",p,new Vector3(x,4.42f,z));
                Box("SuspendedHousing",f,Vector3.zero,new Vector3(2.72f,.105f,.32f),"White",false);
                Box("Diffuser",f,new Vector3(0,-.058f,0),new Vector3(2.58f,.018f,.245f),"Light",false);
                foreach(float dx in new[]{-1.02f,1.02f}) Cyl("Suspension",f,new Vector3(dx,.205f,0),.009f,.31f,"Metal");
                Light light=Group("RoomFill",f,new Vector3(0,-.22f,0)).gameObject.AddComponent<Light>();
                light.type=LightType.Point; light.color=new Color(.97f,.985f,1); light.intensity=.8f;
                light.range=8.2f; light.shadows=LightShadows.None; light.renderMode=LightRenderMode.ForcePixel;
            }
            foreach(float z in new[]{5.8f,11.4f,17.1f})
            {
                Transform vent=Group("SupplyDiffuser",p,new Vector3(0,4.765f,z));
                Box("VentFrame",vent,Vector3.zero,new Vector3(1.68f,.025f,.72f),"White",false);
                Box("VentRecess",vent,new Vector3(0,-.015f,0),new Vector3(1.53f,.015f,.61f),"DarkMetal",false);
                for(int i=0;i<8;i++) Box("VentSlat",vent,new Vector3(0,-.029f,-.264f+i*.075f),new Vector3(1.49f,.02f,.025f),"Metal",false);
            }
            Light sun=Group("SoftDaylight",root,Vector3.zero,Quaternion.Euler(48,-32,0)).gameObject.AddComponent<Light>();
            sun.type=LightType.Directional; sun.color=new Color(.985f,.992f,1); sun.intensity=.55f;
            sun.shadows=LightShadows.Soft; sun.shadowStrength=.38f; sun.shadowBias=.03f; sun.shadowNormalBias=.25f;
            // Broad, shadow-free bounce simulates the white room's real indirect illumination.
            // This also lights vertical panels which ceiling point lights cannot reach efficiently.
            AddBounce("WestWindowBounce",new Vector3(22,-90,0),.24f,new Color(.95f,.978f,1));
            AddBounce("EastWallBounce",new Vector3(28,90,0),.18f,new Color(1,.985f,.967f));
            AddBounce("CeilingBounce",new Vector3(-75,0,0),.20f,new Color(.97f,.98f,1));
            RenderSettings.sun=sun; RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.70f,.735f,.765f);
            RenderSettings.ambientEquatorColor=new Color(.60f,.635f,.665f);
            RenderSettings.ambientGroundColor=new Color(.46f,.49f,.525f);
            RenderSettings.ambientIntensity=1f; RenderSettings.reflectionIntensity=.45f; RenderSettings.fog=false;
            QualitySettings.pixelLightCount=Mathf.Max(QualitySettings.pixelLightCount,8);
            DynamicGI.UpdateEnvironment();
        }

        static void AddBounce(string name,Vector3 rotation,float intensity,Color color)
        {
            Light fill=Group(name,root,Vector3.zero,Quaternion.Euler(rotation)).gameObject.AddComponent<Light>();
            fill.type=LightType.Directional;fill.intensity=intensity;fill.color=color;
            fill.shadows=LightShadows.None;fill.renderMode=LightRenderMode.ForcePixel;
        }

        static void Entry()
        {
            Transform p=Group("EntryZone",root,Vector3.zero);
            foreach(float x in new[]{-1.6f,1.6f}) Box("DoorJamb",p,new Vector3(x,1.63f,.06f),new Vector3(.115f,3.26f,.25f),"Metal");
            Box("DoorHeader",p,new Vector3(0,3.24f,.06f),new Vector3(3.31f,.14f,.25f),"Metal");
            Box("DoorThreshold",p,new Vector3(0,.014f,.05f),new Vector3(3.08f,.025f,.36f),"Metal");
            foreach(float x in new[]{-2.38f,2.38f})
            {
                Box("OpenDoorLeaf",p,new Vector3(x,1.48f,.085f),new Vector3(1.4f,2.96f,.062f),"Glass",false);
                foreach(float dx in new[]{-.70f,.70f}) Box("DoorLeafFrame",p,new Vector3(x+dx,1.48f,.095f),new Vector3(.045f,2.96f,.09f),"Metal",false);
                Box("DoorFrostedBand",p,new Vector3(x,1.39f,.126f),new Vector3(1.38f,.28f,.012f),"PaleBlue",false);
            }
            Box("WalkOffMat",p,new Vector3(0,.012f,.78f),new Vector3(2.8f,.023f,1.28f),"Rubber",false);
            for(int i=0;i<15;i++) Box("EntryMatGroove",p,new Vector3(-1.26f+.18f*i,.026f,.78f),new Vector3(.014f,.006f,1.13f),"Metal",false);
            Box("ExitSign",p,new Vector3(0,3.58f,.045f),new Vector3(1.14f,.34f,.095f),"Green",false);
            Label("ExitText",p,"EXIT  >",new Vector3(0,3.58f,.1f),.11f,Color.white,Quaternion.Euler(0,180,0));
            Label("EntryTitle",p,"ROBOTICS / TRAINING LAB",new Vector3(-4.8f,2.77f,.032f),.18f,Mats["Blue"].color,Quaternion.Euler(0,180,0));
            Label("EntrySubtitle",p,"LEARN - PRACTICE - CREATE",new Vector3(-4.8f,2.38f,.032f),.087f,Mats["DarkMetal"].color,Quaternion.Euler(0,180,0));
            Transform sign=Group("SafetyInstructions",p,new Vector3(7.91f,2.15f,5.75f),Quaternion.Euler(0,90,0));
            Box("Backing",sign,Vector3.zero,new Vector3(1.08f,1.24f,.055f),"White",false);
            Box("Header",sign,new Vector3(0,.45f,-.034f),new Vector3(.99f,.25f,.014f),"Blue",false);
            Label("Title",sign,"SAFETY FIRST",new Vector3(0,.45f,-.047f),.064f,Color.white);
            Label("Rules",sign,"01  WEAR YOUR HELMET\n\n02  COMPLETE THE BRIEFING\n\n03  CHECK THE WORK AREA\n\n04  KNOW THE EMERGENCY STOP",new Vector3(-.45f,-.10f,-.046f),.034f,Mats["DarkMetal"].color,null,TextAnchor.MiddleLeft);
            Box("BottomAccent",sign,new Vector3(0,-.52f,-.032f),new Vector3(.99f,.035f,.017f),"SafetyYellow",false);
            Transform fire=Group("FireExtinguisher",p,new Vector3(7.63f,.97f,6.7f));
            Cyl("RedCylinder",fire,Vector3.zero,.135f,.51f,"Red");
            Orb("Shoulder",fire,new Vector3(0,.24f,0),new Vector3(.266f,.14f,.266f),"Red");
            Cyl("Valve",fire,new Vector3(0,.355f,0),.03f,.12f,"Metal");
            Box("Lever",fire,new Vector3(0,.405f,.035f),new Vector3(.048f,.028f,.22f),"Graphite",false);
            Box("Mount",fire,new Vector3(.21f,-.03f,0),new Vector3(.14f,.29f,.3f),"Metal",false);
            Rod("HoseUpper",fire,new Vector3(0,.34f,.08f),new Vector3(-.11f,.22f,.2f),.018f,"Rubber");
            Rod("HoseLower",fire,new Vector3(-.11f,.22f,.2f),new Vector3(-.13f,-.16f,.18f),.021f,"Rubber");
            Box("Label",fire,new Vector3(-.135f,.02f,0),new Vector3(.007f,.18f,.14f),"Paper",false);
            Transform fs=Group("FirePointSign",p,new Vector3(7.86f,1.99f,6.7f),Quaternion.Euler(0,90,0));
            Box("Sign",fs,Vector3.zero,new Vector3(.57f,.48f,.036f),"Red",false);
            Label("Label",fs,"FIRE\nPOINT",new Vector3(0,0,-.025f),.066f,Color.white);
            Transform stop=Group("EmergencyStopPanel",p,new Vector3(7.81f,1.37f,7.48f),Quaternion.Euler(0,90,0));
            Box("Panel",stop,Vector3.zero,new Vector3(.33f,.46f,.1f),"White",false);
            Box("YellowFace",stop,new Vector3(0,.035f,-.061f),new Vector3(.26f,.24f,.018f),"SafetyYellow",false);
            Cyl("EmergencyStopButton",stop,new Vector3(0,.04f,-.096f),.073f,.07f,"Red").transform.localRotation=Quaternion.Euler(90,0,0);
            Label("EmergencyLabel",stop,"E-STOP",new Vector3(0,-.16f,-.058f),.032f,Mats["Graphite"].color);
        }

        static void HelmetRack()
        {
            Transform p=Group("SafetyHelmetRack",root,new Vector3(6,0,3.2f));
            Box("RackPlinth",p,new Vector3(0,.08f,0),new Vector3(1.38f,.16f,2.23f),"DarkMetal");
            Box("RackBack",p,new Vector3(.60f,1.19f,0),new Vector3(.075f,2.20f,2.18f),"White");
            foreach(float x in new[]{-.59f,.59f}) foreach(float z in new[]{-1.035f,1.035f})
                Box("Upright",p,new Vector3(x,1.18f,z),new Vector3(.064f,2.22f,.064f),"Metal");
            foreach(float h in new[]{.25f,1.12f,1.83f})
            {
                Box("Shelf",p,new Vector3(0,h,0),new Vector3(1.28f,.065f,2.19f),"White");
                Box("FrontTrim",p,new Vector3(-.647f,h,0),new Vector3(.016f,.07f,2.19f),"Blue",false);
            }
            Box("RackHeader",p,new Vector3(-.60f,2.16f,0),new Vector3(.06f,.32f,2.19f),"Blue",false);
            Label("RackTitle",p,"01 / PERSONAL PROTECTION",new Vector3(-.64f,2.16f,0),.078f,Color.white,Quaternion.Euler(0,90,0));
            Helmet("HelmetPickup",p,new Vector3(-.22f,1.155f,-.59f),"SafetyYellow",true);
            Helmet("SpareHelmetYellow",p,new Vector3(-.20f,1.155f,.52f),"SafetyYellow",false);
            Helmet("SpareHelmetWhite",p,new Vector3(-.17f,1.865f,-.52f),"White",false);
            Helmet("SpareHelmetBlue",p,new Vector3(-.17f,1.865f,.55f),"Blue",false);
            foreach(float z in new[]{-.68f,.25f,.75f})
            {
                Box("PPEStorageBox",p,new Vector3(.02f,.44f,z),new Vector3(.93f,.30f,.39f),"PaleBlue");
                Box("BoxGrip",p,new Vector3(-.45f,.46f,z),new Vector3(.018f,.047f,.15f),"DarkMetal",false);
            }
            Label("PickupLabel",p,"HELMETS / COLLECT BEFORE ENTRY",new Vector3(-.667f,.99f,0),.055f,Mats["DarkMetal"].color,Quaternion.Euler(0,90,0));
        }

        static void Helmet(string name,Transform parent,Vector3 pos,string mat,bool pickup)
        {
            Transform p=Group(name,parent,pos,Quaternion.Euler(0,90,0));
            p.gameObject.AddComponent<MeshFilter>().sharedMesh=HelmetMesh();
            p.gameObject.AddComponent<MeshRenderer>().sharedMaterial=Mats[mat];
            Orb("Brim",p,new Vector3(0,.048f,-.025f),new Vector3(.53f,.036f,.66f),mat);
            Orb("CrownRidge",p,new Vector3(0,.225f,0),new Vector3(.053f,.066f,.4f),mat);
            Box("AdjustmentBand",p,new Vector3(0,.049f,.22f),new Vector3(.24f,.047f,.035f),"DarkMetal",false);
            foreach(float x in new[]{-.209f,.209f}) for(int i=0;i<3;i++)
                Box("Vent",p,new Vector3(x,.106f,-.09f+.07f*i),new Vector3(.005f,.013f,.04f),"Graphite",false);
            if(pickup)
            {
                BoxCollider c=p.gameObject.AddComponent<BoxCollider>(); c.center=new Vector3(0,.15f,-.02f); c.size=new Vector3(.55f,.32f,.65f);
                foreach(Transform child in p.GetComponentsInChildren<Transform>()) child.gameObject.isStatic=false;
            }
        }

        static Mesh HelmetMesh()
        {
            const string path=Generated+"/Lab_HardHatShell.asset";
            Mesh mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path); if(mesh!=null) return mesh;
            const int rings=14,slices=40;
            var v=new List<Vector3>(); var uv=new List<Vector2>(); var tr=new List<int>();
            for(int r=0;r<=rings;r++)
            {
                float theta=r/(float)rings*Mathf.PI*.5f;
                for(int s=0;s<=slices;s++)
                {
                    float phi=s/(float)slices*Mathf.PI*2;
                    v.Add(new Vector3(Mathf.Cos(phi)*.232f*Mathf.Sin(theta),.05f+.235f*Mathf.Cos(theta),Mathf.Sin(phi)*.269f*Mathf.Sin(theta)));
                    uv.Add(new Vector2(s/(float)slices,r/(float)rings));
                    if(r<rings&&s<slices)
                    {
                        int a=r*(slices+1)+s,b=a+slices+1;
                        tr.Add(a);tr.Add(a+1);tr.Add(b);tr.Add(a+1);tr.Add(b+1);tr.Add(b);
                    }
                }
            }
            mesh=new Mesh{name="Lab_HardHatShell"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tr,0);
            mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;
        }

        static void TeachingWall()
        {
            Transform p=Group("TeachingDisplayFrame",root,new Vector3(7.72f,2.60f,10.5f),Quaternion.Euler(0,90,0));
            Box("DisplayBackplate",p,new Vector3(0,0,.06f),new Vector3(4.78f,2.81f,.13f),"DarkMetal");
            Box("DisplayBezel",p,Vector3.zero,new Vector3(4.64f,2.66f,.10f),"Graphite",false);
            Transform screen=Group("TeachingDisplay",p,new Vector3(0,.005f,-.058f));
            screen.localScale=new Vector3(4.46f,2.51f,1);
            screen.gameObject.AddComponent<MeshFilter>().sharedMesh=DisplayMesh();
            screen.gameObject.AddComponent<MeshRenderer>().sharedMaterial=Mats["Navy"];
            Transform g=Group("TeachingDisplayGraphics",p,new Vector3(0,0,-.073f));
            Box("SlideAccent",g,new Vector3(-1.98f,.82f,-.003f),new Vector3(.035f,.58f,.008f),"BlueLight",false);
            Label("Eyebrow",g,"ROBOTICS ACADEMY / LESSON 01",new Vector3(-1.84f,1.005f,-.012f),.053f,Mats["BlueLight"].color,null,TextAnchor.MiddleLeft);
            Label("Title",g,"MEET YOUR\nROBOTIC ARM",new Vector3(-1.84f,.57f,-.014f),.18f,Color.white,null,TextAnchor.MiddleLeft);
            Label("Summary",g,"SAFETY / JOINTS / MOTION\n\nComplete the briefing before practice.",new Vector3(-1.84f,-.20f,-.014f),.062f,Mats["PaleBlue"].color,null,TextAnchor.MiddleLeft);
            Box("StartPill",g,new Vector3(-1.21f,-.91f,-.014f),new Vector3(1.27f,.27f,.015f),"Blue",false);
            Label("StartLabel",g,"TRAINING VIDEO",new Vector3(-1.21f,-.91f,-.031f),.065f,Color.white);
            Label("LessonDuration",g,"INTERACTIVE LESSON",new Vector3(.79f,-1.01f,-.026f),.049f,Mats["PaleBlue"].color);
            Vector3 a=new Vector3(.75f,-.7f,-.035f),b=new Vector3(.75f,-.18f,-.035f),c=new Vector3(1.39f,.48f,-.035f),d=new Vector3(.9f,.92f,-.035f);
            Rod("DiagramBase",g,new Vector3(.29f,-.76f,-.035f),new Vector3(1.2f,-.76f,-.035f),.045f,"PaleBlue");
            Rod("DiagramColumn",g,a,b,.073f,"BlueLight");Rod("DiagramUpperArm",g,b,c,.092f,"White");Rod("DiagramForearm",g,c,d,.074f,"White");
            foreach(Vector3 pt in new[]{a,b,c,d})
            {
                Orb("DiagramJoint",g,pt+new Vector3(0,0,-.06f),new Vector3(.21f,.21f,.035f),"BlueLight");
                Orb("DiagramJointCentre",g,pt+new Vector3(0,0,-.083f),new Vector3(.071f,.071f,.019f),"Navy");
            }
            Rod("DiagramGripper",g,d,new Vector3(.63f,.88f,-.035f),.045f,"BlueLight");
            Rod("DiagramFingerA",g,new Vector3(.63f,.88f,-.035f),new Vector3(.49f,1.01f,-.035f),.025f,"PaleBlue");
            Rod("DiagramFingerB",g,new Vector3(.63f,.88f,-.035f),new Vector3(.48f,.79f,-.035f),.025f,"PaleBlue");
            for(int i=0;i<6;i++) Box("DiagramGrid",g,new Vector3(1.02f,-.66f+i*.31f,.014f),new Vector3(1.66f,.009f,.005f),"DarkMetal",false);
            Label("TeachingAreaHeading",root,"02 / LEARN & CHECK",new Vector3(7.865f,4.09f,10.5f),.16f,Mats["Blue"].color,Quaternion.Euler(0,90,0));
            Transform console=Group("TeachingMediaConsole",root,new Vector3(7.33f,.59f,10.5f));
            Box("ConsoleBody",console,Vector3.zero,new Vector3(.9f,.93f,3.9f),"White");
            Box("ConsoleTop",console,new Vector3(0,.495f,0),new Vector3(1.02f,.065f,4.05f),"White");
            Box("ToeKick",console,new Vector3(.06f,-.51f,0),new Vector3(.7f,.13f,3.75f),"DarkMetal");
            foreach(float z in new[]{-1.25f,0,1.25f})
            {
                Box("DoorSeam",console,new Vector3(-.456f,0,z-.60f),new Vector3(.006f,.84f,.015f),"Grout",false);
                Box("Handle",console,new Vector3(-.474f,.28f,z),new Vector3(.045f,.034f,.26f),"Metal",false);
            }
            Label("ConsoleLabel",root,"LEARNING RESOURCES",new Vector3(6.847f,.74f,10.5f),.057f,Mats["Blue"].color,Quaternion.Euler(0,90,0));
            Box("PowerLED",p,new Vector3(1.99f,-1.31f,-.058f),new Vector3(.028f,.012f,.009f),"Indicator",false);
        }

        static Mesh DisplayMesh()
        {
            const string path=Generated+"/Lab_VideoScreen.asset";
            Mesh mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh!=null)return mesh;
            mesh=new Mesh{name="Lab_VideoScreen"};
            mesh.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(-.5f,.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0)};
            mesh.uv=new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,0),new Vector2(1,1)};
            mesh.triangles=new[]{0,1,2,2,1,3};mesh.RecalculateNormals();mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }

        static void Station(int number,float z)
        {
            Transform p=Group("Station0"+number,root,new Vector3(-4.5f,0,z));
            // The original complete training platform supplies its own cabinet, frame and guards.
            // Only room-side utilities belong to this environment builder.
            Box("WallServiceOutlet",p,new Vector3(-3.43f,1.18f,1.2f),new Vector3(.09f,.28f,.37f),"White",false);
            foreach(float dz in new[]{1.11f,1.29f})Cyl("PowerSocket",p,new Vector3(-3.372f,1.18f,dz),.054f,.019f,"Graphite").transform.localRotation=Quaternion.Euler(0,0,90);
            Rod("CableConduit",p,new Vector3(-3.43f,.3f,1.2f),new Vector3(-3.43f,1.03f,1.2f),.027f,"Metal");
        }

        static void RearStorage()
        {
            Transform p=Group("RearStorage",root,Vector3.zero);
            for(int i=0;i<3;i++)
            {
                Transform c=Group("ToolCabinet_"+i,p,new Vector3(-5.5f+1.36f*i,0,20.95f));
                Box("CabinetBody",c,new Vector3(0,1.03f,0),new Vector3(1.24f,1.94f,.75f),"White");
                Box("Plinth",c,new Vector3(0,.09f,0),new Vector3(1.12f,.18f,.66f),"DarkMetal");
                Box("Top",c,new Vector3(0,2.04f,0),new Vector3(1.29f,.07f,.80f),"White");
                foreach(float dx in new[]{-.305f,.305f})
                {
                    Box("Door",c,new Vector3(dx,1.07f,-.388f),new Vector3(.59f,1.78f,.035f),"White",false);
                    Box("Handle",c,new Vector3(dx>0?.081f:-.081f,1.06f,-.433f),new Vector3(.027f,.25f,.046f),"Metal",false);
                }
                Box("DoorJoint",c,new Vector3(0,1.05f,-.41f),new Vector3(.014f,1.79f,.006f),"Grout",false);
                Label("Label",c,new[]{"TOOLS","FIXTURES","CONSUMABLES"}[i],new Vector3(0,1.75f,-.414f),.047f,Mats["Blue"].color);
                for(int s=0;s<4;s++)Box("Vent",c,new Vector3(0,.32f+s*.045f,-.41f),new Vector3(.63f,.013f,.007f),"Grout",false);
            }
            Label("RearWallHeading",p,"PRECISION THROUGH PRACTICE",new Vector3(-2.72f,3.49f,21.969f),.28f,Mats["Blue"].color);
            Label("RearWallSubtitle",p,"ROBOTICS TRAINING CENTRE / SAFE MOTION. SMART THINKING.",new Vector3(-2.73f,3.05f,21.967f),.091f,Mats["DarkMetal"].color);
            Box("TitleUnderline",p,new Vector3(-2.75f,2.78f,21.965f),new Vector3(6.75f,.035f,.015f),"BlueLight",false);
            Transform desk=Group("InstructorDesk",root,new Vector3(5.6f,0,19.7f));
            Box("DeskTop",desk,new Vector3(0,.77f,0),new Vector3(3,.075f,1.15f),"White");
            foreach(float x in new[]{-1.3f,1.3f})Box("DeskLeg",desk,new Vector3(x,.38f,0),new Vector3(.08f,.72f,.95f),"White");
            Box("ModestyPanel",desk,new Vector3(0,.45f,.38f),new Vector3(2.58f,.43f,.04f),"White",false);
            Cyl("MonitorStand",desk,new Vector3(.37f,.955f,.25f),.032f,.32f,"Metal");
            Box("MonitorFoot",desk,new Vector3(.37f,.824f,.19f),new Vector3(.44f,.028f,.27f),"DarkMetal",false);
            Box("Monitor",desk,new Vector3(.37f,1.205f,.24f),new Vector3(.97f,.57f,.052f),"Graphite",false);
            Box("MonitorGlass",desk,new Vector3(.37f,1.205f,.21f),new Vector3(.91f,.505f,.01f),"Navy",false);
            Label("MonitorUI",desk,"LAB CONTROL\n\nALL SYSTEMS READY",new Vector3(.37f,1.21f,.20f),.043f,Mats["PaleBlue"].color);
            Box("Keyboard",desk,new Vector3(.31f,.832f,-.23f),new Vector3(.61f,.025f,.20f),"DarkMetal",false);
            for(int i=0;i<10;i++)Box("Keyline",desk,new Vector3(.055f+i*.055f,.847f,-.23f),new Vector3(.031f,.006f,.16f),"Metal",false);
            Orb("Mouse",desk,new Vector3(.83f,.85f,-.21f),new Vector3(.09f,.048f,.15f),"DarkMetal");
            Box("Manual",desk,new Vector3(-.93f,.826f,-.07f),new Vector3(.33f,.021f,.45f),"Blue",false);
            Box("ManualPaper",desk,new Vector3(-.93f,.84f,-.07f),new Vector3(.29f,.009f,.41f),"Paper",false);
            Label("ManualTitle",desk,"ROBOT\nLAB",new Vector3(-.93f,.847f,-.06f),.033f,Mats["Blue"].color,Quaternion.Euler(90,0,0));
            Transform stool=Group("InstructorStool",desk,new Vector3(.26f,0,-1.05f));
            Cyl("Seat",stool,new Vector3(0,.5f,0),.26f,.085f,"DarkMetal");
            Cyl("Stem",stool,new Vector3(0,.27f,0),.035f,.45f,"Metal");
            for(int i=0;i<5;i++)
            {
                float angle=Mathf.PI*2*i/5;Vector3 end=new Vector3(Mathf.Sin(angle)*.31f,.08f,Mathf.Cos(angle)*.31f);
                Rod("BaseArm",stool,new Vector3(0,.12f,0),end,.021f,"Metal");
                Orb("Caster",stool,end+new Vector3(0,-.035f,0),new Vector3(.065f,.07f,.065f),"Graphite");
            }
            Label("InstructorLabel",root,"INSTRUCTOR / SUPPORT",new Vector3(7.965f,2.75f,19.7f),.115f,Mats["Blue"].color,Quaternion.Euler(0,90,0));
            Transform clock=Group("WallClock",root,new Vector3(3.9f,3.5f,21.96f));
            Cyl("Rim",clock,Vector3.zero,.28f,.054f,"Metal").transform.localRotation=Quaternion.Euler(90,0,0);
            Cyl("Face",clock,new Vector3(0,0,-.035f),.25f,.012f,"White").transform.localRotation=Quaternion.Euler(90,0,0);
            Rod("MinuteHand",clock,new Vector3(0,0,-.047f),new Vector3(0,.19f,-.047f),.008f,"Graphite");
            Rod("HourHand",clock,new Vector3(0,0,-.05f),new Vector3(.11f,-.067f,-.05f),.012f,"Graphite");
            for(int i=0;i<12;i++)
            {float a=i*Mathf.PI/6;Box("ClockIndex",clock,new Vector3(Mathf.Sin(a)*.213f,Mathf.Cos(a)*.213f,-.046f),new Vector3(.017f,.017f,.007f),"DarkMetal",false);}
            Transform aid=Group("FirstAidCabinet",root,new Vector3(7.77f,1.76f,16.2f),Quaternion.Euler(0,90,0));
            Box("Cabinet",aid,Vector3.zero,new Vector3(.62f,.72f,.25f),"White");
            Box("FirstAidSign",aid,new Vector3(0,.06f,-.136f),new Vector3(.39f,.39f,.01f),"Green",false);
            Box("CrossHorizontal",aid,new Vector3(0,.06f,-.145f),new Vector3(.24f,.07f,.014f),"White",false);
            Box("CrossVertical",aid,new Vector3(0,.06f,-.146f),new Vector3(.07f,.24f,.014f),"White",false);
            Label("FirstAidLabel",aid,"FIRST AID",new Vector3(0,-.24f,-.141f),.044f,Mats["DarkMetal"].color);
            foreach(float z in new[]{17.55f,18.18f})
            {
                Transform bin=Group("WasteBin",root,new Vector3(7.46f,0,z));
                Box("BinBody",bin,new Vector3(0,.35f,0),new Vector3(.5f,.65f,.5f),"White");
                Box("Lid",bin,new Vector3(0,.697f,0),new Vector3(.53f,.052f,.53f),z<18?"Blue":"DarkMetal",false);
                Box("Opening",bin,new Vector3(-.055f,.727f,0),new Vector3(.25f,.009f,.30f),"Graphite",false);
            }
        }
    }
}
#endif
