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
        for (int i = 0; i < count; i++)
        {
            positions[i] = Vector3.MoveTowards(positions[i], battalion.SlotWorldPosition(i), maxStep);
            matrices[i] = Matrix4x4.TRS(positions[i] + up, Quaternion.identity, scale);
        }

        props.SetColor(BaseColorId, Selected ? Color.Lerp(TeamColor, Color.white, 0.45f) : TeamColor);
        var rp = new RenderParams(def.soldierMaterial)
        {
            matProps = props,
            worldBounds = new Bounds(transform.position, Vector3.one * (Battalion.MapHalfSize * 2f)),
        };
        // shortcut: RenderMeshInstanced çağrı başına 1023 instance sınırlı; 200'lük bölükler için yeterli.
        Graphics.RenderMeshInstanced(rp, def.soldierMesh, 0, matrices, count);
    }

    // M1 etiketi: tip adı + asker sayısı, takım rengi. İkon M4 ile birlikte.
    void OnGUI()
    {
        var cam = Camera.main;
        if (cam == null || !battalion.IsSpawned) return;

        Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * 4f);
        if (screen.z < 0f) return;

        labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        GUI.color = Selected ? Color.white : TeamColor;
        string text = $"{(Selected ? "▶ " : "")}{battalion.Definition.displayName} {battalion.SoldierCount.Value}";
        GUI.Label(new Rect(screen.x - 80f, Screen.height - screen.y - 12f, 160f, 24f), text, labelStyle);
        GUI.color = Color.white;
    }
}
