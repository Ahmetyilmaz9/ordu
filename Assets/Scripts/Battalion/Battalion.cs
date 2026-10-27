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

    [SerializeField] UnitDefinition definition;
    public UnitDefinition Definition => definition;

    public readonly NetworkVariable<byte> Team = new NetworkVariable<byte>();
    public readonly NetworkVariable<FormationType> Type = new NetworkVariable<FormationType>();
    public readonly NetworkVariable<int> FormationWidth = new NetworkVariable<int>(); // hat genişliği
    public readonly NetworkVariable<int> SoldierCount = new NetworkVariable<int>();   // slot sayısı (ölüler dahil)
    public readonly NetworkVariable<AliveMask> Alive = new NetworkVariable<AliveMask>();
    public readonly NetworkVariable<double> TransitionEndTime = new NetworkVariable<double>(); // sunucu zamanı
    public readonly NetworkVariable<bool> Running = new NetworkVariable<bool>();
    public readonly NetworkVariable<byte> StaminaPercent = new NetworkVariable<byte>(100);
    public readonly NetworkVariable<bool> InCombat = new NetworkVariable<bool>();

    Vector3 destination;
    Quaternion finalFacing;
    bool moving, hasFinalFacing;
    float stamina = 1f;

    // Sunucu: bu karedeki gerçek hız (şarj darbesi bundan hesaplanır).
    public float ServerSpeed { get; private set; }
    public float Stamina01 => IsServer ? stamina : StaminaPercent.Value / 100f;
    public int AliveCount => Alive.Value.Count;

    public override void OnNetworkSpawn()
    {
        All.Add(this);
        if (!IsServer) return;

        Type.Value = FormationType.Line;
        FormationWidth.Value = definition.defaultWidth;
        SoldierCount.Value = Mathf.Min(definition.soldierCount, AliveMask.Capacity);
        Alive.Value = AliveMask.Full(SoldierCount.Value);
    }

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
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (!IsFinite(target) || !IsFinite(facing)) return;

        destination = new Vector3(
            Mathf.Clamp(target.x, -MapHalfSize, MapHalfSize), 0f,
            Mathf.Clamp(target.z, -MapHalfSize, MapHalfSize));
        moving = true;

        facing.y = 0f;
        hasFinalFacing = facing.sqrMagnitude > 0.01f;
        if (hasFinalFacing) finalFacing = Quaternion.LookRotation(facing);

        if (lineWidth > 0 && Type.Value == FormationType.Line)
        {
            int width = Mathf.Clamp(lineWidth, MinLineWidth, SoldierCount.Value);
            if (width != FormationWidth.Value)
            {
                FormationWidth.Value = width;
                StartTransition();
            }
        }
    }

    [Rpc(SendTo.Server)]
    public void SetFormationRpc(FormationType type, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (!System.Enum.IsDefined(typeof(FormationType), type) || type == Type.Value) return;

        Type.Value = type;
        StartTransition();
    }

    [Rpc(SendTo.Server)]
    public void SetRunRpc(bool run, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        Running.Value = run;
    }

    // Sunucu: savaş sistemi bir askeri öldürür (bölüğün ağdan kalkmasını CombatSystem yapar).
    public void ServerKill(int slot)
    {
        var mask = Alive.Value;
        mask.Set(slot, false);
        Alive.Value = mask;
    }

    void StartTransition() =>
        TransitionEndTime.Value = NetworkManager.ServerTime.Time + definition.formationChangeTime;

    void Update()
    {
        if (!IsServer) return;

        float dt = Time.deltaTime;
        ServerSpeed = 0f;
        bool runningNow = Running.Value && moving && stamina > 0f;
        stamina = Mathf.Clamp01(stamina + (runningNow ? -definition.staminaDrainPerSec : definition.staminaRegenPerSec) * dt);
        byte percent = (byte)Mathf.RoundToInt(stamina * 100f);
        if (percent != StaminaPercent.Value) StaminaPercent.Value = percent;

        if (moving) Move(runningNow, dt);
    }

    void Move(bool running, float dt)
    {
        // shortcut: düz çizgide hareket, engel yok; arazi/engel gelince NavMesh'e geçilecek.
        HalfExtents(out float maxRadius);
        float maxTurnDeg = Formation.MaxTurnRate(definition.runSpeed, maxRadius) * Mathf.Rad2Deg * dt;

        Vector3 toTarget = destination - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        if (distance < 0.3f)
        {
            // Vardı: istenen yöne dön, sonra dur.
            if (hasFinalFacing && Quaternion.Angle(transform.rotation, finalFacing) > 0.5f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, finalFacing, maxTurnDeg);
                return;
            }
            moving = false;
            return;
        }

        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toTarget), maxTurnDeg);

        // Hedefe dönük değilken yavaşlar, arkadaysa yerinde döner. Geçişte daha da yavaş.
        float facing = Mathf.Max(0f, Vector3.Dot(transform.forward, toTarget / distance));
        float speed = (running ? definition.runSpeed : definition.walkSpeed) * facing
                      * (IsTransitioning ? definition.transitionSpeedFactor : 1f);
        Vector3 step = transform.forward * Mathf.Min(speed * dt, distance);

        if (BlockedByEnemy(transform.position + step, step)) return; // temas: içine giremez, geri çekilebilir
        transform.position += step;
        ServerSpeed = step.magnitude / dt;
    }

    // Yeni konum bir düşmanla çakışıyor ve adım ona doğruysa hareket yok.
    bool BlockedByEnemy(Vector3 next, Vector3 step)
    {
        Obb self = GetObb(next);
        foreach (var other in All)
        {
            if (other == this || other.Team.Value == Team.Value) continue;
            Vector3 toOther = other.transform.position - next;
            if (Vector3.Dot(step, toOther) > 0f && CombatMath.Overlaps(self, other.GetObb(), 0f))
                return true;
        }
        return false;
    }

    static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
}
