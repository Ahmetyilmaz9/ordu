using UnityEngine;

// İstemci: maç fazı arayüzü. Ordu kurma paneli, yerleştirme bölgesi + hazır düğmesi, saat ve sonuç.
// shortcut: IMGUI; gerçek arayüz M8'de.
[RequireComponent(typeof(MatchManager))]
public class MatchUI : MonoBehaviour
{
    MatchManager match;
    byte[] counts;
    LineRenderer zone;
    GUIStyle center;

    void Awake()
    {
        match = GetComponent<MatchManager>();
        enabled = !Application.isBatchMode;
    }

    void Update()
    {
        // Yerleştirmede kendi bölgeni yerde çiz.
        bool show = match.IsSpawned && match.Phase.Value == MatchPhase.Deploy && match.LocalTeam >= 0;
        if (show && zone == null) zone = CreateZone((byte)match.LocalTeam);
        if (zone != null) zone.enabled = show;
    }

    LineRenderer CreateZone(byte team)
    {
        var lr = new GameObject("DeployZone").AddComponent<LineRenderer>();
        lr.widthMultiplier = 0.8f;
        lr.loop = true;
        lr.material = match.Roster.units[0].soldierMaterial;
        float e = Battalion.MapHalfSize - 5f, near = ArmyRules.DeployFront, s = team == 0 ? -1f : 1f;
        lr.positionCount = 4;
        lr.SetPositions(new[]
        {
            new Vector3(-e, 0.2f, s * near), new Vector3(e, 0.2f, s * near),
            new Vector3(e, 0.2f, s * e), new Vector3(-e, 0.2f, s * e),
        });
        return lr;
    }

    void OnGUI()
    {
        if (!match.IsSpawned) return;
        center ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 18, fontStyle = FontStyle.Bold };

        var phase = match.Phase.Value;
        double left = System.Math.Max(0, match.PhaseEndTime.Value - match.NetworkManager.ServerTime.Time);
        string clock = $"{(int)left / 60:00}:{(int)left % 60:00}";
        int team = match.LocalTeam;
        bool ready = team >= 0 && (match.ReadyMask.Value & (1 << team)) != 0;

        string title = phase switch
        {
            MatchPhase.Waiting => "Rakip bekleniyor…",
            MatchPhase.ArmySelect => $"Ordu kur — {clock}",
            MatchPhase.Deploy => $"Yerleştir — {clock}  (sağ tık / sürükle, sadece kendi bölgen)",
            MatchPhase.Battle => clock,
            _ => match.Winner.Value == MatchManager.Draw ? "BERABERE" : $"{MatchManager.TeamNames[match.Winner.Value]} KAZANDI",
        };
        if (team >= 0 && phase < MatchPhase.Battle) title += $"\nSen: {MatchManager.TeamNames[team]}";
        GUI.Label(new Rect(Screen.width * 0.5f - 300f, 10f, 600f, 60f), title, center);

        if (team < 0 || ready && phase <= MatchPhase.Deploy)
        {
            if (ready) GUI.Label(new Rect(Screen.width * 0.5f - 150f, 70f, 300f, 30f), "Hazırsın — rakip bekleniyor");
            return;
        }

        if (phase == MatchPhase.ArmySelect) ArmyPanel();
        else if (phase == MatchPhase.Deploy &&
                 GUI.Button(new Rect(Screen.width * 0.5f - 60f, 70f, 120f, 30f), "Hazır"))
            match.DeployReadyRpc();
    }

    void ArmyPanel()
    {
        var units = match.Roster.units;
        int[] max = match.MaxSlots();
        counts ??= ArmyRules.Default(max);

        int total = 0;
        foreach (byte c in counts) total += c;

        GUILayout.BeginArea(new Rect(20f, 80f, 320f, 60f + units.Length * 30f), GUI.skin.box);
        GUILayout.Label($"Slot: {total}/{ArmyRules.Slots}");
        for (int i = 0; i < units.Length; i++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{units[i].displayName} ({units[i].soldierCount})", GUILayout.Width(150f));
            if (GUILayout.Button("−", GUILayout.Width(30f)) && counts[i] > 0) counts[i]--;
            GUILayout.Label($"{counts[i]} / {max[i]}", GUILayout.Width(50f));
            if (GUILayout.Button("+", GUILayout.Width(30f)) && counts[i] < max[i] && total < ArmyRules.Slots) counts[i]++;
            GUILayout.EndHorizontal();
        }
        GUI.enabled = ArmyRules.IsValid(counts, max);
        if (GUILayout.Button("Hazır")) match.SubmitArmyRpc(counts);
        GUI.enabled = true;
        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        if (zone != null) Destroy(zone.gameObject);
    }
}
