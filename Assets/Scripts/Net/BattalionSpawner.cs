using System;
using Unity.Netcode;
using UnityEngine;

// Sunucu: bağlanan her oyuncuya kendi bölüklerini doğurur. İlk bağlanan takım 0 (güney), ikinci takım 1 (kuzey).
// Bot modunda bütün oyuncular takım 0, takım 1 sunucuya ait bot ordusu.
// shortcut: sabit doğum noktaları; yerleştirme fazı M6'da gelecek.
[RequireComponent(typeof(NetworkManager))]
public class BattalionSpawner : MonoBehaviour
{
    public const string VsBotPref = "Ordu.VsBot";

    [SerializeField] Battalion battalionPrefab;
    [SerializeField] int battalionsPerPlayer = 2;
    [SerializeField] float spawnDistance = 40f;
    [SerializeField] float spacing = 45f;

    NetworkManager nm;
    int nextTeam;
    bool vsBot;

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
        vsBot = Array.IndexOf(Environment.GetCommandLineArgs(), "-bot") >= 0;
#if UNITY_EDITOR
        vsBot |= UnityEditor.EditorPrefs.GetBool(VsBotPref, false);
#endif
        if (vsBot) SpawnArmy(1, NetworkManager.ServerClientId);
    }

    void OnClientConnected(ulong clientId)
    {
        if (!nm.IsServer) return;
        SpawnArmy(vsBot ? (byte)0 : (byte)(nextTeam++ % 2), clientId);
    }

    void SpawnArmy(byte team, ulong ownerClientId)
    {
        float z = team == 0 ? -spawnDistance : spawnDistance;
        Quaternion facing = team == 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);

        for (int i = 0; i < battalionsPerPlayer; i++)
        {
            float x = (i - (battalionsPerPlayer - 1) * 0.5f) * spacing;
            var battalion = Instantiate(battalionPrefab, new Vector3(x, 0f, z), facing);
            battalion.NetworkObject.SpawnWithOwnership(ownerClientId);
            battalion.Team.Value = team;
        }
    }
}
