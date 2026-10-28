using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// İstemci tarafı Total War tarzı kontroller. Seçim replike edilmez, sunucuya sadece emir gider.
// Sol tık: seç (Shift ekle, çift tık aynı tipten hepsi) · sol tık sürükle: kutuyla seç · Ctrl+A: hepsi
// Sağ tık: zemine = git, düşmana = saldır (takip eder) · sağ tık sürükle: hat çiz (hayalet önizlemeli)
// Ctrl+0-9 grup ata, 0-9 grubu seç (iki kez = kamerayı götür) · Z/X/C/V: Hat/Kolon/Kare/Kama
// R: koş/yürü · F: ateşi tut/serbest · Backspace: dur
public class CommandInput : MonoBehaviour
{
    const float MinDragMeters = 3f;
    const float BoxDragPixels = 8f;
    const float DoubleClickSeconds = 0.35f;

    static readonly (Key key, FormationType type)[] FormationKeys =
    {
        (Key.Z, FormationType.Line), (Key.X, FormationType.Column),
        (Key.C, FormationType.Square), (Key.V, FormationType.Wedge),
    };
    static readonly Key[] DigitKeys =
    {
        Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
        Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
    };
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    readonly List<Battalion> selected = new List<Battalion>();
    readonly List<Battalion>[] groups = new List<Battalion>[10];

    Vector2 leftDownScreen;
    bool leftDown;
    float lastClickTime, lastGroupTime;
    Battalion lastClicked;
    int lastGroup = -1;

    Vector3 rightDragStart;
    bool rightDragging;
    MaterialPropertyBlock ghostProps;
    readonly List<Matrix4x4> ghostMatrices = new List<Matrix4x4>();

    public IReadOnlyList<Battalion> Selected => selected;

    void Update()
    {
        var nm = NetworkManager.Singleton;
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        var cam = Camera.main;
        if (nm == null || !nm.IsConnectedClient || mouse == null || kb == null || cam == null) return;

        selected.RemoveAll(b => b == null || !b.IsSpawned);
        Vector2 mousePos = mouse.position.ReadValue();
        bool overUi = UnitCards.BlocksPointer(mousePos);
        bool ctrl = kb.ctrlKey.isPressed, shift = kb.shiftKey.isPressed;

        // Sol tık: tıkla ya da kutu
        if (mouse.leftButton.wasPressedThisFrame && !overUi)
        {
            leftDown = true;
            leftDownScreen = mousePos;
        }
        if (leftDown && mouse.leftButton.wasReleasedThisFrame)
        {
            leftDown = false;
            if ((mousePos - leftDownScreen).magnitude >= BoxDragPixels) BoxSelect(cam, leftDownScreen, mousePos, shift);
            else ClickSelect(cam, mouse, shift);
        }

        if (ctrl && kb.aKey.wasPressedThisFrame)
        {
            ClearSelection();
            foreach (var b in Battalion.All) if (b.IsOwner) AddToSelection(b);
        }

        // Kontrol grupları
        for (int g = 0; g < DigitKeys.Length; g++)
        {
            if (!kb[DigitKeys[g]].wasPressedThisFrame) continue;
            if (ctrl) groups[g] = new List<Battalion>(selected);
            else SelectGroup(g);
        }

        if (selected.Count > 0)
        {
            foreach (var (key, type) in FormationKeys)
                if (kb[key].wasPressedThisFrame)
                    foreach (var b in selected) b.SetFormationRpc(type);

            if (kb.rKey.wasPressedThisFrame)
            {
                bool run = !selected[0].Running.Value; // ilk seçilene göre aç/kapa, hepsi aynı olsun
                foreach (var b in selected) b.SetRunRpc(run);
            }
            if (kb.fKey.wasPressedThisFrame)
            {
                bool hold = !selected[0].HoldFire.Value;
                foreach (var b in selected) if (b.IsRanged) b.SetHoldFireRpc(hold);
            }
            if (kb.backspaceKey.wasPressedThisFrame)
                foreach (var b in selected) b.HaltRpc();
        }

        HandleRightButton(cam, mouse, overUi);
    }

    // ---- Seçim ----

    public void Select(Battalion b, bool additive)
    {
        if (!additive) ClearSelection();
        if (selected.Contains(b))
        {
            if (additive) RemoveFromSelection(b); // Shift ile tekrar tıklamak çıkarır
            return;
        }
        AddToSelection(b);
    }

    public int GroupOf(Battalion b)
    {
        for (int g = 1; g <= 10; g++)
            if (groups[g % 10] != null && groups[g % 10].Contains(b)) return g % 10;
        return -1;
    }

    void ClickSelect(Camera cam, Mouse mouse, bool shift)
    {
        Battalion hit = null;
        if (TryGroundPoint(cam, mouse.position.ReadValue(), out Vector3 point))
            foreach (var b in Battalion.All)
                if (b.IsOwner && b.Contains(point)) { hit = b; break; }

        if (hit == null)
        {
            if (!shift) ClearSelection();
            return;
        }

        // Çift tık: ekrandaki aynı tipten bütün bölükler
        bool doubleClick = hit == lastClicked && Time.unscaledTime - lastClickTime < DoubleClickSeconds;
        lastClicked = hit;
        lastClickTime = Time.unscaledTime;
        if (doubleClick)
        {
            if (!shift) ClearSelection();
            foreach (var b in Battalion.All)
                if (b.IsOwner && b.UnitIndex.Value == hit.UnitIndex.Value && OnScreen(cam, b.transform.position))
                    AddToSelection(b);
            return;
        }
        Select(hit, shift);
    }

    void BoxSelect(Camera cam, Vector2 a, Vector2 b, bool additive)
    {
        if (!additive) ClearSelection();
        var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        foreach (var bat in Battalion.All)
        {
            if (!bat.IsOwner) continue;
            Vector3 s = cam.WorldToScreenPoint(bat.transform.position);
            if (s.z > 0f && rect.Contains(s)) AddToSelection(bat);
        }
    }

    void SelectGroup(int g)
    {
        var group = groups[g];
        if (group == null) return;
        group.RemoveAll(b => b == null || !b.IsSpawned);
        if (group.Count == 0) return;

        // Aynı grup tuşuna iki kez: kamerayı gruba götür
        if (g == lastGroup && Time.unscaledTime - lastGroupTime < DoubleClickSeconds && RtsCamera.Instance != null)
            RtsCamera.Instance.Focus(Center(group));
        lastGroup = g;
        lastGroupTime = Time.unscaledTime;

        ClearSelection();
        foreach (var b in group) AddToSelection(b);
    }

    void AddToSelection(Battalion b)
    {
        if (selected.Contains(b)) return;
        selected.Add(b);
        b.GetComponent<BattalionView>().Selected = true;
    }

    void RemoveFromSelection(Battalion b)
    {
        selected.Remove(b);
        b.GetComponent<BattalionView>().Selected = false;
    }

    void ClearSelection()
    {
        foreach (var b in selected) if (b != null) b.GetComponent<BattalionView>().Selected = false;
        selected.Clear();
    }

    // ---- Emirler ----

    void HandleRightButton(Camera cam, Mouse mouse, bool overUi)
    {
        if (selected.Count == 0) { rightDragging = false; return; }

        Vector2 mousePos = mouse.position.ReadValue();
        if (mouse.rightButton.wasPressedThisFrame && !overUi && TryGroundPoint(cam, mousePos, out rightDragStart))
            rightDragging = true;
        if (!rightDragging) return;

        TryGroundPoint(cam, mousePos, out Vector3 dragEnd);
        bool isDrag = (dragEnd - rightDragStart).magnitude >= MinDragMeters;

        if (mouse.rightButton.isPressed)
        {
            if (isDrag) DrawGhosts(LineOrders(rightDragStart, dragEnd));
            return;
        }

        // Bırakıldı
        rightDragging = false;
        if (isDrag)
        {
            foreach (var o in LineOrders(rightDragStart, dragEnd)) o.battalion.MoveRpc(o.center, o.facing, o.width);
            return;
        }

        var enemy = EnemyAt(rightDragStart);
        if (enemy != null)
        {
            foreach (var b in selected) b.AttackRpc(enemy.NetworkObject);
            return;
        }
        IssuePointOrder(rightDragStart);
    }

    // Görünen (yani istemcide var olan) bir düşman bölüğün üstüne mi tıklandı?
    static Battalion EnemyAt(Vector3 point)
    {
        foreach (var b in Battalion.All)
            if (!b.IsOwner && b.Contains(point)) return b;
        return null;
    }

    // Tıklama: bölükler aralarındaki mesafeyi koruyarak gider, varışta yön değişmez.
    void IssuePointOrder(Vector3 target)
    {
        Vector3 center = Center(selected);
        foreach (var b in selected)
            b.MoveRpc(target + (b.transform.position - center), Vector3.zero, 0);
    }

    // Sürükleme: çizgi seçili bölüklere soldan sağa eşit bölünür; her biri kendi parçasının
    // ortasına gider, çizgiye dik yöne bakar, hat genişliği parça uzunluğundan çıkar.
    List<(Battalion battalion, Vector3 center, Vector3 facing, int width)> LineOrders(Vector3 start, Vector3 end)
    {
        Vector3 along = end - start;
        Vector3 facing = Vector3.Cross(along, Vector3.up).normalized; // soldan sağa çizilen hat ileri bakar
        Vector3 step = along / selected.Count;

        // Soldaki bölük solda kalsın: bölükleri çizgi yönündeki konumlarına göre sırala.
        var ordered = new List<Battalion>(selected);
        ordered.Sort((a, b) => Vector3.Dot(a.transform.position, along).CompareTo(Vector3.Dot(b.transform.position, along)));

        var orders = new List<(Battalion, Vector3, Vector3, int)>();
        for (int i = 0; i < ordered.Count; i++)
        {
            var b = ordered[i];
            int width = Mathf.RoundToInt(step.magnitude / b.Definition.slotSpacing) + 1;
            orders.Add((b, start + step * (i + 0.5f), facing, width));
        }
        return orders;
    }

    // Hayalet: askerlerin varınca duracağı yerler (hat genişliği dahil), soluk küreler.
    void DrawGhosts(List<(Battalion battalion, Vector3 center, Vector3 facing, int width)> orders)
    {
        ghostProps ??= new MaterialPropertyBlock();
        ghostProps.SetColor(BaseColorId, new Color(0.85f, 0.85f, 0.85f));
        foreach (var (b, center, facing, width) in orders)
        {
            var def = b.Definition;
            int count = b.SoldierCount.Value;
            int lineWidth = b.Type.Value == FormationType.Line ? Mathf.Clamp(width, Battalion.MinLineWidth, count) : b.FormationWidth.Value;
            var rot = Quaternion.LookRotation(facing);
            var alive = b.Alive.Value;

            ghostMatrices.Clear();
            for (int i = 0; i < count; i++)
            {
                if (!alive.Get(i)) continue;
                Vector3 p = center + rot * Formation.SlotLocalOffset(b.Type.Value, i, lineWidth, count, def.slotSpacing);
                ghostMatrices.Add(Matrix4x4.TRS(p + Vector3.up * 0.2f, Quaternion.identity, Vector3.one * 0.45f));
            }
            var rp = new RenderParams(def.soldierMaterial)
            {
                matProps = ghostProps,
                worldBounds = new Bounds(center, Vector3.one * (Battalion.MapHalfSize * 2f)),
            };
            Graphics.RenderMeshInstanced(rp, def.soldierMesh, 0, ghostMatrices);
        }
    }

    // ---- Yardımcılar ----

    static Vector3 Center(List<Battalion> list)
    {
        Vector3 c = Vector3.zero;
        foreach (var b in list) c += b.transform.position;
        return c / Mathf.Max(1, list.Count);
    }

    static bool OnScreen(Camera cam, Vector3 world)
    {
        Vector3 s = cam.WorldToScreenPoint(world);
        return s.z > 0f && s.x >= 0f && s.y >= 0f && s.x <= Screen.width && s.y <= Screen.height;
    }

    static bool TryGroundPoint(Camera cam, Vector2 screen, out Vector3 point)
    {
        Ray ray = cam.ScreenPointToRay(screen);
        if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }
        point = default;
        return false;
    }

    // Kutu seçimi çerçevesi
    void OnGUI()
    {
        var mouse = Mouse.current;
        if (!leftDown || mouse == null) return;
        Vector2 a = leftDownScreen, b = mouse.position.ReadValue();
        if ((b - a).magnitude < BoxDragPixels) return;
        // IMGUI y ekseni ters
        var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y), Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));
        GUI.color = new Color(1f, 1f, 1f, 0.15f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Box(r, GUIContent.none);
    }
}
