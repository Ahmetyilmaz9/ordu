using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Sunucu: bağlanan oyunculara takım verir, MatchManager'ın istediği orduyu yerleştirme bölgesine doğurur.
// İlk bağlanan takım 0 (güney), ikinci takım 1 (kuzey). Bot modunda takım 1 sunucuya ait bot.
// Yerleştirmede bölükler sadece kendi takımına görünür (sunucu rakibe hiç göndermez); savaşta herkese açılır.
[RequireComponent(typeof(NetworkManager))]
public class BattalionSpawner : MonoBehaviour
{
    public const string VsBotPref = "Ordu.VsBot";
    const ulong NoOwner = ulong.MaxValue;

    [SerializeField] Battalion battalionPrefab;
    [SerializeField] UnitRoster roster;

    NetworkManager nm;
    readonly ulong[] teamOwner = { NoOwner, NoOwner };

    // Singleton Awake sırasında henüz atanmamış olabilir; aynı objedeki bileşeni al.
    void Awake()
    {
        nm = GetComponent<NetworkManager>();
        nm.OnServerStarted += OnServerStarted;
        nm.OnClientConnectedCallback += OnClientConnected;
    }

    void OnDestroy()
    {
        nm.OnServerStarted -= OnServerStarted;
        nm.OnClientConnectedCallback -= OnClientConnected;
    }

    // Bot modu: dedicated build'de "-bot" argümanı, editörde Ordu menüsündeki ayar.
    void OnServerStarted()
    {
        bool vsBot = Array.IndexOf(Environment.GetCommandLineArgs(), "-bot") >= 0;
#if UNITY_EDITOR
        vsBot |= UnityEditor.EditorPrefs.GetBool(VsBotPref, false);
#endif
        if (vsBot) teamOwner[1] = NetworkManager.ServerClientId;
    }

    // shortcut: üçüncü oyuncu izleyici kalır, kopan oyuncunun takımı boşalmaz; yeniden bağlanma M6b'de.
    void OnClientConnected(ulong clientId)
    {
        if (!nm.IsServer) return;
        for (int t = 0; t < 2; t++)
        {
            if (teamOwner[t] != NoOwner) continue;
            teamOwner[t] = clientId;
            return;
        }
    }

    public bool HasTeam(int team) => teamOwner[team] != NoOwner;
    public bool IsBot(int team) => teamOwner[team] == NetworkManager.ServerClientId;
    public ulong OwnerOf(int team) => teamOwner[team];
    public int TeamOf(ulong clientId) => teamOwner[0] == clientId ? 0 : teamOwner[1] == clientId ? 1 : -1;

    public void SpawnArmy(byte team, byte[] counts)
    {
        var units = new List<byte>();
        for (int i = 0; i < counts.Length; i++)
            for (int n = 0; n < counts[i]; n++) units.Add((byte)i);

        var widths = new float[units.Count];
        for (int i = 0; i < widths.Length; i++)
        {
            var def = roster.units[units[i]];
            widths[i] = Mathf.Min(def.defaultWidth, def.soldierCount) * def.slotSpacing;
        }
        var layout = ArmyRules.Layout(widths, (Battalion.MapHalfSize - 5f) * 2f);

        // Takım 1 karşıdan bakar: x ve z aynalanır, böylece oyuncunun solu yine solu.
        float side = team == 0 ? -1f : 1f;
        Quaternion facing = team == 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
        for (int i = 0; i < units.Count; i++)
        {
            var pos = new Vector3(-side * layout[i].x, 0f, side * (ArmyRules.DeployFront + 8f + layout[i].y * ArmyRules.RowGap));
            var battalion = Instantiate(battalionPrefab, pos, facing);
            battalion.ServerInit(team, units[i]);
            battalion.NetworkObject.CheckObjectVisibility = clientId => TeamOf(clientId) == team;
            battalion.NetworkObject.SpawnWithOwnership(teamOwner[team]);
        }
    }

    // Savaş başlıyor: gizli bölükleri herkese göster.
    // shortcut: tam açılma; görüşe göre gösterme/gizleme (savaş sisi) M7'de.
    public void RevealAll()
    {
        foreach (var b in Battalion.All)
        {
            var obj = b.NetworkObject;
            obj.CheckObjectVisibility = null;
            foreach (ulong clientId in nm.ConnectedClientsIds)
                if (!obj.IsNetworkVisibleTo(clientId)) obj.NetworkShow(clientId);
        }
    }
}
