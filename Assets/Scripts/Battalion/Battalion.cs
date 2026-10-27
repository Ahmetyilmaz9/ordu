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
    public readonly NetworkVariable<int> SoldierCount = new NetworkVariable<int>();
    public readonly NetworkVariable<double> TransitionEndTime = new NetworkVariable<double>(); // sunucu zamanı

    Vector3 destination;
    Quaternion finalFacing;
    bool moving, hasFinalFacing;

    public override void OnNetworkSpawn()
    {
        All.Add(this);
        if (!IsServer) return;

        Type.Value = FormationType.Line;
        FormationWidth.Value = definition.defaultWidth;
        SoldierCount.Value = definition.soldierCount;
    }

    public override void OnNetworkDespawn() => All.Remove(this);

    // Geçiş sırasında bölük yavaş ve savunmasız (savunma cezası M3'te).
    public bool IsTransitioning => NetworkManager != null && NetworkManager.ServerTime.Time < TransitionEndTime.Value;

    public Vector2 HalfExtents(out float maxRadius) =>
        Formation.HalfExtents(Type.Value, FormationWidth.Value, SoldierCount.Value, definition.slotSpacing, out maxRadius);

    public Vector3 SlotWorldPosition(int slot) =>
        transform.TransformPoint(Formation.SlotLocalOffset(Type.Value, slot, FormationWidth.Value, SoldierCount.Value,
            definition.slotSpacing));

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

    void StartTransition() =>
        TransitionEndTime.Value = NetworkManager.ServerTime.Time + definition.formationChangeTime;

    void Update()
    {
        if (!IsServer || !moving) return;

        // shortcut: düz çizgide hareket, engel yok; arazi/engel gelince NavMesh'e geçilecek.
        HalfExtents(out float maxRadius);
        float maxTurnDeg = Formation.MaxTurnRate(definition.runSpeed, maxRadius) * Mathf.Rad2Deg * Time.deltaTime;

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
        float speed = definition.walkSpeed * facing * (IsTransitioning ? definition.transitionSpeedFactor : 1f);
        transform.position += transform.forward * Mathf.Min(speed * Time.deltaTime, distance);
    }

    static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
}
