using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editörde elle kurulum yerine menüden: Ordu/...
public static class OrduMenu
{
    const string ScenePath = "Assets/Scenes/M0_Network.unity";

    [MenuItem("Ordu/M0 Sahnesini Kur")]
    static void BuildM0Scene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // Önce kaydet: sahne içi NetworkObject kimliği (GlobalObjectIdHash) sahne asset'ine bağlı.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);

        var cam = Camera.main.transform;
        cam.position = new Vector3(0f, 8f, -9f);
        cam.LookAt(Vector3.zero);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";

        var nmGo = new GameObject("NetworkManager");
        var nm = nmGo.AddComponent<NetworkManager>();
        var transport = nmGo.AddComponent<UnityTransport>();
        nm.NetworkConfig ??= new NetworkConfig();
        nm.NetworkConfig.NetworkTransport = transport;
        nmGo.AddComponent<NetworkBootstrap>();

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "ServerCube";
        cube.transform.position = new Vector3(3f, 0.5f, 0f);
        cube.AddComponent<NetworkObject>();
        cube.AddComponent<NetworkTransform>().AuthorityMode = NetworkTransform.AuthorityModes.Server;
        cube.AddComponent<ServerCubeMover>();

        EditorSceneManager.SaveScene(scene);
        // Yeniden açınca OnValidate kimliği kesin üretir.
        EditorSceneManager.OpenScene(ScenePath);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("[Ordu] M0 sahnesi kuruldu: " + ScenePath);
    }

    [MenuItem("Ordu/Build/Dedicated Server (Windows)")]
    static void BuildServer() =>
        Build("Builds/Server/OrduServer.exe", (int)StandaloneBuildSubtarget.Server);

    [MenuItem("Ordu/Build/Client (Windows)")]
    static void BuildClient() =>
        Build("Builds/Client/Ordu.exe", (int)StandaloneBuildSubtarget.Player);

    static void Build(string path, int subtarget)
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = path,
            target = BuildTarget.StandaloneWindows64,
            subtarget = subtarget,
        });

        if (report.summary.result == BuildResult.Succeeded)
            Debug.Log($"[Ordu] Build hazır: {path}");
        else
            Debug.LogError($"[Ordu] Build başarısız ({report.summary.result}). " +
                           "Server için Unity Hub'da 'Windows Dedicated Server Build Support' modülü kurulu olmalı.");
    }
}
