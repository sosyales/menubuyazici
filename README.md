# MenuBu Printer Agent (Local Bridge Denemesi)

Windows masaüstü uygulaması - Restoran siparişlerini otomatik olarak termal yazıcılara yazdırır.

## 🚀 Hızlı Başlangıç

### İndirme

[Releases](https://github.com/sosyales/menubu/releases) sayfasından en son sürümü indirin.

### Kurulum

1. `MenuBuPrinterAgent.exe` dosyasını çalıştırın
2. Sistem tepsisinde ikona sağ tıklayın → "Giriş Yap"
3. Email ve şifrenizi girin
4. "Yazıcı Ayarla" ile yazıcınızı seçin

## 📋 Gereksinimler

- Windows 10 veya üzeri
- .NET 6.0 Runtime (uygulama ile birlikte gelir)
- Termal yazıcı (58mm veya 80mm)
- İnternet bağlantısı

## 🔧 Özellikler

### Temel Özellikler
- ✅ **Otomatik Sipariş Yazdırma**: Yeni siparişler anında yazdırılır
- ✅ **Tüm Entegrasyonlar**: Getir, Migros, Trendyol, Yemeksepeti desteği
- ✅ **Self Service & Masa Siparişleri**: Tüm sipariş tipleri destekleniyor
- ✅ **58mm ve 80mm Yazıcılar**: Her iki boyut için optimize edilmiş

### Gelişmiş Özellikler
- ✅ **Lokal HTML Köprüsü**: Panelde açılan fiş HTML’i `http://127.0.0.1:9075/print` üzerinden doğrudan ajana gönderilir, tarayıcıdaki tasarım bire bir yazdırılır.
- ✅ **Uzak Kuyruk Fallback’i**: Lokal ajan yoksa işler otomatik olarak merkezi kuyruğa düşer ve basit “lines” formatıyla yazdırılır.
- ✅ **Çoklu Yazıcı Eşleştirme**: Farklı yazıcılara farklı fişler
- ✅ **Otomatik Yeniden Bağlanma**: Bağlantı kesildiğinde 15 saniye sonra tekrar dener
- ✅ **Otomatik Başlatma**: Windows açılışında otomatik çalışır
- ✅ **Kuyruk Yönetimi**: Bekleyen işleri görüntüleme ve temizleme
- ✅ **Bildirimler**: Her işlem için sistem bildirimleri

## ⚙️ Ayarlar

### Yazıcı Ayarları
- **Yazıcı Seçimi**: Varsayılan veya belirli bir yazıcı seçin
- **Yazıcı Genişliği**: 58mm veya 80mm
- **Font Boyutu**: -3 ile +3 arası ayarlama

### Yazıcı Eşleştirme
- Web panelinden tanımlanan yazıcıları fiziksel yazıcılarla eşleştirin
- Mutfak, adisyon, bar gibi farklı yazıcılar kullanın

## 📖 Detaylı Dokümantasyon

[KURULUM.md](KURULUM.md) dosyasına bakın.

## 🏗️ Geliştirme

```bash
# Projeyi klonla
git clone https://github.com/sosyales/menubu.git
cd menubu/Yazici

# Derle
dotnet build

# Çalıştır
dotnet run
```

## ⚡ Anlık Yazdırma

- Ajan varsayılan olarak `wss://menubu.com.tr/ws/print-jobs` adresine WebSocket bağlantısı açar ve yeni işleri anında alır.
- Push kanalını veya endpoint adresini değiştirmek isterseniz `AppData\Roaming\MenuBu\printer-agent.json` içindeki `EnablePushChannel` ve `PushEndpoint` alanlarını düzenleyebilirsiniz.
- Sunucu push kanalı kapalıysa ajan otomatik olarak mevcut REST kuyruğu üzerinden polling’e geri döner.

## 🎨 HTML / Lines Akışı

- Panel açık cihazlarda fiş HTML’i tarayıcıdan direkt olarak lokal köprüye gider; `HtmlPrinter` WebView2 ile aynı tasarımı bastığı için tarayıcıdaki görünüm bire bir alınır.
- Dışarıdaki ajanlar için HTML gönderilemiyorsa `lines` alanı devreye girer ve klasik termal yazıcı stiliyle metin çıktısı alınır.
- Margin, genişlik ve WebView2’ye eklenen koruyucu CSS blokları `Printing/HtmlPrinter.cs` içindeki `PrepareHtml` metodundan yönetilir.
- Panel JS tarafı lokal çağrı başarıyla dönerse işe merkezi kuyruktan düşürmez; başarısız olursa otomatik olarak `api/queue-print-job.php` endpoint’ine geri döner.

### Lokal HTTP API

- Endpoint: `http://127.0.0.1:9075/print`
- Method: `POST`
- Body:

```json
{
  "html": "<html>...</html>",
  "printerWidth": "58mm",
  "metadata": { "orderId": 123 }
}
```

- Yanıt: `{ "success": true }` veya `{ "success": false, "error": "..." }`
- CORS sadece `https://menubu.com.tr` (ve www) kaynaklarına açık; diğer origin’ler veya uzak IP’lerden erişim reddedilir.

## 📦 Build

GitHub Actions otomatik olarak her push'ta derler ve release oluşturur.

Repo'ya push attığınızda `.github/workflows/build-printer-agent.yml` çalışır; runner üzerinde:

1. `dotnet publish` ile self-contained `publish/win-x64` çıktısı üretir
2. Inno Setup'u kurup `Installer/MenuBuPrinterAgent.iss` ile Program Files kurulum exe'si hazırlar
3. Her iki çıktı da pipeline artefaktı olarak eklenir (Actions sekmesinden indirilebilir)

Kendi makinenizde self-contained paket üretmek için:

```powershell
cd Yazici\build
.\publish-selfcontained.ps1
```

Bu adım `publish\win-x64` klasörünü oluşturur. Ardından Inno Setup ile `Installer/MenuBuPrinterAgent.iss` dosyasını açıp `Build` diyerek kurulum paketi alabilirsiniz. Installer eski sürümü otomatik kaldırır, Program Files'a kurulumu yapar ve başlangıca ekler.

## 🔄 GitHub'a Push Etme

```bash
cd /var/www/fastuser/data/www/menubu.com.tr/Yazici

# Değişiklikleri ekle
git add -A

# Commit
git commit -m "Açıklama mesajı"

# Push (SSH key gerekli)
git push origin main
```

**Not:** GitHub'a SSH key eklenmeli:
1. `ssh-keygen -t ed25519 -C "email@example.com"`
2. `cat ~/.ssh/id_ed25519.pub` - Çıktıyı kopyala
3. GitHub → Settings → SSH Keys → New SSH key
4. Yapıştır ve kaydet

## 📝 Lisans

Proprietary - MenuBu © 2025

## 🆘 Destek

Sorun yaşıyorsanız [Issues](https://github.com/sosyales/menubu/issues) açın.
