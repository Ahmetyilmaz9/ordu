using Unity.Netcode;
using UnityEngine;

// Sunucu: sunucuya ait (bot) bölükleri yönetir. Oyuncuyla aynı emir yolunu (RPC) kullanır.
// Kural: en yakın düşmana yürü; yakın dövüşçü yaklaşınca koşar (şarj), atıcı menzilde durup ateş eder.
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
                if (other.Team.Value == bot.Team.Value) continue;
                float d = (other.transform.position - bot.transform.position).sqrMagnitude;
                if (d < best) { best = d; target = other; }
            }
            if (target == null) continue;

            Vector3 toTarget = target.transform.position - bot.transform.position;
            float distance = toTarget.magnitude;

            // Atıcı: menzilin içine girince durur, hedefe dönük ateş eder (CombatSystem kendisi atar).
            if (bot.IsRanged && distance < bot.Definition.range * 0.8f)
            {
                bot.MoveRpc(bot.transform.position, toTarget, 0);
                if (bot.Running.Value) bot.SetRunRpc(false);
                continue;
            }

            bot.MoveRpc(target.transform.position, toTarget, 0);
            bool charge = !bot.IsRanged && distance < chargeDistance;
            if (bot.Running.Value != charge) bot.SetRunRpc(charge);
        }
    }
}
