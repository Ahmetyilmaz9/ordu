using System.Collections.Generic;
using UnityEngine;

// Total War tarzı bölük kartları: ekranın altında kendi bölüklerin. Tıkla seç (Shift ekle),
// çift tıkla kamerayı götür. Kart: tip, sayı, grup no, moral ve doldurma çubuğu, durum.
// shortcut: IMGUI; gerçek arayüz M8b'de.
[RequireComponent(typeof(CommandInput))]
public class UnitCards : MonoBehaviour
{
    const float CardWidth = 96f, CardHeight = 74f, Gap = 4f, Margin = 8f;

    static Rect panel; // ekran koordinatı (Input System: y aşağıdan)

    CommandInput input;
    readonly List<Battalion> own = new List<Battalion>();
    float lastClickTime;
    Battalion lastClicked;
    GUIStyle small;

    // Kartların üstündeki tıklama sahaya geçmesin.
    public static bool BlocksPointer(Vector2 screen) => panel.Contains(screen);

    void Awake()
    {
        input = GetComponent<CommandInput>();
        enabled = !Application.isBatchMode;
    }

    void OnGUI()
    {
        own.Clear();
        foreach (var b in Battalion.All) if (b.IsOwner) own.Add(b);
        if (own.Count == 0) { panel = Rect.zero; return; }
        own.Sort((a, b) => a.NetworkObjectId.CompareTo(b.NetworkObjectId));

        small ??= new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.UpperLeft, wordWrap = false };
        float total = own.Count * (CardWidth + Gap) - Gap;
        float x0 = (Screen.width - total) * 0.5f, y = Screen.height - CardHeight - Margin;
        panel = new Rect(x0, Margin, total, CardHeight);

        for (int i = 0; i < own.Count; i++)
        {
            var b = own[i];
            var r = new Rect(x0 + i * (CardWidth + Gap), y, CardWidth, CardHeight);
            bool isSelected = Contains(input.Selected, b);

            GUI.color = isSelected ? new Color(1f, 0.9f, 0.5f) : Color.white;
            if (GUI.Button(r, GUIContent.none)) OnCardClicked(b);
            GUI.color = Color.white;

            int group = input.GroupOf(b);
            string status = b.IsRouting ? "KAÇIYOR" : b.InCombat.Value ? "⚔ savaşta" : b.IsTransitioning ? "diziliyor" : Formation.DisplayName(b.Type.Value);
            GUI.Label(new Rect(r.x + 4, r.y + 2, r.width - 8, 16), (group >= 0 ? $"[{group}] " : "") + b.Definition.displayName, small);
            GUI.Label(new Rect(r.x + 4, r.y + 17, r.width - 8, 16), $"{b.AliveCount} / {b.SoldierCount.Value}", small);
            GUI.Label(new Rect(r.x + 4, r.y + 32, r.width - 8, 16), status, small);

            Bar(new Rect(r.x + 4, r.y + 52, r.width - 8, 5), b.MoralePercent.Value / 100f,
                b.IsRouting ? Color.red : b.MoralePercent.Value < Morale.ShakenBelow ? Color.yellow : Color.green);
            if (b.IsRanged)
            {
                double left = b.ReloadEndTime.Value - b.NetworkManager.ServerTime.Time;
                float ready = left <= 0 ? 1f : 1f - (float)(left / b.Definition.reloadTime);
                Bar(new Rect(r.x + 4, r.y + 60, r.width - 8, 5), ready, ready >= 1f ? Color.cyan : new Color(0.2f, 0.4f, 0.6f));
            }
        }
    }

    void OnCardClicked(Battalion b)
    {
        bool doubleClick = b == lastClicked && Time.unscaledTime - lastClickTime < 0.35f;
        lastClicked = b;
        lastClickTime = Time.unscaledTime;
        if (doubleClick && RtsCamera.Instance != null) RtsCamera.Instance.Focus(b.transform.position);
        input.Select(b, Event.current.shift);
    }

    static void Bar(Rect r, float fill, Color color)
    {
        GUI.color = Color.black;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = color;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fill), r.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    static bool Contains(IReadOnlyList<Battalion> list, Battalion b)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == b) return true;
        return false;
    }
}
