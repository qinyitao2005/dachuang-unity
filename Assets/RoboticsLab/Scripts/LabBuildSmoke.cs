using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace RoboticsLab
{
    // Only runs when explicitly launched with -robot-smoke; normal players never see it.
    public class LabBuildSmoke : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-robot-smoke")>=0)
                new GameObject("BuildSmokeValidation").AddComponent<LabBuildSmoke>();
        }
        IEnumerator Start()
        {
            yield return new WaitForSeconds(2);
            string folder=Path.GetDirectoryName(Application.dataPath);
            var app=FindObjectOfType<LabTrainingApp>();
            bool scene=app && app.player && app.stations!=null && app.stations.Length==3;
            bool font=app && app.uiFont;
            bool shader=app && app.videoScreenMaterial && app.videoScreenMaterial.shader.isSupported;
            if(scene){var p=app.helmetPickup.position;app.player.Teleport(new Vector3(p.x-.85f,.08f,p.z),90);app.EquipHelmet();}
            yield return new WaitForSeconds(3.5f);
            if(scene){app.player.Teleport(new Vector3(4.9f,.08f,10.5f),90);app.OpenLesson();}
            yield return new WaitForSeconds(4);
            bool video=app && app.lessonVideo && app.lessonVideo.isPrepared && app.lessonVideo.frame>0;
            bool helmet=app && app.player.wornHelmet && app.player.wornHelmet.activeInHierarchy;
            // A hidden Windows swap chain may return a black ScreenCapture despite a
            // running scene. Render the actual player camera into an explicit target.
            bool rendered=scene&&CaptureCamera(app.player.viewCamera,Path.Combine(folder,"StandaloneSmoke.png"));
            yield return new WaitForSeconds(1);
            bool wall=app&&app.wallLearningDisplay&&app.wallLearningDisplay.GetComponentInChildren<Canvas>()&&app.wallLearningDisplay.GetComponentInChildren<Canvas>().renderMode==RenderMode.WorldSpace;
            System.Collections.Generic.List<LabTrajectoryStep> steps;string parseError;
            bool programming=app&&app.trajectoryProgram&&app.trajectoryPanel
                &&LabTrajectoryParser.TryParse("SPEED 60\nPEN UP\nMOVE 10 20 30\nWAIT 0.2",Vector3.zero,out steps,out parseError)&&steps.Count==4;
            var details=GameObject.Find("RoomPolish");
            bool room=details&&details.transform.Find("EntranceLobby")&&details.GetComponentsInChildren<MeshRenderer>().Length>40;
            bool grip=scene,robotWire=scene;
            if(scene)foreach(var station in app.stations)
            {
                station.BeginOperation();station.SetMode(1);
                yield return null;
                string reason;float gap;
                var collision=station.GetComponent<LabRobotWireCollision>();
                grip&=station.ringGripTcp&&station.ringGripTool&&station.ringGripTool.gameObject.activeInHierarchy
                    &&station.ringSolidRenderer&&station.ringSolidRenderer.enabled&&!station.ringRenderer.enabled&&!station.wireRenderer.enabled
                    &&station.wireRoot.Find("SteelGuideSolid")
                    &&Mathf.Abs(Vector3.Distance(station.ActualToolPosition,station.RingGripPosition)-station.ringRadius)<.00005f
                    &&Vector3.Distance(station.ringRoot.TransformPoint(Vector3.up*station.ringRadius),station.RingGripPosition)<.00005f;
                robotWire&=collision&&collision.Check(out reason,out gap);
                station.EndOperation();
            }
            bool passed=scene&&font&&shader&&video&&helmet&&wall&&rendered&&programming&&room&&grip&&robotWire;
            string report="{\"passed\":"+passed.ToString().ToLowerInvariant()+",\"scene\":"+scene.ToString().ToLowerInvariant()+",\"bundledChineseFont\":"+font.ToString().ToLowerInvariant()+",\"videoShader\":"+shader.ToString().ToLowerInvariant()+",\"videoDecoded\":"+video.ToString().ToLowerInvariant()+",\"helmet\":"+helmet.ToString().ToLowerInvariant()+",\"wallLearning\":"+wall.ToString().ToLowerInvariant()+",\"unityVersion\":\""+Application.unityVersion+"\"}";
            report=report.Substring(0,report.Length-1)+",\"cameraRendered\":"+rendered.ToString().ToLowerInvariant()+"}";
            report=report.Substring(0,report.Length-1)+",\"trajectoryProgramming\":"+programming.ToString().ToLowerInvariant()+",\"roomDetails\":"+room.ToString().ToLowerInvariant()+"}";
            report=report.Substring(0,report.Length-1)+",\"ringRimGrip\":"+grip.ToString().ToLowerInvariant()+",\"robotWireClearance\":"+robotWire.ToString().ToLowerInvariant()+"}";
            File.WriteAllText(Path.Combine(folder,"StandaloneSmoke.json"),report);
            Debug.Log("ROBOT_STANDALONE_SMOKE "+report);
            Application.Quit(passed?0:2);
        }

        static bool CaptureCamera(Camera camera,string path)
        {
            if(!camera)return false;
            var target=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);
            var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
            var previousTarget=camera.targetTexture;
            var previousActive=RenderTexture.active;
            try
            {
                camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());
                var pixels=image.GetPixels32();int visible=0;
                for(int i=0;i<pixels.Length;i+=257)
                    if(pixels[i].r>16||pixels[i].g>16||pixels[i].b>16)visible++;
                return visible>pixels.Length/257/10;
            }
            finally
            {
                camera.targetTexture=previousTarget;RenderTexture.active=previousActive;
                target.Release();Destroy(target);Destroy(image);
            }
        }
    }
}
