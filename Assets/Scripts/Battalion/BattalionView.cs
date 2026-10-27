using UnityEngine;

// İstemci görseli: askerler GameObject değil, pozisyon dizisi + bölük başına tek instanced çizim.
// M8'de sadece mesh/materyal değişir (VAT); dizi ve slot takibi aynı kalır.
[RequireComponent(typeof(Battalion))]
public class BattalionView : MonoBehaviour
{
    static readonly Color[] TeamColors = { new Color(0.2f, 0.45f, 1f), new Color(1f, 0.3f, 0.25f) };
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static GUIStyle labelStyle;

    public bool Selected { get; set; }

    Battalion battalion;
    Vector3[] positions;
    Matrix4x4[] matrices;
    MaterialPropertyBlock props;
    LineRenderer volley;
    float volleyHideTime;

    Color TeamColor => TeamColors[battalion.Team.Value % TeamColors.Length];

    void Awake()
    {
        battalion = GetComponent<Battalion>();
        enabled = !Application.isBatchMode; // headless sunucu çizmez
    }

    void LateUpdate()
    {
        if (!battalion.IsSpawned) return;
        int count = battalion.SoldierCount.Value;
        if (count == 0) return;

        var def = battalion.Definition;
        if (positions == null || positions.Length != count)
        {
            positions = new Vector3[count];
            matrices = new Matrix4x4[count];
            for (int i = 0; i < count; i++) positions[i] = battalion.SlotWorldPosition(i);
            props = new MaterialPropertyBlock();
        }

        // Asker slota ışınlanmaz, sınırlı hızla takip eder. Dönüş limiti uçtaki slotu koşu hızında tutar,
        // biraz fazlası yetişmeye yeter.
        float maxStep = def.runSpeed * 1.5f * Time.deltaTime;
        Vector3 up = Vector3.up * def.soldierScale * 0.5f;
        Vector3 scale = Vector3.one * def.soldierScale;
        var alive = battalion.Alive.Value;
        int banner = battalion.BannerSlot.Value;
        int drawn = 0;
        for (int i = 0; i < count; i++)
        {
            positions[i] = Vector3.MoveTowards(positions[i], battalion.SlotWorldPosition(i), maxStep);
            if (!alive.Get(i)) continue;
            // Sancaktar büyük çizilir (bayrak modeli M8'de).
            Vector3 s = i == banner ? scale * 1.8f : scale;
            matrices[drawn++] = Matrix4x4.TRS(positions[i] + up, Quaternion.identity, s);
        }
        if (volley != null && Time.time > volleyHideTime) volley.enabled = false;
        if (drawn == 0) return; // ölüm efekti ("poof") M8'de

        props.SetColor(BaseColorId, Selected ? Color.Lerp(TeamColor, Color.white, 0.45f) : TeamColor);
        var rp = new RenderParams(def.soldierMaterial)
        {
            matProps = props,
            worldBounds = new Bounds(transform.position, Vector3.one * (Battalion.MapHalfSize * 2f)),
        };
        // shortcut: RenderMeshInstanced çağrı başına 1023 instance sınırlı; 200'lük bölükler için yeterli.
        Graphics.RenderMeshInstanced(rp, def.soldierMesh, 0, matrices, drawn);
    }

    // Salvo efekti: tamamen kozmetik, sunucu kaybı zaten çözdü.
    // shortcut: çizgi; ok yağmuru ve bölük dumanı M8'de.
    public void ShowVolley(Vector3 target)
    {
        if (!enabled) return;
        if (volley == null)
        {
            volley = new GameObject("Volley").AddComponent<LineRenderer>();
            volley.widthMultiplier = 0.6f;
            volley.material = battalion.Definition.soldierMaterial;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, battalion.Definition.firearmMoraleMultiplier > 1f ? Color.white : Color.yellow);
            volley.SetPropertyBlock(block);
        }
        Vector3 lift = Vector3.up * 1.5f;
        volley.SetPositions(new[] { transform.position + lift, target + lift });
        volley.enabled = true;
        volleyHideTime = Time.time + 0.4f;
    }

    void OnDestroy()
    {
        if (volley != null) Destroy(volley.gameObject);
    }

    // Etiket: tip + sayı, formasyon, durum, moral çubuğu. Takım rengi; kaçarken yanıp söner.
    void OnGUI()
    {
        var cam = Camera.main;
        if (cam == null || !battalion.IsSpawned) return;

        Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * 4f);
        if (screen.z < 0f) return;

        labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        bool routing = battalion.IsRouting;
        if (routing && Mathf.Repeat(Time.time, 0.6f) < 0.3f) return;
        GUI.color = Selected ? Color.white : TeamColor;
        string formation = Formation.DisplayName(battalion.Type.Value) + (battalion.IsTransitioning ? " (diziliyor…)" : "");
        string state = Morale.DisplayName(battalion.Status.Value) + " · "
                       + (battalion.InCombat.Value ? "⚔ " : "") + (battalion.Running.Value ? "koşu " : "")
                       + $"yorgunluk %{100 - battalion.StaminaPercent.Value}";
        if (battalion.IsRanged) state += battalion.HoldFire.Value ? " · ATEŞİ TUT" : " · ateş serbest";
        string text = $"{(Selected ? "▶ " : "")}{battalion.Definition.displayName} {battalion.AliveCount}\n{formation}\n{state}";
        float top = Screen.height - screen.y - 30f;
        GUI.Label(new Rect(screen.x - 100f, top, 200f, 60f), text, labelStyle);

        // Moral çubuğu: yeşil > sarı > kırmızı
        float morale = battalion.MoralePercent.Value / 100f;
        var bar = new Rect(screen.x - 40f, top + 60f, 80f, 6f);
        GUI.color = Color.black;
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = morale >= Morale.ShakenBelow / 100f ? Color.green : routing ? Color.red : Color.yellow;
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * morale, bar.height), Texture2D.whiteTexture);

        // Doldurma çubuğu: rakibin atıcısında da görünür — "ateş etti, şimdi süvari gönder" buradan okunur.
        // shortcut: halka yerine çubuk; etiket UI'ı M8'de.
        if (battalion.IsRanged)
        {
            double left = battalion.ReloadEndTime.Value - battalion.NetworkManager.ServerTime.Time;
            float ready = left <= 0 ? 1f : 1f - (float)(left / battalion.Definition.reloadTime);
            var reload = new Rect(bar.x, bar.y + 9f, bar.width, bar.height);
            GUI.color = Color.black;
            GUI.DrawTexture(reload, Texture2D.whiteTexture);
            GUI.color = ready >= 1f ? Color.cyan : new Color(0.2f, 0.4f, 0.6f);
            GUI.DrawTexture(new Rect(reload.x, reload.y, reload.width * ready, reload.height), Texture2D.whiteTexture);
            if (ready < 1f) GUI.Label(new Rect(screen.x - 60f, reload.y + 4f, 120f, 20f), "DOLDURUYOR", labelStyle);
        }
        GUI.color = Color.white;
    }
}
