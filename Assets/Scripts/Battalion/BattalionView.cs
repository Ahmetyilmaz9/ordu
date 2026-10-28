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

    // Yakın dövüş görseli (sadece istemci; kaybı sunucu bölük seviyesinde zaten çözdü).
    const float EngageDepth = 7f;     // düşman dikdörtgenine bu kadar yakın asker birebir dövüşe girer
    const float DuelDistance = 1.1f;  // iki asker arası dövüş mesafesi
    const float PressForward = 2f;    // arka saflar öne yüklenir
    const float FightSpeed = 2.5f;

    static readonly System.Collections.Generic.List<BattalionView> Views = new System.Collections.Generic.List<BattalionView>();

    Battalion battalion;
    Vector3[] positions;
    Vector2[] jitter;
    float[] speedFactor, phase;
    Matrix4x4[] matrices;
    MaterialPropertyBlock props;
    LineRenderer volley;
    float volleyHideTime;
    Vector3 lastCenter;
    readonly System.Collections.Generic.List<Vector3> nearFoes = new System.Collections.Generic.List<Vector3>();

    Color TeamColor => TeamColors[battalion.Team.Value % TeamColors.Length];

    void Awake()
    {
        battalion = GetComponent<Battalion>();
        enabled = !Application.isBatchMode; // headless sunucu çizmez
    }

    void OnEnable() => Views.Add(this);
    void OnDisable() => Views.Remove(this);

    void LateUpdate()
    {
        if (!battalion.IsSpawned) return;
        int count = battalion.SoldierCount.Value;
        if (count == 0) return;

        var def = battalion.Definition;
        if (positions == null || positions.Length != count) Allocate(count, def);

        float dt = Time.deltaTime, t = Time.time;
        bool marching = (transform.position - lastCenter).sqrMagnitude > 0.0004f;
        lastCenter = transform.position;
        var alive = battalion.Alive.Value;
        var obb = battalion.GetObb();
        bool routing = battalion.IsRouting;
        bool fighting = battalion.InCombat.Value && !routing && CollectNearFoes(obb, alive); // kaçan düello yapmaz
        float spread = routing ? 4f : 1f; // kaçarken dağılırlar

        Vector3 up = Vector3.up * def.soldierScale * 0.5f;
        Vector3 scale = Vector3.one * def.soldierScale;
        int banner = battalion.BannerSlot.Value;
        int drawn = 0;
        for (int i = 0; i < count; i++)
        {
            if (!alive.Get(i)) continue; // ölüm efekti ("poof") M8b'de

            // Kendi yeri: slot + sabit küçük sapma (kusursuz kutu olmasın).
            Vector3 home = battalion.SlotWorldPosition(i) + transform.rotation * new Vector3(jitter[i].x, 0f, jitter[i].y) * spread;
            Vector3 target = home;
            float speed = def.runSpeed * 1.5f * speedFactor[i];
            bool engaged = false;

            if (fighting)
            {
                Vector2 me = new Vector2(positions[i].x, positions[i].z);
                if (CombatMath.DistanceToObb(Foe(), me) < EngageDepth && NearestFoe(positions[i], out Vector3 foe))
                {
                    // Birebir: rakibine dövüş mesafesinde dur, ileri geri hamle yap.
                    Vector3 away = positions[i] - foe;
                    away.y = 0f;
                    away = away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward;
                    // Aynı rakibe yığılmasınlar: yana biraz dağıl.
                    Vector3 side = Vector3.Cross(away, Vector3.up) * ((i % 3) - 1) * 0.6f;
                    target = foe + away * (DuelDistance + Mathf.Sin(t * 6f + phase[i]) * 0.3f) + side;
                    speed = FightSpeed;
                    engaged = true;
                }
                else target = home + transform.forward * PressForward; // arka saf öne yüklenir
            }

            positions[i] = Vector3.MoveTowards(positions[i], target, speed * dt);
            float bob = marching || engaged || routing ? Mathf.Abs(Mathf.Sin(t * (engaged ? 10f : 8f) + phase[i])) * 0.12f : 0f;
            // Sancaktar büyük çizilir (bayrak modeli M8b'de).
            Vector3 s = i == banner ? scale * 1.8f : scale;
            matrices[drawn++] = Matrix4x4.TRS(positions[i] + up + Vector3.up * bob, Quaternion.identity, s);
        }
        if (volley != null && Time.time > volleyHideTime) volley.enabled = false;
        if (drawn == 0) return;

        props.SetColor(BaseColorId, Selected ? Color.Lerp(TeamColor, Color.white, 0.45f) : TeamColor);
        var rp = new RenderParams(def.soldierMaterial)
        {
            matProps = props,
            worldBounds = new Bounds(transform.position, Vector3.one * (Battalion.MapHalfSize * 2f)),
        };
        // shortcut: RenderMeshInstanced çağrı başına 1023 instance sınırlı; 200'lük bölükler için yeterli.
        Graphics.RenderMeshInstanced(rp, def.soldierMesh, 0, matrices, drawn);
    }

    void Allocate(int count, UnitDefinition def)
    {
        positions = new Vector3[count];
        matrices = new Matrix4x4[count];
        jitter = new Vector2[count];
        speedFactor = new float[count];
        phase = new float[count];
        for (int i = 0; i < count; i++)
        {
            jitter[i] = CombatMath.SoldierJitter(i, def.slotSpacing * 0.18f);
            speedFactor[i] = 0.9f + 0.2f * ((i * 37 % 101) / 100f);
            phase[i] = i * 1.7f;
            positions[i] = battalion.SlotWorldPosition(i);
        }
        props = new MaterialPropertyBlock();
    }

    // Temastaki düşman bölüklerin, bizim dikdörtgene yakın hayattaki askerleri (kare başına bir kez).
    Obb foeObb;
    Obb Foe() => foeObb;

    bool CollectNearFoes(in Obb mine, AliveMask myAlive)
    {
        nearFoes.Clear();
        bool any = false;
        foreach (var v in Views)
        {
            var other = v.battalion;
            if (v == this || v.positions == null || !other.IsSpawned || other.Team.Value == battalion.Team.Value) continue;
            var theirs = other.GetObb();
            if (!CombatMath.Overlaps(mine, theirs, 2f)) continue;
            if (!any) foeObb = theirs; // shortcut: birden çok düşmanla temasta ilk düşmanın kenarına göre seçer
            any = true;
            var theirAlive = other.Alive.Value;
            for (int j = 0; j < v.positions.Length; j++)
            {
                if (!theirAlive.Get(j)) continue;
                Vector3 p = v.positions[j];
                if (CombatMath.DistanceToObb(mine, new Vector2(p.x, p.z)) < EngageDepth) nearFoes.Add(p);
            }
        }
        return any && nearFoes.Count > 0;
    }

    bool NearestFoe(Vector3 from, out Vector3 foe)
    {
        foe = default;
        float best = float.MaxValue;
        foreach (var p in nearFoes)
        {
            float d = (p - from).sqrMagnitude;
            if (d < best) { best = d; foe = p; }
        }
        return best < float.MaxValue;
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
