using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// Rolü kendisi seçer ve başlatır. Host modu yok: ya sunucu ya istemci.
public class NetworkBootstrap : MonoBehaviour
{
    [SerializeField] string serverAddress = "127.0.0.1";
    [SerializeField] ushort port = 7777;

    void Start()
    {
        var nm = NetworkManager.Singleton;
        var transport = (UnityTransport)nm.NetworkConfig.NetworkTransport;

        if (IsServerRole())
        {
            transport.SetConnectionData(serverAddress, port, "0.0.0.0");
            nm.StartServer();
        }
        else
        {
            transport.SetConnectionData(serverAddress, port);
            nm.StartClient();
        }
    }

    // Sunucu: dedicated build, "-server" argümanı ya da MPPM ana editörü (Player1).
    // Diğer her şey istemci. "-client" ile zorlanabilir.
    static bool IsServerRole()
    {
#if UNITY_SERVER
        return true;
#else
        var args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "-server") >= 0) return true;
        if (Array.IndexOf(args, "-client") >= 0) return false;
#if UNITY_EDITOR
        // MPPM sanal oyuncuları "-name Player2/3/4" ile açılır; ana editörde yok ya da Player1.
        int i = Array.IndexOf(args, "-name");
        return i < 0 || i + 1 >= args.Length || args[i + 1] == "Player1";
#else
        return false;
#endif
#endif
    }

    void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        string status;
        if (nm.IsServer) status = $"SUNUCU | bağlı istemci: {nm.ConnectedClientsIds.Count}";
        else if (nm.IsConnectedClient) status = $"İSTEMCİ | bağlı (id {nm.LocalClientId})";
        else status = "İSTEMCİ | bağlanıyor...";

        GUI.Label(new Rect(10, 10, 400, 24), status);
    }
}
