using System;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using UnityEditor;
using UnityEngine;

// Project-local bootstrap. Reuses the existing localhost HTTP configuration.
// Does not change Codex configuration or Unity EditorPrefs.
[InitializeOnLoad]
public static class RobotMcpConnection
{
    static bool connecting;
    static double retryAt;

    static RobotMcpConnection()
    {
        if (Application.isBatchMode) return;
        SessionState.SetBool("MCPForUnity.StartupConfigRewrite.Ran", true);
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) Application.runInBackground = true;
        };
    }

    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || connecting || EditorApplication.timeSinceStartup < retryAt) return;
        if (MCPServiceLocator.TransportManager.IsRunning(TransportMode.Http)) return;
        Connect();
    }

    [MenuItem("Robot Training/Connect MCP")]
    public static async void Connect()
    {
        if (connecting) return;
        connecting = true;
        retryAt = EditorApplication.timeSinceStartup + 15;
        try
        {
            bool connected = await MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http);
            if (connected) Debug.Log("ROBOT_MCP_CONNECTED: " + Application.dataPath);
        }
        catch (Exception ex) { Debug.LogWarning("Robot MCP: " + ex.Message); }
        finally { connecting = false; }
    }
}
