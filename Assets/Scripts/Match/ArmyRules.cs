using UnityEngine;

// Ordu kurma ve yerleştirme kuralları: saf, menüden test edilir.
public static class ArmyRules
{
    public const int Slots = 10;
    public const float DeployFront = 45f;   // yerleştirme bölgesi haritanın ortasından en az bu kadar uzakta başlar
    public const float RowGap = 18f; // 3 sıra: 53, 71, 89 m — harita (100) içinde kalır
    public const float BattalionGap = 4f;

    // Güven sınırı: istemciden gelen ordu. Tip sayısı tutmalı, toplam 1..Slots, tip başına limit aşılmamalı.
    public static bool IsValid(byte[] counts, int[] maxSlots)
    {
        if (counts == null || counts.Length != maxSlots.Length) return false;
        int total = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] > maxSlots[i]) return false;
            total += counts[i];
        }
        return total > 0 && total <= Slots;
    }

    // Süre biterse ya da bot için: limitlere uyarak tipleri sırayla birer birer ekle.
    public static byte[] Default(int[] maxSlots)
    {
        var counts = new byte[maxSlots.Length];
        int total = 0;
        bool added = true;
        while (total < Slots && added)
        {
            added = false;
            for (int i = 0; i < counts.Length && total < Slots; i++)
            {
                if (counts[i] >= maxSlots[i]) continue;
                counts[i]++;
                total++;
                added = true;
            }
        }
        return counts;
    }

    // Takımın yerleştirme bölgesine kırp. Takım 0 güney (−z), takım 1 kuzey (+z).
    public static Vector3 ClampToZone(byte team, Vector3 p)
    {
        float edge = Battalion.MapHalfSize - 5f;
        float x = Mathf.Clamp(p.x, -edge, edge);
        float z = Mathf.Clamp(p.z, -edge, edge);
        z = team == 0 ? Mathf.Min(z, -DeployFront) : Mathf.Max(z, DeployFront);
        return new Vector3(x, 0f, z);
    }

    // Varsayılan diziliş: genişliklere göre soldan sağa, sıra dolunca bir sıra geriye. (x, sıra no)
    public static Vector2[] Layout(float[] widths, float maxRowWidth)
    {
        var result = new Vector2[widths.Length];
        int row = 0, rowStart = 0;
        float cursor = 0f;
        for (int i = 0; i <= widths.Length; i++)
        {
            bool full = i < widths.Length && cursor > 0f && cursor + widths[i] > maxRowWidth;
            if (i == widths.Length || full)
            {
                // Bu sırayı ortala
                float shift = -(cursor - BattalionGap) * 0.5f;
                for (int j = rowStart; j < i; j++) result[j].x += shift;
                if (i == widths.Length) break;
                row++;
                rowStart = i;
                cursor = 0f;
            }
            result[i] = new Vector2(cursor + widths[i] * 0.5f, row);
            cursor += widths[i] + BattalionGap;
        }
        return result;
    }
}
