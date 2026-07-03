using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    // Invoked headless: Unity.exe -batchmode -quit -projectPath unity -executeMethod BuildScript.BuildWebGL
    public static void BuildWebGL()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).Parent.FullName;
        string outputDir = Path.Combine(projectRoot, "dist");

        // Brotli + JS decompression fallback: Cloudflare static assets cap files at
        // 25 MiB, and the fallback avoids any Content-Encoding header requirements.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.stripEngineCode = true;
        PlayerSettings.SetManagedStrippingLevel(
            UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.High);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Main.unity" },
            locationPathName = outputDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"WebGL build failed: {report.summary.result}");
            EditorApplication.Exit(1);
        }

        // Post-process the template page: kill the browser context menu
        // (left-click-only game) and expose the Unity instance for testing.
        string indexPath = Path.Combine(outputDir, "index.html");
        if (File.Exists(indexPath))
        {
            string html = File.ReadAllText(indexPath);
            if (!html.Contains("contextmenu"))
                html = html.Replace("</body>",
                    "<script>document.addEventListener('contextmenu',function(e){e.preventDefault();});</script></body>");
            const string thenHook = "}).then((unityInstance) => {";
            if (html.Contains(thenHook) && !html.Contains("window.unityInstance"))
                html = html.Replace(thenHook, thenHook + "\n                window.unityInstance = unityInstance;");
            File.WriteAllText(indexPath, html);
        }

        Debug.Log($"WebGL build OK -> {outputDir} ({report.summary.totalSize / (1024 * 1024)} MB)");
    }
}
