using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Sunucu: bölük-vs-bölük savaş çözümü. Tek tek düello yok; temas cephesi, kanat/arka çarpanı,
// yorgunluk ve şarj darbesi ile bölük başına kayıp hesaplanır, ölen asker yöne göre seçilir.
[RequireComponent(typeof(NetworkManager))]
public class CombatSystem : MonoBehaviour
{
    [SerializeField] float contactMargin = 1f;   // bu kadar yakın dikdörtgenler temasta sayılır
    [SerializeField] float lethality = 0.05f;    // cephedeki asker × saldırı/savunma × bu = öldürme/sn
    [SerializeField] float transitionVulnerability = 1.5f; // formasyon değiştiren bölük daha çok kayıp verir

    NetworkManager nm;
    readonly Dictionary<(ulong, ulong), float> pendingKills = new Dictionary<(ulong, ulong), float>();
    readonly HashSet<(ulong, ulong)> contacts = new HashSet<(ulong, ulong)>();
    readonly HashSet<(ulong, ulong)> contactsThisFrame = new HashSet<(ulong, ulong)>();
    readonly List<Battalion> dead = new List<Battalion>();
    readonly HashSet<Battalion> fighting = new HashSet<Battalion>();

    void Awake() => nm = GetComponent<NetworkManager>();

    void Update()
    {
        if (!nm.IsServer) return;

        float dt = Time.deltaTime;
        var all = Battalion.All;
        contactsThisFrame.Clear();
        fighting.Clear();

        for (int i = 0; i < all.Count; i++)
        for (int j = i + 1; j < all.Count; j++)
        {
            Battalion a = all[i], b = all[j];
            if (a.Team.Value == b.Team.Value || a.AliveCount == 0 || b.AliveCount == 0) continue;

            Obb oa = a.GetObb(), ob = b.GetObb();
            if (!CombatMath.Overlaps(oa, ob, contactMargin)) continue;

            var key = (a.NetworkObjectId, b.NetworkObjectId);
            contactsThisFrame.Add(key);
            fighting.Add(a);
            fighting.Add(b);

            HitSide sideOnB = CombatMath.SideOf(ob, oa.Center);
            HitSide sideOnA = CombatMath.SideOf(oa, ob.Center);
            int fightersA = Fighters(a, oa, ob, sideOnA, out float centerOnA);
            int fightersB = Fighters(b, ob, oa, sideOnB, out float centerOnB);

            float killsOnB = MeleeKills(a, fightersA, b, sideOnB) * dt;
            float killsOnA = MeleeKills(b, fightersB, a, sideOnA) * dt;

            // Kanattan/arkadan yenen saldırı moral eritir (kaçan saldıran baskı kurmaz).
            if (!a.IsRouting) b.ServerMoraleHit(Morale.SidePressurePerSec(sideOnB, b.Type.Value) * dt);
            if (!b.IsRouting) a.ServerMoraleHit(Morale.SidePressurePerSec(sideOnA, a.Type.Value) * dt);

            // Şarj: sadece temasın ilk karesinde, o andaki hıza göre.
            if (!contacts.Contains(key))
            {
                Charge(a, fightersA, b, sideOnB, ref killsOnB, ref killsOnA);
                Charge(b, fightersB, a, sideOnA, ref killsOnA, ref killsOnB);
            }

            ApplyKills(b, (b.NetworkObjectId, a.NetworkObjectId), killsOnB, sideOnB, centerOnB);
            ApplyKills(a, (a.NetworkObjectId, b.NetworkObjectId), killsOnA, sideOnA, centerOnA);
        }

        contacts.Clear();
        contacts.UnionWith(contactsThisFrame);

        foreach (var b in all)
        {
            bool inCombat = fighting.Contains(b);
            if (b.InCombat.Value != inCombat) b.InCombat.Value = inCombat;
            if (b.AliveCount == 0) dead.Add(b);
        }
        foreach (var b in dead) b.NetworkObject.Despawn();
        dead.Clear();
    }

    // Temas cephesi: kenar boyunca çakışan uzunluk kaç askerin gerçekten dövüştüğünü belirler.
    static int Fighters(Battalion self, in Obb selfObb, in Obb enemyObb, HitSide side, out float contactCenter)
    {
        float length = CombatMath.ContactLength(selfObb, enemyObb, side, out contactCenter);
        var def = self.Definition;
        int edgeCapacity = side == HitSide.Front || side == HitSide.Rear
            ? Formation.EffectiveWidth(self.Type.Value, self.FormationWidth.Value, self.SoldierCount.Value)
            : Formation.RankCount(self.SoldierCount.Value,
                Formation.EffectiveWidth(self.Type.Value, self.FormationWidth.Value, self.SoldierCount.Value));
        int fighters = Mathf.FloorToInt(length / def.slotSpacing) + 1;
        return Mathf.Min(fighters, edgeCapacity, self.AliveCount);
    }

    float MeleeKills(Battalion attacker, int fighters, Battalion defender, HitSide sideOnDefender)
    {
        float fatigue = (0.5f + 0.5f * attacker.Stamina01) * Morale.AttackFactor(attacker.Status.Value);
        float vulnerable = defender.IsTransitioning ? transitionVulnerability : 1f;
        return fighters * lethality * attacker.Definition.attack / defender.Definition.defense * fatigue
               * CombatMath.SideMultiplier(sideOnDefender, defender.Type.Value) * vulnerable;
    }

    // Şarj darbesi = cephedeki asker × hız × chargeImpact. Toparlanmış (geçişte olmayan) bir
    // kargı hattına önden çarpan, darbenin antiChargeFront kadarını kendisi yer.
    static void Charge(Battalion attacker, int fighters, Battalion defender, HitSide sideOnDefender,
        ref float killsOnDefender, ref float killsOnAttacker)
    {
        // Yürüyen ya da kaçan şarj etmez.
        if (attacker.IsRouting || attacker.ServerSpeed <= attacker.Definition.walkSpeed + 0.1f) return;

        float impact = fighters * attacker.ServerSpeed * attacker.Definition.chargeImpact
                       * (0.5f + 0.5f * attacker.Stamina01)
                       * CombatMath.SideMultiplier(sideOnDefender, defender.Type.Value);

        float brace = sideOnDefender == HitSide.Front && !defender.IsTransitioning ? defender.Definition.antiChargeFront : 0f;
        killsOnDefender += impact * (1f - brace);
        killsOnAttacker += impact * brace;
    }

    void ApplyKills(Battalion victim, (ulong, ulong) key, float kills, HitSide side, float contactCenter)
    {
        pendingKills.TryGetValue(key, out float pending);
        pending += kills;
        var def = victim.Definition;
        while (pending >= 1f)
        {
            int slot = CombatMath.PickCasualty(victim.Type.Value, victim.FormationWidth.Value,
                victim.SoldierCount.Value, def.slotSpacing, victim.Alive.Value, side, contactCenter);
            if (slot < 0) break;
            victim.ServerKill(slot);
            pending -= 1f;
        }
        pendingKills[key] = pending;
    }
}
