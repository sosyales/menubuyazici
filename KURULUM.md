# MenuBu Yazıcı Ajanı - Kurulum ve Sorun Giderme

## Genel Bakış

MenuBu Yazıcı Ajanı, restoran siparişlerini otomatik olarak termal yazıcılara yazdıran bir Windows masaüstü uygulamasıdır.

## Sistem Gereksinimleri

- **İşletim Sistemi:** Windows 10 veya üzeri
- **.NET Runtime:** .NET 6.0 veya üzeri
- **Yazıcı:** Termal yazıcı (58mm veya 80mm)
- **İnternet:** Stabil internet bağlantısı

## Kurulum Adımları

### 1. Uygulamayı İndirin
- Yazıcı ajanı Windows uygulamasıdır
- Projeyi derleyin veya hazır .exe dosyasını kullanın

### 2. Yazıcıyı Kurun
- Termal yazıcınızı Windows'a kurun
- Yazıcı adını not edin (örn: "POS-80", "Thermal Printer")

### 3. Uygulamayı Başlatın
1. `MenuBuPrinterAgent.exe` dosyasını çalıştırın
2. Sistem tepsisinde MenuBu ikonu görünecektir
3. İkona sağ tıklayın ve "Giriş Yap" seçin

### 4. Giriş Yapın
- **Email:** anamuralem@gmail.com
- **Şifre:** Nazmi33!
- "Giriş Yap" butonuna tıklayın

### 5. Yazıcıyı Seçin
1. Sistem tepsisindeki ikona sağ tıklayın
2. "Yazıcı Ayarla" seçeneğine tıklayın
3. Listeden yazıcınızı seçin
4. Yazıcı genişliğini seçin (58mm veya 80mm)
5. "Kaydet" butonuna tıklayın

## API Endpoint'leri

Ajan aşağıdaki API'leri kullanır:

### 1. Kimlik Doğrulama
```
GET https://menubu.com.tr/api/print-auth.php?email={email}&password={password}
```

**Yanıt:**
```json
{
  "success": true,
  "business_id": 1,
  "business_name": "Demo Restoran",
  "api_key": "f1f1fbcf51d270ffc0fffd29724c1d3eb958eeee14a2b9922fec25c369beeafa"
}
```

### 2. Bekleyen İşleri Getir
```
GET https://menubu.com.tr/api/print-jobs.php?email={email}&password={password}
```

**Yanıt:**
```json
{
  "success": true,
  "business_id": 1,
  "business_name": "Demo Restoran",
  "printer_width": "58mm",
  "jobs": [
    {
      "id": 504,
      "printer_id": 1,
      "job_type": "receipt",
      "payload": "{...}",
      "created_at": "2025-11-11 14:51:12"
    }
  ]
}
```

### 3. İş Durumunu Güncelle
```
POST https://menubu.com.tr/api/print-jobs.php?id={job_id}
Content-Type: application/json

{
  "key": "api_key",
  "status": "printed",
  "error_message": null
}
```

### 4. Yazıcı Konfigürasyonları
```
GET https://menubu.com.tr/api/printer-configs.php?business_id={business_id}
```

## Çalışma Mantığı

1. **Başlangıç:**
   - Uygulama başlar ve sistem tepsisinde çalışır
   - Kullanıcı giriş yapar (email/şifre)
   - API key alınır

2. **Polling (6 saniyede bir):**
   - API'den bekleyen işler çekilir (`status='pending'`)
   - Yeni işler tespit edilir

3. **Yazdırma:**
   - İş durumu `printing` olarak güncellenir
   - Payload parse edilir (JSON)
   - Yazıcıya gönderilir
   - Başarılı: `printed`, Hata: `failed`

4. **Yazıcı Seçimi:**
   - Job tipine göre (receipt, kitchen) uygun yazıcı seçilir
   - Konfigürasyon yoksa varsayılan yazıcı kullanılır

## Sorun Giderme

### Ajan Bağlanmıyor

**Kontrol Listesi:**
1. İnternet bağlantısı var mı?
2. Email/şifre doğru mu?
3. API endpoint'leri erişilebilir mi?

**Test:**
```bash
curl "https://menubu.com.tr/api/print-auth.php?email=anamuralem@gmail.com&password=Nazmi33!"
```

### Yazdırmıyor

**Kontrol Listesi:**
1. Yazıcı Windows'a kurulu mu?
2. Yazıcı açık ve hazır mı?
3. Yazıcı adı doğru seçilmiş mi?
4. Bekleyen iş var mı?

**Bekleyen İşleri Kontrol:**
```bash
curl "https://menubu.com.tr/api/print-jobs.php?email=anamuralem@gmail.com&password=Nazmi33!"
```

### Bekleyen İşleri Temizle

Sistem tepsisinden:
1. İkona sağ tıklayın
2. "Kuyruğu Temizle" seçin
3. Onaylayın

Veya API ile:
```bash
curl -X POST "https://menubu.com.tr/api/print-jobs.php?action=clear&business_id=1" \
  -H "Content-Type: application/json" \
  -d '{"key":"f1f1fbcf51d270ffc0fffd29724c1d3eb958eeee14a2b9922fec25c369beeafa"}'
```

### Log Dosyaları

Sunucu tarafı loglar:
```
/var/www/fastuser/data/www/menubu.com.tr/logs/print_agent.log
```

## Veritabanı Yapısı

### print_jobs Tablosu
```sql
CREATE TABLE print_jobs (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  business_id INT UNSIGNED NOT NULL,
  printer_id INT UNSIGNED NULL,
  job_type ENUM('kitchen','customer','receipt','other') DEFAULT 'kitchen',
  payload TEXT NOT NULL,
  status ENUM('pending','printing','printed','failed') DEFAULT 'pending',
  error_message TEXT NULL,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  printed_at TIMESTAMP NULL,
  INDEX idx_business_status (business_id, status),
  INDEX idx_created (created_at)
);
```

### printer_configs Tablosu
```sql
CREATE TABLE printer_configs (
  id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  business_id INT UNSIGNED NOT NULL,
  name VARCHAR(255) NOT NULL,
  printer_type VARCHAR(50) DEFAULT 'all',
  is_active TINYINT(1) DEFAULT 1,
  is_default TINYINT(1) DEFAULT 0,
  sort_order INT DEFAULT 0,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);
```

## Test İşi Oluşturma

```sql
INSERT INTO print_jobs (business_id, printer_id, job_type, payload, status) 
VALUES (
  1, 
  1, 
  'receipt', 
  '{"order":{"title":"Test Sipariş","items":[{"name":"Test Ürün","quantity":1,"price":10.00}],"total":10.00}}', 
  'pending'
);
```

## Güvenlik

- API key güvenli saklanır
- HTTPS kullanılır
- Şifreler hash'lenir (bcrypt)
- CSRF koruması

## Destek

Sorun yaşıyorsanız:
1. Log dosyalarını kontrol edin
2. API endpoint'lerini test edin
3. Yazıcı bağlantısını kontrol edin
4. Uygulamayı yeniden başlatın

## Otomatik Başlatma

Ajan Windows başlangıcına otomatik eklenir:
- Kayıt: `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`
- Değer: `MenuBuPrinterAgent`

## Güncellemeler

Yeni sürümler için:
1. Uygulamayı kapatın
2. Yeni .exe dosyasını indirin
3. Eski dosyanın üzerine yazın
4. Uygulamayı başlatın

## Geliştirici Notları

### GitHub'a Push Etme

```bash
cd /var/www/fastuser/data/www/menubu.com.tr/Yazici

# Değişiklikleri kontrol et
git status

# Tüm değişiklikleri ekle
git add -A

# Commit
git commit -m "Değişiklik açıklaması"

# Push (SSH ile)
git push origin main
```

### Build Kontrolü

Push sonrası:
1. https://github.com/sosyales/menubu/actions - Build durumu
2. https://github.com/sosyales/menubu/releases - İndirme linkleri

### Yaygın Hatalar

**CS0017: Multiple entry points**
- Duplicate `Yazici/` klasörü silin
- Sadece root'ta `Program.cs` olmalı

**Null warnings**
- `null` yerine `string.Empty` kullan
- Null check ekle: `?? "default"`

**Remote rejected**
```bash
git pull origin main --allow-unrelated-histories
git push origin main
```

### SSH Key Kurulumu

```bash
# Key oluştur
ssh-keygen -t ed25519 -C "anamuralem@gmail.com"

# Public key'i göster
cat ~/.ssh/id_ed25519.pub

# GitHub'a ekle: Settings → SSH Keys → New SSH key
```
