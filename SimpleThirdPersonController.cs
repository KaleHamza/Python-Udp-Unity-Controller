using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine.SceneManagement;
// Bu script, hem UDP sunucusu olarak çalışır hem de CharacterController bileşenini kullanarak karakteri hareket ettirir.
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class SimpleThirdPersonController : MonoBehaviour
{
    // -------------------------------------------------------------
    // 🚀 AĞ SUNUCUSU (UDP) AYARLARI
    // -------------------------------------------------------------
    [Header("Network Server Settings")]
    public int listenPort = 9050;   // Unity Dinleme Portu (Python'daki UNITY_PORT ile aynı olmalı)
    public int clientAckPort = 9051; // Python/Client Yanıt Portu (Python'daki ACK_PORT ile aynı olmalı)

    private UdpClient _udpClient;
    private Thread _receiveThread;
    private string _receivedCommand = "";
    private readonly object _commandLock = new object();
    private IPEndPoint _clientEndPoint;
    private bool _isServerRunning = true;

    // Ağ Girdileri: Python'dan gelen sürekli hareket verisini tutar.
    private Vector2 _networkMoveInput = Vector2.zero; // x:yatay (A/D), y:dikey (W/S) hareket
    private bool _networkJumpInput = false;         // Zıplama komutu (tek seferlik)
    // -------------------------------------------------------------

    [Header("Ayarlar")]
    public float moveSpeed = 5f;
    public float turnSpeed = 10f;
    public float gravity = 20f;
    public float jumpVelocity = 7f;

    private CharacterController _controller;
    private Animator _animator;
    private Transform _camTransform;
    private float _verticalVelocity = 0f; // Yer çekimi için kendi hız değişkenimiz

    void Start()
    {
        // Gerekli bileşenleri al
        _controller = GetComponent<CharacterController>();
        _animator = GetComponent<Animator>();

        // Kamerayı bul (karakteri kamera yönüne çevirmek için)
        if (Camera.main != null)
        {
            _camTransform = Camera.main.transform;
        }
        else
        {
            Debug.LogError("Sahnede 'Main Camera' tag'li bir kamera bulunamadı!");
        }

        // UDP sunucusunu arka planda başlat
        StartUdpServer();
    }

    void Update()
    {
        // 1. Gelen Komutu İşle (Ana Unity Thread'inde)
        lock (_commandLock)
        {
            if (!string.IsNullOrEmpty(_receivedCommand))
            {
                ProcessCommand(_receivedCommand);
                _receivedCommand = ""; // Komut işlendi, sıfırla
            }
        }

        // 2. Karakteri Hareket Ettir
        MoveAndJump();

        // Zıplama komutunu her karede sıfırla, böylece sadece bir kez zıplar
        _networkJumpInput = false;
    }

    /// <summary>
    /// Ağdan gelen girdilere göre karakteri hareket ettirir ve zıplama/yer çekimi uygular.
    /// </summary>
    void MoveAndJump()
    {
        if (_camTransform == null || _controller == null) return;

        // Ağdan gelen girdiyi al
        Vector2 moveInput = _networkMoveInput;

        // Yatay hareket vektörünü hesapla (x:sağ-sol, z:ileri-geri)
        Vector3 direction = new Vector3(moveInput.x, 0f, moveInput.y).normalized;
        Vector3 moveVelocity = Vector3.zero;

        // Karakterin Yönlendirilmesi ve Yürüme
        if (direction.magnitude >= 0.1f)
        {
            // Kamera yönüne göre hedef açıyı hesapla
            float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + _camTransform.eulerAngles.y;

            // Yumuşak dönüş
            float angle = Mathf.LerpAngle(transform.eulerAngles.y, targetAngle, turnSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);

            // Hareket yönü
            Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;

            // Hesaplanan yönü hıza aktar
            moveVelocity = moveDir * moveSpeed;

            // Animasyon: Hızı ayarla
            if (_animator != null) _animator.SetFloat("Speed", direction.magnitude);
        }
        else
        {
            // Animasyon: Durdur
            if (_animator != null) _animator.SetFloat("Speed", 0f);
        }

        // -------------------------------------------------------------
        // Yer Çekimi ve Zıplama
        // -------------------------------------------------------------
        if (_controller.isGrounded)
        {
            _verticalVelocity = -gravity * Time.deltaTime; // Hafifçe yere yapıştır

            // Ağdan Zıplama komutu gelirse
            if (_networkJumpInput)
            {
                _verticalVelocity = jumpVelocity;
                // Animasyon tetikleyicisi eklenebilir
                // if (_animator != null) _animator.SetTrigger("Jump");
            }
        }
        else
        {
            // Yer çekimini uygula
            _verticalVelocity -= gravity * Time.deltaTime;
        }

        // Son Hareket Vektörü (Yatay hareket + Dikey hareket)
        Vector3 finalMove = moveVelocity;
        finalMove.y = _verticalVelocity;

        // Karakteri hareket ettir (Time.deltaTime ile çarpımı unutma!)
        _controller.Move(finalMove * Time.deltaTime);
    }

    // -------------------------------------------------------------
    // 🚀 UDP MANTIKLARI
    // -------------------------------------------------------------
    private void StartUdpServer()
    {
        _isServerRunning = true;
        _receiveThread = new Thread(new ThreadStart(ReceiveData));
        _receiveThread.IsBackground = true;
        _receiveThread.Start();
        Debug.Log($"[UNITY] UDP Sunucusu Başlatıldı. Port: {listenPort}");
    }

    /// <summary>
    /// Arka plan thread'inde sürekli olarak UDP paketlerini dinler.
    /// </summary>
    private void ReceiveData()
    {
        try
        {
            _udpClient = new UdpClient(listenPort);
            Debug.Log($"[UNITY] UDP Dinleme başladı: {listenPort}");

            while (_isServerRunning)
            {
                try
                {
                    IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                    // Veri gelene kadar burada bloklanır
                    byte[] data = _udpClient.Receive(ref anyIP);

                    _clientEndPoint = anyIP; // Client'ın IP'sini sakla

                    // Veriyi string'e çevir ve büyük harfe dönüştür
                    string message = Encoding.UTF8.GetString(data).Trim().ToUpper();

                    // Thread-safe komut atama (Ana Unity thread'inde işlenecek)
                    lock (_commandLock)
                    {
                        _receivedCommand = message;
                    }

                    Debug.Log($"[UNITY] Komut alındı: {message} (From: {anyIP.Address})");

                    // Yanıt gönder (ACK)
                    SendAcknowledgement(message);
                }
                catch (SocketException ex)
                {
                    if (!_isServerRunning) break;
                    Debug.LogWarning($"[UNITY] Socket Exception: {ex.Message}");
                }
                catch (System.ObjectDisposedException)
                {
                    if (!_isServerRunning) break; // Sunucu kapatılırken olan hatayı görmezden gel
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UNITY] UDP Başlangıç/Genel Hatası: {e.Message}");
        }
        finally
        {
            // Hata olsa bile soketi ve thread'i temizle
            if (_udpClient != null)
            {
                _udpClient.Close();
                Debug.Log("[UNITY] UDP Client kapatıldı");
            }
        }
    }

    /// <summary>
    /// Python client'a komutun alındığına dair onay (ACK) gönderir.
    /// </summary>
    private void SendAcknowledgement(string command)
    {
        if (_clientEndPoint != null && _udpClient != null)
        {
            try
            {
                // Client'ın ACK portuna yanıt gönder
                IPEndPoint ackEndPoint = new IPEndPoint(_clientEndPoint.Address, clientAckPort);
                string ackMessage = $"ACK:{command}";
                byte[] ackData = Encoding.UTF8.GetBytes(ackMessage);

                _udpClient.Send(ackData, ackData.Length, ackEndPoint);
                // Debug.Log($"[UNITY] ACK gönderildi: {ackMessage}"); // Çok fazla log birikmesini önlemek için kapatılabilir
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[UNITY] ACK Gönderilemedi (Client dinlemiyor olabilir): {e.Message}");
            }
        }
    }

    /// <summary>
    /// Alınan komutları hareket girdilerine dönüştürür.
    /// </summary>
    private void ProcessCommand(string command)
    {
        // Debug.Log($"[UNITY] Komut işleniyor: {command}"); // Çok fazla log birikmesini önlemek için kapatılabilir

        switch (command)
        {
            case "LEFT":
                _networkMoveInput = new Vector2(-1f, 0f); // Sol
                break;
            case "RIGHT":
                _networkMoveInput = new Vector2(1f, 0f);  // Sağ
                break;
            case "FORWARD":
                _networkMoveInput = new Vector2(0f, 1f);  // İleri
                break;
            case "BACK":
            case "BACKWARD":
                _networkMoveInput = new Vector2(0f, -1f); // Geri
                break;
            case "STOP":
                _networkMoveInput = Vector2.zero;        // Hareketi durdur
                break;
            case "JUMP":
                _networkJumpInput = true;               // Zıplama tetikleyici
                break;
            case "QUIT":
                SceneManager.LoadScene("MainMenu");        // Hareketi durdur
                break;
            default:
                Debug.LogWarning($"[UNITY] Bilinmeyen Komut: {command}");
                break;
        }
    }

    // Uygulama kapatıldığında veya script yok edildiğinde sunucuyu düzgünce kapat
    private void OnApplicationQuit()
    {
        ShutdownUdpServer();
    }

    private void OnDestroy()
    {
        ShutdownUdpServer();
    }

    private void ShutdownUdpServer()
    {
        Debug.Log("[UNITY] UDP Sunucusu kapatılıyor...");
        _isServerRunning = false;

        // UdpClient'ı kapatmak, Receive metodundaki bloklamayı sonlandırır
        if (_udpClient != null)
        {
            // Close() metodu, thread'in SocketException fırlatıp bloklamadan çıkmasını sağlar.
            _udpClient.Close();
            _udpClient = null;
        }

        // Thread'in sonlanmasını bekle
        if (_receiveThread != null && _receiveThread.IsAlive)
        {
            _receiveThread.Join(100); // Kısa bir süre bekle (100ms)
        }
    }
}