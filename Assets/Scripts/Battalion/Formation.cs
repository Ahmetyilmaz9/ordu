using UnityEngine;

public enum FormationType : byte { Line, Column, Square, Wedge }

// Slot matematiği: hiçbir asker pozisyonu ağdan gelmez, hepsi buradan çıkar.
// Unity'de +Z ileri, +X sağa. Rank 0 = ön saf, file 0 = sol kanat.
public static class Formation
{
    public const int ColumnWidth = 6;

    public static string DisplayName(FormationType type) => type switch
    {
        FormationType.Line => "Hat",
        FormationType.Column => "Kolon",
        FormationType.Square => "Kare",
        _ => "Kama",
    };

    public static int RankCount(int count, int width) => (count + width - 1) / width;

    // Hat genişliği oyuncudan gelir; diğer tiplerin genişliği şekilden çıkar.
    public static int EffectiveWidth(FormationType type, int lineWidth, int count) => type switch
    {
        FormationType.Column => Mathf.Min(ColumnWidth, count),
        // shortcut: dolu kare; içi boş, dört yöne bakan kare M3'te kanat/arka çarpanıyla birlikte.
        FormationType.Square => Mathf.CeilToInt(Mathf.Sqrt(count)),
        _ => Mathf.Clamp(lineWidth, 1, count),
    };

    public static Vector3 SlotLocalOffset(FormationType type, int slot, int lineWidth, int count, float spacing) =>
        type == FormationType.Wedge
            ? WedgeOffset(slot, count, spacing)
            : GridOffset(slot, EffectiveWidth(type, lineWidth, count), count, spacing);

    public static Vector3 GridOffset(int slot, int width, int count, float spacing)
    {
        int rank = slot / width;
        int file = slot % width;
        float z = -(rank - (RankCount(count, width) - 1) * 0.5f) * spacing;
        float x = (file - (width - 1) * 0.5f) * spacing;
        return new Vector3(x, 0f, z);
    }

    // Kama: rank r'de 2r+1 asker, uç önde. Slot r²..(r+1)²-1 arası rank r'dedir.
    static Vector3 WedgeOffset(int slot, int count, float spacing)
    {
        int rank = (int)Mathf.Sqrt(slot);
        int file = slot - rank * rank;
        int ranks = Mathf.CeilToInt(Mathf.Sqrt(count));
        float x = (file - rank) * spacing;
        float z = -(rank - (ranks - 1) * 0.5f) * spacing;
        return new Vector3(x, 0f, z);
    }

    // Merkeze göre yarım boyutlar (x = yarım genişlik, y = yarım derinlik) ve en uzak slot mesafesi.
    // 200 slotluk döngü; bölük başına kare başına bir kez ucuz.
    public static Vector2 HalfExtents(FormationType type, int lineWidth, int count, float spacing, out float maxRadius)
    {
        Vector2 half = Vector2.zero;
        maxRadius = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 o = SlotLocalOffset(type, i, lineWidth, count, spacing);
            half = Vector2.Max(half, new Vector2(Mathf.Abs(o.x), Mathf.Abs(o.z)));
            maxRadius = Mathf.Max(maxRadius, new Vector2(o.x, o.z).magnitude);
        }
        return half;
    }

    // Uçtaki asker koşu hızını asla aşmasın: ω = v / r_max (rad/sn).
    public static float MaxTurnRate(float runSpeed, float maxRadius) =>
        maxRadius > 0.01f ? runSpeed / maxRadius : 10f;
}
