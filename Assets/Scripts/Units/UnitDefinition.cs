using UnityEngine;

// Birim istatistikleri burada; bölük kodu asla birim tipine göre if yazmaz, buradan okur.
[CreateAssetMenu(menuName = "Ordu/Unit Definition")]
public class UnitDefinition : ScriptableObject
{
    public string unitId = "piyade";
    public string displayName = "Piyade";
    public string factionId = "Faction_Default";

    [Header("Bölük")]
    public int soldierCount = 100;
    public int defaultWidth = 20;
    public float slotSpacing = 1.5f;

    [Header("Hareket (m/sn)")]
    public float walkSpeed = 2.5f;
    public float runSpeed = 4.5f;

    [Header("Görsel")]
    public Mesh soldierMesh;
    public Material soldierMaterial; // GPU instancing açık olmalı
    public float soldierScale = 0.8f;
}
