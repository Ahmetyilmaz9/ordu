using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Savaş sisi (sunucu). Bir takım, kendi bölüklerinden birinin görüş mesafesindeki düşmanı görür.
// Görmediği düşman bölüğün verisi o oyuncuya HİÇ gönderilmez (NetworkHide): paket dinleyerek maphack olmaz.
// Yeniden göründüğünde NGO nesneyi baştan doğurur; konum, hayatta maskesi, moral tam gelir.
[RequireComponent(typeof(NetworkManager), typeof(BattalionSpawner))]
public class FogSystem : MonoBehaviour
{
    [SerializeField] float updateInterval = 0.2f;

    static readonly HashSet<Battalion>[] seenBy = { new HashSet<Battalion>(), new HashSet<Battalion>() };

    NetworkManager nm;
    BattalionSpawner spawner;
    float nextUpdate;

    // Sunucu: takım bu bölüğü görüyor mu? (atış ve bot da buna uyar — görmediğine ateş yok)
    public static bool TeamSees(int team, Battalion b) => b.Team.Value == team || seenBy[team].Contains(b);

    void Awake()
    {
        nm = GetComponent<NetworkManager>();
        spawner = GetComponent<BattalionSpawner>();
    }

    void Update()
    {
        var match = MatchManager.Current;
        if (!nm.IsServer || match == null || match.Phase.Value < MatchPhase.Battle || Time.time < nextUpdate) return;
        nextUpdate = Time.time + updateInterval;

        bool revealAll = match.Phase.Value == MatchPhase.Ended; // maç bitti: herkes her şeyi görsün
        for (int team = 0; team < 2; team++)
        {
            seenBy[team].RemoveWhere(b => b == null || !b.IsSpawned);
            ulong viewer = spawner.OwnerOf(team);
            bool networked = !spawner.IsBot(team) && nm.ConnectedClients.ContainsKey(viewer);

            foreach (var target in Battalion.All)
            {
                if (target.Team.Value == team) continue;
                bool was = seenBy[team].Contains(target);
                bool now = revealAll || AnySees(team, target, was);
                if (now) seenBy[team].Add(target); else seenBy[team].Remove(target);
                if (!networked) continue; // bot sunucuda yaşar, ona paket gitmez

                var obj = target.NetworkObject;
                bool sent = obj.IsNetworkVisibleTo(viewer);
                if (now && !sent) obj.NetworkShow(viewer);
                else if (!now && sent) obj.NetworkHide(viewer);
            }
        }
    }

    static bool AnySees(int team, Battalion target, bool wasVisible)
    {
        foreach (var own in Battalion.All)
        {
            if (own.Team.Value != team) continue;
            float distance = Vector3.Distance(own.transform.position, target.transform.position);
            if (FogRules.Sees(distance, own.Definition.sightRange, wasVisible)) return true;
        }
        return false;
    }
}

// Görüş kuralı: saf, menüden test edilir.
public static class FogRules
{
    // Sınırda gidip gelen bölük her karede doğup silinmesin: gizlemek için biraz daha uzağa gitmeli.
    public const float Hysteresis = 10f;

    public static bool Sees(float distance, float sightRange, bool wasVisible) =>
        distance <= sightRange + (wasVisible ? Hysteresis : 0f);
}
