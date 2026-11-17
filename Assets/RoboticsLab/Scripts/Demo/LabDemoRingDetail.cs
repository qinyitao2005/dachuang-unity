using UnityEngine;

namespace RoboticsLab
{
    /// <summary>A recording-only second real camera, making the small gripper and wire readable in the film.</summary>
    public sealed class LabDemoRingDetail : MonoBehaviour
    {
        Camera detailCamera;
        RenderTexture texture;
        Font font;
        GUIStyle label;

        public void Initialize(LabRobotStation station, Font uiFont)
        {
            font = uiFont;
            Bounds bounds = new Bounds(station.transform.TransformPoint(station.wirePoints[0]), Vector3.zero);
            foreach (Vector3 p in station.wirePoints) bounds.Encapsulate(station.transform.TransformPoint(p));
            bounds.Expand(station.ringRadius * 2.5f);
            texture = new RenderTexture(640, 900, 24, RenderTextureFormat.ARGB32);
            detailCamera = new GameObject("RecordingRingCloseupCamera").AddComponent<Camera>();
            detailCamera.CopyFrom(Camera.main);
            detailCamera.targetTexture = texture; detailCamera.rect = new Rect(0, 0, 1, 1);
            detailCamera.fieldOfView = 36; detailCamera.aspect = 640f / 900f;
            detailCamera.nearClipPlane = .01f; detailCamera.farClipPlane = 15;
            float distance = bounds.extents.magnitude / Mathf.Tan(18 * Mathf.Deg2Rad) / detailCamera.aspect;
            Vector3 normal = station.ringRoot ? station.ringRoot.forward : station.transform.forward;
            if (Vector3.Dot(normal, Camera.main.transform.position - bounds.center) < 0) normal = -normal;
            Vector3 direction = (normal + Vector3.up * .3f).normalized;
            detailCamera.transform.position = bounds.center + direction * distance;
            detailCamera.transform.LookAt(bounds.center); detailCamera.enabled = true;
        }

        void OnGUI()
        {
            if (!texture) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 21, fontStyle = FontStyle.Bold,
                    wordWrap = true, clipping = TextClipping.Overflow };
                label.normal.textColor = Color.white;
            }
            GUI.depth = -400; var matrix = GUI.matrix; var color = GUI.color;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / 1600f, Screen.height / 900f, 1));
            GUI.color = new Color(.035f, .075f, .11f, .98f); GUI.DrawTexture(new Rect(1200, 156, 375, 630), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(1216, 172, 345, 34), "圆环特写 / 实际运动", label);
            GUI.DrawTexture(new Rect(1216, 218, 343, 482), texture, ScaleMode.ScaleToFit, false);
            var detail = new GUIStyle(label) { fontSize = 17, fontStyle = FontStyle.Normal };
            GUI.Label(new Rect(1216, 720, 343, 56), "夹具夹住圈缘，沿钢丝运动\n同场景第二机位实时画面", detail);
            GUI.color = color; GUI.matrix = matrix;
        }
        void OnDestroy()
        {
            if (detailCamera) Destroy(detailCamera.gameObject);
            if (texture) { texture.Release(); Destroy(texture); }
        }
    }
}
