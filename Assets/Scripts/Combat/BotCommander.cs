using Unity.Netcode;
using UnityEngine;

// Sunucu: sunucuya ait (bot) bölükleri yönetir. Oyuncuyla aynı emir yolunu (RPC) kullanır.
// Kural: gördüğü en yakın düşmana saldır; yakın dövüşçü yaklaşınca koşar (şarj). Kimseyi görmezse ilerler.
[RequireComponent(typeof(NetworkManager))]
public class BotCommander : MonoBehaviour
{
    [SerializeField] float thinkInterval = 1f;
    [SerializeField] float chargeDistance = 35f;

    NetworkManager nm;
    float nextThink;

    void Awake() => nm = GetComponent<NetworkManager>();

    void Update()
    {
        if (!nm.IsServer || !MatchManager.BattleRunning || Time.time < nextThink) return;
        nextThink = Time.time + thinkInterval;

        foreach (var bot in Battalion.All)
        {
            if (bot.OwnerClientId != NetworkManager.ServerClientId || bot.InCombat.Value || bot.IsRouting) continue;

            Battalion target = null;
            float best = float.MaxValue;
            foreach (var other in Battalion.All)
            {
                if (other.Team.Value == bot.Team.Value || !FogSystem.TeamSees(bot.Team.Value, other)) continue; // bot da sise uyar
                float d = (other.transform.position - bot.transform.position).sqrMagnitude;
                if (d < best) { best = d; target = other; }
            }
            if (target == null)
            {
                // Kimse görünmüyor: rakip tarafa doğru ilerle (keşif).
                Vector3 ahead = new Vector3(bot.transform.position.x, 0f, bot.Team.Value == 0 ? 60f : -60f);
                bot.MoveRpc(ahead, ahead - bot.transform.position, 0);
                continue;
            }

            // Saldırı emri: takip eder; atıcı menzilde kendisi durup ateş eder.
            if (bot.AttackTarget != target) bot.AttackRpc(target.NetworkObject);
            float distance = Vector3.Distance(target.transform.position, bot.transform.position);
            bool charge = !bot.IsRanged && distance < chargeDistance;
            if (bot.Running.Value != charge) bot.SetRunRpc(charge);
        }
    }
}
