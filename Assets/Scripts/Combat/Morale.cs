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

    public const float EnemyNearRadius = 25f;
    public const float FriendRadius = 30f;

    // Bir askerin kaybı (disiplin, moral vuruşunu alan tarafta uygulanır).
    public static float CasualtyLoss(int slotCount) => CasualtyWeight / Mathf.Max(1, slotCount);

    public static float SidePressurePerSec(HitSide side, FormationType defenderType)
    {
        if (defenderType == FormationType.Square || side == HitSide.Front) return 0f;
        return side == HitSide.Rear ? RearPerSec : FlankPerSec;
    }

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
