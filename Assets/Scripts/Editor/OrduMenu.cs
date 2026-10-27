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
    const string ScenePath = "Assets/Scenes/Battle.unity";
    const string UnitPath = "Assets/Data/Units/Test_Piyade.asset";
    const string MaterialPath = "Assets/Art/Materials/Soldier.mat";
    const string PrefabPath = "Assets/Prefabs/Battalion.prefab";
    const string PrefabListPath = "Assets/DefaultNetworkPrefabs.asset";

    [MenuItem("Ordu/Savaş Sahnesini Kur")]
    static void BuildBattleScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var prefab = BuildBattalionPrefab();

        // Önce kaydet: sahne asset'i olmadan NetworkManager ayarı ve sahne kimlikleri oturmaz.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);

        var cam = Camera.main.transform;
        cam.position = new Vector3(0f, 70f, -80f);
        cam.LookAt(Vector3.zero);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = Vector3.one * (Battalion.MapHalfSize * 2f / 10f); // Plane 10 m

        var nmGo = new GameObject("NetworkManager");
        var nm = nmGo.AddComponent<NetworkManager>();
        var transport = nmGo.AddComponent<UnityTransport>();
        nm.NetworkConfig ??= new NetworkConfig();
        nm.NetworkConfig.NetworkTransport = transport;
        var prefabList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
        if (!nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(prefabList))
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabList);

        nmGo.AddComponent<NetworkBootstrap>();
        Set(nmGo.AddComponent<BattalionSpawner>(), "battalionPrefab", prefab.GetComponent<Battalion>());
        nmGo.AddComponent<CommandInput>();

        EditorSceneManager.SaveScene(scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("[Ordu] Savaş sahnesi kuruldu: " + ScenePath);
    }

    static GameObject BuildBattalionPrefab()
    {
        var material = LoadOrCreate(MaterialPath, () =>
            new Material(Shader.Find("Universal Render Pipeline/Lit")) { enableInstancing = true });

        var unit = LoadOrCreate(UnitPath, () =>
        {
            var def = ScriptableObject.CreateInstance<UnitDefinition>();
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            def.soldierMesh = sphere.GetComponent<MeshFilter>().sharedMesh; // yerleşik küre mesh'i
            Object.DestroyImmediate(sphere);
            return def;
        });
        unit.soldierMaterial = material;
        EditorUtility.SetDirty(unit);

        var go = new GameObject("Battalion");
        go.AddComponent<NetworkObject>();
        var nt = go.AddComponent<NetworkTransform>();
        nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
        nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
        Set(go.AddComponent<Battalion>(), "definition", unit);
        go.AddComponent<BattalionView>();

        EnsureFolder(PrefabPath);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);

        // NGO yeni prefab'ı çoğu zaman kendisi ekler; eklemediyse biz ekleyelim (çift kayıt hata verir).
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
        if (!list.Contains(prefab))
        {
            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }
        AssetDatabase.SaveAssets();
        return prefab;
    }

    [MenuItem("Ordu/Test/Slot Matematiği")]
    static void TestFormation()
    {
        // 20 genişlik × 5 saf, 1.5 m aralık
        Check(Formation.RankCount(100, 20) == 5, "100 asker / 20 genişlik = 5 saf");
        Check(Formation.RankCount(101, 20) == 6, "yarım son saf da sayılır");
        Check(Approx(Formation.SlotLocalOffset(0, 20, 100, 1.5f), new Vector3(-14.25f, 0f, 3f)), "slot 0 = ön saf, sol kanat");
        Check(Approx(Formation.SlotLocalOffset(99, 20, 100, 1.5f), new Vector3(14.25f, 0f, -3f)), "slot 99 = arka saf, sağ kanat");

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < 100; i++) sum += Formation.SlotLocalOffset(i, 20, 100, 1.5f);
        Check(sum.magnitude < 0.01f, "dolu formasyon merkezlenmiş");

        // Uçtaki askerin hızı = ω × r_max = koşu hızı
        float omega = Formation.MaxTurnRate(4.5f, 20, 100, 1.5f);
        float edgeSpeed = omega * Formation.HalfExtents(20, 100, 1.5f).magnitude;
        Check(Mathf.Abs(edgeSpeed - 4.5f) < 0.001f, $"uçtaki asker koşu hızında döner ({edgeSpeed:F3})");
        Check(Formation.MaxTurnRate(4.5f, 40, 200, 1.5f) < omega, "geniş hat daha yavaş döner");

        Debug.Log("[Ordu] Slot matematiği testleri bitti.");
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

    static void Check(bool ok, string what)
    {
        if (ok) Debug.Log("[Ordu] OK   " + what);
        else Debug.LogError("[Ordu] FAIL " + what);
    }

    static bool Approx(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-6f;

    static void Set(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static T LoadOrCreate<T>(string path, System.Func<T> create) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        EnsureFolder(path);
        asset = create();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    static void EnsureFolder(string assetPath) =>
        EnsureFolderExists(System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/'));

    static void EnsureFolderExists(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolderExists(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }
}
