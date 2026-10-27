using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// İstemci tarafı: seçim replike edilmez, sunucuya sadece emir gider.
// Sol tık seç (Shift ile ekle). Sağ tık: tıkla = git, sürükle = hat çiz (konum + yön + genişlik).
// 1-4: Hat / Kolon / Kare / Kama. R: koş/yürü (koşarak temas = şarj). F: ateşi tut/serbest.
public class CommandInput : MonoBehaviour
{
    const float MinDragLength = 3f;

    static readonly (Key key, FormationType type)[] FormationKeys =
    {
        (Key.Digit1, FormationType.Line), (Key.Digit2, FormationType.Column),
        (Key.Digit3, FormationType.Square), (Key.Digit4, FormationType.Wedge),
    };

    readonly List<Battalion> selected = new List<Battalion>();
    Vector3 dragStart;
    bool dragging;
    LineRenderer preview;

    void Update()
    {
        var nm = NetworkManager.Singleton;
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;
        var cam = Camera.main;
        if (nm == null || !nm.IsConnectedClient || mouse == null || cam == null) return;

        selected.RemoveAll(b => b == null || !b.IsSpawned);

        if (mouse.leftButton.wasPressedThisFrame && TryGroundPoint(cam, mouse, out Vector3 clickPoint))
            Select(clickPoint, keyboard != null && keyboard.shiftKey.isPressed);

        if (keyboard != null)
            foreach (var (key, type) in FormationKeys)
                if (keyboard[key].wasPressedThisFrame)
                    foreach (var b in selected) b.SetFormationRpc(type);

        if (keyboard != null && keyboard.rKey.wasPressedThisFrame && selected.Count > 0)
        {
            bool run = !selected[0].Running.Value; // ilk seçilene göre aç/kapa, hepsi aynı olsun
            foreach (var b in selected) b.SetRunRpc(run);
        }

        if (keyboard != null && keyboard.fKey.wasPressedThisFrame && selected.Count > 0)
        {
            bool hold = !selected[0].HoldFire.Value;
            foreach (var b in selected) if (b.IsRanged) b.SetHoldFireRpc(hold);
        }

        HandleMoveOrder(cam, mouse);
    }

    void Select(Vector3 point, bool additive)
    {
        if (!additive)
        {
            foreach (var b in selected) b.GetComponent<BattalionView>().Selected = false;
            selected.Clear();
        }

        foreach (var b in Battalion.All)
        {
            if (b.IsOwner && b.Contains(point) && !selected.Contains(b))
            {
                selected.Add(b);
                b.GetComponent<BattalionView>().Selected = true;
                break;
            }
        }
    }

    void HandleMoveOrder(Camera cam, Mouse mouse)
    {
        if (selected.Count == 0) { dragging = false; ShowPreview(false); return; }

        if (mouse.rightButton.wasPressedThisFrame && TryGroundPoint(cam, mouse, out dragStart))
            dragging = true;
        if (!dragging) return;

        TryGroundPoint(cam, mouse, out Vector3 dragEnd);
        bool isDrag = (dragEnd - dragStart).magnitude >= MinDragLength;

        if (mouse.rightButton.isPressed)
        {
            ShowPreview(isDrag);
            if (isDrag) DrawPreview(dragStart, dragEnd);
            return;
        }

        // Bırakıldı.
        dragging = false;
        ShowPreview(false);
        if (isDrag) IssueLineOrder(dragStart, dragEnd);
        else IssuePointOrder(dragStart);
    }

    // Tıklama: bölükler aralarındaki mesafeyi koruyarak gider, varışta yön değişmez.
    void IssuePointOrder(Vector3 target)
    {
        Vector3 center = Vector3.zero;
        foreach (var b in selected) center += b.transform.position;
        center /= selected.Count;

        foreach (var b in selected)
            b.MoveRpc(target + (b.transform.position - center), Vector3.zero, 0);
    }

    // Sürükleme: çizgi seçili bölüklere soldan sağa eşit bölünür; her biri kendi parçasının
    // ortasına gider, çizgiye dik yöne bakar, hat genişliği parça uzunluğundan çıkar.
    void IssueLineOrder(Vector3 start, Vector3 end)
    {
        Vector3 along = end - start;
        Vector3 facing = Vector3.Cross(along, Vector3.up).normalized; // soldan sağa çizilen hat ileri bakar
        Vector3 right = along / selected.Count;

        // Soldaki bölük solda kalsın: bölükleri çizgi yönündeki konumlarına göre sırala.
        var ordered = new List<Battalion>(selected);
        ordered.Sort((a, b) => Vector3.Dot(a.transform.position, along).CompareTo(Vector3.Dot(b.transform.position, along)));

        for (int i = 0; i < ordered.Count; i++)
        {
            var b = ordered[i];
            Vector3 center = start + right * (i + 0.5f);
            int width = Mathf.RoundToInt(right.magnitude / b.Definition.slotSpacing) + 1;
            b.MoveRpc(center, facing, width);
        }
    }

    void DrawPreview(Vector3 start, Vector3 end)
    {
        Vector3 lift = Vector3.up * 0.2f;
        Vector3 facing = Vector3.Cross(end - start, Vector3.up).normalized;
        Vector3 mid = (start + end) * 0.5f;
        // Hat çizgisi + ortadan çıkan yön oku
        preview.positionCount = 5;
        preview.SetPositions(new[] { start + lift, mid + lift, mid + facing * 4f + lift, mid + lift, end + lift });
    }

    void ShowPreview(bool show)
    {
        if (show && preview == null)
        {
            preview = new GameObject("OrderPreview").AddComponent<LineRenderer>();
            preview.widthMultiplier = 0.4f;
            preview.material = selected[0].Definition.soldierMaterial; // build'de dahil olduğu kesin
        }
        if (preview != null) preview.enabled = show;
    }

    static bool TryGroundPoint(Camera cam, Mouse mouse, out Vector3 point)
    {
        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }
        point = default;
        return false;
    }
}
