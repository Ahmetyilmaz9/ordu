using Unity.Netcode;

// Bölüğün "hayatta" bit maskesi: slot i hayattaysa bit i = 1. 256 slot (doküman maks 200).
// shortcut: değişince 32 byte'ın tamamı gider (indeks deltası değil); sis/geç bağlanma bedava çözülür.
// Bant genişliği sorun olursa ölüm indeksi RPC'sine geçilir.
public struct AliveMask : INetworkSerializeByMemcpy, System.IEquatable<AliveMask>
{
    public const int Capacity = 256;

    public ulong W0, W1, W2, W3;

    public static AliveMask Full(int count)
    {
        var m = new AliveMask();
        for (int i = 0; i < count; i++) m.Set(i, true);
        return m;
    }

    public bool Get(int i) => ((GetWord(i >> 6) >> (i & 63)) & 1UL) != 0;

    public void Set(int i, bool alive)
    {
        ulong bit = 1UL << (i & 63);
        ulong w = GetWord(i >> 6);
        SetWord(i >> 6, alive ? w | bit : w & ~bit);
    }

    public int Count => PopCount(W0) + PopCount(W1) + PopCount(W2) + PopCount(W3);

    ulong GetWord(int w) => w switch { 0 => W0, 1 => W1, 2 => W2, _ => W3 };

    void SetWord(int w, ulong value)
    {
        switch (w)
        {
            case 0: W0 = value; break;
            case 1: W1 = value; break;
            case 2: W2 = value; break;
            default: W3 = value; break;
        }
    }

    static int PopCount(ulong x)
    {
        int n = 0;
        while (x != 0) { x &= x - 1; n++; }
        return n;
    }

    public bool Equals(AliveMask o) => W0 == o.W0 && W1 == o.W1 && W2 == o.W2 && W3 == o.W3;
}
