using Unity.Netcode;
using UnityEngine;

public enum MatchPhase : byte { Waiting, ArmySelect, Deploy, Battle, Ended }

// Maç akışı (sunucu): iki takım → ordu kurma (90 sn) → gizli yerleştirme (60 sn) → savaş (15 dk) → sonuç.
// İki taraf da hazırsa süre beklenmez. Zafer: rakibin sahada bölüğü kalmayınca (imha olan ya da
// kenardan kaçan gitmiş sayılır); süre dolunca kaçmayan bölüklerdeki asker sayısı çok olan kazanır.
public class MatchManager : NetworkBehaviour
{
    public const sbyte NoWinner = -1, Draw = 2;
    public static readonly string[] TeamNames = { "Mavi", "Kırmızı" };

    public static MatchManager Current { get; private set; }

    // Match objesi yoksa (eski sahne) savaş hep açık sayılır.
    public static bool BattleRunning => Current == null || Current.Phase.Value == MatchPhase.Battle;
    public static bool Deploying => Current != null && Current.Phase.Value == MatchPhase.Deploy;

    [SerializeField] UnitRoster roster;
    [SerializeField] float armySelectSeconds = 90f;
    [SerializeField] float deploySeconds = 60f;
    [SerializeField] float matchDurationSeconds = 15f * 60f;

    public readonly NetworkVariable<MatchPhase> Phase = new NetworkVariable<MatchPhase>();
    public readonly NetworkVariable<double> PhaseEndTime = new NetworkVariable<double>(); // sunucu zamanı
    public readonly NetworkVariable<sbyte> Winner = new NetworkVariable<sbyte>(NoWinner);
    public readonly NetworkVariable<byte> ReadyMask = new NetworkVariable<byte>();       // bit t = takım t hazır
    public readonly NetworkVariable<ulong> Owner0 = new NetworkVariable<ulong>(ulong.MaxValue);
    public readonly NetworkVariable<ulong> Owner1 = new NetworkVariable<ulong>(ulong.MaxValue);

    public UnitRoster Roster => roster;

    readonly byte[][] armies = new byte[2][];
    BattalionSpawner spawner;

    public override void OnNetworkSpawn()
    {
        Current = this;
        if (IsServer) spawner = NetworkManager.GetComponent<BattalionSpawner>();
    }

    public override void OnNetworkDespawn()
    {
        if (Current == this) Current = null;
    }

    public int LocalTeam
    {
        get
        {
            if (IsServer) return -1; // sunucu (editördeki ana pencere) izleyicidir; bot sunucuya ait olsa da
            ulong id = NetworkManager.LocalClientId;
            return Owner0.Value == id ? 0 : Owner1.Value == id ? 1 : -1;
        }
    }

    public int[] MaxSlots()
    {
        var max = new int[roster.units.Length];
        for (int i = 0; i < max.Length; i++) max[i] = roster.units[i].maxSlots;
        return max;
    }

    [Rpc(SendTo.Server)]
    public void SubmitArmyRpc(byte[] counts, RpcParams rpcParams = default)
    {
        int team = spawner.TeamOf(rpcParams.Receive.SenderClientId);
        // Güven sınırı: istemciden gelen ordu kurallara uymalı.
        if (Phase.Value != MatchPhase.ArmySelect || team < 0 || !ArmyRules.IsValid(counts, MaxSlots())) return;
        armies[team] = counts;
        ReadyMask.Value |= (byte)(1 << team);
    }

    [Rpc(SendTo.Server)]
    public void DeployReadyRpc(RpcParams rpcParams = default)
    {
        int team = spawner.TeamOf(rpcParams.Receive.SenderClientId);
        if (Phase.Value != MatchPhase.Deploy || team < 0) return;
        ReadyMask.Value |= (byte)(1 << team);
    }

    void Update()
    {
        if (!IsServer) return;

        if (Owner0.Value != spawner.OwnerOf(0)) Owner0.Value = spawner.OwnerOf(0);
        if (Owner1.Value != spawner.OwnerOf(1)) Owner1.Value = spawner.OwnerOf(1);

        bool timeUp = NetworkManager.ServerTime.Time >= PhaseEndTime.Value;
        bool bothReady = ReadyMask.Value == 3;

        switch (Phase.Value)
        {
            case MatchPhase.Waiting:
                if (!spawner.HasTeam(0) || !spawner.HasTeam(1)) return;
                for (int t = 0; t < 2; t++)
                    if (spawner.IsBot(t)) armies[t] = ArmyRules.Default(MaxSlots());
                Enter(MatchPhase.ArmySelect, armySelectSeconds);
                break;

            case MatchPhase.ArmySelect:
                if (!bothReady && !timeUp) return;
                for (int t = 0; t < 2; t++)
                    spawner.SpawnArmy((byte)t, armies[t] ?? ArmyRules.Default(MaxSlots())); // süre bittiyse varsayılan
                Enter(MatchPhase.Deploy, deploySeconds);
                break;

            case MatchPhase.Deploy:
                if (!bothReady && !timeUp) return;
                spawner.RevealAll();
                Enter(MatchPhase.Battle, matchDurationSeconds);
                break;

            case MatchPhase.Battle:
                CheckVictory(timeUp);
                break;
        }
    }

    void Enter(MatchPhase phase, float seconds)
    {
        Phase.Value = phase;
        PhaseEndTime.Value = NetworkManager.ServerTime.Time + seconds;
        // Bot her fazda hemen hazır.
        byte ready = 0;
        for (int t = 0; t < 2; t++)
            if (spawner.IsBot(t)) ready |= (byte)(1 << t);
        ReadyMask.Value = ready;
    }

    void CheckVictory(bool timeUp)
    {
        var (present0, present1) = (false, false);
        int soldiers0 = 0, soldiers1 = 0;
        foreach (var b in Battalion.All)
        {
            int standing = b.IsRouting ? 0 : b.AliveCount;
            if (b.Team.Value == 0) { present0 = true; soldiers0 += standing; }
            else { present1 = true; soldiers1 += standing; }
        }

        sbyte result = MatchRules.Decide(present0, present1, soldiers0, soldiers1, timeUp);
        if (result == NoWinner) return;
        Winner.Value = result;
        Phase.Value = MatchPhase.Ended;
    }
}

// Zafer kararı: saf, menüden test edilir.
public static class MatchRules
{
    public static sbyte Decide(bool present0, bool present1, int soldiers0, int soldiers1, bool timeUp)
    {
        if (!present0 && !present1) return MatchManager.Draw;
        if (!present1) return 0;
        if (!present0) return 1;
        if (!timeUp) return MatchManager.NoWinner;
        return soldiers0 == soldiers1 ? MatchManager.Draw : soldiers0 > soldiers1 ? (sbyte)0 : (sbyte)1;
    }
}
