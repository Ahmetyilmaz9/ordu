using UnityEngine;

// Birim istatistikleri burada; bölük kodu asla birim tipine göre if yazmaz, buradan okur.
[CreateAssetMenu(menuName = "Ordu/Unit Definition")]
public class UnitDefinition : ScriptableObject
{
    public string unitId = "piyade";
    public string displayName = "Piyade";
    public string factionId = "Faction_Default";

    [Header("Ordu kurma")]
    public int maxSlots = 3; // tip başına slot limiti (puan bütçesi yerine)

    [Header("Bölük")]
    public int soldierCount = 100;
    public int defaultWidth = 20;
    public float slotSpacing = 1.5f;

    [Header("Hareket (m/sn)")]
    public float walkSpeed = 2.5f;
    public float runSpeed = 4.5f;

    [Header("Formasyon")]
    public float formationChangeTime = 4.5f;   // geçiş süresi (sn)
    public float transitionSpeedFactor = 0.4f; // geçiş sırasında hız çarpanı

    // Total War modeli: isabet = %35 + (saldırı − savunma)/100, %8–%90 arası. Vuruş hasarı: temel hasarın
    // zırhın ortalama %75'i kadarı engellenir, zırh delici kısım her zaman geçer. Asker canı bitince ölür.
    [Header("Yakın dövüş (0–100 ölçeği)")]
    public float meleeAttack = 30f;
    public float meleeDefense = 30f;
    public float weaponDamage = 25f;        // vuruş başına temel hasar (zırh azaltır)
    public float armorPiercing = 5f;        // vuruş başına zırhı delen hasar
    public float armor = 20f;
    public float hitPoints = 100f;          // asker başına can
    public bool mounted;                    // atlı mı (kargının bonusu buna)
    public float bonusVsMounted;            // atlıya karşı saldırı ve hasar bonusu (kargı)

    [Header("Şarj ve moral")]
    public float chargeImpact = 0.05f;   // temas anında: cephedeki asker × hız (m/sn) × bu = öldürme
    [Range(0f, 1f)]
    public float antiChargeFront = 0f;   // önden şarjın bu oranı saldırana geri döner (kargı)
    public float discipline = 1f;        // moral kayıpları buna bölünür (yeniçeri > azap)

    [Header("Görüş")]
    public float sightRange = 60f; // m; savaş sisinde bu bölüğün gördüğü mesafe

    [Header("Menzilli (range 0 = sadece yakın dövüş)")]
    public float range = 0f;                 // m
    public float reloadTime = 0f;            // sn; okçu kısa (sürekli), tüfek uzun (doldurma penceresi)
    public float missileAccuracy = 0f;       // atıcı başına isabet olasılığı (dibinden)
    public float missileDamage = 0f;         // isabet başına temel hasar (zırh azaltır: okçu zırha karşı zayıf)
    public float missileArmorPiercing = 0f;  // isabet başına zırh delen hasar (tüfek yüksek)
    public int shootingRanks = 0;            // ateş eden saf sayısı; 0 = hepsi (okçu tepeden atar)
    public float firearmMoraleMultiplier = 1f; // barut kuralı: tüfek kaybı 2 kat moral kırar

    [Header("Yorgunluk (0-1 / sn)")]
    public float staminaDrainPerSec = 1f / 40f; // tam yorgunluğa 40 sn koşu
    public float staminaRegenPerSec = 1f / 60f;

    [Header("Görsel")]
    public Mesh soldierMesh;
    public Material soldierMaterial; // GPU instancing açık olmalı
    public float soldierScale = 0.8f;
    public Vector3 soldierShape = Vector3.one; // siluet: uzaktan tipi okutan oran (kargı uzun, süvari uzun gövde)
}
