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
    const string UnitsFolder = "Assets/Data/Units/";
    const string RosterPath = "Assets/Data/Units/Roster.asset";
    const string MaterialPath = "Assets/Art/Materials/Soldier.mat";
    const string SmokeMaterialPath = "Assets/Art/Materials/Smoke.mat";
    const string SmokeTexturePath = "Assets/Art/Textures/SmokePuff.png";
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

        Camera.main.gameObject.AddComponent<RtsCamera>(); // konumu kendisi yönetir

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
        var spawner = nmGo.AddComponent<BattalionSpawner>();
        Set(spawner, "battalionPrefab", prefab.GetComponent<Battalion>());
        Set(spawner, "roster", AssetDatabase.LoadAssetAtPath<UnitRoster>(RosterPath));
        nmGo.AddComponent<CommandInput>();
        nmGo.AddComponent<UnitCards>();
        nmGo.AddComponent<CombatSystem>();
        nmGo.AddComponent<BotCommander>();
        nmGo.AddComponent<FogSystem>();

        // Maç saati ve zafer: sahneye yerleşik NetworkObject (NetworkManager objesine konamaz).
        var match = new GameObject("Match");
        match.AddComponent<NetworkObject>();
        Set(match.AddComponent<MatchManager>(), "roster", AssetDatabase.LoadAssetAtPath<UnitRoster>(RosterPath));
        match.AddComponent<MatchUI>();

        EditorSceneManager.SaveScene(scene);
        // Yeniden açınca sahne içi NetworkObject kimliği (GlobalObjectIdHash) kesin üretilir.
        EditorSceneManager.OpenScene(ScenePath);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("[Ordu] Savaş sahnesi kuruldu: " + ScenePath);
    }

    static GameObject BuildBattalionPrefab()
    {
        var material = LoadOrCreate(MaterialPath, () =>
            new Material(Shader.Find("Universal Render Pipeline/Lit")) { enableInstancing = true });

        var roster = LoadOrCreate(RosterPath, ScriptableObject.CreateInstance<UnitRoster>);
        roster.units = new UnitDefinition[UnitSpecs.Length];
        for (int i = 0; i < UnitSpecs.Length; i++)
            roster.units[i] = Unit(UnitSpecs[i].asset, material, UnitSpecs[i].configure);
        roster.smokeMaterial = SmokeMaterial();
        EditorUtility.SetDirty(roster);

        var go = new GameObject("Battalion");
        go.AddComponent<NetworkObject>();
        var nt = go.AddComponent<NetworkTransform>();
        nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
        nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
        Set(go.AddComponent<Battalion>(), "roster", roster);
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

    const string VsBotMenu = "Ordu/Bota Karşı Oyna";

    [MenuItem(VsBotMenu)]
    static void ToggleVsBot()
    {
        bool on = !EditorPrefs.GetBool(BattalionSpawner.VsBotPref, false);
        EditorPrefs.SetBool(BattalionSpawner.VsBotPref, on);
        Debug.Log(on ? "[Ordu] Bot modu AÇIK: oyuncular takım 0, takım 1 bot." : "[Ordu] Bot modu KAPALI.");
    }

    [MenuItem(VsBotMenu, true)]
    static bool ToggleVsBotValidate()
    {
        Menu.SetChecked(VsBotMenu, EditorPrefs.GetBool(BattalionSpawner.VsBotPref, false));
        return true;
    }

    [MenuItem("Ordu/Test/Savaş Matematiği")]
    static void TestCombat()
    {
        // Hayatta maskesi
        var mask = AliveMask.Full(200);
        Check(mask.Count == 200 && mask.Get(199) && !mask.Get(200), "maske: 200 asker hayatta, 201. yok");
        mask.Set(64, false);
        Check(mask.Count == 199 && !mask.Get(64) && mask.Get(63) && mask.Get(65), "maske: kelime sınırında tek ölüm");

        // Dikdörtgenler: A merkezde kuzeye bakıyor (30×6), B önünde 5 m'de
        var a = new Obb(Vector3.zero, Vector3.right, Vector3.forward, new Vector2(15f, 3f));
        var bFront = new Obb(new Vector3(0f, 0f, 6.5f), Vector3.right, Vector3.back, new Vector2(15f, 3f));
        var bFar = new Obb(new Vector3(0f, 0f, 20f), Vector3.right, Vector3.back, new Vector2(15f, 3f));
        Check(CombatMath.Overlaps(a, bFront, 1f), "0,5 m boşluk: temas var");
        Check(!CombatMath.Overlaps(a, bFar, 1f), "14 m boşluk: temas yok");

        Check(CombatMath.SideOf(a, new Vector2(0f, 10f)) == HitSide.Front, "önden gelen: cephe");
        Check(CombatMath.SideOf(a, new Vector2(0f, -10f)) == HitSide.Rear, "arkadan gelen: arka");
        Check(CombatMath.SideOf(a, new Vector2(-20f, 0f)) == HitSide.Left, "soldan gelen: sol kanat");
        Check(CombatMath.SideOf(a, new Vector2(14f, 5f)) == HitSide.Front, "geniş hattın ucuna önden gelen: cephe");

        // Temas cephesi: 10 m genişliğinde düşman 30 m'lik hattın önünde -> ~10 m temas
        var narrow = new Obb(new Vector3(5f, 0f, 6.5f), Vector3.right, Vector3.back, new Vector2(5f, 3f));
        float len = CombatMath.ContactLength(a, narrow, HitSide.Front, out float center);
        Check(Mathf.Abs(len - 10f) < 0.01f && Mathf.Abs(center - 5f) < 0.01f, $"dar düşman: temas 10 m, merkez x=5 ({len:F1}, {center:F1})");

        // Yöne göre ölüm (hat 20×5, 1.5 m): cepheden vurulursa ön saf, temas noktasına en yakın
        var full = AliveMask.Full(100);
        int front = CombatMath.PickCasualty(FormationType.Line, 20, 100, 1.5f, full, HitSide.Front, 14.25f);
        Check(front == 19, $"cephe, sağ uçta temas: ön safın sağ ucu ölür ({front})");
        int left = CombatMath.PickCasualty(FormationType.Line, 20, 100, 1.5f, full, HitSide.Left, 0f);
        Check(left % 20 == 0, $"sol kanat: sol sıradan biri ölür ({left})");
        int rear = CombatMath.PickCasualty(FormationType.Line, 20, 100, 1.5f, full, HitSide.Rear, 0f);
        Check(rear >= 80, $"arka: arka saftan biri ölür ({rear})");

        var onlyOne = new AliveMask();
        onlyOne.Set(57, true);
        Check(CombatMath.PickCasualty(FormationType.Line, 20, 100, 1.5f, onlyOne, HitSide.Front, 0f) == 57, "tek hayatta kalan seçilir");
        Check(CombatMath.PickCasualty(FormationType.Line, 20, 100, 1.5f, new AliveMask(), HitSide.Front, 0f) == -1, "kimse yoksa -1");

        Check(CombatMath.SideMultiplier(HitSide.Rear, FormationType.Line) > CombatMath.SideMultiplier(HitSide.Left, FormationType.Line)
              && CombatMath.SideMultiplier(HitSide.Left, FormationType.Line) > 1f, "arka > kanat > cephe");
        Check(CombatMath.SideMultiplier(HitSide.Rear, FormationType.Square) == 1f, "karenin arkası yok");

        // Moral
        Check(Morale.Evaluate(MoraleState.Steady, 50f) == MoraleState.Steady, "moral 50: sağlam");
        Check(Morale.Evaluate(MoraleState.Steady, 30f) == MoraleState.Shaken, "moral 30: sarsıldı");
        Check(Morale.Evaluate(MoraleState.Shaken, 5f) == MoraleState.Routing, "moral 5: kaçış");
        Check(Morale.Evaluate(MoraleState.Routing, 30f) == MoraleState.Routing, "kaçan, 30'da hâlâ kaçıyor (eşik 45)");
        Check(Morale.Evaluate(MoraleState.Routing, 50f) == MoraleState.Shaken, "kaçan, 45 üstünde toparlanır");
        Check(100f - 3 * Morale.CeilingDropPerRout < Morale.RallyAbove
              && 100f - 2 * Morale.CeilingDropPerRout >= Morale.RallyAbove, "ikinci kaçıştan dönülür, üçüncüden dönülmez");
        Check(Mathf.Abs(Morale.CasualtyLoss(100) * 100 - Morale.CasualtyWeight) < 0.01f, "bölüğün tamamı = CasualtyWeight moral");
        Check(Morale.SidePressurePerSec(HitSide.Rear, FormationType.Line) > Morale.SidePressurePerSec(HitSide.Left, FormationType.Line)
              && Morale.SidePressurePerSec(HitSide.Front, FormationType.Line) == 0f
              && Morale.SidePressurePerSec(HitSide.Rear, FormationType.Square) == 0f, "moral baskısı: arka > kanat, cephe/kare 0");
        Check(Morale.AttackFactor(MoraleState.Routing) == 0f, "kaçan dövüşmez");
        Check(Morale.FleePoint(0, new Vector3(30f, 0f, 60f), 100f) == new Vector3(30f, 0f, -100f), "mavi kaçan kendi (güney) kenarına koşar");
        Check(Morale.FleePoint(1, new Vector3(-20f, 0f, -70f), 100f) == new Vector3(-20f, 0f, 100f), "kırmızı kaçan kendi (kuzey) kenarına koşar");

        // Zafer
        Check(MatchRules.Decide(true, true, 100, 50, false) == MatchManager.NoWinner, "süre varken iki taraf da sahada: devam");
        Check(MatchRules.Decide(true, false, 10, 0, false) == 0, "kırmızının bölüğü kalmadı: mavi kazanır");
        Check(MatchRules.Decide(false, true, 0, 10, false) == 1, "mavinin bölüğü kalmadı: kırmızı kazanır");
        Check(MatchRules.Decide(true, true, 80, 120, true) == 1, "süre doldu: çok askeri kalan kazanır");
        Check(MatchRules.Decide(true, true, 50, 50, true) == MatchManager.Draw, "süre doldu, eşit: berabere");

        // Vuruş modeli (Total War)
        Check(Mathf.Abs(CombatMath.HitChance(30f, 30f) - 0.35f) < 1e-4f, "eşit saldırı/savunma: %35 isabet");
        Check(CombatMath.HitChance(100f, 0f) == 0.9f && CombatMath.HitChance(0f, 100f) == 0.08f, "isabet %8–%90 arası");
        Check(Mathf.Abs(CombatMath.DamagePerHit(40f, 0f, 100f) - 10f) < 1e-4f, "tam zırh temel hasarın %75'ini keser");
        Check(Mathf.Abs(CombatMath.DamagePerHit(40f, 60f, 100f) - 70f) < 1e-4f, "zırh delici kısım hep geçer");

        var spear = ScriptableObject.CreateInstance<UnitDefinition>();
        spear.meleeAttack = 25f; spear.weaponDamage = 28f; spear.armorPiercing = 8f; spear.bonusVsMounted = 25f;
        var archer = ScriptableObject.CreateInstance<UnitDefinition>();
        archer.meleeDefense = 10f; archer.armor = 10f;
        float spearOnArcher = CombatMath.MeleeKillsPerSec(24, spear, archer);
        Check(spearOnArcher > 2f && spearOnArcher < 3f, $"kargılı okçuya önden: ~2,4 ölü/sn ({spearOnArcher:F2})");
        var horse = ScriptableObject.CreateInstance<UnitDefinition>();
        horse.meleeDefense = 30f; horse.armor = 45f;
        float vsFoot = CombatMath.MeleeKillsPerSec(10, spear, horse);
        horse.mounted = true;
        Check(CombatMath.MeleeKillsPerSec(10, spear, horse) > vsFoot * 1.5f, "kargı atlıya karşı çok daha etkili");
        Object.DestroyImmediate(spear); Object.DestroyImmediate(archer); Object.DestroyImmediate(horse);

        // Hızlı kayıp şoku
        Check(Mathf.Abs(Morale.LossShock(0f, 100) - 1f) < 1e-4f && Mathf.Abs(Morale.LossShock(10f, 100) - 1.4f) < 1e-4f,
            "son 5 sn'de %10 kayıp: her yeni kayıp ×1,4 moral");
        Check(Mathf.Abs(Morale.DecayRecentLosses(10f, Morale.RecentLossHalfLife) - 5f) < 1e-3f, "son kayıplar yarı ömürde yarıya iner");

        // Menzilli
        Check(CombatMath.Shooters(100, 25, 2) == 50, "tüfek: 2 saf × 25 = 50 atıcı");
        Check(CombatMath.Shooters(30, 25, 2) == 30, "atıcı sayısı hayattakileri aşmaz");
        Check(CombatMath.Shooters(120, 24, 0) == 120, "okçu: hepsi atar");
        var musket = ScriptableObject.CreateInstance<UnitDefinition>();
        musket.range = 50f; musket.missileAccuracy = 0.3f; musket.missileDamage = 40f; musket.missileArmorPiercing = 60f;
        var foot = ScriptableObject.CreateInstance<UnitDefinition>();
        foot.armor = 20f;
        float point = CombatMath.VolleyKills(50, musket, foot, 0f);
        Check(Mathf.Abs(point - 14.1f) < 0.01f, $"tüfek salvosu dibinden piyadeye ~14 ölü ({point:F2})");
        Check(Mathf.Abs(CombatMath.VolleyKills(50, musket, foot, 50f) - point * 0.4f) < 0.01f, "menzil sonunda %40");
        Object.DestroyImmediate(musket); Object.DestroyImmediate(foot);

        // Ordu kurma ve yerleştirme
        int[] max = { 3, 3, 3, 3, 3 };
        Check(ArmyRules.IsValid(new byte[] { 3, 3, 2, 1, 1 }, max), "10 slot, limitler içinde: geçerli");
        Check(!ArmyRules.IsValid(new byte[] { 3, 3, 3, 1, 1 }, max), "11 slot: geçersiz");
        Check(!ArmyRules.IsValid(new byte[] { 4, 0, 0, 0, 0 }, max), "tip limiti aşıldı: geçersiz");
        Check(!ArmyRules.IsValid(new byte[] { 0, 0, 0, 0, 0 }, max), "boş ordu: geçersiz");
        Check(!ArmyRules.IsValid(new byte[] { 1, 1 }, max) && !ArmyRules.IsValid(null, max), "yanlış uzunluk/null: geçersiz");
        var def = ArmyRules.Default(max);
        Check(ArmyRules.IsValid(def, max) && def[0] + def[1] + def[2] + def[3] + def[4] == 10, "varsayılan ordu 10 slot ve geçerli");
        Check(ArmyRules.Default(new[] { 1, 1 }).Length == 2 && ArmyRules.Default(new[] { 1, 1 })[0] == 1, "limit yetmezse olduğu kadar");

        Check(ArmyRules.ClampToZone(0, new Vector3(0f, 0f, 30f)).z <= -ArmyRules.DeployFront, "mavi rakip yarıya konamaz");
        Check(ArmyRules.ClampToZone(1, new Vector3(0f, 0f, -30f)).z >= ArmyRules.DeployFront, "kırmızı rakip yarıya konamaz");
        Check(Mathf.Abs(ArmyRules.ClampToZone(0, new Vector3(500f, 0f, -70f)).x) < Battalion.MapHalfSize, "harita dışına konamaz");

        var layout = ArmyRules.Layout(new[] { 30f, 30f, 30f }, 190f);
        Check(layout[0].y == 0 && layout[2].y == 0 && Mathf.Abs(layout[1].x) < 0.01f, "üç bölük tek sırada, ortadaki merkezde");
        var rows = ArmyRules.Layout(new[] { 100f, 100f }, 150f);
        Check(rows[0].y == 0 && rows[1].y == 1 && Mathf.Abs(rows[1].x) < 0.01f, "sığmayan bir sıra geriye, ortalı");

        // Sunucu adresi (istemcinin IP kutusu)
        Check(NetworkBootstrap.TryParseEndpoint("192.168.1.20", 7777, out var ip, out var port) && ip == "192.168.1.20" && port == 7777, "IP, varsayılan port");
        Check(NetworkBootstrap.TryParseEndpoint(" 85.10.1.2:9000 ", 7777, out ip, out port) && ip == "85.10.1.2" && port == 9000, "IP:port, boşluklar kırpılır");
        Check(!NetworkBootstrap.TryParseEndpoint("oyun.example.com", 7777, out _, out _), "alan adı reddedilir (transport çözmez)");
        Check(!NetworkBootstrap.TryParseEndpoint("1.2.3", 7777, out _, out _), "eksik IP reddedilir");
        Check(!NetworkBootstrap.TryParseEndpoint("1.2.3.4:0", 7777, out _, out _) && !NetworkBootstrap.TryParseEndpoint("1.2.3.4:99999", 7777, out _, out _), "geçersiz port reddedilir");
        Check(!NetworkBootstrap.TryParseEndpoint("", 7777, out _, out _) && !NetworkBootstrap.TryParseEndpoint("::1", 7777, out _, out _), "boş ve IPv6 reddedilir");

        // Yakın dövüş görseli yardımcıları
        Check(Mathf.Abs(CombatMath.DistanceToObb(a, new Vector2(0f, 8f)) - 5f) < 0.001f, "dikdörtgenin 5 m önündeki nokta: 5 m");
        Check(CombatMath.DistanceToObb(a, new Vector2(3f, 1f)) == 0f, "içerideki nokta: 0");
        Check(Mathf.Abs(CombatMath.DistanceToObb(a, new Vector2(18f, 7f)) - 5f) < 0.001f, "köşeden çapraz: 3-4-5");
        bool jitterOk = true;
        for (int i = 0; i < 200; i++)
        {
            var j = CombatMath.SoldierJitter(i, 0.3f);
            jitterOk &= Mathf.Abs(j.x) <= 0.3f && Mathf.Abs(j.y) <= 0.3f && j == CombatMath.SoldierJitter(i, 0.3f);
        }
        Check(jitterOk, "asker sapması sınır içinde ve her seferinde aynı");

        // Savaş sisi
        Check(FogRules.Sees(59f, 60f, false) && !FogRules.Sees(61f, 60f, false), "görüş sınırı: 60 m içi görünür, dışı değil");
        Check(FogRules.Sees(65f, 60f, true) && !FogRules.Sees(65f, 60f, false), "görünen bölük sınırı biraz aşınca hemen kaybolmaz");
        Check(!FogRules.Sees(60f + FogRules.Hysteresis + 1f, 60f, true), "payı da aşınca gizlenir");

        Debug.Log("[Ordu] Savaş matematiği testleri bitti.");
    }

    // Birimler. Değerler sadece ilk oluşturmada yazılır; sonra Inspector'dan dengelenir, menü ezmez.
    // "Ordu → Denge → Önerilen Birim Değerlerini Uygula" bunları mevcut dosyalara bilerek yeniden yazar.
    // Yakın dövüş: 0–100 ölçeği, asker canı 100. Kontrol: kargılı okçuya önden ~2,4 ölü/sn (okçu ~15 sn'de kırılır).
    static readonly (string asset, System.Action<UnitDefinition> configure)[] UnitSpecs =
    {
        ("Test_Piyade", d =>
        {
            d.unitId = "piyade"; d.displayName = "Piyade";
            d.meleeAttack = 30f; d.meleeDefense = 30f; d.weaponDamage = 25f; d.armorPiercing = 5f; d.armor = 20f;
            d.discipline = 0.9f; d.chargeImpact = 0.05f;
        }),
        ("Okcu", d =>
        {
            d.unitId = "okcu"; d.displayName = "Okçu";
            d.soldierCount = 120; d.defaultWidth = 24;
            d.meleeAttack = 15f; d.meleeDefense = 10f; d.weaponDamage = 15f; d.armorPiercing = 0f; d.armor = 10f; // yakında çaresiz
            d.discipline = 0.7f; d.chargeImpact = 0.03f;
            d.range = 70f; d.reloadTime = 3f; d.shootingRanks = 0;                         // sürekli
            d.missileAccuracy = 0.12f; d.missileDamage = 22f; d.missileArmorPiercing = 0f; // zayıf, zırhta erir
        }),
        ("Tufekli", d =>
        {
            d.unitId = "tufekli"; d.displayName = "Tüfekli";
            d.soldierCount = 100; d.defaultWidth = 25;
            d.meleeAttack = 20f; d.meleeDefense = 15f; d.weaponDamage = 20f; d.armorPiercing = 5f; d.armor = 25f; // yakında çöker
            d.discipline = 1.3f; d.chargeImpact = 0.03f;
            d.range = 50f; d.reloadTime = 18f; d.shootingRanks = 2;                         // aralıklı
            d.missileAccuracy = 0.3f; d.missileDamage = 40f; d.missileArmorPiercing = 60f; // yıkıcı, zırh deler
            d.firearmMoraleMultiplier = 2f; // barut kuralı
        }),
        ("Sipahi", d =>
        {
            d.unitId = "sipahi"; d.displayName = "Sipahi";
            d.soldierCount = 50; d.defaultWidth = 10; d.slotSpacing = 2f; // atlı, seyrek
            d.walkSpeed = 4f; d.runSpeed = 9f; d.staminaDrainPerSec = 1f / 30f;
            d.meleeAttack = 45f; d.meleeDefense = 30f; d.weaponDamage = 32f; d.armorPiercing = 10f; d.armor = 45f;
            d.mounted = true; d.discipline = 1.1f;
            d.chargeImpact = 0.15f; // 10 cephe × 9 m/sn × 0,15 ≈ 13 ölü tek darbede
            d.soldierScale = 1.1f;
            d.sightRange = 90f; // keşif birimi
        }),
        ("Kargili", d =>
        {
            d.unitId = "kargili"; d.displayName = "Kargılı";
            d.soldierCount = 180; d.defaultWidth = 30;
            d.walkSpeed = 2.2f; d.runSpeed = 4f;
            d.meleeAttack = 25f; d.meleeDefense = 40f; d.weaponDamage = 28f; d.armorPiercing = 8f; d.armor = 35f;
            d.bonusVsMounted = 25f;   // atlıya karşı saldırı ve hasar bonusu
            d.chargeImpact = 0.02f;
            d.antiChargeFront = 0.7f; // önden şarjın %70'i saldırana döner: süvariye duvar
        }),
    };

    // Yumuşak kenarlı duman kartı: kod üretir, PNG olarak kaydeder; materyal Sprites/Default (saydam, her pipeline'da).
    static Material SmokeMaterial()
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SmokeTexturePath);
        if (texture == null)
        {
            const int size = 64;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(size - 1, size - 1) * 0.5f) / (size * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
            EnsureFolder(SmokeTexturePath);
            System.IO.File.WriteAllBytes(SmokeTexturePath, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(SmokeTexturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(SmokeTexturePath);
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SmokeTexturePath);
        }
        var material = LoadOrCreate(SmokeMaterialPath, () => new Material(Shader.Find("Sprites/Default")));
        material.mainTexture = texture;
        EditorUtility.SetDirty(material);
        return material;
    }

    [MenuItem("Ordu/Denge/Önerilen Birim Değerlerini Uygula")]
    static void ApplyUnitSpecs()
    {
        if (!EditorUtility.DisplayDialog("Birim değerleri", "Birim dosyalarındaki savaş değerleri önerilen değerlerle değiştirilecek. Inspector'da yaptığın ayarlar kaybolur.", "Uygula", "Vazgeç"))
            return;
        foreach (var (asset, configure) in UnitSpecs)
        {
            var def = AssetDatabase.LoadAssetAtPath<UnitDefinition>(UnitsFolder + asset + ".asset");
            if (def == null) continue;
            configure(def);
            EditorUtility.SetDirty(def);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Ordu] Önerilen birim değerleri uygulandı.");
    }

    // Siluet (görsel, her kurulumda yazılır): uzaktan tip okunsun. Ölçüler metre (kargı uzun ve ince,
    // okçu ince, sipahi uzun gövdeli, tüfekli tıknaz). Takım rengi gövdede kalır.
    static readonly System.Collections.Generic.Dictionary<string, (PrimitiveType mesh, Vector3 size)> Silhouettes =
        new System.Collections.Generic.Dictionary<string, (PrimitiveType, Vector3)>
        {
            { "Test_Piyade", (PrimitiveType.Capsule, new Vector3(0.6f, 1.0f, 0.6f)) },
            { "Okcu",        (PrimitiveType.Capsule, new Vector3(0.4f, 1.0f, 0.4f)) },
            { "Tufekli",     (PrimitiveType.Cylinder, new Vector3(0.65f, 0.9f, 0.65f)) },
            { "Sipahi",      (PrimitiveType.Cube, new Vector3(0.7f, 1.3f, 1.8f)) },
            { "Kargili",     (PrimitiveType.Cube, new Vector3(0.45f, 1.7f, 0.45f)) },
        };

    static UnitDefinition Unit(string name, Material material, System.Action<UnitDefinition> configure)
    {
        var def = LoadOrCreate(UnitsFolder + name + ".asset", () =>
        {
            var d = ScriptableObject.CreateInstance<UnitDefinition>();
            configure(d);
            return d;
        });
        var (primitive, size) = Silhouettes.TryGetValue(name, out var s) ? s : (PrimitiveType.Sphere, Vector3.one);
        var temp = GameObject.CreatePrimitive(primitive);
        def.soldierMesh = temp.GetComponent<MeshFilter>().sharedMesh; // yerleşik mesh
        Object.DestroyImmediate(temp);
        // Hedef boyuta göre oran (yerleşik mesh boyutu: küp 1, kapsül/silindir 1×2×1), soldierScale'e bölünmüş.
        Vector3 meshSize = def.soldierMesh.bounds.size;
        def.soldierShape = new Vector3(size.x / meshSize.x, size.y / meshSize.y, size.z / meshSize.z) / def.soldierScale;
        def.soldierMaterial = material;
        EditorUtility.SetDirty(def);
        return def;
    }

    [MenuItem("Ordu/Test/Slot Matematiği")]
    static void TestFormation()
    {
        const FormationType Line = FormationType.Line;
        // Hat: 20 genişlik × 5 saf, 1.5 m aralık
        Check(Formation.RankCount(100, 20) == 5, "100 asker / 20 genişlik = 5 saf");
        Check(Formation.RankCount(101, 20) == 6, "yarım son saf da sayılır");
        Check(Approx(Formation.SlotLocalOffset(Line, 0, 20, 100, 1.5f), new Vector3(-14.25f, 0f, 3f)), "slot 0 = ön saf, sol kanat");
        Check(Approx(Formation.SlotLocalOffset(Line, 99, 20, 100, 1.5f), new Vector3(14.25f, 0f, -3f)), "slot 99 = arka saf, sağ kanat");

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < 100; i++) sum += Formation.SlotLocalOffset(Line, i, 20, 100, 1.5f);
        Check(sum.magnitude < 0.01f, "dolu hat merkezlenmiş");

        // Uçtaki askerin hızı = ω × r_max = koşu hızı
        Formation.HalfExtents(Line, 20, 100, 1.5f, out float r20);
        Formation.HalfExtents(Line, 40, 200, 1.5f, out float r40);
        float omega = Formation.MaxTurnRate(4.5f, r20);
        Check(Mathf.Abs(omega * r20 - 4.5f) < 0.001f, $"uçtaki asker koşu hızında döner ({omega * r20:F3})");
        Check(Formation.MaxTurnRate(4.5f, r40) < omega, "geniş hat daha yavaş döner");

        // Diğer tipler
        Check(Formation.EffectiveWidth(FormationType.Column, 20, 100) == Formation.ColumnWidth, "kolon dar");
        Check(Formation.EffectiveWidth(FormationType.Square, 20, 100) == 10, "100 askerlik kare 10×10");
        Check(Mathf.Abs(Formation.SlotLocalOffset(FormationType.Wedge, 0, 20, 100, 1.5f).x) < 1e-4f, "kamanın ucu ortada");
        Check(Formation.SlotLocalOffset(FormationType.Wedge, 0, 20, 100, 1.5f).z >
              Formation.SlotLocalOffset(FormationType.Wedge, 1, 20, 100, 1.5f).z, "kamanın ucu önde");

        // Hiçbir tipte iki asker aynı slota düşmesin
        foreach (FormationType type in System.Enum.GetValues(typeof(FormationType)))
        foreach (int count in new[] { 50, 100, 101, 200 })
        {
            var seen = new System.Collections.Generic.HashSet<Vector2Int>();
            for (int i = 0; i < count; i++)
            {
                Vector3 o = Formation.SlotLocalOffset(type, i, 20, count, 1f) * 2f; // yarım-slot ofsetleri tamsayıya
                seen.Add(new Vector2Int(Mathf.RoundToInt(o.x), Mathf.RoundToInt(o.z)));
            }
            Check(seen.Count == count, $"{type} {count}: slotlar benzersiz");
        }

        Debug.Log("[Ordu] Slot matematiği testleri bitti.");
    }

    [MenuItem("Ordu/Build/Dedicated Server (Windows)")]
    static void BuildServer() =>
        Build("Builds/Server/OrduServer.exe", (int)StandaloneBuildSubtarget.Server);

    [MenuItem("Ordu/Build/Client (Windows)")]
    static void BuildClient() =>
        Build("Builds/Client/Ordu.exe", (int)StandaloneBuildSubtarget.Player);

    // VPS ve Edgegap Linux istiyor. Unity Hub: "Linux Dedicated Server Build Support" modülü.
    [MenuItem("Ordu/Build/Dedicated Server (Linux)")]
    static void BuildLinuxServer() =>
        Build("Builds/LinuxServer/OrduServer.x86_64", (int)StandaloneBuildSubtarget.Server, BuildTarget.StandaloneLinux64);

    static void Build(string path, int subtarget, BuildTarget target = BuildTarget.StandaloneWindows64)
    {
        // BuildPlayer editörün aktif platformunu değiştirir; geri almazsak MPPM sanal oyuncuları
        // sunucu platformunda açılır. Build bitince eski platforma dön.
        var previousTarget = EditorUserBuildSettings.activeBuildTarget;
        var previousSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = path,
            target = target,
            subtarget = subtarget,
        });

        EditorUserBuildSettings.standaloneBuildSubtarget = previousSubtarget;
        if (EditorUserBuildSettings.activeBuildTarget != previousTarget)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(previousTarget), previousTarget);

        if (report.summary.result == BuildResult.Succeeded)
            Debug.Log($"[Ordu] Build hazır: {path}");
        else
            Debug.LogError($"[Ordu] Build başarısız ({report.summary.result}). " +
                           "Server için Unity Hub'da ilgili 'Dedicated Server Build Support' modülü kurulu olmalı.");
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
