using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// Ensures the MCP-for-Unity HTTP bridge connects on editor load, without
/// requiring the MCP window's manual "Start" click. Connects to an already
/// running local server (uvx mcp-for-unity --transport http) or starts one.
[InitializeOnLoad]
public static class McpAutoConnect
{
    static McpAutoConnect()
    {
        // Let the package finish its own InitializeOnLoad work first.
        EditorApplication.delayCall += () =>
        {
            try
            {
                EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", true);
                EditorPrefs.SetBool("MCPForUnity.UseHttpTransport", true);
                Connect();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"McpAutoConnect: {ex.Message}");
            }
        };
    }

    private static void Connect()
    {
        Assembly asm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name.Contains("MCPForUnity"));
        if (asm == null) { Debug.LogWarning("McpAutoConnect: MCPForUnity assembly not found"); return; }

        Type locator = asm.GetType("MCPForUnity.Editor.Services.MCPServiceLocator");
        if (locator == null) { Debug.LogWarning("McpAutoConnect: MCPServiceLocator not found"); return; }

        object bridge = locator.GetProperty("Bridge",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        if (bridge == null) { Debug.LogWarning("McpAutoConnect: Bridge service not found"); return; }

        MethodInfo start = bridge.GetType().GetMethod("StartAsync",
            BindingFlags.Public | BindingFlags.Instance);
        if (start == null) { Debug.LogWarning("McpAutoConnect: StartAsync not found"); return; }

        start.Invoke(bridge, null);
        Debug.Log("McpAutoConnect: bridge StartAsync invoked");
    }

    [MenuItem("Throughput/Connect MCP Bridge")]
    public static void ConnectNow() => Connect();
}
