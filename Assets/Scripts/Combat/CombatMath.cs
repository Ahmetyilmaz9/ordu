using UnityEngine;

public enum HitSide : byte { Front, Rear, Left, Right }

// Bölüğün yerdeki dikdörtgeni (XZ düzlemi). Right/Forward birim vektör, Half = yarım genişlik/derinlik.
public struct Obb
{
    public Vector2 Center, Right, Forward, Half;

    public Obb(Vector3 center, Vector3 right, Vector3 forward, Vector2 half)
    {
        Center = new Vector2(center.x, center.z);
        Right = new Vector2(right.x, right.z).normalized;
        Forward = new Vector2(forward.x, forward.z).normalized;
        Half = half;
    }

    // Bu dikdörtgenin bir eksen üzerindeki izdüşüm yarıçapı.
    public float Radius(Vector2 axis) =>
        Half.x * Mathf.Abs(Vector2.Dot(Right, axis)) + Half.y * Mathf.Abs(Vector2.Dot(Forward, axis));
}

// Savaşın saf matematiği: Unity/ağ bağımlılığı yok, menüden test edilir.
public static class CombatMath
{
    // Ayırma ekseni testi (SAT): iki dikdörtgen arası boşluk margin'den küçükse temas var.
    public static bool Overlaps(in Obb a, in Obb b, float margin)
    {
        Vector2 d = b.Center - a.Center;
        return !Separated(a, b, d, a.Right, margin) && !Separated(a, b, d, a.Forward, margin)
            && !Separated(a, b, d, b.Right, margin) && !Separated(a, b, d, b.Forward, margin);
    }

    static bool Separated(in Obb a, in Obb b, Vector2 d, Vector2 axis, float margin) =>
        Mathf.Abs(Vector2.Dot(d, axis)) > a.Radius(axis) + b.Radius(axis) + margin;

    // Noktanın dikdörtgene uzaklığı (içindeyse 0). İstemci yakın dövüşte kimin dövüşeceğini seçerken kullanır.
    public static float DistanceToObb(in Obb o, Vector2 point)
    {
        Vector2 d = point - o.Center;
        float x = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, o.Right)) - o.Half.x);
        float z = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, o.Forward)) - o.Half.y);
        return Mathf.Sqrt(x * x + z * z);
    }

    // Askere sabit, küçük bir dizilim sapması (her istemcide aynı): kutu gibi kusursuz sıralar olmasın.
    public static Vector2 SoldierJitter(int slot, float amount)
    {
        uint h = (uint)slot * 2654435761u;
        float a = (h & 0xFFFF) / 65535f, b = (h >> 16) / 65535f;
        return new Vector2(a - 0.5f, b - 0.5f) * (2f * amount);
    }

    // Saldıran, savunanın hangi kenarına geliyor? Yerel yön yarım boyutlara göre normalize edilir,
    // böylece geniş bir hattın ucuna yakın gelen saldırı da doğru kenara düşer.
    public static HitSide SideOf(in Obb defender, Vector2 attackerCenter)
    {
        Vector2 d = attackerCenter - defender.Center;
        float x = Vector2.Dot(d, defender.Right) / Mathf.Max(defender.Half.x, 0.01f);
        float z = Vector2.Dot(d, defender.Forward) / Mathf.Max(defender.Half.y, 0.01f);
        if (Mathf.Abs(z) >= Mathf.Abs(x)) return z >= 0f ? HitSide.Front : HitSide.Rear;
        return x >= 0f ? HitSide.Right : HitSide.Left;
    }

    public static Vector2 EdgeAxis(in Obb o, HitSide side) =>
        side == HitSide.Front || side == HitSide.Rear ? o.Right : o.Forward;

    // Saldıranın, savunanın vurulan kenarı boyunca kapladığı uzunluk (temas cephesi) ve
    // bu temasın kenar üzerindeki orta noktası (savunanın yerel koordinatında).
    public static float ContactLength(in Obb defender, in Obb attacker, HitSide side, out float contactCenter)
    {
        Vector2 axis = EdgeAxis(defender, side);
        float edgeHalf = side == HitSide.Front || side == HitSide.Rear ? defender.Half.x : defender.Half.y;
        float c = Vector2.Dot(attacker.Center - defender.Center, axis);
        float r = attacker.Radius(axis);
        float lo = Mathf.Max(-edgeHalf, c - r), hi = Mathf.Min(edgeHalf, c + r);
        contactCenter = Mathf.Clamp(c, -edgeHalf, edgeHalf);
        return Mathf.Max(0f, hi - lo);
    }

    public static float SideMultiplier(HitSide side, FormationType defenderType)
    {
        if (defenderType == FormationType.Square) return 1f; // kare: her yön cephe
        return side switch { HitSide.Front => 1f, HitSide.Rear => 2.2f, _ => 1.6f };
    }

    // Ateş eden asker sayısı: ön saflar (tüfek) ya da hepsi (okçu, tepeden atar).
    public static int Shooters(int alive, int width, int shootingRanks) =>
        shootingRanks <= 0 ? alive : Mathf.Min(alive, width * shootingRanks);

    // Salvo öldürmesi: menzil sonunda isabet %40'a düşer.
    // shortcut: zırh yok; okçunun "zırha karşı zayıf" olması zırh istatistiği gelince.
    public static float VolleyKills(int shooters, float killsPerShot, float distance, float range) =>
        shooters * killsPerShot * Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(distance / Mathf.Max(range, 0.01f)));

    // Yöne göre ölüm: vurulan kenara en yakın safta, temas noktasına en yakın hayattaki asker ölür.
    // Saf önceliği (×100) yanal mesafeden her zaman baskın. -1 = ölecek kimse yok.
    public static int PickCasualty(FormationType type, int lineWidth, int count, float spacing,
        in AliveMask alive, HitSide side, float contactCenter)
    {
        Vector2 n = side switch
        {
            HitSide.Front => new Vector2(0f, 1f),
            HitSide.Rear => new Vector2(0f, -1f),
            HitSide.Left => new Vector2(-1f, 0f),
            _ => new Vector2(1f, 0f),
        };
        bool alongX = side == HitSide.Front || side == HitSide.Rear;

        int best = -1;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            if (!alive.Get(i)) continue;
            Vector3 o = Formation.SlotLocalOffset(type, i, lineWidth, count, spacing);
            float depth = o.x * n.x + o.z * n.y;
            float lateral = Mathf.Abs((alongX ? o.x : o.z) - contactCenter);
            float score = depth * 100f - lateral;
            if (score > bestScore) { bestScore = score; best = i; }
        }
        return best;
    }
}
