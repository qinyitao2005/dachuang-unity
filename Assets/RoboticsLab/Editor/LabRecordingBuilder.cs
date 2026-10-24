using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace RoboticsLab.Editor
{
    public static class LabRecordingBuilder
    {
        [MenuItem("Robot Lab/Build L-C Recording Demo")]
        public static void Build()
        {
            Directory.CreateDirectory("Builds/Recording");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{LabSceneBuilder.ScenePath},
                locationPathName="Builds/Recording/RobotLabRecording.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            File.WriteAllText("Documentation/RecordingBuildReport.txt",report.summary.result+"\nErrors: "+report.summary.totalErrors+"\nWarnings: "+report.summary.totalWarnings);
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Recording build failed");
            Debug.Log("RECORDING_BUILD_SUCCEEDED");
        }
    }
}
