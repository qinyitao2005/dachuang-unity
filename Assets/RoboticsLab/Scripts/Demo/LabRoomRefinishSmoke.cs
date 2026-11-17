using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>Opt-in real-scene photography and room/audio acceptance. Inactive in normal play.</summary>
    public sealed class LabRoomRefinishSmoke : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public bool passed;
            public string unityVersion, exception;
            public float wallSeconds;
            public List<string> checks = new List<string>();
            public List<string> screenshots = new List<string>();
            public List<string> cameraPoses = new List<string>();
        }

        readonly Report report = new Report();
        LabTrainingApp app;
        string output;
        float started, originalVolume;
        bool finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--room-refinish-smoke") < 0) return;
            if (!FindObjectOfType<LabRoomRefinishSmoke>())
                new GameObject("RoomRefinishSmoke").AddComponent<LabRoomRefinishSmoke>();
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            started = Time.realtimeSinceStartup;
            originalVolume = AudioListener.volume;
            output = ResolveOutput();
            report.unityVersion = Application.unityVersion;
            IEnumerator sequence = Flatten(Checks());
            while (true)
            {
                bool next = false; object waiting = null;
                try { next = sequence.MoveNext(); if (next) waiting = sequence.Current; }
                catch (Exception error) { report.exception = error.ToString(); Debug.LogException(error); }
                if (!next) break;
                yield return waiting;
            }
            Finish();
        }

        void Update()
        {
            if (!finished && started > 0 && Time.realtimeSinceStartup - started > 75)
            {
                report.exception = "Room refinish smoke exceeded 75 seconds.";
                StopAllCoroutines(); Finish();
            }
        }

        IEnumerator Checks()
        {
            float until = Time.realtimeSinceStartup + 15;
            while (!(app = FindObjectOfType<LabTrainingApp>()) && Time.realtimeSinceStartup < until) yield return null;
            Require(app && app.stations != null && app.stations.Length == 3, "Normal scene retains exactly three assigned robot stations.");
            while (!UnityEngine.Rendering.SplashScreen.isFinished && Time.realtimeSinceStartup - started < 20) yield return null;
            yield return new WaitForSecondsRealtime(1);
            Require(app.player && app.teachingPoint && app.wallLearningDisplay && app.lessonVideo,
                "Player, physical learning point, wall lesson UI and video remain assigned.");
            Require(app.helmetPickup && app.helmetPickup.gameObject.activeInHierarchy,
                "Original helmet pickup is active and still assigned.");
            Require(app.QuestionCount == 20, "Twenty-question bank is still loaded.");
            var models = new HashSet<Transform>();
            foreach (LabRobotStation station in app.stations)
            {
                Require(station && station.modelRoot && models.Add(station.modelRoot)
                    && station.modelRoot.GetComponentsInChildren<Renderer>(true).Length > 20,
                    "Each station retains its distinct original detailed equipment model.");
            }

            Require(GameObject.Find("InteriorRefinish"), "Finished interior layer is present in the normal scene.");
            yield return CheckMusic();

            // These are actual in-room camera photographs. No geometry, lights, materials,
            // post-processing or gameplay/UI settings are changed to stage the result.
            yield return Capture("RoomRefinish_01_Entrance.png", new Vector3(1.0f, 2.02f, 2.25f), new Vector3(-1.0f, 1.95f, 11.2f), 67);
            yield return Capture("RoomRefinish_02_Workstations.png", new Vector3(1.5f, 2.35f, 5.8f), new Vector3(-4.2f, 1.72f, 12.5f), 66);
            yield return Capture("RoomRefinish_03_LearningWall.png", new Vector3(1.7f, 2.18f, 8.5f), new Vector3(7.5f, 2.35f, 10.7f), 64);
            yield return Capture("RoomRefinish_04_Overview.png", new Vector3(5.6f, 3.55f, 20.25f), new Vector3(-2.4f, 1.8f, 8.0f), 68);
        }

        IEnumerator CheckMusic()
        {
            var music = FindObjectOfType<LabBackgroundMusic>();
            Require(music && music.Track && music.Sources != null && music.Sources.Length == 2,
                "User BGM is installed with two streaming playback sources.");
            Require(music.Track.loadType == AudioClipLoadType.Streaming && music.Track.length > 180 && music.Track.length < 185,
                "The supplied full 182-second track uses streaming import.");
            yield return new WaitForSecondsRealtime(3);
            originalVolume = AudioListener.volume;
            AudioListener.volume = .65f;
            yield return new WaitForSecondsRealtime(.25f);
            float sourcePeak = 0, listenerPeak = 0;
            for (int i = 0; i < 10; i++)
            {
                sourcePeak = Mathf.Max(sourcePeak, MusicPeak(music.Sources, false));
                listenerPeak = Mathf.Max(listenerPeak, MusicPeak(music.Sources, true));
                yield return new WaitForSecondsRealtime(.06f);
            }
            Require(sourcePeak > .00001f && listenerPeak > .00001f,
                "Real BGM samples: sourcePeak=" + sourcePeak + "; listenerPeak=" + listenerPeak);
            AudioListener.volume = 0;
            yield return new WaitForSecondsRealtime(.3f);
            float mutedPeak = MusicPeak(music.Sources, true);
            Require(mutedPeak < .000001f, "Global volume zero silences listener samples; peak=" + mutedPeak);
            AudioListener.volume = .65f;
            AudioSource playing = null;
            foreach (AudioSource source in music.Sources) if (source && source.isPlaying) { playing = source; break; }
            Require(playing, "An audible source is active before pause check.");
            int sampleBefore = playing.timeSamples;
            float previousScale = Time.timeScale;
            try
            {
                Time.timeScale = 0;
                yield return new WaitForSecondsRealtime(.45f);
                Require(playing.isPlaying && playing.timeSamples != sampleBefore && MusicPeak(music.Sources, false) > .00001f,
                    "Music advances with real output while gameplay timeScale is zero.");
            }
            finally { Time.timeScale = previousScale; }

            // Exercise the real app state used by video viewers without editing preferences.
            string originalPanel = app.panel;
            try
            {
                app.panel = "lesson";
                yield return new WaitForSecondsRealtime(1.3f);
                Require(music.IsDucked && music.CurrentGain < .12f, "Wall-video state smoothly ducks background music.");
            }
            finally { app.panel = originalPanel; }
            yield return new WaitForSecondsRealtime(1.8f);
            Require(!music.IsDucked && music.CurrentGain > .22f, "Music returns to room balance after video closes.");
            int loopsBefore = music.CompletedLoops;
            Require(music.Seek(music.Track.length - 3f), "Seek schedules the real end-of-track crossfade.");
            yield return new WaitForSecondsRealtime(4);
            float loopPeak = 0;
            for (int i = 0; i < 10; i++)
            {
                loopPeak = Mathf.Max(loopPeak, MusicPeak(music.Sources, true));
                yield return new WaitForSecondsRealtime(.06f);
            }
            Require(music.CompletedLoops > loopsBefore && loopPeak > .00001f,
                "Crossfade hands off with actual output; loops=" + music.CompletedLoops + "; peak=" + loopPeak);
            AudioListener.volume = originalVolume;
        }

        static float MusicPeak(AudioSource[] sources, bool listener)
        {
            var samples = new float[1024];
            float peak = 0;
            if (listener)
            {
                AudioListener.GetOutputData(samples, 0);
                foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
            }
            else foreach (AudioSource source in sources)
            {
                if (!source || !source.isPlaying) continue;
                source.GetOutputData(samples, 0);
                foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
            }
            return peak;
        }

        IEnumerator Capture(string name, Vector3 position, Vector3 lookAt, float fieldOfView)
        {
            yield return new WaitForEndOfFrame();
            string directory = Path.Combine(Path.GetDirectoryName(output), "Screenshots");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name);
            var go = new GameObject("RoomEvidenceCamera");
            var camera = go.AddComponent<Camera>();
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            RenderTexture oldActive = RenderTexture.active;
            try
            {
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.backgroundColor = new Color(.8f, .85f, .9f);
                camera.nearClipPlane = .05f; camera.farClipPlane = 100;
                camera.fieldOfView = fieldOfView; camera.aspect = 16f / 9f;
                camera.allowHDR = true; camera.allowMSAA = true;
                camera.transform.position = position; camera.transform.LookAt(lookAt);
                camera.targetTexture = target; camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
                float minimum = 1, maximum = 0;
                for (int y = 0; y < 18; y++) for (int x = 0; x < 32; x++)
                {
                    float value = image.GetPixel(x * 60 + 30, y * 60 + 30).grayscale;
                    minimum = Mathf.Min(minimum, value); maximum = Mathf.Max(maximum, value);
                }
                Require(maximum - minimum > .2f && maximum > .35f, "Rendered room image contains visible scene detail: " + name);
                File.WriteAllBytes(path, image.EncodeToPNG());
                report.screenshots.Add(path);
                report.cameraPoses.Add(name + ": position=" + position + "; lookAt=" + lookAt + "; FOV=" + fieldOfView);
            }
            finally
            {
                RenderTexture.active = oldActive;
                Destroy(image); Destroy(target); Destroy(go);
            }
        }

        void Require(bool valid, string message)
        {
            report.checks.Add((valid ? "PASS " : "FAIL ") + message);
            if (!valid) throw new InvalidOperationException(message);
        }

        void Finish()
        {
            if (finished) return;
            finished = true; AudioListener.volume = originalVolume;
            report.passed = string.IsNullOrEmpty(report.exception) && report.screenshots.Count == 4;
            report.wallSeconds = Time.realtimeSinceStartup - started;
            try { Directory.CreateDirectory(Path.GetDirectoryName(output)); File.WriteAllText(output, JsonUtility.ToJson(report, true)); }
            catch (Exception error) { report.passed = false; Debug.LogException(error); }
            Debug.Log("ROOM_REFINISH_SMOKE=" + report.passed + "; report=" + output);
            Application.Quit(report.passed ? 0 : 1);
        }

        static string ResolveOutput()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--room-refinish-output") return Path.GetFullPath(args[i + 1]);
            var directory = new DirectoryInfo(Application.dataPath);
            for (int i = 0; directory != null && i < 7; i++, directory = directory.Parent)
                if (Directory.Exists(Path.Combine(directory.FullName, "Assets")) && Directory.Exists(Path.Combine(directory.FullName, "ProjectSettings")))
                    return Path.Combine(directory.FullName, "Documentation", "RoomRefinishSmoke.json");
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), "RoomRefinishSmoke.json");
        }

        static IEnumerator Flatten(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(root);
            while (stack.Count > 0)
            {
                IEnumerator current = stack.Peek();
                if (!current.MoveNext()) { (current as IDisposable)?.Dispose(); stack.Pop(); continue; }
                if (current.Current is IEnumerator nested) stack.Push(nested); else yield return current.Current;
            }
        }
    }
}
