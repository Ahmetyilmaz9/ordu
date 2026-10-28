using UnityEngine;

public enum MoraleState : byte { Steady, Shaken, Routing }

// Moral kuralları (0-100 ölçek). Birim farkı UnitDefinition.discipline ile; kurallar herkese aynı.
// shortcut: sabitler kodda; bot-vs-bot denge testinde (M4 sonrası) ScriptableObject'e taşınır.
public static class Morale
{
    public const float ShakenBelow = 40f;
    public const float RoutBelow = 10f;
    public const float RallyAbove = 45f;

    public const float CasualtyWeight = 150f;     // bölüğün tamamını kaybetmek = 150 moral
    public const float FlankPerSec = 4f;
    public const float RearPerSec = 8f;
    public const float FriendRoutShock = 15f;
    public const float BannerShock = 25f;
    public const float FatiguePerSec = 2f;        // yorgunken (stamina < 0.25) savaşta
    public const float RecoveryPerSec = 3f;       // savaş dışında, kaçmıyorken
    public const float RallyPerSec = 4f;          // kaçarken, yakında düşman yoksa
    public const float RallySupportPerSec = 4f;   // + yakında sağlam dost varsa
    public const float CeilingDropPerRout = 25f; // her kaçışta: 75 → 50 → 25; üçüncü kaçıştan dönülmez

    // Hızlı kayıp şoku (Total War): son saniyelerde çok adam kaybeden bölük her yeni kayıpta daha çok sarsılır.
    public const float RecentLossWeight = 4f;      // son kayıpların bölüğe oranı × bu = ek çarpan
    public const float RecentLossHalfLife = 5f;    // sn; "son kayıplar" bu sürede yarıya iner

    public const float EnemyNearRadius = 25f;
    public const float FriendRadius = 30f;

    // Bir askerin kaybı (disiplin, moral vuruşunu alan tarafta uygulanır).
    public static float CasualtyLoss(int slotCount) => CasualtyWeight / Mathf.Max(1, slotCount);

    // Örn. son 5 sn'de bölüğün %10'u öldüyse her yeni kayıp ×1,4 moral götürür.
    public static float LossShock(float recentLosses, int slotCount) =>
        1f + RecentLossWeight * recentLosses / Mathf.Max(1, slotCount);

    public static float DecayRecentLosses(float recentLosses, float dt) =>
        recentLosses * Mathf.Pow(0.5f, dt / RecentLossHalfLife);

    public static float SidePressurePerSec(HitSide side, FormationType defenderType)
    {
        if (defenderType == FormationType.Square || side == HitSide.Front) return 0f;
        return side == HitSide.Rear ? RearPerSec : FlankPerSec;
    }

    // Kaçış noktası: kendi tarafının kenarı (takım 0 güney, 1 kuzey), aynı x hizasında.
    public static Vector3 FleePoint(byte team, Vector3 from, float mapHalfSize) =>
        new Vector3(from.x, 0f, team == 0 ? -mapHalfSize : mapHalfSize);

    // Kaçan bölük sadece toparlanma eşiğini geçince döner; diğerleri morale göre.
    public static MoraleState Evaluate(MoraleState current, float morale)
    {
        if (current == MoraleState.Routing)
            return morale >= RallyAbove ? MoraleState.Shaken : MoraleState.Routing;
        if (morale < RoutBelow) return MoraleState.Routing;
        return morale < ShakenBelow ? MoraleState.Shaken : MoraleState.Steady;
    }

    // Savaş gücü çarpanı: sarsılan zayıf, kaçan hiç dövüşmez.
    public static float AttackFactor(MoraleState state) => state switch
    {
        MoraleState.Steady => 1f,
        MoraleState.Shaken => 0.75f,
        _ => 0f,
    };

    public static string DisplayName(MoraleState state) => state switch
    {
        MoraleState.Steady => "Sağlam",
        MoraleState.Shaken => "Sarsıldı",
        _ => "KAÇIYOR",
    };
}
