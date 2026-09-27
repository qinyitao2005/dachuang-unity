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
    /// <summary>Optional, independently removable room detail layer. Existing equipment is read-only.</summary>
    public static class LabRoomPolishBuilder
    {
        const string RootName = "RoomPolish";
        const string AssetsRoot = "Assets/RoboticsLab/Generated/RoomPolish";
        const string FontPath = "Assets/RoboticsLab/Fonts/NotoSansSC-Regular.ttf";
        const int FontSize = 80;
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, GeometryBatch> batches = new Dictionary<string, GeometryBatch>();
        static readonly List<SignText> labels = new List<SignText>();
        static readonly Dictionary<char, CharacterInfo> glyphs = new Dictionary<char, CharacterInfo>();
        static Mesh cube, cylinder, plane;
        static Material textMaterial;
        static float characterHeight;

        sealed class GeometryBatch
        {
            public string category;
            public Material material;
            public readonly List<CombineInstance> parts = new List<CombineInstance>();
        }

        sealed class SignText
        {
            public string name, category, text;
            public Vector3 position;
            public Quaternion rotation;
            public float height;
            public Color color;
            public bool left;
        }

        [MenuItem("Robot Lab/Polish Training Room")]
        public static void Polish()
        {
            RequireTeachingScene();
            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (!font) throw new InvalidOperationException("The bundled Noto Sans SC font is required: " + FontPath);
            if (!Shader.Find("RoboticsLab/DepthTestedText"))
                throw new InvalidOperationException("Import the existing depth-tested text shader first.");
            Folder(AssetsRoot);
            materials.Clear(); batches.Clear(); labels.Clear(); glyphs.Clear();
            PreparePrimitiveMeshes();
            ArchitectureDetails(); WindowDetails(); CeilingDetails();
            WorkcellWayfinding(); EntryWayfinding(); EntranceLobby(); TeachingWallDetails(); ToolBoard();
            BakeFontAtlas(font);

            Scene scene = SceneManager.GetActiveScene();
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Polish training room");
            foreach (GameObject existing in scene.GetRootGameObjects().Where(g => g.name == RootName))
                Undo.DestroyObjectImmediate(existing);
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Create room detail layer");
            var groups = new Dictionary<string, Transform>();
            foreach (var pair in batches.OrderBy(p => p.Key))
            {
                GeometryBatch batch = pair.Value;
                Mesh mesh = PersistentMesh("Geometry_" + pair.Key);
                mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(batch.parts.ToArray(), true, true, false);
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
                var go = new GameObject(batch.material.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(Category(root.transform, groups, batch.category), false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                ConfigureRenderer(go.GetComponent<MeshRenderer>(), batch.material);
                go.isStatic = true;
            }
            foreach (SignText label in labels) BuildLabel(root.transform, groups, label);
            EntranceLobbyCollision(Category(root.transform, groups, "EntranceLobby"));
            if (root.GetComponentsInChildren<Collider>(true).Length != 4 || root.GetComponentsInChildren<Light>(true).Length != 0)
                throw new InvalidOperationException("Only the four lobby boundary colliders are permitted; no additional lights.");
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Documentation");
            File.WriteAllText("Documentation/RoomPolishAuthoring.txt",
                "Room detail authoring / " + DateTime.UtcNow.ToString("O") + "\n"
                + "Scene=" + scene.path + "\nRoot=" + RootName + "\n"
                + "Mesh renderers=" + root.GetComponentsInChildren<MeshRenderer>(true).Length + "\n"
                + "Combined decorative primitive parts=" + batches.Values.Sum(b => b.parts.Count) + "\n"
                + "Static labels=" + labels.Count + "; verified font glyphs=" + glyphs.Count + "\n"
                + "Font=" + FontPath + "; atlas is copied into a persistent project asset.\n"
                + "Added lights=0; added colliders=4 (lobby floor and three walls only). Existing scene objects and materials were not edited.\n"
                + "Scene marked dirty; save after visual and interaction review.\n",
                new UTF8Encoding(false));
            Selection.activeGameObject = root;
            Debug.Log("ROBOT_ROOM_POLISH_READY renderers=" + root.GetComponentsInChildren<MeshRenderer>(true).Length
                + " labels=" + labels.Count + " lights=0 colliders=4 (lobby only); save the reviewed scene.");
        }

        static void RequireTeachingScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring room details.");
            if (SceneManager.GetActiveScene().path != LabSceneBuilder.ScenePath)
                throw new InvalidOperationException("Open RoboticsTrainingRoom.unity first; this menu does not switch or rebuild scenes.");
            if (!GameObject.Find("RoboticsLab_Environment")) throw new InvalidOperationException("The existing teaching room is required.");
        }

        static void ArchitectureDetails()
        {
            // The room already has seams and metal skirting. Add low protective panels and
            // small cap profiles aligned to that grid, without covering windows or services.
            for (int i = 0; i < 11; i++)
            {
                float z = 1 + i * 2;
                Box("WallFinishes", new Vector3(-7.979f, 1.03f, z), new Vector3(.026f, 1.52f, 1.97f), M("Panel"));
                Box("WallFinishes", new Vector3(7.979f, .70f, z), new Vector3(.026f, .86f, 1.97f), M("Panel"));
            }
            foreach (float x in new[] { -7.948f, 7.948f })
            {
                Box("WallFinishes", new Vector3(x, .252f, 11), new Vector3(.035f, .015f, 21.95f), M("White"));
                Box("WallFinishes", new Vector3(x, x < 0 ? 1.804f : 1.144f, 11), new Vector3(.025f, .018f, 21.95f), M("White"));
            }
            Box("WallFinishes", new Vector3(0, .252f, 21.947f), new Vector3(15.93f, .015f, .035f), M("White"));
            foreach (float x in new[] { -7.94f, 7.94f })
                Box("WallFinishes", new Vector3(x, 4.72f, 11), new Vector3(.08f, .055f, 21.96f), M("White"));
        }

        static void WindowDetails()
        {
            Material sky = DaylightMaterial();
            foreach (float z in new[] { 5.5f, 11f, 16.5f })
            {
                // A brighter backdrop sits behind the original glazing and exterior mullions.
                Part("WindowDetails", plane, new Vector3(-8.175f, 2.96f, z),
                    new Vector3(3.34f, 1.64f, 1), Quaternion.Euler(0, -90, 0), sky);
                foreach (float y in new[] { 2.165f, 3.755f })
                    Box("WindowDetails", new Vector3(-7.988f, y, z), new Vector3(.018f, .014f, 3.30f), M("Gasket"));
                foreach (float dz in new[] { -1.635f, -.044f, .044f, 1.635f })
                    Box("WindowDetails", new Vector3(-7.988f, 2.96f, z + dz), new Vector3(.018f, 1.59f, .014f), M("Gasket"));
                Box("WindowDetails", new Vector3(-7.866f, 3.865f, z), new Vector3(.17f, .115f, 3.48f), M("White"));
                Box("WindowDetails", new Vector3(-7.961f, 3.673f, z), new Vector3(.014f, .19f, 3.27f), M("Blind"));
                Box("WindowDetails", new Vector3(-7.865f, 2.60f, z + .09f), new Vector3(.034f, .17f, .039f), M("Metal"));
                Rod("WindowDetails", new Vector3(-7.805f, 2.525f, z + .09f), new Vector3(-7.805f, 2.68f, z + .09f), .012f, M("White"));
                Rod("WindowDetails", new Vector3(-7.84f, 3.75f, z + 1.57f), new Vector3(-7.84f, 3.06f, z + 1.57f), .0045f, M("White"));
            }
        }

        static void CeilingDetails()
        {
            // Use the existing lights and ventilation positions; no additional Light component.
            foreach (float x in new[] { -3.4f, 3.4f }) foreach (float z in new[] { 3f, 8.3f, 13.6f, 19f })
            {
                foreach (float dx in new[] { -1.366f, 1.366f })
                    Box("CeilingDetails", new Vector3(x + dx, 4.42f, z), new Vector3(.018f, .105f, .33f), M("Metal"));
                Box("CeilingDetails", new Vector3(x, 4.489f, z), new Vector3(.28f, .033f, .21f), M("Panel"));
            }
            // Fine covered conduit follows the right ceiling perimeter, above the camera paths.
            Box("CeilingDetails", new Vector3(7.25f, 4.708f, 11), new Vector3(.145f, .07f, 21.35f), M("White"));
            Box("CeilingDetails", new Vector3(7.163f, 4.715f, 11), new Vector3(.014f, .036f, 21.35f), M("Metal"));
            foreach (float z in new[] { 3f, 8.3f, 13.6f, 19f })
            {
                Box("CeilingDetails", new Vector3(5.47f, 4.708f, z), new Vector3(3.56f, .035f, .042f), M("White"));
                Box("CeilingDetails", new Vector3(7.25f, 4.683f, z), new Vector3(.19f, .023f, .09f), M("Metal"));
            }
            for (int i = 0; i < 3; i++)
            {
                float z = new[] { 5.8f, 11.4f, 17.1f }[i];
                foreach (float x in new[] { -.77f, .77f }) foreach (float dz in new[] { -.29f, .29f })
                    Cylinder("CeilingDetails", new Vector3(x, 4.746f, z + dz), .013f, .007f, M("Metal"));
                Text("VentID" + i, "CeilingDetails", "SA-0" + (i + 1), new Vector3(.59f, 4.744f, z + .25f),
                    Quaternion.Euler(-90, 0, 0), .027f, Color.gray);
            }
        }

        static void WorkcellWayfinding()
        {
            string[] names = { "轨迹绘制", "精密操作", "综合实训" };
            for (int i = 0; i < 3; i++)
            {
                float z = 5.5f + 5.5f * i;
                Vector3 sign = new Vector3(-3.18f, 2.97f, z);
                Quaternion face = Quaternion.Euler(0, -90, 0);
                Panel("WorkcellSigns", sign, face, new Vector2(1.45f, .50f), M("White"));
                LocalBox("WorkcellSigns", sign, face, new Vector3(-.51f, 0, -.03f), new Vector3(.34f, .42f, .016f), M("Blue"));
                Text("StationNumber" + i, "WorkcellSigns", "0" + (i + 1), P(sign, face, -.51f, 0, -.045f), face, .165f, Color.white);
                Text("StationName" + i, "WorkcellSigns", names[i], P(sign, face, .19f, .085f, -.035f), face, .097f, M("Blue").color);
                Text("StationCode" + i, "WorkcellSigns", "TRAINING CELL  0" + (i + 1), P(sign, face, .19f, -.104f, -.035f), face, .032f, M("DarkMetal").color);
                foreach (float dz in new[] { -.55f, .55f })
                    Rod("WorkcellSigns", new Vector3(sign.x, 3.23f, z + dz), new Vector3(sign.x, 4.76f, z + dz), .006f, M("Metal"));

                // Existing yellow borders stay visible. Only short inner corner patches are added.
                foreach (float dz in new[] { -1.42f, 1.42f })
                {
                    Vector3 corner = new Vector3(-2.63f, .012f, z + dz);
                    Box("FloorWayfinding", corner, new Vector3(.23f, .009f, .55f), M("SafetyYellow"));
                    for (int stripe = 0; stripe < 4; stripe++)
                        Box("FloorWayfinding", corner + new Vector3(0, .006f, -.18f + stripe * .12f),
                            new Vector3(.25f, .002f, .040f), M("Graphite"), Quaternion.Euler(0, -25, 0));
                }
                Text("CellOperatorArea" + i, "FloorWayfinding", "操作区", new Vector3(-2.06f, .019f, z + .67f),
                    Quaternion.Euler(90, 90, 0), .108f, M("Blue").color);
            }
            Text("ClearAisle", "FloorWayfinding", "通行区域  /  保持畅通", new Vector3(2.85f, .018f, 15.0f),
                Quaternion.Euler(90, 0, 0), .105f, M("Blue").color);
        }

        static void EntryWayfinding()
        {
            Vector3 entry = new Vector3(-4.8f, 1.57f, .068f);
            Quaternion inward = Quaternion.Euler(0, 180, 0);
            Panel("EntrySigns", entry, inward, new Vector2(3.72f, 1.02f), M("White"));
            LocalBox("EntrySigns", entry, inward, new Vector3(-1.69f, 0, -.028f), new Vector3(.034f, .83f, .012f), M("Blue"));
            Text("RoomTitleChinese", "EntrySigns", "机器人虚拟仿真实训室", P(entry, inward, 0, .225f, -.039f), inward, .182f, M("Blue").color);
            Text("RoomTitleEnglish", "EntrySigns", "ROBOTICS  /  VIRTUAL TRAINING", P(entry, inward, 0, -.045f, -.039f), inward, .063f, M("DarkMetal").color);
            Text("EntrySequence", "EntrySigns", "安全准备  →  课程学习  →  知识检查  →  机械臂实操",
                P(entry, inward, 0, -.295f, -.039f), inward, .080f, M("DarkMetal").color);

            Vector3 ppe = new Vector3(7.93f, 2.48f, 3.2f);
            Quaternion rightWall = Quaternion.Euler(0, 90, 0);
            Panel("EntrySigns", ppe, rightWall, new Vector2(2.94f, .46f), M("White"));
            LocalBox("EntrySigns", ppe, rightWall, new Vector3(-1.31f, 0, -.032f), new Vector3(.03f, .35f, .012f), M("Blue"));
            Text("SafetyPreparation", "EntrySigns", "01  安全准备", P(ppe, rightWall, -.50f, .055f, -.04f), rightWall, .133f, M("Blue").color);
            Text("CollectHelmetHint", "EntrySigns", "靠近安全帽按 E 领取", P(ppe, rightWall, .69f, 0, -.04f), rightWall, .083f, M("DarkMetal").color);
            // Low-key room number plate at the door, below the existing emergency exit signage.
            Vector3 plaque = new Vector3(3.55f, 2.15f, .068f);
            Panel("EntrySigns", plaque, inward, new Vector2(.44f, .27f), M("Blue"));
            Text("RoomNumber", "EntrySigns", "LAB 01", P(plaque, inward, 0, 0, -.033f), inward, .065f, Color.white);
        }

        static void EntranceLobby()
        {
            // Existing doorway is x=-1.6..1.6, at z=0 and opens towards -Z.
            // This shallow external shell closes the view to the sky without changing the room,
            // or its threshold. Four separate boundary colliders support the new lobby only.
            const string group = "EntranceLobby";
            Box(group, new Vector3(0, -.06f, -1.82f), new Vector3(4.52f, .10f, 3.38f), M("Grout"));
            for (int x = 0; x < 4; x++) for (int z = 0; z < 3; z++)
                Box(group, new Vector3(-1.68f + x * 1.12f, -.016f, -.70f - z * 1.12f),
                    new Vector3(1.11f, .025f, 1.11f), M((x + z) % 3 == 0 ? "FloorAlternate" : "Floor"));
            Box(group, new Vector3(0, 1.73f, -3.57f), new Vector3(4.68f, 3.46f, .16f), M("Wall"));
            foreach (float x in new[] { -2.30f, 2.30f })
            {
                Box(group, new Vector3(x, 1.73f, -1.84f), new Vector3(.16f, 3.46f, 3.62f), M("Wall"));
                Box(group, new Vector3(x < 0 ? -2.207f : 2.207f, .12f, -1.84f),
                    new Vector3(.025f, .24f, 3.60f), M("Metal"));
                Box(group, new Vector3(x < 0 ? -2.211f : 2.211f, 1.12f, -1.84f),
                    new Vector3(.017f, .046f, 3.60f), M("BlueLight"));
            }
            Box(group, new Vector3(0, .12f, -3.478f), new Vector3(4.42f, .24f, .025f), M("Metal"));
            Box(group, new Vector3(0, 1.12f, -3.48f), new Vector3(4.42f, .046f, .017f), M("BlueLight"));
            foreach (float x in new[] { -1.12f, 0, 1.12f })
                Box(group, new Vector3(x, 1.88f, -3.483f), new Vector3(.01f, 2.91f, .013f), M("Panel"));
            Box(group, new Vector3(0, 3.51f, -1.84f), new Vector3(4.68f, .14f, 3.62f), M("White"));
            Box(group, new Vector3(0, 3.418f, -1.93f), new Vector3(2.28f, .035f, .38f), M("Panel"));
            Box(group, new Vector3(0, 3.392f, -1.93f), new Vector3(2.16f, .013f, .29f), M("Light"));
            Vector3 plaque = new Vector3(0, 2.20f, -3.449f);
            Quaternion inward = Quaternion.Euler(0, 180, 0);
            Panel(group, plaque, inward, new Vector2(2.56f, .61f), M("White"));
            Text("LobbyTeachingWing", group, "教学区  /  TRAINING WING", P(plaque, inward, 0, .095f, -.033f),
                inward, .106f, M("Blue").color);
            Text("LobbyRoomDirection", group, "机器人实训室  ·  LAB 01", P(plaque, inward, 0, -.134f, -.033f),
                inward, .064f, M("DarkMetal").color);
        }

        static void EntranceLobbyCollision(Transform root)
        {
            Vector3[] centers={new Vector3(0,-.055f,-1.70f),new Vector3(-2.30f,1.73f,-1.84f),new Vector3(2.30f,1.73f,-1.84f),new Vector3(0,1.73f,-3.57f)};
            Vector3[] sizes={new Vector3(4.52f,.10f,3.66f),new Vector3(.16f,3.46f,3.62f),new Vector3(.16f,3.46f,3.62f),new Vector3(4.68f,3.46f,.16f)};
            for(int i=0;i<centers.Length;i++)
            {
                var go=new GameObject("LobbyBoundary_"+i,typeof(BoxCollider));
                go.transform.SetParent(root,false);go.transform.localPosition=centers[i];
                go.GetComponent<BoxCollider>().size=sizes[i];go.isStatic=true;
            }
        }

        static void TeachingWallDetails()
        {
            // Wall screen spans z=8.1..12.9; all new teaching accessories are outside it.
            Vector3 board = new Vector3(7.915f, 2.19f, 14.3f);
            Quaternion face = Quaternion.Euler(0, 90, 0);
            Panel("LearningAccessories", board, face, new Vector2(1.59f, 1.66f), M("White"));
            LocalBox("LearningAccessories", board, face, new Vector3(0, .618f, -.032f), new Vector3(1.45f, .29f, .014f), M("Blue"));
            Text("OperatingSequence", "LearningAccessories", "实训流程", P(board, face, 0, .618f, -.042f), face, .115f, Color.white);
            string[] steps = { "01  佩戴安全帽", "02  完成课程与问答", "03  选择工位后点动", "04  异常时按空格急停" };
            for (int i = 0; i < steps.Length; i++)
            {
                float y = .305f - i * .257f;
                Text("OperatingStep" + i, "LearningAccessories", steps[i], P(board, face, -.64f, y, -.042f), face, .071f, M("DarkMetal").color, true);
                LocalBox("LearningAccessories", board, face, new Vector3(0, y - .103f, -.032f), new Vector3(1.31f, .005f, .008f), M("Panel"));
            }
            Text("SimulatorNotice", "LearningAccessories", "虚拟仿真教学  ·  小幅操作", P(board, face, 0, -.67f, -.042f), face, .056f, M("Blue").color);

            Vector3 holder = new Vector3(7.79f, .83f, 14.3f);
            LocalBox("LearningAccessories", holder, face, Vector3.zero, new Vector3(1.23f, .44f, .045f), M("Panel"));
            LocalBox("LearningAccessories", holder, face, new Vector3(0, -.205f, -.09f), new Vector3(1.24f, .025f, .20f), M("White"));
            LocalBox("LearningAccessories", holder, face, new Vector3(0, -.1f, -.18f), new Vector3(1.24f, .20f, .025f), M("White"));
            for (int i = 0; i < 3; i++)
            {
                float x = -.40f + i * .40f;
                LocalBox("LearningAccessories", holder, face, new Vector3(x, .057f, -.075f), new Vector3(.29f, .34f, .038f), M("Paper"));
                LocalBox("LearningAccessories", holder, face, new Vector3(x, .15f, -.101f), new Vector3(.24f, .065f, .008f), M("BlueLight"));
                Text("ManualCover" + i, "LearningAccessories", new[] { "安全", "操作", "维护" }[i], P(holder, face, x, .04f, -.103f), face, .046f, M("Blue").color);
            }
            Text("ManualHolder", "LearningAccessories", "学习资料  /  MANUALS", P(holder, face, 0, -.13f, -.20f), face, .053f, M("Blue").color);

            // Visible service connections below the console; none crosses the display face.
            Box("LearningAccessories", new Vector3(7.948f, .73f, 13.24f), new Vector3(.048f, .60f, .046f), M("White"));
            Box("LearningAccessories", new Vector3(7.907f, .425f, 13.24f), new Vector3(.084f, .16f, .28f), M("Panel"));
        }

        static void ToolBoard()
        {
            Vector3 board = new Vector3(-7.902f, 1.15f, 8.25f);
            Quaternion face = Quaternion.Euler(0, -90, 0);
            Panel("ToolStorage", board, face, new Vector2(1.52f, 1.04f), M("White"));
            LocalBox("ToolStorage", board, face, new Vector3(0, .378f, -.031f), new Vector3(1.38f, .19f, .012f), M("Blue"));
            Text("ToolBoardTitle", "ToolStorage", "工具归位  /  TOOLS", P(board, face, 0, .378f, -.041f), face, .070f, Color.white);
            for (int x = 0; x < 9; x++) for (int y = 0; y < 4; y++)
                LocalBox("ToolStorage", board, face, new Vector3(-.62f + x * .155f, .18f - y * .135f, -.032f), new Vector3(.018f, .018f, .008f), M("Gasket"));
            for (int tool = 0; tool < 3; tool++)
            {
                float x = -.49f + tool * .255f;
                LocalBox("ToolStorage", board, face, new Vector3(x, -.006f, -.055f), new Vector3(.032f, .25f, .025f), M("Metal"));
                LocalBox("ToolStorage", board, face, new Vector3(x, -.148f, -.055f), new Vector3(.060f, .075f, .04f), M(tool == 2 ? "Blue" : "DarkMetal"));
                LocalBox("ToolStorage", board, face, new Vector3(x, .133f, -.055f), new Vector3(.073f, .046f, .028f), M("Metal"));
            }
            LocalBox("ToolStorage", board, face, new Vector3(.43f, .053f, -.060f), new Vector3(.25f, .037f, .032f), M("Metal"));
            LocalBox("ToolStorage", board, face, new Vector3(.325f, -.050f, -.060f), new Vector3(.032f, .24f, .032f), M("Metal"));
            Text("ToolBoardReminder", "ToolStorage", "用后归位  ·  保持整洁", P(board, face, 0, -.37f, -.041f), face, .058f, M("DarkMetal").color);
            LocalBox("ToolStorage", board, face, new Vector3(0, -.495f, -.11f), new Vector3(1.49f, .038f, .22f), M("White"));
        }

        static Material M(string name)
        {
            if (materials.TryGetValue(name, out Material material)) return material;
            material = AssetDatabase.LoadAssetAtPath<Material>("Assets/RoboticsLab/Generated/Lab_" + name + ".mat");
            if (!material)
            {
                Color color = name == "Panel" ? new Color(.90f, .926f, .933f)
                    : name == "Gasket" ? new Color(.16f, .205f, .23f)
                    : name == "Blind" ? new Color(.89f, .927f, .937f) : Color.white;
                string path = AssetsRoot + "/Polish_" + name + ".mat";
                material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material) { material = new Material(Shader.Find("Standard")) { name = "Polish_" + name }; AssetDatabase.CreateAsset(material, path); }
                material.name = "Polish_" + name;
                material.color = color; material.SetFloat("_Glossiness", .24f); material.SetFloat("_Metallic", .04f);
                EditorUtility.SetDirty(material);
            }
            materials[name] = material;
            return material;
        }

        static Material DaylightMaterial()
        {
            string texturePath = AssetsRoot + "/WindowDaylight.asset";
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (!texture) { texture = new Texture2D(16, 128, TextureFormat.RGBA32, false, true); AssetDatabase.CreateAsset(texture, texturePath); }
            var colors = new Color[16 * 128];
            for (int y = 0; y < 128; y++) for (int x = 0; x < 16; x++)
                colors[y * 16 + x] = Color.Lerp(new Color(.63f, .78f, .835f), new Color(.955f, .98f, 1), Mathf.SmoothStep(0, 1, y / 127f));
            texture.SetPixels(colors); texture.Apply(false, false); texture.wrapMode = TextureWrapMode.Clamp;
            EditorUtility.SetDirty(texture);
            string path = AssetsRoot + "/WindowDaylight.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Unlit/Texture")) { name = "WindowDaylight" }; AssetDatabase.CreateAsset(material, path); }
            material.mainTexture = texture; EditorUtility.SetDirty(material); return material;
        }

        static void PreparePrimitiveMeshes()
        {
            if (!cube) cube = PrimitiveMesh(PrimitiveType.Cube);
            if (!cylinder) cylinder = PrimitiveMesh(PrimitiveType.Cylinder);
            if (!plane)
            {
                plane = new Mesh { name = "PolishTemporaryPlane", hideFlags = HideFlags.HideAndDontSave };
                plane.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0) };
                plane.uv = new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one };
                plane.triangles = new[] { 0, 1, 2, 2, 1, 3 }; plane.RecalculateNormals();
            }
        }
        static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            go.hideFlags = HideFlags.HideAndDontSave;
            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return mesh;
        }
        static void Part(string category, Mesh mesh, Vector3 position, Vector3 scale, Quaternion rotation, Material material)
        {
            string key = category + "_" + material.name;
            if (!batches.TryGetValue(key, out GeometryBatch batch))
                batches[key] = batch = new GeometryBatch { category = category, material = material };
            batch.parts.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(position, rotation, scale) });
        }
        static void Box(string category, Vector3 position, Vector3 scale, Material material, Quaternion? rotation = null)
            => Part(category, cube, position, scale, rotation ?? Quaternion.identity, material);
        static void Cylinder(string category, Vector3 position, float radius, float height, Material material, Quaternion? rotation = null)
            => Part(category, cylinder, position, new Vector3(radius * 2, height * .5f, radius * 2), rotation ?? Quaternion.identity, material);
        static void Rod(string category, Vector3 a, Vector3 b, float radius, Material material)
            => Cylinder(category, (a + b) * .5f, radius, Vector3.Distance(a, b), material, Quaternion.FromToRotation(Vector3.up, b - a));
        static Vector3 P(Vector3 position, Quaternion rotation, float x, float y, float z) => position + rotation * new Vector3(x, y, z);
        static void LocalBox(string category, Vector3 position, Quaternion rotation, Vector3 offset, Vector3 scale, Material material)
            => Box(category, position + rotation * offset, scale, material, rotation);
        static void Panel(string category, Vector3 position, Quaternion rotation, Vector2 size, Material material)
        {
            LocalBox(category, position, rotation, Vector3.zero, new Vector3(size.x, size.y, .046f), material);
            foreach (float x in new[] { -1f, 1f }) foreach (float y in new[] { -1f, 1f })
                Cylinder(category, P(position, rotation, x * (size.x * .5f - .045f), y * (size.y * .5f - .045f), -.026f),
                    .009f, .005f, M("Metal"), rotation * Quaternion.Euler(90, 0, 0));
        }
        static void Text(string name, string category, string text, Vector3 position, Quaternion rotation, float height, Color color, bool left = false)
            => labels.Add(new SignText { name = name, category = category, text = text, position = position,
                rotation = rotation, height = height, color = color, left = left });

        static void BakeFontAtlas(Font font)
        {
            string alphabet = new string(("机H " + string.Join("", labels.Select(l => l.text))).Where(c => c != '\n' && c != '\r').Distinct().ToArray());
            font.RequestCharactersInTexture(alphabet, FontSize, FontStyle.Normal);
            foreach (char c in alphabet)
            {
                if (!font.GetCharacterInfo(c, out CharacterInfo info, FontSize, FontStyle.Normal))
                    throw new InvalidOperationException("The bundled Chinese font is missing sign glyph U+" + ((int)c).ToString("X4") + ": " + c);
                glyphs[c] = info;
            }
            characterHeight = Mathf.Max(1, glyphs['机'].maxY - glyphs['机'].minY);
            Texture source = font.material.mainTexture;
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            var pixels = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(source, target); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); pixels.Apply(false, false);
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
            string path = AssetsRoot + "/ChineseSignAtlas.asset";
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!atlas) { atlas = pixels; AssetDatabase.CreateAsset(atlas, path); }
            else { EditorUtility.CopySerialized(pixels, atlas); Object.DestroyImmediate(pixels); }
            atlas.name = "ChineseSignAtlas";
            atlas.wrapMode = TextureWrapMode.Clamp; atlas.filterMode = FilterMode.Bilinear; EditorUtility.SetDirty(atlas);
            path = AssetsRoot + "/ChineseSignText.mat";
            textMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!textMaterial) { textMaterial = new Material(Shader.Find("RoboticsLab/DepthTestedText")) { name = "ChineseSignText" }; AssetDatabase.CreateAsset(textMaterial, path); }
            textMaterial.mainTexture = atlas; textMaterial.color = Color.white; EditorUtility.SetDirty(textMaterial);
        }

        static void BuildLabel(Transform root, Dictionary<string, Transform> groups, SignText label)
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>();
            var triangles = new List<int>(); var colors = new List<Color>();
            float scale = label.height / characterHeight;
            string[] lines = label.text.Split('\n');
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            for (int line = 0; line < lines.Length; line++)
            {
                float width = lines[line].Sum(c => glyphs[c].advance);
                float x = label.left ? 0 : -width * .5f;
                foreach (char c in lines[line])
                {
                    CharacterInfo g = glyphs[c];
                    float y = -line * characterHeight * 1.6f;
                    if (!char.IsWhiteSpace(c))
                    {
                        int v = vertices.Count;
                        vertices.Add(new Vector3(x + g.minX, y + g.minY, 0) * scale);
                        vertices.Add(new Vector3(x + g.minX, y + g.maxY, 0) * scale);
                        vertices.Add(new Vector3(x + g.maxX, y + g.minY, 0) * scale);
                        vertices.Add(new Vector3(x + g.maxX, y + g.maxY, 0) * scale);
                        uv.Add(g.uvBottomLeft); uv.Add(g.uvTopLeft); uv.Add(g.uvBottomRight); uv.Add(g.uvTopRight);
                        triangles.AddRange(new[] { v, v + 1, v + 2, v + 2, v + 1, v + 3 });
                        for (int j = 0; j < 4; j++) colors.Add(label.color);
                        minY = Mathf.Min(minY, (y + g.minY) * scale); maxY = Mathf.Max(maxY, (y + g.maxY) * scale);
                    }
                    x += g.advance;
                }
            }
            float centerY = vertices.Count > 0 ? (minY + maxY) * .5f : 0;
            for (int i = 0; i < vertices.Count; i++) vertices[i] -= Vector3.up * centerY;
            Mesh mesh = PersistentMesh("Text_" + label.name);
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.SetColors(colors);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            var go = new GameObject(label.name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(Category(root, groups, label.category), false);
            go.transform.localPosition = label.position; go.transform.localRotation = label.rotation;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            ConfigureRenderer(go.GetComponent<MeshRenderer>(), textMaterial); go.isStatic = true;
        }

        static Mesh PersistentMesh(string name)
        {
            string path = AssetsRoot + "/" + name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh) { mesh = new Mesh { name = name }; AssetDatabase.CreateAsset(mesh, path); }
            else mesh.Clear();
            return mesh;
        }
        static Transform Category(Transform root, Dictionary<string, Transform> groups, string name)
        {
            if (!groups.TryGetValue(name, out Transform group))
            {
                group = new GameObject(name).transform; group.SetParent(root, false); groups[name] = group;
            }
            return group;
        }
        static void ConfigureRenderer(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true; renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            Folder(parent); AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
        }

        [MenuItem("Robot Lab/Capture/Room Polish Before And After")]
        public static void CaptureComparison()
        {
            GameObject polish = SceneManager.GetActiveScene().GetRootGameObjects().SingleOrDefault(g => g.name == RootName);
            if (!polish) throw new InvalidOperationException("Run Polish Training Room first; comparison toggles only its independent detail layer.");
            bool wasActive = polish.activeSelf;
            try
            {
                polish.SetActive(false); CaptureViews("Before");
                polish.SetActive(true); CaptureViews("After");
            }
            finally { polish.SetActive(wasActive); }
            Debug.Log("ROBOT_ROOM_POLISH_COMPARISON_READY Documentation/Screenshots/RoomPolish_*.png");
        }

        [MenuItem("Robot Lab/Capture/Room Polish Current")]
        public static void CaptureCurrent() => CaptureViews("Current");

        static void CaptureViews(string state)
        {
            Capture(state + "_Overview", new Vector3(6, 3.15f, 1.5f), new Vector3(-2, 1.65f, 11.3f), 68);
            Capture(state + "_Teaching", new Vector3(.9f, 2.5f, 5.5f), new Vector3(7.4f, 2.1f, 11.05f), 69);
            Capture(state + "_Entry", new Vector3(2.4f, 2.35f, 6.2f), new Vector3(-.7f, 1.8f, .35f), 72);
            Capture(state + "_Workcells", new Vector3(1.6f, 2.6f, 3), new Vector3(-5.2f, 1.8f, 11.2f), 62);
        }

        static void Capture(string name, Vector3 position, Vector3 target, float fieldOfView)
        {
            Directory.CreateDirectory("Documentation/Screenshots");
            var go = new GameObject("RoomPolishTemporaryCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            camera.fieldOfView = fieldOfView; camera.nearClipPlane = .08f; camera.farClipPlane = 90;
            camera.clearFlags = CameraClearFlags.Skybox; camera.allowHDR = false;
            var texture = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
            var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply();
                File.WriteAllBytes("Documentation/Screenshots/RoomPolish_" + name + ".png", pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null; texture.Release();
                Object.DestroyImmediate(pixels); Object.DestroyImmediate(texture); Object.DestroyImmediate(go);
            }
        }
    }
}
