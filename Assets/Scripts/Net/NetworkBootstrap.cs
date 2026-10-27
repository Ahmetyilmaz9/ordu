using System;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// Rolü kendisi seçer ve başlatır. Host modu yok: ya sunucu ya istemci.
// İstemci: editörde ve "-ip" verilmişse kendiliğinden bağlanır, yoksa IP kutusu gösterir.
// shortcut: IP ile bağlanma; eşleştirme (Edgegap vb.) genel testte, o da sadece IP:port verecek.
public class NetworkBootstrap : MonoBehaviour
{
    public const ushort DefaultPort = 7777;
    const string LastAddressPref = "Ordu.LastServer";

    NetworkManager nm;
    string addressInput;
    string message;
    bool connecting;

    void Start()
    {
        nm = NetworkManager.Singleton;
        var transport = (UnityTransport)nm.NetworkConfig.NetworkTransport;

        if (IsServerRole())
        {
            // Headless sunucu kare sınırı olmadan çekirdeği %100 yer; simülasyon için 60 yeter.
            if (Application.isBatchMode) Application.targetFrameRate = 60;
            transport.SetConnectionData("127.0.0.1", DefaultPort, "0.0.0.0"); // "-port" argümanı bunu ezer
            nm.StartServer();
            return;
        }

        nm.OnClientDisconnectCallback += OnDisconnected;
        string argIp = ArgValue("-ip");
        addressInput = argIp ?? PlayerPrefs.GetString(LastAddressPref, "127.0.0.1");
#if UNITY_EDITOR
        Connect(); // MPPM sanal oyuncuları hep yerel sunucuya
#else
        if (argIp != null) Connect();
#endif
    }

    void OnDestroy()
    {
        if (nm != null) nm.OnClientDisconnectCallback -= OnDisconnected;
    }

    void Connect()
    {
        if (!TryParseEndpoint(addressInput, DefaultPort, out string ip, out ushort port))
        {
            message = "Geçersiz adres. Örnek: 192.168.1.20 ya da 85.10.1.2:7777";
            return;
        }
        PlayerPrefs.SetString(LastAddressPref, addressInput);
        ((UnityTransport)nm.NetworkConfig.NetworkTransport).SetConnectionData(true, ip, port);
        message = null;
        connecting = nm.StartClient();
        if (!connecting) message = "İstemci başlatılamadı.";
    }

    // Bağlantı kurulamadı ya da koptu: kapat, kutuyu yeniden göster.
    void OnDisconnected(ulong clientId)
    {
        if (nm.IsServer) return;
        message = string.IsNullOrEmpty(nm.DisconnectReason)
            ? "Bağlantı kesildi ya da sunucuya ulaşılamadı."
            : nm.DisconnectReason;
        nm.Shutdown();
        connecting = false;
    }

    // "1.2.3.4" ya da "1.2.3.4:7777". UnityTransport alan adı çözmez, sadece IPv4.
    public static bool TryParseEndpoint(string input, ushort defaultPort, out string ip, out ushort port)
    {
        ip = null;
        port = defaultPort;
        if (string.IsNullOrWhiteSpace(input)) return false;

        string[] parts = input.Trim().Split(':');
        if (parts.Length > 2) return false;
        if (parts.Length == 2 && (!ushort.TryParse(parts[1], out port) || port == 0)) return false;
        if (!IPAddress.TryParse(parts[0], out var address) || address.AddressFamily != AddressFamily.InterNetwork
            || parts[0].Split('.').Length != 4) return false;

        ip = parts[0];
        return true;
    }

    static string ArgValue(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // Sunucu: dedicated build, "-server" argümanı ya da MPPM ana editörü (Player1).
    // Diğer her şey istemci. "-client" ile zorlanabilir.
    static bool IsServerRole()
    {
        // Editörde UNITY_SERVER'a bakma: aktif platform "Dedicated Server" olunca MPPM sanal oyuncuları
        // da bu tanımla derlenir ve hepsi sunucu olup 7777 için çakışır.
#if UNITY_SERVER && !UNITY_EDITOR
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
        if (nm == null) return;

        if (nm.IsServer)
        {
            GUI.Label(new Rect(10, 10, 400, 24), $"SUNUCU | bağlı istemci: {nm.ConnectedClientsIds.Count}");
            return;
        }
        if (nm.IsConnectedClient)
        {
            GUI.Label(new Rect(10, 10, 400, 24), $"İSTEMCİ | bağlı (id {nm.LocalClientId})");
            return;
        }
        if (connecting)
        {
            GUI.Label(new Rect(10, 10, 400, 24), $"Bağlanıyor: {addressInput}…");
            return;
        }

        // Bağlan kutusu
        var box = new Rect(Screen.width * 0.5f - 160f, Screen.height * 0.5f - 60f, 320f, 120f);
        GUILayout.BeginArea(box, GUI.skin.box);
        GUILayout.Label("Sunucu adresi (IP ya da IP:port)");
        addressInput = GUILayout.TextField(addressInput ?? "");
        if (GUILayout.Button("Bağlan")) Connect();
        if (message != null) GUILayout.Label(message);
        GUILayout.EndArea();
    }
}
