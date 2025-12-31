import socket
import time
import pyautogui
import keyboard
import ctypes  # Windows üzerinde hızlı mouse tıklama kontrolü için

# --- AYARLAR ---
UNITY_IP = "198.10.12.X"#SERVER BİLGİSAYAR IPsi
MOVE_PORT = 9050    # Hareket, Menü ve Tıklama verisi için
CAMERA_PORT = 9060  # Sadece Kamera/Bakış açısı verisi için

# UDP Soket Oluşturma
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

# Ekran boyutlarını al (Koordinatları normalize etmek için)
SCREEN_WIDTH, SCREEN_HEIGHT = pyautogui.size()

def send_to_unity(cmd, port):
    """Belirlenen porta mesaj gönderir."""
    sock.sendto(cmd.encode("utf-8"), (UNITY_IP, port))

def log_command(cmd):
    """Mouse paketleri dışındaki komutları konsola yazdırır."""
    if not cmd.startswith("MOUSE:"):
        print(f"📡 Gönderildi: {cmd}")

def menu_interface():
    while True:
        print("\n--- UNITY UZAKTAN YÖNETİM (ÇİFT PORT) ---")
        print("1. Karakter Seç (Char_01)")
        print("2. Karakter Seç (Char_02)")
        print("3. Köy Sahnesini Yükle")
        print("4. Şehir Sahnesini Yükle")
        print("5. Şahil Sahnesini Yükle")
        print("6. TAM KONTROL MODUNA GEÇ (WASD + MOUSE)")
        print("x. Çıkış")
        
        choice = input("Seçiminiz: ")
        
        if choice == '1': send_to_unity("0", MOVE_PORT)
        elif choice == '2': send_to_unity("1", MOVE_PORT)
        elif choice == '3': send_to_unity("LOAD:VillageScenes", MOVE_PORT)
        elif choice == '4': send_to_unity("LOAD:CityScenes", MOVE_PORT)
        elif choice == '5': send_to_unity("LOAD:BeachScenes", MOVE_PORT)
        elif choice == '6': start_movement_loop()
        elif choice == 'x': break

def start_movement_loop():
    print("\n🚀 Kontrol modu aktif!")
    print("- Hareket: WASD | Zıplama: SPACE")
    print("- Mouse: 9050 ve 9060 portlarına gönderiliyor")
    print("- Çıkış: 'ESC'")
    
    curr_move = "STOP"
    last_mouse_send = 0
    mouse_interval = 0.02  # Saniyede 50 paket (Akıcı kamera için)

    while True:
        # ESC ile döngüden çık
        if keyboard.is_pressed('esc'): 
            send_to_unity("STOP", MOVE_PORT)
            print("🛑 Kontrol modundan çıkıldı.")
            break
        
        # --- 1. HAREKET KONTROLÜ (KLAVYE - 9050 PORTU) ---
        new_cmd = "STOP"
        if keyboard.is_pressed('w'): new_cmd = "FORWARD"
        elif keyboard.is_pressed('s'): new_cmd = "BACKWARD"
        elif keyboard.is_pressed('a'): new_cmd = "LEFT"
        elif keyboard.is_pressed('d'): new_cmd = "RIGHT"
        
        if new_cmd != curr_move:
            curr_move = new_cmd
            send_to_unity(curr_move, MOVE_PORT)
            log_command(curr_move)
        
        if keyboard.is_pressed('space'): 
            send_to_unity("JUMP", MOVE_PORT)
            log_command("JUMP")
        if keyboard.is_pressed('q'): new_cmd = "QUIT"
        # --- 2. MOUSE KONTROLÜ (ÇİFT PORT GÖNDERİMİ) ---
        current_time = time.time()
        if current_time - last_mouse_send > mouse_interval:
            # Mouse pozisyonunu al
            mx, my = pyautogui.position()
            
            # Koordinatları 0.0 - 1.0 arasına getir (Unity uyumlu)
            norm_x = mx / SCREEN_WIDTH
            norm_y = 1.0 - (my / SCREEN_HEIGHT)
            
            # Sol tık kontrolü (Windows API kullanır, çok hızlıdır)
            # 0x01 = Sol Tık
            is_clicked = 1 if ctypes.windll.user32.GetKeyState(0x01) & 0x8000 else 0
            
            # Paket formatı: MOUSE:X:Y:CLICK
            mouse_cmd = f"MOUSE:{norm_x:.4f}:{norm_y:.4f}:{is_clicked}"
            
            # 🚀 KRİTİK NOKTA: Veriyi her iki porta da yolla
            send_to_unity(mouse_cmd, MOVE_PORT)   # Menü imleci ve tıklama için
            send_to_unity(mouse_cmd, CAMERA_PORT) # Oyun içi kamera dönüşü için
            
            last_mouse_send = current_time
        
        # İşlemciyi yormamak için çok kısa bekleme
        time.sleep(0.001)

if __name__ == "__main__":
    try:
        menu_interface()
    except KeyboardInterrupt:
        print("\nProgram kapatıldı.")
    finally:
        sock.close()
