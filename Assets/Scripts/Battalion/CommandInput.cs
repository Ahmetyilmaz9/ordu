using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// İstemci tarafı: seçim replike edilmez, sunucuya sadece emir gider.
// Sol tık seç (Shift ile ekle), sağ tık hareket.
public class CommandInput : MonoBehaviour
{
    readonly List<Battalion> selected = new List<Battalion>();

    void Update()
    {
        var nm = NetworkManager.Singleton;
        var mouse = Mouse.current;
        var cam = Camera.main;
        if (nm == null || !nm.IsConnectedClient || mouse == null || cam == null) return;

        selected.RemoveAll(b => b == null || !b.IsSpawned);

        if (mouse.leftButton.wasPressedThisFrame && TryGroundPoint(cam, mouse, out Vector3 clickPoint))
        {
            bool additive = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            if (!additive) ClearSelection();

            foreach (var b in Battalion.All)
            {
                if (b.IsOwner && b.Contains(clickPoint) && !selected.Contains(b))
                {
                    selected.Add(b);
                    b.GetComponent<BattalionView>().Selected = true;
                    break;
                }
            }
        }

        if (mouse.rightButton.wasPressedThisFrame && selected.Count > 0 && TryGroundPoint(cam, mouse, out Vector3 target))
        {
            // Çoklu seçimde bölükler aralarındaki mesafeyi korur (formasyon emri M2'de).
            Vector3 center = Vector3.zero;
            foreach (var b in selected) center += b.transform.position;
            center /= selected.Count;

            foreach (var b in selected)
                b.MoveRpc(target + (b.transform.position - center));
        }
    }

    void ClearSelection()
    {
        foreach (var b in selected) b.GetComponent<BattalionView>().Selected = false;
        selected.Clear();
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
