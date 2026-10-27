using Unity.Netcode;
using UnityEngine;

// Sunucu: bağlanan her oyuncuya kendi bölüklerini doğurur. İlk bağlanan takım 0 (güney), ikinci takım 1 (kuzey).
// shortcut: sabit doğum noktaları; yerleştirme fazı M6'da gelecek.
[RequireComponent(typeof(NetworkManager))]
public class BattalionSpawner : MonoBehaviour
{
    [SerializeField] Battalion battalionPrefab;
    [SerializeField] int battalionsPerPlayer = 2;
    [SerializeField] float spawnDistance = 40f;
    [SerializeField] float spacing = 45f;

    NetworkManager nm;
    int nextTeam;

    // Singleton Awake sırasında henüz atanmamış olabilir; aynı objedeki bileşeni al.
    void Awake()
    {
        nm = GetComponent<NetworkManager>();
        nm.OnClientConnectedCallback += OnClientConnected;
    }

    void OnDestroy() => nm.OnClientConnectedCallback -= OnClientConnected;

    void OnClientConnected(ulong clientId)
    {
        if (!nm.IsServer) return;

        byte team = (byte)(nextTeam++ % 2);
        float z = team == 0 ? -spawnDistance : spawnDistance;
        Quaternion facing = team == 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);

        for (int i = 0; i < battalionsPerPlayer; i++)
        {
            float x = (i - (battalionsPerPlayer - 1) * 0.5f) * spacing;
            var battalion = Instantiate(battalionPrefab, new Vector3(x, 0f, z), facing);
            battalion.NetworkObject.SpawnWithOwnership(clientId);
            battalion.Team.Value = team;
        }
    }
}
