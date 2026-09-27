using System.Collections.Generic;
using System.IO;
using UnityEngine;

[System.Serializable]
public class TransformData
{
    public string path;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;
}

[System.Serializable]
public class TransformDataList
{
    public List<TransformData> items = new List<TransformData>();
}

public class ArmPoseSaver : MonoBehaviour
{
    public string fileName = "arm_pose.json";

    [ContextMenu("Save Pose")]
    public void Save()
    {
        TransformDataList list = new TransformDataList();
        Collect(transform, list);

        string json = JsonUtility.ToJson(list, true);
        string path = Path.Combine(Application.persistentDataPath, fileName);
        File.WriteAllText(path, json);
        Debug.Log("Pose saved: " + path);
    }

    [ContextMenu("Load Pose")]
    public void Load()
    {
        string path = Path.Combine(Application.persistentDataPath, fileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning("No pose file: " + path);
            return;
        }

        TransformDataList list = JsonUtility.FromJson<TransformDataList>(File.ReadAllText(path));
        foreach (TransformData data in list.items)
        {
            Transform t = transform.Find(data.path);
            if (t == null)
                continue;
            t.localPosition = data.position;
            t.localRotation = data.rotation;
            t.localScale = data.scale;
        }
        Debug.Log("Pose loaded: " + path);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5))
            Save();
        if (Input.GetKeyDown(KeyCode.F9))
            Load();
    }

    static void Collect(Transform current, TransformDataList list)
    {
        list.items.Add(new TransformData
        {
            path = GetPath(current),
            position = current.localPosition,
            rotation = current.localRotation,
            scale = current.localScale
        });
        foreach (Transform child in current)
            Collect(child, list);
    }

    static string GetPath(Transform t)
    {
        string rootName = "";
        Transform cur = t;
        while (cur != null)
        {
            rootName = cur.name + (rootName == "" ? "" : "/" + rootName);
            cur = cur.parent;
        }
        return rootName;
    }
}
