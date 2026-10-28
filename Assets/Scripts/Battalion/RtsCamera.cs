using UnityEngine;
using UnityEngine.InputSystem;

// Total War tarzı kamera: WASD/ok tuşları ya da ekran kenarı ile kaydır, Q/E ya da orta tuşla döndür,
// tekerlekle yakınlaş. Yerdeki bir odak noktasının etrafında döner.
public class RtsCamera : MonoBehaviour
{
    public static RtsCamera Instance { get; private set; }

    [SerializeField] float panSpeed = 60f;      // m/sn (90 m mesafede; uzaklaştıkça hızlanır)
    [SerializeField] float rotateSpeed = 90f;   // derece/sn
    [SerializeField] float minDistance = 15f;
    [SerializeField] float maxDistance = 220f;
    [SerializeField] float edgePixels = 8f;

    Vector3 pivot = new Vector3(0f, 0f, -55f);
    float yaw, pitch = 50f, distance = 90f;
    bool teamApplied;

    void Awake() => Instance = this;

    public void Focus(Vector3 worldPoint) => pivot = new Vector3(worldPoint.x, 0f, worldPoint.z);

    void LateUpdate()
    {
        ApplyTeamStartOnce();

        var kb = Keyboard.current;
        var mouse = Mouse.current;
        float dt = Time.unscaledDeltaTime;

        Vector2 move = Vector2.zero;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
            if (kb.qKey.isPressed) yaw += rotateSpeed * dt;
            if (kb.eKey.isPressed) yaw -= rotateSpeed * dt;
        }

        if (mouse != null)
        {
            Vector2 p = mouse.position.ReadValue();
            bool inside = Application.isFocused && p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height;
            if (inside && !mouse.middleButton.isPressed)
            {
                if (p.x < edgePixels) move.x -= 1f;
                if (p.x > Screen.width - edgePixels) move.x += 1f;
                if (p.y < edgePixels) move.y -= 1f;
                if (p.y > Screen.height - edgePixels) move.y += 1f;
            }

            if (mouse.middleButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw += delta.x * 0.25f;
                pitch = Mathf.Clamp(pitch - delta.y * 0.25f, 20f, 85f);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f && inside) distance = Mathf.Clamp(distance * Mathf.Pow(0.88f, scroll / 120f), minDistance, maxDistance);
        }

        if (move != Vector2.zero)
        {
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(move.x, 0f, move.y).normalized;
            pivot += dir * panSpeed * (distance / 90f) * dt;
            float edge = Battalion.MapHalfSize;
            pivot.x = Mathf.Clamp(pivot.x, -edge, edge);
            pivot.z = Mathf.Clamp(pivot.z, -edge, edge);
        }

        var rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(pivot + rotation * new Vector3(0f, 0f, -distance), rotation);
    }

    // Kırmızı takım haritanın kuzeyinde: kamerayı kendi ordusunun arkasından başlat.
    void ApplyTeamStartOnce()
    {
        var match = MatchManager.Current;
        if (teamApplied || match == null || !match.IsSpawned || match.LocalTeam < 0) return;
        teamApplied = true;
        if (match.LocalTeam == 1)
        {
            yaw = 180f;
            pivot = new Vector3(0f, 0f, 55f);
        }
    }
}
