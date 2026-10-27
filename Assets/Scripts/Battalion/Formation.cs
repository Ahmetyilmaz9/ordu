using UnityEngine;

// Slot matematiği: hiçbir asker pozisyonu ağdan gelmez, hepsi buradan çıkar.
// Unity'de +Z ileri, +X sağa. Rank 0 = ön saf, file 0 = sol kanat.
public static class Formation
{
    public static int RankCount(int count, int width) => (count + width - 1) / width;

    public static Vector3 SlotLocalOffset(int slot, int width, int count, float spacing)
    {
        int rank = slot / width;
        int file = slot % width;
        float z = -(rank - (RankCount(count, width) - 1) * 0.5f) * spacing;
        float x = (file - (width - 1) * 0.5f) * spacing;
        return new Vector3(x, 0f, z);
    }

    // Merkezden en uzak slota kadar yarım boyutlar (x = yarım genişlik, y = yarım derinlik).
    public static Vector2 HalfExtents(int width, int count, float spacing) =>
        new Vector2((width - 1) * 0.5f * spacing, (RankCount(count, width) - 1) * 0.5f * spacing);

    // Uçtaki asker koşu hızını asla aşmasın: ω = v / r_max (rad/sn).
    public static float MaxTurnRate(float runSpeed, int width, int count, float spacing)
    {
        float r = HalfExtents(width, count, spacing).magnitude;
        return r > 0.01f ? runSpeed / r : 10f;
    }
}
