using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// One-shot project setup, runnable headless:
///   Unity.exe -batchmode -quit -projectPath unity -executeMethod ProjectSetup.CreateMainScene
public static class ProjectSetup
{
    [MenuItem("Throughput/Create Main Scene")]
    public static void CreateMainScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.063f, 0.071f, 0.094f);

        var boot = new GameObject("Bootstrap");
        boot.AddComponent<Throughput.Game.GameController>();

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true),
        };

        PlayerSettings.companyName = "eric-rolph";
        PlayerSettings.productName = "Throughput: The Inference Grid";
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;
        PlayerSettings.runInBackground = false;

        AssetDatabase.SaveAssets();
        Debug.Log("ProjectSetup: Main scene created and registered in build settings.");
    }

    [MenuItem("Throughput/Build WebGL")]
    public static void BuildWebGL() => BuildScript.BuildWebGL();
}
