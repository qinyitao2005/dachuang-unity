using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RobotTeaching.Editor
{
    public static class TrajectoryMenuCreator
    {
        [MenuItem("GameObject/机器人教学/机械臂循迹菜单", false, 10)]
        public static void Create()
        {
            var root = new GameObject("机械臂循迹菜单");
            var menu = root.AddComponent<RobotTrajectoryMenu>();
            menu.Build();
            Undo.RegisterCreatedObjectUndo(root, "创建机械臂循迹菜单");
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
        }
    }
}
