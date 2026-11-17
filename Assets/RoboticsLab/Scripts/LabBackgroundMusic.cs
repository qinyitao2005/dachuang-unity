using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoboticsLab
{
    /// <summary>
    /// Scene-independent room music. The existing settings menu controls the final
    /// AudioListener volume; this component only controls the music balance.
    /// </summary>
    [DefaultExecutionOrder(350)]
    public sealed class LabBackgroundMusic : MonoBehaviour
    {
        public const string ResourcePath = "RoboticsLabAudio/RoboticsTrainingGrounds";
        public const string ObjectName = "LabBackgroundMusic";

        [SerializeField, Range(0f, 1f)] float roomVolume = .24f;
        [SerializeField, Range(0f, 1f)] float videoVolume = .085f;
        [SerializeField, Min(.1f)] float fadeInSeconds = 2f;
        [SerializeField, Min(.1f)] float crossfadeSeconds = 2f;

        static LabBackgroundMusic instance;
        readonly AudioSource[] sources = new AudioSource[2];
        LabTrainingApp app;
        AudioClip track;
        int current;
        double currentEndDsp;
        double nextStartDsp;
        bool nextScheduled;
        bool ready;
        float gain;

        public AudioSource[] Sources => sources;
        public AudioClip Track => track;
        public float CurrentGain => gain;
        public bool IsDucked { get; private set; }
        public int CompletedLoops { get; private set; }
        public double NextLoopDspTime => nextStartDsp;
        public float CrossfadeSeconds => track ? Mathf.Min(crossfadeSeconds, track.length * .25f) : crossfadeSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetInstance() { instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (instance || FindObjectOfType<LabBackgroundMusic>() || !FindObjectOfType<LabTrainingApp>()) return;
            new GameObject(ObjectName).AddComponent<LabBackgroundMusic>();
        }

        void Awake()
        {
            if (instance && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            app = FindObjectOfType<LabTrainingApp>();
            SceneManager.sceneLoaded += OnSceneLoaded;
            track = Resources.Load<AudioClip>(ResourcePath);
            if (!track || track.length <= 0f)
            {
                Debug.LogWarning("Room BGM is missing: Resources/" + ResourcePath, this);
                enabled = false;
                return;
            }

            for (int i = 0; i < sources.Length; i++)
            {
                var child = new GameObject("MusicSource" + (i + 1));
                child.transform.SetParent(transform, false);
                var source = child.AddComponent<AudioSource>();
                source.clip = track;
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.dopplerLevel = 0f;
                source.bypassReverbZones = true;
                source.ignoreListenerPause = true;
                source.volume = 0f;
                sources[i] = source;
            }
            ready = true;
        }

        void OnEnable()
        {
            if (!ready) return;
            gain = 0f;
            Seek(0f);
        }

        void OnDisable()
        {
            foreach (var source in sources) if (source) source.Stop();
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (instance == this) instance = null;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            app = FindObjectOfType<LabTrainingApp>();
        }

        /// <summary>
        /// Seek within the original recording and rebuild both scheduled sources.
        /// This also allows validating the real recording's loop boundary without
        /// waiting three minutes. It does not change global volume or saved settings.
        /// </summary>
        public bool Seek(float seconds)
        {
            if (!ready || !isActiveAndEnabled || float.IsNaN(seconds) || float.IsInfinity(seconds)) return false;
            foreach (var source in sources) source.Stop();
            current = 0;
            nextScheduled = false;
            seconds = Mathf.Clamp(seconds, 0f, Mathf.Max(0f, track.length - .02f));
            double playbackStart = AudioSettings.dspTime + .15;
            currentEndDsp = playbackStart + track.length - seconds;
            nextStartDsp = currentEndDsp - CrossfadeSeconds;
            sources[current].time = seconds;
            sources[current].PlayScheduled(playbackStart);
            // A seek into the overlap starts both streams at their matching offsets.
            if (nextStartDsp <= playbackStart) ScheduleNext(playbackStart);
            ApplySourceGains(AudioSettings.dspTime);
            return true;
        }

        void Update()
        {
            if (!ready) return;
            IsDucked = app && (app.DemoVideoVisible || app.panel == "lesson");
            float target = IsDucked ? videoVolume : roomVolume;
            float fadeRate = Mathf.Max(.01f, roomVolume) / (IsDucked ? .8f : fadeInSeconds);
            gain = Mathf.MoveTowards(gain, target, Time.unscaledDeltaTime * fadeRate);
            double now = AudioSettings.dspTime;

            if (now >= currentEndDsp)
            {
                // A long suspended application may miss the scheduling window.
                // Resume the track cleanly instead of keeping a silent stopped source.
                if (!nextScheduled || now >= nextStartDsp + track.length)
                {
                    CompletedLoops++;
                    Seek(0f);
                    return;
                }
                sources[current].Stop();
                current = 1 - current;
                currentEndDsp = nextStartDsp + track.length;
                nextStartDsp = currentEndDsp - CrossfadeSeconds;
                nextScheduled = false;
                CompletedLoops++;
            }

            // Allow the streaming decoder time to prepare before its DSP start.
            if (!nextScheduled && now >= nextStartDsp - 1.0) ScheduleNext(now + .03);
            ApplySourceGains(now);
        }

        void ScheduleNext(double earliestStart)
        {
            var source = sources[1 - current];
            double start = System.Math.Max(nextStartDsp, earliestStart);
            source.time = Mathf.Clamp((float)(start - nextStartDsp), 0f, track.length - .02f);
            source.volume = 0f;
            source.PlayScheduled(start);
            nextScheduled = true;
        }

        void ApplySourceGains(double now)
        {
            float mix = nextScheduled ? Mathf.Clamp01((float)((now - nextStartDsp) / CrossfadeSeconds)) : 0f;
            // Equal-power crossfade keeps the seam audible at the same soft balance.
            sources[current].volume = gain * Mathf.Cos(mix * Mathf.PI * .5f);
            sources[1 - current].volume = gain * Mathf.Sin(mix * Mathf.PI * .5f);
        }
    }
}
