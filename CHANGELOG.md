# Değişiklik Günlüğü

## v2.2.0 - Print.php Entegrasyonu ve Kullanılabilirlik İyileştirmeleri

### ✨ Yeni Özellikler
- **Print.php URL Desteği**: Yazıcı ajanı artık print.php URL'lerinden JSON çekiyor
- **Tüm Entegrasyonlar Destekleniyor**: Getir, Migros, Trendyol, Yemeksepeti otomatik yazdırılıyor
- **Başarılı Yazdırma Bildirimi**: Her başarılı yazdırma için kısa bildirim
- **Daha Hızlı Yeniden Bağlanma**: Bağlantı kesildiğinde 15 saniye sonra otomatik deneme (önceden 30 saniye)

### 🔧 İyileştirmeler
- **Polling Interval**: 2 saniyeden 3 saniyeye çıkarıldı (sunucu yükü azaltıldı)
- **HTTP Timeout**: Print.php çağrıları için 20 saniye timeout
- **Hata Mesajları**: Daha kısa ve anlaşılır hata mesajları
- **Bildirim Başlıkları**: Daha açıklayıcı bildirim başlıkları
- **Sessiz Otomatik Giriş**: Otomatik giriş yapıldığında bildirim gösterilmiyor

### 🐛 Düzeltmeler
- URL'den JSON çekme hatası düzeltildi
- Boş JSON yanıtları kontrol ediliyor
- Timeout hataları daha iyi yönetiliyor
- Hata mesajları 100 karakterle sınırlandırıldı

### 📦 Teknik Değişiklikler
- PrinterManager URL'den JSON parse ediyor
- Hata kontrolü ve validasyon iyileştirildi
- HTTP client timeout ayarları optimize edildi
- Accept header eklendi (application/json)

### 🔄 API Değişiklikleri
- enqueuePrintJobForIntegration artık URL gönderiyor
- enqueuePrintJobForOrder artık URL gönderiyor
- Payload formatı: `{"url": "https://menubu.com.tr/api/.../print.php?id=..."}`

---

## v2.1.0 - HTML Yazdırma ve İyileştirmeler

### ✨ Yeni Özellikler
- **WebView2 ile HTML Yazdırma**: Artık print.php'deki HTML tasarımı direkt yazdırılıyor
- **Otomatik Yeniden Bağlanma**: Bağlantı kesildiğinde 30 saniye sonra otomatik tekrar deneme
- **Bağlantı Bildirimleri**: Bağlantı kesildiğinde ve geri geldiğinde bildirim
- **Balloon Tip Tıklama**: Bağlantı kesildi bildirimine tıklayarak yeniden bağlanma

### 🔧 İyileştirmeler
- HTML tasarımı 58mm ve 80mm için otomatik optimize ediliyor
- Uygulama her zaman sistem tray'de açık kalıyor
- Daha iyi hata mesajları ve kullanıcı bildirimleri

### 🐛 Düzeltmeler
- Metin kesme sorunu çözüldü
- Sağa yaslama sorunu düzeltildi
- Ürün opsiyonları ve fiyatları tam gösteriliyor

### 📦 Teknik Değişiklikler
- Microsoft.Web.WebView2 paketi eklendi
- HtmlPrinter sınıfı oluşturuldu
- PrinterManager IDisposable implement edildi
- Otomatik yeniden bağlanma mekanizması eklendi

### 🔄 API Değişiklikleri
- queue-print.php artık HTML payload gönderiyor
- Yazıcı ajanı hem `lines` hem `html` formatını destekliyor
