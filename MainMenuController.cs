using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine.EventSystems; // UI etkileşimi için eklendi

public class MainMenuController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI welcomeText;

    [Header("Karakter Seçim UI")]
    public GameObject characterSelectionPanel;
    public Transform contentContainer;
    public GameObject characterButtonPrefab;
    public Button startGameButton;

    [Header("Mouse Kontrol Ayarları")]
    public RectTransform virtualCursor; // Sahneye bir Image ekleyip buraya sürükleyin
    public GraphicRaycaster raycaster; // Canvas üzerindeki GraphicRaycaster
    public EventSystem eventSystem;

    [Header("Network Settings")]
    public int listenPort = 9050;
    private UdpClient _udpClient;
    private Thread _receiveThread;
    private string _receivedCommand = "";
    private readonly object _commandLock = new object();
    private bool _isServerRunning = true;

    private List<CharacterSelectButton> createdButtons = new List<CharacterSelectButton>();
    private string currentSelectionID = "";

    void Start()
    {
        SetupCharacterSelection();
        StartUdpServer();

        // Raycaster ve EventSystem otomatik atanmadıysa bul
        if (raycaster == null) raycaster = GetComponentInParent<GraphicRaycaster>();
        if (eventSystem == null) eventSystem = EventSystem.current;
    }

    void Update()
    {
        lock (_commandLock)
        {
            if (!string.IsNullOrEmpty(_receivedCommand))
            {
                ProcessNetworkCommand(_receivedCommand);
                _receivedCommand = "";
            }
        }
    }

    private void ProcessNetworkCommand(string cmd)
    {
        // 1. Mouse Komutu İşleme (Format: MOUSE:X:Y:CLICK)
        if (cmd.StartsWith("MOUSE:"))
        {
            HandleRemoteMouse(cmd);
            return;
        }

        Debug.Log($"[NETWORK CMD] Gelen: {cmd}");

        // 2. Sahne Yükleme Komutu
        if (cmd.StartsWith("LOAD:"))
        {
            string sceneName = cmd.Replace("LOAD:", "").Trim();
            LoadSelectedScene(sceneName);
        }
        // 3. Karakter Seçim Komutu
        else if (int.TryParse(cmd, out int index))
        {
            if (index >= 0 && index < createdButtons.Count)
            {
                string charID = createdButtons[index].GetCharacterID();
                OnCharacterSelected(charID);
                Debug.Log($"[NETWORK] Karakter seçildi: {charID}");
            }
        }
    }

    private void HandleRemoteMouse(string cmd)
    {
        try
        {
            string[] parts = cmd.Split(':');
            if (parts.Length < 4) return;

            // Python'dan gelen 0-1 arası normalize değerleri ekran çözünürlüğüne çevir
            float normX = float.Parse(parts[1]);
            float normY = float.Parse(parts[2]);
            bool isClicking = parts[3] == "1";

            float screenX = normX * Screen.width;
            float screenY = normY * Screen.height;

            // Sanal imleci hareket ettir
            if (virtualCursor != null)
            {
                virtualCursor.position = new Vector2(screenX, screenY);
            }

            // Eğer tıklama varsa UI üzerinde Raycast fırlat
            if (isClicking)
            {
                SimulateClick(screenX, screenY);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Mouse veri işleme hatası: " + e.Message);
        }
    }

    private void SimulateClick(float x, float y)
    {
        PointerEventData pointerData = new PointerEventData(eventSystem);
        pointerData.position = new Vector2(x, y);

        List<RaycastResult> results = new List<RaycastResult>();
        raycaster.Raycast(pointerData, results);

        foreach (RaycastResult result in results)
        {
            // Tıklanan objede Button bileşeni var mı bak
            Button btn = result.gameObject.GetComponentInParent<Button>();
            if (btn != null && btn.interactable)
            {
                Debug.Log($"[NETWORK] Butona tıklandı: {result.gameObject.name}");
                btn.onClick.Invoke();
                break; // İlk bulduğun etkileşimli butona tıkla ve çık
            }
        }
    }

    // --- UDP MANTIĞI ---
    private void StartUdpServer()
    {
        _receiveThread = new Thread(() => {
            try
            {
                _udpClient = new UdpClient(listenPort);
                while (_isServerRunning)
                {
                    IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udpClient.Receive(ref anyIP);
                    string message = Encoding.UTF8.GetString(data).Trim();
                    lock (_commandLock) { _receivedCommand = message; }
                }
            }
            catch (System.Exception e) { Debug.Log("UDP Error: " + e.Message); }
        });
        _receiveThread.IsBackground = true;
        _receiveThread.Start();
    }

    // --- UI FONKSİYONLARI (Değişmedi) ---
    public void WelcomeTextChanger(string patientName)
    {
        if (welcomeText != null)
            welcomeText.text = patientName + " hoş geldiniz. Lütfen karakterinizi seçiniz.";
    }

    public void SetupCharacterSelection()
    {
        foreach (Transform child in contentContainer) Destroy(child.gameObject);
        createdButtons.Clear();

        if (GameManager.Instance.loader.patient == null) return;
        PatientData patient = GameManager.Instance.loader.patient;

        int age = CalculateAge(patient.patientBirth);
        bool isChild = age < 18;

        foreach (var charDef in GameManager.Instance.allCharacters)
        {
            bool genderMatch = charDef.gender.ToLower() == patient.gender.ToLower();
            bool ageMatch = charDef.isChild == isChild;

            if (genderMatch && ageMatch)
            {
                CreateButton(charDef);
            }
        }

        string previouslySelected = GameManager.Instance.loader.patient.patientCharacter;
        if (!string.IsNullOrEmpty(previouslySelected))
        {
            currentSelectionID = previouslySelected;
            if (startGameButton != null) startGameButton.interactable = true;
        }
        else
        {
            if (startGameButton != null) startGameButton.interactable = false;
        }
        

    }

    void CreateButton(CharacterDefinition data)
    {
        GameObject newBtnObj = Instantiate(characterButtonPrefab, contentContainer);
        CharacterSelectButton btnScript = newBtnObj.GetComponent<CharacterSelectButton>();
        btnScript.Setup(data, this);
        createdButtons.Add(btnScript);
    }

    public void OnCharacterSelected(string charID)
    {
        currentSelectionID = charID;
        foreach (var btn in createdButtons) btn.Deselect();

        var selectedBtn = createdButtons.Find(b => b.GetCharacterID() == charID);
        // if (selectedBtn != null) selectedBtn.VisualSelect(); 

        GameManager.Instance.SetSelectedCharacter(charID);
        if (startGameButton != null) startGameButton.interactable = true;
    }

    public void LoadVillageScene() { LoadSelectedScene("VillageScenes"); }
    public void LoadCityScene() { LoadSelectedScene("CityScenes"); }
    public void LoadBeachScene() { LoadSelectedScene("BeachScenes"); }

    void LoadSelectedScene(string sceneName)
    {
        if (string.IsNullOrEmpty(GameManager.Instance.loader.patient.patientCharacter))
        {
            Debug.LogWarning("Lütfen önce karakter seçiniz!");
            return;
        }
        SceneManager.LoadScene(sceneName);
    }

    int CalculateAge(string birthDateString)
    {
        if (System.DateTime.TryParse(birthDateString, out System.DateTime birthDate))
        {
            int age = System.DateTime.Now.Year - birthDate.Year;
            if (System.DateTime.Now.DayOfYear < birthDate.DayOfYear) age--;
            return age;
        }
        return 20;
    }

    private void OnDestroy()
    {
        _isServerRunning = false;
        if (_udpClient != null) _udpClient.Close();
    }

    


    public void ExitGame() { Application.Quit(); }
}