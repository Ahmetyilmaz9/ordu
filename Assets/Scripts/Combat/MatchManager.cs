using Unity.Netcode;
using UnityEngine;

// Maç saati ve zafer. İki takım da sahaya çıkınca saat başlar.
// Zafer: rakibin sahada bölüğü kalmayınca (imha olan ya da kenardan kaçan gitmiş sayılır;
// kaçan ama haritadaki sayılır). Süre dolunca kaçmayan bölüklerdeki asker sayısı çok olan kazanır.
public class MatchManager : NetworkBehaviour
{
    public const sbyte NoWinner = -1, Draw = 2;
    static readonly string[] TeamNames = { "Mavi", "Kırmızı" };

    [SerializeField] float matchDurationSeconds = 15f * 60f;

    public readonly NetworkVariable<double> EndTime = new NetworkVariable<double>(); // 0 = başlamadı
    public readonly NetworkVariable<sbyte> Winner = new NetworkVariable<sbyte>(NoWinner);

    static GUIStyle style;

    void Update()
    {
        if (!IsServer || Winner.Value != NoWinner) return;

        var (present0, present1) = (false, false);
        int soldiers0 = 0, soldiers1 = 0;
        foreach (var b in Battalion.All)
        {
            int standing = b.IsRouting ? 0 : b.AliveCount;
            if (b.Team.Value == 0) { present0 = true; soldiers0 += standing; }
            else { present1 = true; soldiers1 += standing; }
        }

        double now = NetworkManager.ServerTime.Time;
        if (EndTime.Value == 0)
        {
            if (present0 && present1) EndTime.Value = now + matchDurationSeconds;
            return;
        }

        sbyte result = MatchRules.Decide(present0, present1, soldiers0, soldiers1, now >= EndTime.Value);
        if (result != NoWinner) Winner.Value = result;
    }

    void OnGUI()
    {
        if (!IsSpawned) return;
        style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 18, fontStyle = FontStyle.Bold };

        string text;
        if (EndTime.Value == 0) text = "Rakip bekleniyor…";
        else
        {
            double left = System.Math.Max(0, EndTime.Value - NetworkManager.ServerTime.Time);
            text = $"{(int)left / 60:00}:{(int)left % 60:00}";
        }
        if (Winner.Value == Draw) text += "\nBERABERE";
        else if (Winner.Value != NoWinner) text += $"\n{TeamNames[Winner.Value]} KAZANDI";

        GUI.Label(new Rect(Screen.width * 0.5f - 150f, 10f, 300f, 60f), text, style);
    }
}

// Zafer kararı: saf, menüden test edilir.
public static class MatchRules
{
    public static sbyte Decide(bool present0, bool present1, int soldiers0, int soldiers1, bool timeUp)
    {
        if (!present0 && !present1) return MatchManager.Draw;
        if (!present1) return 0;
        if (!present0) return 1;
        if (!timeUp) return MatchManager.NoWinner;
        return soldiers0 == soldiers1 ? MatchManager.Draw : soldiers0 > soldiers1 ? (sbyte)0 : (sbyte)1;
    }
}
