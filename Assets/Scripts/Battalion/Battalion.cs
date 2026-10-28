using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Ağdaki tek birim: bölük. Askerler ağda yok, istemcide BattalionView hesaplar.
// Simülasyon SADECE sunucuda; istemciler transform'u NetworkTransform ile alır.
[RequireComponent(typeof(NetworkObject))]
public class Battalion : NetworkBehaviour
{
    public const float MapHalfSize = 100f;
    public const int MinLineWidth = 4;

    public static readonly List<Battalion> All = new List<Battalion>();

    [SerializeField] UnitRoster roster;
    UnitDefinition definition; // roster[UnitIndex]; spawn'da iki tarafta da aynı asset'ten
    public UnitDefinition Definition => definition;
    public UnitRoster Roster => roster;

    public readonly NetworkVariable<byte> UnitIndex = new NetworkVariable<byte>();
    public readonly NetworkVariable<bool> HoldFire = new NetworkVariable<bool>();
    public readonly NetworkVariable<double> ReloadEndTime = new NetworkVariable<double>(); // sunucu zamanı

    // Spawn'dan önce sunucunun verdiği başlangıç değerleri.
    byte pendingTeam, pendingUnit;

    public readonly NetworkVariable<byte> Team = new NetworkVariable<byte>();
    public readonly NetworkVariable<FormationType> Type = new NetworkVariable<FormationType>();
    public readonly NetworkVariable<int> FormationWidth = new NetworkVariable<int>(); // hat genişliği
    public readonly NetworkVariable<int> SoldierCount = new NetworkVariable<int>();   // slot sayısı (ölüler dahil)
    public readonly NetworkVariable<AliveMask> Alive = new NetworkVariable<AliveMask>();
    public readonly NetworkVariable<double> TransitionEndTime = new NetworkVariable<double>(); // sunucu zamanı
    public readonly NetworkVariable<bool> Running = new NetworkVariable<bool>();
    public readonly NetworkVariable<byte> StaminaPercent = new NetworkVariable<byte>(100);
    public readonly NetworkVariable<bool> InCombat = new NetworkVariable<bool>();
    public readonly NetworkVariable<MoraleState> Status = new NetworkVariable<MoraleState>();
    public readonly NetworkVariable<byte> MoralePercent = new NetworkVariable<byte>(100);
    public readonly NetworkVariable<short> BannerSlot = new NetworkVariable<short>(); // sancaktar

    Vector3 destination;
    Quaternion finalFacing;
    bool moving, hasFinalFacing;

    // Sunucu: saldırı emrinin hedefi (takip edilir; atıcı menzilde durur ve önce buna ateş eder).
    public Battalion AttackTarget { get; private set; }

    public const float MeleePenetration = 3f;   // hatlar bu kadar iç içe geçebilir (kutu kenarında durmasın)
    public const float AutoChargeDistance = 35f; // saldırıda yakın dövüşçü hedefe bu kadar kala kendiliğinden koşar
    bool autoCharge;
    float stamina = 1f;
    float morale = 100f, moraleCeiling = 100f;
    float recentLosses; // hızlı kayıp şoku için, zamanla söner
    bool bannerLost;

    // Sunucu: bu karedeki gerçek hız (şarj darbesi bundan hesaplanır).
    public float ServerSpeed { get; private set; }
    public float Stamina01 => IsServer ? stamina : StaminaPercent.Value / 100f;
    public int AliveCount => Alive.Value.Count;

    // Sunucu: Spawn'dan önce çağrılır.
    public void ServerInit(byte team, byte unitIndex)
    {
        pendingTeam = team;
        pendingUnit = unitIndex;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Team.Value = pendingTeam;
            UnitIndex.Value = (byte)Mathf.Min(pendingUnit, roster.units.Length - 1);
        }
        definition = roster.units[UnitIndex.Value];
        All.Add(this);
        if (!IsServer) return;

        Type.Value = FormationType.Line;
        FormationWidth.Value = definition.defaultWidth;
        SoldierCount.Value = Mathf.Min(definition.soldierCount, AliveMask.Capacity);
        Alive.Value = AliveMask.Full(SoldierCount.Value);
        BannerSlot.Value = (short)FrontCenterAlive();
    }

    public bool IsRouting => Status.Value == MoraleState.Routing;

    // Ön safın ortasındaki hayattaki asker (sancaktar yeri).
    int FrontCenterAlive() =>
        CombatMath.PickCasualty(Type.Value, FormationWidth.Value, SoldierCount.Value, definition.slotSpacing,
            Alive.Value, HitSide.Front, 0f);

    public override void OnNetworkDespawn() => All.Remove(this);

    // Geçiş sırasında bölük yavaş ve savunmasız.
    public bool IsTransitioning => NetworkManager != null && NetworkManager.ServerTime.Time < TransitionEndTime.Value;

    public Vector2 HalfExtents(out float maxRadius) =>
        Formation.HalfExtents(Type.Value, FormationWidth.Value, SoldierCount.Value, definition.slotSpacing, out maxRadius);

    public Vector3 SlotLocalOffset(int slot) =>
        Formation.SlotLocalOffset(Type.Value, slot, FormationWidth.Value, SoldierCount.Value, definition.slotSpacing);

    public Vector3 SlotWorldPosition(int slot) => transform.TransformPoint(SlotLocalOffset(slot));

    // Yerdeki dikdörtgen: slot merkezleri + yarım asker payı.
    public Obb GetObb() => GetObb(transform.position);

    Obb GetObb(Vector3 position) =>
        new Obb(position, transform.right, transform.forward,
            HalfExtents(out _) + Vector2.one * (definition.slotSpacing * 0.5f));

    // Dünya noktası bölüğün dikdörtgeni içinde mi (seçim için, bir slot payıyla).
    public bool Contains(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        Vector2 half = HalfExtents(out _) + Vector2.one * definition.slotSpacing;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.y;
    }

    // facing sıfırsa varışta yön değişmez. lineWidth 0 ise genişlik değişmez.
    [Rpc(SendTo.Server)]
    public void MoveRpc(Vector3 target, Vector3 facing, int lineWidth, RpcParams rpcParams = default)
    {
        // Güven sınırı: sadece sahibi emir verebilir, değerler geçerli olmalı.
        if (rpcParams.Receive.SenderClientId != OwnerClientId || IsRouting) return;
        if (!IsFinite(target) || !IsFinite(facing)) return;
        AttackTarget = null; // yeni hareket emri saldırıyı bozar

        destination = new Vector3(
            Mathf.Clamp(target.x, -MapHalfSize, MapHalfSize), 0f,
            Mathf.Clamp(target.z, -MapHalfSize, MapHalfSize));
        moving = true;

        facing.y = 0f;
        hasFinalFacing = facing.sqrMagnitude > 0.01f;
        if (hasFinalFacing) finalFacing = Quaternion.LookRotation(facing);

        // Yerleştirme: yürümez, kendi bölgesi içinde anında yerine konur.
        if (MatchManager.Deploying)
        {
            transform.position = ArmyRules.ClampToZone(Team.Value, destination);
            if (hasFinalFacing) transform.rotation = finalFacing;
            moving = false;
        }

        if (lineWidth > 0 && Type.Value == FormationType.Line)
        {
            // Genişlik yolda değişir, askerler yeni yerlerine akar; yavaşlatan geçiş sadece şekil değişiminde.
            FormationWidth.Value = Mathf.Clamp(lineWidth, MinLineWidth, SoldierCount.Value);
        }
    }

    [Rpc(SendTo.Server)]
    public void SetFormationRpc(FormationType type, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || IsRouting) return;
        if (!System.Enum.IsDefined(typeof(FormationType), type) || type == Type.Value) return;

        Type.Value = type;
        StartTransition();
    }

    [Rpc(SendTo.Server)]
    public void SetRunRpc(bool run, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || IsRouting) return;
        Running.Value = run;
    }

    // Ateşi tut / serbest bırak: salvoyu temasa saklamak oyunun merkez kararı.
    [Rpc(SendTo.Server)]
    public void SetHoldFireRpc(bool hold, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || IsRouting) return;
        HoldFire.Value = hold;
    }

    public bool IsRanged => definition.range > 0f;
    public bool IsReloading => NetworkManager != null && NetworkManager.ServerTime.Time < ReloadEndTime.Value;

    // Sunucu: salvo atıldı; doldurma başlar, istemciler kozmetik efekti çizer.
    public void ServerFired(Vector3 targetPosition)
    {
        ReloadEndTime.Value = NetworkManager.ServerTime.Time + definition.reloadTime;

        // Efekt sadece atanı gören istemcilere: görmeyene gitse atanın yerini ele verir.
        var observers = new List<ulong>();
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            if (NetworkObject.IsNetworkVisibleTo(clientId)) observers.Add(clientId);
        if (observers.Count > 0) VolleyRpc(targetPosition, RpcTarget.Group(observers, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void VolleyRpc(Vector3 targetPosition, RpcParams rpcParams) => GetComponent<BattalionView>().ShowVolley(targetPosition);

    // Sunucu: savaş sistemi bir askeri öldürür (bölüğün ağdan kalkmasını CombatSystem yapar).
    // moraleMultiplier: barut kuralı — tüfekle ölen asker daha çok moral kırar.
    public void ServerKill(int slot, float moraleMultiplier = 1f)
    {
        var mask = Alive.Value;
        mask.Set(slot, false);
        Alive.Value = mask;

        ServerMoraleHit(Morale.CasualtyLoss(SoldierCount.Value) * Morale.LossShock(recentLosses, SoldierCount.Value) * moraleMultiplier);
        recentLosses += 1f;
        if (slot == BannerSlot.Value)
        {
            // Sancak düştü: tek seferlik şok, ön saftan biri devralır.
            if (!bannerLost) ServerMoraleHit(Morale.BannerShock);
            bannerLost = true;
            BannerSlot.Value = (short)FrontCenterAlive();
        }
    }

    // Sunucu: moral kaybı (disiplin böler).
    public void ServerMoraleHit(float amount) => morale -= amount / Mathf.Max(0.1f, definition.discipline);

    void UpdateMorale(float dt)
    {
        recentLosses = Morale.DecayRecentLosses(recentLosses, dt);
        if (IsRouting)
        {
            if (!EnemyWithin(Morale.EnemyNearRadius))
                morale += (Morale.RallyPerSec + (SteadyFriendWithin(Morale.FriendRadius) ? Morale.RallySupportPerSec : 0f)) * dt;
        }
        else if (!InCombat.Value) morale += Morale.RecoveryPerSec * dt;
        else if (stamina < 0.25f) ServerMoraleHit(Morale.FatiguePerSec * dt);

        morale = Mathf.Clamp(morale, 0f, moraleCeiling);
        byte percent = (byte)Mathf.RoundToInt(morale);
        if (percent != MoralePercent.Value) MoralePercent.Value = percent;

        var next = Morale.Evaluate(Status.Value, morale);
        if (next == Status.Value) return;

        if (next == MoraleState.Routing) StartRout();
        else if (IsRouting) Rally();
        Status.Value = next;
    }

    // Saldır: düşman bölüğe git ve savaş; hareket ederse takip et.
    [Rpc(SendTo.Server)]
    public void AttackRpc(NetworkObjectReference targetRef, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || IsRouting || MatchManager.Deploying) return;
        // Güven sınırı: hedef var olmalı, düşman olmalı ve takım onu görüyor olmalı (sisli hedefe emir yok).
        if (!targetRef.TryGet(out NetworkObject obj) || !obj.TryGetComponent(out Battalion target)) return;
        if (target.Team.Value == Team.Value || !FogSystem.TeamSees(Team.Value, target)) return;
        AttackTarget = target;
        hasFinalFacing = false;
    }

    [Rpc(SendTo.Server)]
    public void HaltRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || IsRouting) return;
        AttackTarget = null;
        moving = false;
    }

    // Saldırı hedefini takip: hedef kaybolursa (öldü, kaçıp sise girdi) dur.
    void FollowAttackTarget(float dt)
    {
        var target = AttackTarget;
        if (target == null) return;
        if (!target.IsSpawned || !FogSystem.TeamSees(Team.Value, target))
        {
            AttackTarget = null;
            moving = false;
            return;
        }

        Vector3 to = target.transform.position - transform.position;
        to.y = 0f;
        if (IsRanged && to.magnitude <= definition.range * 0.85f)
        {
            // Menzilde: dur, hedefe dön (ateşi CombatSystem açar).
            moving = false;
            HalfExtents(out float maxRadius);
            float maxTurnDeg = Formation.MaxTurnRate(definition.runSpeed, maxRadius) * Mathf.Rad2Deg * dt;
            if (to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), maxTurnDeg);
            return;
        }
        destination = new Vector3(target.transform.position.x, 0f, target.transform.position.z);
        autoCharge = !IsRanged && to.magnitude < AutoChargeDistance;
        moving = true;
    }

    void StartRout()
    {
        AttackTarget = null;
        // Kontrol oyuncudan çıkar, kendi tarafının kenarına koşar (düşmanın arkasına değil). Yakındaki dostlar sarsılır.
        // Her kaçış moral tavanını düşürür: 75, 50, 25 → üçüncü kaçıştan toparlanılamaz (25 < RallyAbove).
        moraleCeiling -= Morale.CeilingDropPerRout;
        Vector3 p = transform.position;
        destination = Morale.FleePoint(Team.Value, p, MapHalfSize);
        moving = true;
        hasFinalFacing = false;

        foreach (var other in All)
            if (other != this && other.Team.Value == Team.Value && !other.IsRouting
                && (other.transform.position - p).sqrMagnitude < Morale.FriendRadius * Morale.FriendRadius)
                other.ServerMoraleHit(Morale.FriendRoutShock);
    }

    void Rally()
    {
        moving = false;
        Running.Value = false;
    }

    bool EnemyWithin(float radius)
    {
        foreach (var other in All)
            if (other.Team.Value != Team.Value && (other.transform.position - transform.position).sqrMagnitude < radius * radius)
                return true;
        return false;
    }

    bool SteadyFriendWithin(float radius)
    {
        foreach (var other in All)
            if (other != this && other.Team.Value == Team.Value && other.Status.Value == MoraleState.Steady
                && (other.transform.position - transform.position).sqrMagnitude < radius * radius)
                return true;
        return false;
    }

    // Yerleştirmede dizilim anında değişir; savaşta süre ister.
    void StartTransition()
    {
        if (!MatchManager.Deploying)
            TransitionEndTime.Value = NetworkManager.ServerTime.Time + definition.formationChangeTime;
    }

    void Update()
    {
        if (!IsServer) return;

        float dt = Time.deltaTime;
        ServerSpeed = 0f;
        UpdateMorale(dt);
        autoCharge &= AttackTarget != null;
        bool runningNow = IsRouting || ((Running.Value || autoCharge) && moving && stamina > 0f); // kaçan yorgunluğa bakmaz
        stamina = Mathf.Clamp01(stamina + (runningNow ? -definition.staminaDrainPerSec : definition.staminaRegenPerSec) * dt);
        byte percent = (byte)Mathf.RoundToInt(stamina * 100f);
        if (percent != StaminaPercent.Value) StaminaPercent.Value = percent;

        if (!IsRouting) FollowAttackTarget(dt);
        if (moving) Move(runningNow, dt);

        // Kaçan bölük harita kenarına varınca sahadan çıkar (zaferde yok sayılır).
        Vector3 p = transform.position;
        if (IsRouting && Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)) >= MapHalfSize - 1f)
            NetworkObject.Despawn();
    }

    void Move(bool running, float dt)
    {
        // shortcut: düz çizgide hareket, engel yok; arazi/engel gelince NavMesh'e geçilecek.
        HalfExtents(out float maxRadius);
        float maxTurnDeg = Formation.MaxTurnRate(definition.runSpeed, maxRadius) * Mathf.Rad2Deg * dt;

        Vector3 toTarget = destination - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        // Total War gibi: bölük yerinde dönüp beklemez, emri aldığı an hedefe yürür. Yön (varış yönü
        // ya da yürüyüş yönü) yolda, dönüş hızı sınırıyla çevrilir; askerler yeni slotlarına akarak geçer.
        Quaternion wanted = hasFinalFacing ? finalFacing
            : distance > 0.3f ? Quaternion.LookRotation(toTarget) : transform.rotation;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, maxTurnDeg);

        if (distance < 0.3f)
        {
            // Vardı; varış yönüne dönmesi bitince dur.
            if (Quaternion.Angle(transform.rotation, wanted) <= 0.5f) moving = false;
            return;
        }

        // Geçişte (formasyon değişimi) yavaş.
        float speed = (running ? definition.runSpeed : definition.walkSpeed)
                      * (IsTransitioning ? definition.transitionSpeedFactor : 1f);
        Vector3 step = toTarget / distance * Mathf.Min(speed * dt, distance);

        if (BlockedByEnemy(transform.position + step, step)) return; // temas: içine giremez, geri çekilebilir
        transform.position += step;
        ServerSpeed = step.magnitude / dt;
    }

    // Yeni konum bir düşmanla çakışıyor ve adım ona doğruysa hareket yok.
    bool BlockedByEnemy(Vector3 next, Vector3 step)
    {
        if (IsRouting) return false; // kaçan kimseye takılmaz, düşmanın içinden geçip kaçar
        Obb self = GetObb(next);
        foreach (var other in All)
        {
            if (other == this || other.Team.Value == Team.Value) continue;
            Vector3 toOther = other.transform.position - next;
            if (Vector3.Dot(step, toOther) > 0f && CombatMath.Overlaps(self, other.GetObb(), -MeleePenetration))
                return true;
        }
        return false;
    }

    static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
}
