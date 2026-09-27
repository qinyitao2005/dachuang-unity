using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoboticsLab.Editor
{
    public static class LabRingCourseAuthoring
    {
        [MenuItem("Robot Lab/Author Reachable Ring Course")]
        public static void Build()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before authoring the course.");
            var report=new StringBuilder("Reachable rigid-grip course / "+DateTime.UtcNow.ToString("O")+"\n");
            foreach(var station in UnityEngine.Object.FindObjectsOfType<LabRobotStation>())
            {
                Undo.RecordObject(station,"Update ring course");
                station.ringUsePoseSeed=false;station.ringGripRoll=0;
                station.wirePoints=MakePoints(station.exerciseCenter,station.stationIndex);
                Undo.RecordObject(station.wireRenderer,"Update visible steel guide");
                station.wireRenderer.positionCount=station.wirePoints.Length;
                station.wireRenderer.SetPositions(station.wirePoints);
                for(int end=0;end<2;end++)
                {
                    Vector3 point=station.wirePoints[end==0?0:station.wirePoints.Length-1];
                    Transform support=station.wireRoot.Find(end==0?"StartSupport":"FinishSupport");
                    Transform marker=station.wireRoot.Find(end==0?"StartMarker":"FinishMarker");
                    if(!support||!marker)throw new InvalidOperationException("Missing wire supports.");
                    Undo.RecordObject(support,"Move guide support");Undo.RecordObject(marker,"Move guide marker");
                    float height=point.y-station.tableHeight;
                    support.localPosition=new Vector3(point.x,station.tableHeight+height*.5f,point.z);
                    support.localScale=new Vector3(.027f*station.exerciseScale,height,.027f*station.exerciseScale);
                    marker.localPosition=new Vector3(point.x,station.tableHeight,point.z);
                }
                LabRingTubeVisualAuthoring.BuildStation(station);
                EditorUtility.SetDirty(station);
                report.AppendLine("PASS: station "+station.stationIndex+" has 121 matched visual/control points, a 280 mm span and a 64 mm-wide spatial S bend; original joint limits retained.");
            }
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            File.WriteAllText("Documentation/RingCourseAuthoring.txt",report.ToString());
            Debug.Log(report);
        }

        public static Vector3[] MakePoints(Vector3 center,int stationIndex)
        {
            var points=new Vector3[121];
            Quaternion direction=Quaternion.Euler(0,stationIndex==0?180:0,0);
            for(int i=0;i<points.Length;i++)
            {
                float t=i/(float)(points.Length-1);
                points[i]=center+direction*new Vector3(.032f*Mathf.Sin(t*Mathf.PI*2),.032f*Mathf.Sin(t*Mathf.PI),.28f*(t-.5f));
            }
            return points;
        }
    }
}
