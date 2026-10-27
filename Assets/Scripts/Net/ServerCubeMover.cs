using Unity.Netcode;
using UnityEngine;

// M0 testi: küp sadece sunucuda hareket eder, istemciler NetworkTransform ile görür.
public class ServerCubeMover : NetworkBehaviour
{
    [SerializeField] float radius = 3f;
    [SerializeField] float speed = 1f;

    void Update()
    {
        if (!IsServer) return; // simülasyon SADECE sunucuda

        float t = Time.time * speed;
        transform.position = new Vector3(Mathf.Cos(t) * radius, 0.5f, Mathf.Sin(t) * radius);
    }
}
