using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Ağdaki tek birim: bölük. Askerler ağda yok, istemcide BattalionView hesaplar.
// Simülasyon SADECE sunucuda; istemciler transform'u NetworkTransform ile alır.
[RequireComponent(typeof(NetworkObject))]
public class Battalion : NetworkBehaviour
{
    public const float MapHalfSize = 100f;

    public static readonly List<Battalion> All = new List<Battalion>();

    [SerializeField] UnitDefinition definition;
    public UnitDefinition Definition => definition;

    public readonly NetworkVariable<byte> Team = new NetworkVariable<byte>();
    public readonly NetworkVariable<int> FormationWidth = new NetworkVariable<int>();
    public readonly NetworkVariable<int> SoldierCount = new NetworkVariable<int>();

    Vector3 destination;
    bool moving;

    public override void OnNetworkSpawn()
    {
        All.Add(this);
        if (!IsServer) return;

        FormationWidth.Value = definition.defaultWidth;
        SoldierCount.Value = definition.soldierCount;
    }

    public override void OnNetworkDespawn() => All.Remove(this);

    public Vector2 HalfExtents => Formation.HalfExtents(FormationWidth.Value, SoldierCount.Value, definition.slotSpacing);

    public Vector3 SlotWorldPosition(int slot) =>
        transform.TransformPoint(Formation.SlotLocalOffset(slot, FormationWidth.Value, SoldierCount.Value, definition.slotSpacing));

    // Dünya noktası bölüğün dikdörtgeni içinde mi (seçim için, bir slot payıyla).
    public bool Contains(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        Vector2 half = HalfExtents + Vector2.one * definition.slotSpacing;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.y;
    }

    [Rpc(SendTo.Server)]
    public void MoveRpc(Vector3 target, RpcParams rpcParams = default)
    {
        // Güven sınırı: sadece sahibi emir verebilir, hedef harita içinde olmalı.
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (!float.IsFinite(target.x) || !float.IsFinite(target.z)) return;

        destination = new Vector3(
            Mathf.Clamp(target.x, -MapHalfSize, MapHalfSize), 0f,
            Mathf.Clamp(target.z, -MapHalfSize, MapHalfSize));
        moving = true;
    }

    void Update()
    {
        if (!IsServer || !moving) return;

        // shortcut: düz çizgide hareket, engel yok; arazi/engel gelince NavMesh'e geçilecek.
        Vector3 toTarget = destination - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        if (distance < 0.3f)
        {
            moving = false;
            return;
        }

        float maxTurnDeg = Formation.MaxTurnRate(definition.runSpeed, FormationWidth.Value, SoldierCount.Value,
            definition.slotSpacing) * Mathf.Rad2Deg * Time.deltaTime;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toTarget), maxTurnDeg);

        // Hedefe dönük değilken yavaşlar, arkadaysa yerinde döner.
        float facing = Mathf.Max(0f, Vector3.Dot(transform.forward, toTarget / distance));
        float step = Mathf.Min(definition.walkSpeed * facing * Time.deltaTime, distance);
        transform.position += transform.forward * step;
    }
}
