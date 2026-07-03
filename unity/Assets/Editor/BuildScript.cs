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

        // Left-click-only game: kill the browser context menu on the page.
        string indexPath = Path.Combine(outputDir, "index.html");
        if (File.Exists(indexPath))
        {
            string html = File.ReadAllText(indexPath);
            const string guard = "document.addEventListener('contextmenu'";
            if (!html.Contains(guard))
                File.WriteAllText(indexPath, html.Replace("</body>",
                    "<script>document.addEventListener('contextmenu',function(e){e.preventDefault();});</script></body>"));
        }

        Debug.Log($"WebGL build OK -> {outputDir} ({report.summary.totalSize / (1024 * 1024)} MB)");
    }
}
