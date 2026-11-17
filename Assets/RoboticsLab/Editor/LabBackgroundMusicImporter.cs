using UnityEditor;
using UnityEngine;

namespace RoboticsLab.Editor
{
    /// <summary>Keep the supplied three-minute track compressed and streamed in builds.</summary>
    public sealed class LabBackgroundMusicImporter : AssetPostprocessor
    {
        public const string AudioPath = "Assets/RoboticsLab/Resources/RoboticsLabAudio/RoboticsTrainingGrounds.wav";

        void OnPreprocessAudio()
        {
            if (assetPath == AudioPath) Configure((AudioImporter)assetImporter);
        }

        [MenuItem("Robotics Lab/Audio/Configure Supplied Background Music")]
        public static void ConfigureImport()
        {
            AssetDatabase.ImportAsset(AudioPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(AudioPath) as AudioImporter;
            if (!importer) throw new System.InvalidOperationException("Missing supplied room BGM: " + AudioPath);
            Configure(importer);
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioPath);
            Debug.Log("Room BGM ready: " + clip.name + ", " + clip.length.ToString("F3") +
                      " s, " + clip.channels + " channels; Vorbis Streaming, quality 0.70.");
        }

        static void Configure(AudioImporter importer)
        {
            importer.forceToMono = false;
            importer.loadInBackground = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = .70f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = false;
            importer.defaultSampleSettings = settings;
        }
    }
}
