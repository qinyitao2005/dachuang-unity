using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RoboticsLab.Editor
{
    /// <summary>
    /// Finishes the existing teaching room in place. Never rebuilds equipment, changes a
    /// collider, moves an existing transform, or touches robot/controller components.
    /// Generated geometry and materials belong exclusively to the InteriorRefinish layer.
    /// </summary>
    public static class LabInteriorRefinishBuilder
    {
        public const string RootName = "InteriorRefinish";
        const string AssetsRoot = "Assets/RoboticsLab/Generated/InteriorRefinish";
        const string FontPath = "Assets/RoboticsLab/Fonts/NotoSansSC-Regular.ttf";
        const int FontSize = 80;
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Batch> batches = new Dictionary<string, Batch>();
        static readonly List<Sign> signs = new List<Sign>();
        static readonly Dictionary<char, CharacterInfo> glyphs = new Dictionary<char, CharacterInfo>();
        static Mesh cube, cylinder, quad, leaf, planter;
        static Material textMaterial;
        static float glyphHeight;
        static int changedRenderers;

        sealed class Batch
        {
            public string group;
            public Material material;
            public bool shadows;
            public readonly List<CombineInstance> parts = new List<CombineInstance>();
        }
        sealed class Sign
        {
            public string name, text;
            public Vector3 position;
            public Quaternion rotation;
            public float height;
            public Color color;
        }

        [MenuItem("Robot Lab/Refinish Existing Training Room")]
        public static void Run() => ApplyToOpenScene();

        public static void ApplyToOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != LabSceneBuilder.ScenePath)
                throw new InvalidOperationException("Open the existing RoboticsTrainingRoom scene outside Play Mode first.");
            GameObject environment = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "RoboticsLab_Environment");
            if (!environment) throw new InvalidOperationException("The existing room is required. This tool never builds a replacement scene.");
            int oldColliders = scene.GetRootGameObjects().Sum(g => g.GetComponentsInChildren<Collider>(true).Length);
            int oldLights = scene.GetRootGameObjects().Sum(g => g.GetComponentsInChildren<Light>(true).Length);
            Folder(AssetsRoot);
            materials.Clear(); batches.Clear(); signs.Clear(); glyphs.Clear(); changedRenderers = 0;
            PrepareMeshes(); PrepareMaterials();

            // A repeat application replaces only this tool's own additive detail layer.
            foreach (GameObject previous in scene.GetRootGameObjects().Where(g => g.name == RootName))
                Object.DestroyImmediate(previous);
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            RefinishOriginalSurfaces(environment.transform, scene);
            FloorFinishes(); WallFinishes(); CeilingFinishes(); LearningFeatureWall();
            EntryAndRearFeature(); StationSigns(); FurnitureFinishes(); Plants();
            BuildBatches(root.transform);
            BuildSigns(root.transform);
            TuneExistingLighting(environment.transform);
            if (root.GetComponentsInChildren<Collider>(true).Length != 0 || root.GetComponentsInChildren<Light>(true).Length != 0)
                throw new InvalidOperationException("Refinish geometry must not introduce colliders or realtime lights.");
            int colliders = scene.GetRootGameObjects().Sum(g => g.GetComponentsInChildren<Collider>(true).Length);
            int lights = scene.GetRootGameObjects().Sum(g => g.GetComponentsInChildren<Light>(true).Length);
            if (oldColliders != colliders || oldLights != lights)
                throw new InvalidOperationException("Existing collider/light counts changed unexpectedly.");
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Documentation");
            string report = "Interior refinish / " + DateTime.UtcNow.ToString("O") + "\n"
                + "Scene=" + scene.path + "\nRoot=" + RootName + "\n"
                + "Combined geometry renderers=" + batches.Count + "; geometric parts=" + batches.Values.Sum(b => b.parts.Count) + "\n"
                + "Static label renderers=" + signs.Count + "; original room renderers adjusted=" + changedRenderers + "\n"
                + "Existing colliders preserved=" + colliders + "; existing lights=" + lights + "; added lights=0; added colliders=0\n"
                + "Station labels: 01 循迹 / 02 套圈 / 03 画图. Existing RoomPolish/WorkcellSigns hidden.\n"
                + "Equipment, joint transforms, robot scripts, helmets, wall learning UI and original CAD rack are preserved.\n"
                + "White wall panels; seamless warm mineral floor; workcell pads and clear aisle; acoustic ceiling rafts; wood/graphite learning wall; perimeter plants.\n"
                + "Lighting: existing directional daylight, soft contact shadows, lower indirect fill. No new packages or realtime lights.\n"
                + "Scene marked dirty, not saved by this authoring tool.\n";
            File.WriteAllText("Documentation/InteriorRefinishAuthoring.txt", report, new UTF8Encoding(false));
            Debug.Log("ROBOT_INTERIOR_REFINISH_READY batches=" + batches.Count + " parts=" + batches.Values.Sum(b => b.parts.Count)
                + " signs=" + signs.Count + " addedLights=0 addedColliders=0");
        }

        static void RefinishOriginalSurfaces(Transform environment, Scene scene)
        {
            Transform architecture = environment.Find("Architecture");
            if (!architecture) throw new InvalidOperationException("Existing Architecture group is required.");
            var hidden = new HashSet<string> { "WallSkirting", "NorthSkirting", "EastPanelJoint", "WestPanelJoint", "NorthPanelJoint",
                "EastServiceRail", "WestServiceRail", "PedestrianGuide", "PedestrianGuideFine", "ArrowLeft", "ArrowRight", "ApproachLine", "FloorStart" };
            foreach (Renderer renderer in architecture.GetComponentsInChildren<Renderer>(true))
            {
                string name = renderer.name;
                if (name.StartsWith("EpoxyFloorPanel_", StringComparison.Ordinal) || hidden.Contains(name))
                { renderer.enabled = false; changedRenderers++; }
                // The closed original shell blocks all directional daylight. Treat the shell
                // as an architectural cutaway for lighting, while retaining its real colliders.
                if (name.Contains("Wall") || name == "Ceiling" || name == "EntranceLintel")
                {
                    renderer.sharedMaterial = M(name == "Ceiling" ? "CeilingWhite" : "WallWhite");
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = true; changedRenderers++;
                }
            }
            Transform ceiling = environment.Find("CeilingServices");
            if (ceiling)
                foreach (Renderer r in ceiling.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name == "CeilingSeam" || r.name == "CeilingCrossSeam") { r.enabled = false; changedRenderers++; }
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
            foreach (Transform t in environment.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "TeachingAreaHeading" || t.name == "RearWallHeading" || t.name == "RearWallSubtitle" || t.name == "TitleUnderline")
                    foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true)) { r.enabled = false; changedRenderers++; }
            }
            var polish = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "RoomPolish");
            if (polish)
                foreach (string category in new[] { "WorkcellSigns", "WallFinishes", "CeilingDetails" })
                {
                    Transform old = polish.transform.Find(category);
                    if (old) foreach (Renderer r in old.GetComponentsInChildren<Renderer>(true)) { r.enabled = false; changedRenderers++; }
                }
        }

        static void FloorFinishes()
        {
            // A 1 mm visual surface sits above the existing floor; collision stays unchanged.
            Box("Floor", new Vector3(0, -.002f, 11), new Vector3(15.98f, .006f, 21.98f), "MineralFloor");
            Box("Floor", new Vector3(1.30f, .002f, 11), new Vector3(3.50f, .002f, 21.9f), "AisleIvory");
            foreach (float x in new[] { -.46f, 3.06f })
                Box("Floor", new Vector3(x, .0038f, 11), new Vector3(.025f, .0015f, 21.8f), "Teal");
            // Learning zone is a calm, slightly warmer field. Original cabinet bases stay visible.
            Box("Floor", new Vector3(5.69f, .002f, 10.75f), new Vector3(4.20f, .002f, 8.65f), "LearningFloor");
            for (int i = 0; i < 3; i++)
            {
                float z = 5.5f + i * 5.5f;
                Box("Floor", new Vector3(-4.53f, .002f, z), new Vector3(4.09f, .002f, 3.43f), "WorkcellFloor");
                Box("Floor", new Vector3(-1.51f, .0045f, z), new Vector3(1.98f, .0015f, .025f), "Teal");
                // Baked soft contact darkening grounds the cabinet even on lower shadow settings.
                Contact(new Vector3(-4.5f, .0048f, z), new Vector2(2.25f, 2.00f));
            }
            foreach (float z in new[] { 2.4f, 8.2f, 13.7f, 19.2f })
            {
                Box("Wayfinding", new Vector3(1.16f, .0055f, z), new Vector3(.035f, .001f, .32f), "Teal", Quaternion.Euler(0, 45, 0));
                Box("Wayfinding", new Vector3(1.37f, .0055f, z), new Vector3(.035f, .001f, .32f), "Teal", Quaternion.Euler(0, -45, 0));
            }
            Contact(new Vector3(7.34f, .0048f, 10.5f), new Vector2(1.48f, 4.5f));
        }

        static void WallFinishes()
        {
            // Fine dark skirting and reveals establish scale without recreating large grey grids.
            foreach (float x in new[] { -7.956f, 7.956f })
            {
                Box("WallTrim", new Vector3(x, .075f, 11), new Vector3(.055f, .15f, 21.98f), "Graphite");
                Box("WallTrim", new Vector3(x, 4.66f, 11), new Vector3(.06f, .022f, 21.98f), "Reveal");
                Box("WallPanels", new Vector3(x, .68f, 11), new Vector3(.045f, 1.02f, 21.90f), "LowerPanel");
                Box("WallTrim", new Vector3(x < 0 ? -7.918f : 7.918f, 1.205f, 11), new Vector3(.022f, .018f, 21.88f), "Reveal");
            }
            Box("WallTrim", new Vector3(0, .075f, 21.956f), new Vector3(15.94f, .15f, .055f), "Graphite");
            for (int i = 1; i < 10; i++)
            {
                float z = i * 2.2f;
                Box("WallPanels", new Vector3(-7.925f, .68f, z), new Vector3(.009f, 1.01f, .008f), "PanelSeam");
                Box("WallPanels", new Vector3(7.925f, .68f, z), new Vector3(.009f, 1.01f, .008f), "PanelSeam");
                if (z < 7 || z > 14)
                    Box("WallPanels", new Vector3(7.979f, 2.91f, z), new Vector3(.008f, 3.33f, .008f), "PanelSeam");
            }
            // Window reveals read as finished white joinery, with a continuous wood sill.
            foreach (float z in new[] { 5.5f, 11f, 16.5f })
            {
                Box("WindowJoinery", new Vector3(-7.80f, 2.075f, z), new Vector3(.44f, .045f, 3.62f), "Oak");
                Box("WindowJoinery", new Vector3(-7.935f, 3.87f, z), new Vector3(.13f, .075f, 3.60f), "CeilingWhite");
                foreach (float end in new[] { -1.78f, 1.78f })
                    Box("WindowJoinery", new Vector3(-7.947f, 2.98f, z + end), new Vector3(.10f, 1.76f, .06f), "CeilingWhite");
            }
        }

        static void CeilingFinishes()
        {
            // Smooth plaster perimeter with a warm recessed light line, not an exposed slab.
            foreach (float x in new[] { -7.29f, 7.29f })
            {
                Box("Ceiling", new Vector3(x, 4.65f, 11), new Vector3(1.32f, .25f, 21.98f), "CeilingWhite");
                Box("Ceiling", new Vector3(x < 0 ? -6.61f : 6.61f, 4.662f, 11), new Vector3(.038f, .038f, 21.65f), "WarmLight");
            }
            foreach (float z in new[] { .42f, 21.58f })
                Box("Ceiling", new Vector3(0, 4.66f, z), new Vector3(13.20f, .22f, .84f), "CeilingWhite");
            foreach (float z in new[] { 5.5f, 11f, 16.5f })
            {
                Box("AcousticRafts", new Vector3(-3.82f, 4.66f, z), new Vector3(4.72f, .11f, 4.58f), "CeilingWhite");
                Box("AcousticRafts", new Vector3(-3.82f, 4.599f, z), new Vector3(4.50f, .012f, 4.36f), "AcousticWhite");
                foreach (float x in new[] { -6.12f, -1.52f })
                    Box("AcousticRafts", new Vector3(x, 4.592f, z), new Vector3(.016f, .018f, 4.32f), "PanelSeam");
            }
            Box("AcousticRafts", new Vector3(4.65f, 4.65f, 10.5f), new Vector3(3.35f, .12f, 7.8f), "CeilingWhite");
            Box("AcousticRafts", new Vector3(4.65f, 4.581f, 10.5f), new Vector3(3.12f, .014f, 7.57f), "AcousticWhite");
            foreach (float z in new[] { 7.30f, 13.7f })
                Box("AcousticRafts", new Vector3(4.65f, 4.566f, z), new Vector3(2.85f, .018f, .043f), "WarmLight");
        }

        static void LearningFeatureWall()
        {
            // All layers sit behind the existing screen plane (x about 7.66); its raycast/UI stays intact.
            Box("LearningWall", new Vector3(7.955f, 2.62f, 10.5f), new Vector3(.065f, 3.55f, 6.14f), "Graphite");
            foreach (float z in new[] { 7.52f, 13.48f })
            {
                Box("LearningWall", new Vector3(7.885f, 2.62f, z), new Vector3(.075f, 3.55f, .37f), "Oak");
                Box("LearningWall", new Vector3(7.838f, 2.62f, z < 10 ? z + .22f : z - .22f), new Vector3(.014f, 3.22f, .014f), "WarmLight");
            }
            Box("LearningWall", new Vector3(7.844f, 4.403f, 10.5f), new Vector3(.16f, .045f, 6.17f), "Oak");
            Box("LearningWall", new Vector3(7.816f, 4.366f, 10.5f), new Vector3(.018f, .012f, 5.62f), "WarmLight");
            SignAt("LearningHeader", "课程学习  /  LEARNING STUDIO", new Vector3(7.81f, 4.184f, 10.5f), Quaternion.Euler(0, 90, 0), .100f, new Color(.94f, .95f, .92f));
        }

        static void EntryAndRearFeature()
        {
            // Door surround, small reception finish, and a readable understated rear identity panel.
            foreach (float x in new[] { -1.73f, 1.73f })
            {
                Box("EntryJoinery", new Vector3(x, 1.59f, .06f), new Vector3(.17f, 3.18f, .18f), "Graphite");
                Box("EntryJoinery", new Vector3(x < 0 ? -1.87f : 1.87f, 1.59f, .075f), new Vector3(.09f, 3.18f, .12f), "Oak");
            }
            Box("EntryJoinery", new Vector3(0, 3.23f, .065f), new Vector3(3.65f, .17f, .18f), "Graphite");
            Box("EntryJoinery", new Vector3(0, 3.139f, .106f), new Vector3(3.15f, .012f, .018f), "WarmLight");
            // The original entry information panel remains on top of this narrow wood field.
            Box("EntryJoinery", new Vector3(-4.80f, 1.58f, .024f), new Vector3(4.12f, 1.38f, .04f), "Oak");
            Box("RearIdentity", new Vector3(-2.7f, 3.38f, 21.93f), new Vector3(5.90f, 1.08f, .055f), "LowerPanel");
            Box("RearIdentity", new Vector3(-5.49f, 3.38f, 21.888f), new Vector3(.065f, .77f, .025f), "Teal");
            SignAt("LabIdentity", "机器人虚拟仿真实训室", new Vector3(-2.60f, 3.53f, 21.885f), Quaternion.identity, .21f, M("Graphite").color);
            SignAt("LabIdentitySub", "ROBOTICS  /  TRAIN · PROGRAM · VALIDATE", new Vector3(-2.60f, 3.20f, 21.885f), Quaternion.identity, .061f, M("Teal").color);
        }

        static void StationSigns()
        {
            string[] names = { "循迹训练", "套圈训练", "绘图编程" };
            string[] english = { "PATH FOLLOWING", "WIRE LOOP", "DRAWING" };
            Quaternion face = Quaternion.Euler(0, -90, 0);
            for (int i = 0; i < 3; i++)
            {
                Vector3 center = new Vector3(-3.18f, 2.97f, 5.5f * (i + 1));
                LocalBox("StationSigns", center, face, Vector3.zero, new Vector3(1.55f, .52f, .055f), "WallWhite");
                LocalBox("StationSigns", center, face, new Vector3(-.55f, 0, -.034f), new Vector3(.35f, .44f, .018f), "Teal");
                SignAt("StationNumber" + i, "0" + (i + 1), center + face * new Vector3(-.55f, 0, -.047f), face, .145f, Color.white);
                SignAt("StationName" + i, names[i], center + face * new Vector3(.18f, .085f, -.043f), face, .10f, M("Graphite").color);
                SignAt("StationEnglish" + i, english[i], center + face * new Vector3(.18f, -.112f, -.043f), face, .035f, M("Teal").color);
                foreach (float dz in new[] { -.60f, .60f })
                    Rod("StationSigns", center + new Vector3(0, .27f, dz), center + new Vector3(0, 1.61f, dz), .005f, "Graphite");
            }
        }

        static void FurnitureFinishes()
        {
            // Thin replaceable veneers sit on cabinet/desk tops, below their existing accessories.
            Box("Furniture", new Vector3(7.33f, 1.119f, 10.5f), new Vector3(1.025f, .008f, 4.055f), "Oak");
            Box("Furniture", new Vector3(5.6f, .811f, 19.7f), new Vector3(3.01f, .006f, 1.16f), "Oak");
            // A clean right-wall pinboard, beyond all safety and learning interfaces.
            Box("Furniture", new Vector3(7.945f, 2.24f, 19.7f), new Vector3(.055f, 1.23f, 2.76f), "Oak");
            Box("Furniture", new Vector3(7.903f, 2.24f, 19.7f), new Vector3(.03f, 1.12f, 2.65f), "AcousticWhite");
            Quaternion face = Quaternion.Euler(0, 90, 0);
            SignAt("SupportHeader", "教学支持  /  SUPPORT", new Vector3(7.88f, 2.61f, 19.7f), face, .078f, M("Teal").color);
            string[] notes = { "安全规范", "操作记录", "设备检查" };
            for (int i = 0; i < 3; i++)
            {
                float z = 18.92f + .78f * i;
                Box("Furniture", new Vector3(7.875f, 2.20f, z), new Vector3(.008f, .47f, .55f), "WallWhite");
                Box("Furniture", new Vector3(7.868f, 2.395f, z), new Vector3(.008f, .019f, .47f), "Teal");
                SignAt("SupportNote" + i, notes[i], new Vector3(7.863f, 2.29f, z), face, .051f, M("Graphite").color);
                for (int line = 0; line < 3; line++)
                    Box("Furniture", new Vector3(7.862f, 2.18f - line * .065f, z), new Vector3(.002f, .009f, .36f - line * .04f), "PanelSeam");
            }
        }

        static void Plants()
        {
            foreach (Vector3 basePos in new[] { new Vector3(7.18f, .005f, 6.23f), new Vector3(7.20f, .005f, 17.92f) })
            {
                Contact(basePos + Vector3.up * .001f, new Vector2(1.05f, 1.05f));
                Part("Plants", planter, basePos, Vector3.one, Quaternion.identity, M("PlanterStone"), true);
                Cylinder("Plants", basePos + Vector3.up * .475f, .245f, .014f, "Soil");
                for (int branch = 0; branch < 5; branch++)
                {
                    float angle = branch * 137.508f;
                    Vector3 outwards = Quaternion.Euler(0, angle, 0) * Vector3.right;
                    float height = 1.08f + (branch % 3) * .21f;
                    Vector3 stemStart = basePos + new Vector3(0, .46f, 0);
                    Vector3 stemEnd = basePos + Vector3.up * height + outwards * .19f;
                    Rod("Plants", stemStart, stemEnd, .011f, "Stem");
                    for (int l = 0; l < 5; l++)
                    {
                        float along = .37f + l * .135f;
                        Vector3 anchor = Vector3.Lerp(stemStart, stemEnd, along);
                        Vector3 dir = Quaternion.Euler(0, angle + ((l % 2 == 0) ? -45 : 125), 0) * new Vector3(.91f, .33f, 0);
                        Rod("Plants", anchor, anchor + dir * .12f, .004f, "Stem");
                        Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                        Part("Plants", leaf, anchor + dir * .09f, new Vector3(.21f, .21f, .38f), rot, M(l % 3 == 0 ? "LeafLight" : "LeafDark"), true);
                    }
                }
            }
        }

        static void TuneExistingLighting(Transform environment)
        {
            foreach (Light light in environment.GetComponentsInChildren<Light>(true))
            {
                switch (light.name)
                {
                    case "SoftDaylight":
                        light.transform.rotation = Quaternion.Euler(49, 64, 0);
                        light.color = new Color(1f, .985f, .96f); light.intensity = 1.05f;
                        light.shadows = LightShadows.Soft; light.shadowStrength = .72f;
                        light.shadowBias = .018f; light.shadowNormalBias = .09f;
                        light.shadowResolution = LightShadowResolution.High; RenderSettings.sun = light;
                        break;
                    case "RoomFill": light.intensity = .62f; light.color = new Color(.968f, .98f, 1f); break;
                    case "WestWindowBounce": light.intensity = .36f; light.color = new Color(.88f, .94f, 1f); break;
                    case "EastWallBounce": light.intensity = .12f; light.color = new Color(1f, .95f, .88f); break;
                    case "CeilingBounce": light.intensity = .45f; break;
                }
                EditorUtility.SetDirty(light);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.52f, .55f, .58f);
            RenderSettings.ambientEquatorColor = new Color(.43f, .455f, .48f);
            RenderSettings.ambientGroundColor = new Color(.38f, .39f, .41f);
            RenderSettings.ambientIntensity = 1f; RenderSettings.reflectionIntensity = .38f;
            RenderSettings.fog = false;
            DynamicGI.UpdateEnvironment();
        }

        static void PrepareMaterials()
        {
            Material("WallWhite", new Color(.97f, .965f, .94f), .23f);
            Material("CeilingWhite", new Color(.98f, .981f, .97f), .15f);
            Material("AcousticWhite", new Color(.89f, .90f, .875f), .10f);
            Material("LowerPanel", new Color(.81f, .84f, .82f), .29f);
            Material("Graphite", new Color(.115f, .15f, .17f), .32f);
            Material("Reveal", new Color(.20f, .25f, .26f), .20f);
            Material("PanelSeam", new Color(.60f, .65f, .64f), .16f);
            Material("Teal", new Color(.045f, .37f, .40f), .24f);
            Material("MineralFloor", new Color(.72f, .73f, .70f), .27f);
            Material("AisleIvory", new Color(.91f, .92f, .88f), .28f);
            Material("WorkcellFloor", new Color(.48f, .57f, .58f), .23f);
            Material("LearningFloor", new Color(.82f, .79f, .70f), .23f);
            Material("Oak", new Color(.79f, .64f, .46f), .30f);
            Material("PlanterStone", new Color(.76f, .77f, .72f), .16f);
            Material("Soil", new Color(.13f, .105f, .07f), .06f);
            Material("Stem", new Color(.25f, .29f, .13f), .12f);
            Material("LeafDark", new Color(.075f, .25f, .13f), .24f);
            Material("LeafLight", new Color(.15f, .37f, .18f), .23f);
            var light = Material("WarmLight", new Color(1f, .956f, .80f), .1f);
            light.shader = Shader.Find("Unlit/Color");
            M("Oak").mainTexture = ProceduralTexture("OakGrain", 128, 128, (x, y) =>
            {
                float grain = Mathf.Sin(x * .46f + Mathf.Sin(y * .024f) * 2.2f) * .045f
                    + (Mathf.PerlinNoise(x * .17f, y * .009f) - .5f) * .09f;
                return new Color(.95f + grain, .94f + grain, .92f + grain, 1);
            });
            Texture2D mineral = ProceduralTexture("MineralGrain", 128, 128, (x, y) =>
            {
                float n = .95f + Mathf.PerlinNoise(x * .31f, y * .31f) * .05f;
                return new Color(n, n, n, 1);
            });
            foreach (string name in new[] { "MineralFloor", "AisleIvory", "WorkcellFloor", "LearningFloor", "PlanterStone" })
            { M(name).mainTexture = mineral; M(name).mainTextureScale = new Vector2(16, 16); }
            Material contact = Material("ContactShadow", new Color(.11f, .14f, .15f, .28f), 0);
            contact.shader = Shader.Find("RoboticsLab/DepthTestedText");
            contact.mainTexture = ProceduralTexture("ContactFalloff", 64, 64, (x, y) =>
            {
                float u = Mathf.Abs((x + .5f) / 32f - 1), v = Mathf.Abs((y + .5f) / 32f - 1);
                float a = Mathf.Pow(Mathf.Clamp01(1 - Mathf.Pow(u, 4) - Mathf.Pow(v, 4)), 2);
                return new Color(1, 1, 1, a);
            }, false);
            foreach (Material material in materials.Values) EditorUtility.SetDirty(material);
        }

        static Material M(string name) => materials[name];
        static Material Material(string name, Color color, float smoothness)
        {
            string path = AssetsRoot + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Standard")) { name = "Refinish_" + name }; AssetDatabase.CreateAsset(material, path); }
            material.shader = Shader.Find("Standard"); material.color = color;
            material.SetFloat("_Glossiness", smoothness); material.SetFloat("_Metallic", 0);
            material.mainTexture = null; material.mainTextureScale = Vector2.one;
            materials[name] = material; return material;
        }
        static Texture2D ProceduralTexture(string name, int width, int height, Func<int, int, Color> pixel, bool repeat = true)
        {
            string path = AssetsRoot + "/" + name + ".asset";
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!texture) { texture = new Texture2D(width, height, TextureFormat.RGBA32, true, false); AssetDatabase.CreateAsset(texture, path); }
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixels[y * width + x] = pixel(x, y);
            texture.SetPixels(pixels); texture.Apply(true, false); texture.name = "Refinish_" + name;
            texture.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear; texture.anisoLevel = 4; EditorUtility.SetDirty(texture); return texture;
        }

        static void PrepareMeshes()
        {
            if (!cube) cube = PrimitiveMesh(PrimitiveType.Cube);
            if (!cylinder) cylinder = PrimitiveMesh(PrimitiveType.Cylinder);
            if (!quad)
            {
                quad = new Mesh { name = "RefinishQuad", hideFlags = HideFlags.HideAndDontSave };
                quad.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0) };
                quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one };
                quad.triangles = new[] { 0, 1, 2, 2, 1, 3 }; quad.colors = Enumerable.Repeat(Color.white, 4).ToArray(); quad.RecalculateNormals();
            }
            if (!leaf)
            {
                leaf = new Mesh { name = "RefinishLeaf", hideFlags = HideFlags.HideAndDontSave };
                Vector3[] faceVertices = { Vector3.zero, new Vector3(-.38f, .06f, .28f), new Vector3(-.46f, .08f, .52f),
                    new Vector3(-.26f, .02f, .79f), new Vector3(0, -.10f, 1), new Vector3(.26f, .02f, .79f),
                    new Vector3(.46f, .08f, .52f), new Vector3(.38f, .06f, .28f), new Vector3(0, .19f, .49f) };
                leaf.vertices = faceVertices.Concat(faceVertices).ToArray();
                var tris = new List<int>();
                // Separate the back vertices so opposing faces retain real lighting normals.
                for (int i = 0; i < 8; i++) { int next = (i + 1) % 8; tris.AddRange(new[] { 8, i, next, 17, next + 9, i + 9 }); }
                leaf.SetTriangles(tris, 0); leaf.RecalculateNormals(); leaf.RecalculateBounds();
            }
            if (!planter)
            {
                var verts = new List<Vector3>(); var tris = new List<int>(); var uv = new List<Vector2>();
                float[] heights = { 0, .025f, .44f, .49f, .49f, .445f };
                float[] radii = { .205f, .23f, .28f, .285f, .252f, .248f };
                for (int ring = 0; ring < heights.Length; ring++) for (int i = 0; i <= 32; i++)
                { float a = Mathf.PI * 2 * i / 32; verts.Add(new Vector3(Mathf.Cos(a) * radii[ring], heights[ring], Mathf.Sin(a) * radii[ring])); uv.Add(new Vector2(i / 32f, ring / 5f)); }
                for (int ring = 0; ring < heights.Length - 1; ring++) for (int i = 0; i < 32; i++)
                { int v = ring * 33 + i; tris.AddRange(new[] { v, v + 33, v + 1, v + 1, v + 33, v + 34 }); }
                planter = new Mesh { name = "RefinishPlanter", hideFlags = HideFlags.HideAndDontSave };
                planter.SetVertices(verts); planter.SetTriangles(tris, 0); planter.SetUVs(0, uv); planter.RecalculateNormals(); planter.RecalculateBounds();
            }
        }
        static Mesh PrimitiveMesh(PrimitiveType type)
        { var go = GameObject.CreatePrimitive(type); var mesh = go.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(go); return mesh; }
        static void Part(string group, Mesh mesh, Vector3 position, Vector3 scale, Quaternion rotation, Material material, bool shadows = false)
        {
            string key = group + "_" + material.name;
            if (!batches.TryGetValue(key, out Batch batch)) batches[key] = batch = new Batch { group = group, material = material, shadows = shadows };
            batch.parts.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(position, rotation, scale) });
        }
        static void Box(string group, Vector3 position, Vector3 scale, string material, Quaternion? rotation = null)
            => Part(group, cube, position, scale, rotation ?? Quaternion.identity, M(material));
        static void LocalBox(string group, Vector3 center, Quaternion rotation, Vector3 offset, Vector3 scale, string material)
            => Box(group, center + rotation * offset, scale, material, rotation);
        static void Cylinder(string group, Vector3 position, float radius, float height, string material)
            => Part(group, cylinder, position, new Vector3(radius * 2, height * .5f, radius * 2), Quaternion.identity, M(material));
        static void Rod(string group, Vector3 a, Vector3 b, float radius, string material)
            => Part(group, cylinder, (a + b) * .5f, new Vector3(radius * 2, (a - b).magnitude * .5f, radius * 2), Quaternion.FromToRotation(Vector3.up, b - a), M(material));
        static void Contact(Vector3 position, Vector2 dimensions)
            => Part("ContactShadows", quad, position, new Vector3(dimensions.x, dimensions.y, 1), Quaternion.Euler(90, 0, 0), M("ContactShadow"));
        static void BuildBatches(Transform root)
        {
            var groups = new Dictionary<string, Transform>();
            foreach (var pair in batches.OrderBy(p => p.Key))
            {
                Batch batch = pair.Value; Mesh mesh = PersistentMesh("Geometry_" + pair.Key);
                mesh.indexFormat = IndexFormat.UInt32; mesh.CombineMeshes(batch.parts.ToArray(), true, true, false); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
                if (!groups.TryGetValue(batch.group, out Transform group)) { group = new GameObject(batch.group).transform; group.SetParent(root, false); groups[batch.group] = group; }
                var go = new GameObject(batch.material.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(group, false); go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = batch.material;
                renderer.shadowCastingMode = batch.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = true; renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }
        }

        static void SignAt(string name, string text, Vector3 position, Quaternion rotation, float height, Color color)
            => signs.Add(new Sign { name = name, text = text, position = position, rotation = rotation, height = height, color = color });
        static void BuildSigns(Transform root)
        {
            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (!font) throw new InvalidOperationException("Bundled Chinese font is missing: " + FontPath);
            string alphabet = new string(("机H " + string.Join("", signs.Select(s => s.text))).Distinct().ToArray());
            font.RequestCharactersInTexture(alphabet, FontSize, FontStyle.Normal);
            foreach (char c in alphabet)
            {
                if (!font.GetCharacterInfo(c, out CharacterInfo glyph, FontSize, FontStyle.Normal))
                    throw new InvalidOperationException("Missing refinish sign glyph: " + c);
                glyphs[c] = glyph;
            }
            glyphHeight = Mathf.Max(1, glyphs['机'].maxY - glyphs['机'].minY);
            Texture source = font.material.mainTexture;
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture prior = RenderTexture.active;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            try { Graphics.Blit(source, rt); RenderTexture.active = rt; copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); copy.Apply(false, false); }
            finally { RenderTexture.active = prior; RenderTexture.ReleaseTemporary(rt); }
            string atlasPath = AssetsRoot + "/SignAtlas.asset";
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            if (!atlas) { atlas = copy; AssetDatabase.CreateAsset(atlas, atlasPath); }
            else { EditorUtility.CopySerialized(copy, atlas); Object.DestroyImmediate(copy); }
            atlas.name = "RefinishSignAtlas"; atlas.wrapMode = TextureWrapMode.Clamp; EditorUtility.SetDirty(atlas);
            textMaterial = Material("SignText", Color.white, 0); textMaterial.shader = Shader.Find("RoboticsLab/DepthTestedText");
            textMaterial.mainTexture = atlas; EditorUtility.SetDirty(textMaterial);
            Transform signRoot = new GameObject("Labels").transform; signRoot.SetParent(root, false);
            foreach (Sign sign in signs)
            {
                var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>(); var colors = new List<Color>();
                float scale = sign.height / glyphHeight;
                float x = -sign.text.Sum(c => glyphs[c].advance) * .5f;
                float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
                foreach (char c in sign.text)
                {
                    CharacterInfo g = glyphs[c];
                    if (!char.IsWhiteSpace(c))
                    {
                        int v = vertices.Count;
                        vertices.Add(new Vector3(x + g.minX, g.minY, 0) * scale); vertices.Add(new Vector3(x + g.minX, g.maxY, 0) * scale);
                        vertices.Add(new Vector3(x + g.maxX, g.minY, 0) * scale); vertices.Add(new Vector3(x + g.maxX, g.maxY, 0) * scale);
                        uv.Add(g.uvBottomLeft); uv.Add(g.uvTopLeft); uv.Add(g.uvBottomRight); uv.Add(g.uvTopRight);
                        triangles.AddRange(new[] { v, v + 1, v + 2, v + 2, v + 1, v + 3 });
                        // The unlit shader consumes vertex RGB directly in the active render space.
                        Color ink = QualitySettings.activeColorSpace == ColorSpace.Linear ? sign.color.linear : sign.color;
                        for (int k = 0; k < 4; k++) colors.Add(ink);
                        minY = Mathf.Min(minY, g.minY * scale); maxY = Mathf.Max(maxY, g.maxY * scale);
                    }
                    x += g.advance;
                }
                for (int v = 0; v < vertices.Count; v++) vertices[v] -= Vector3.up * ((minY + maxY) * .5f);
                Mesh mesh = PersistentMesh("Text_" + sign.name);
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
                var go = new GameObject(sign.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(signRoot, false); go.transform.localPosition = sign.position; go.transform.localRotation = sign.rotation;
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = textMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
        }

        static Mesh PersistentMesh(string name)
        {
            string path = AssetsRoot + "/" + name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh) { mesh = new Mesh { name = name }; AssetDatabase.CreateAsset(mesh, path); }
            else mesh.Clear(); return mesh;
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/')); Folder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
        }
    }
}
