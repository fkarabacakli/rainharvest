# RainHarvest Backend

Akıllı yağmur suyu hasat sisteminin backend'i: ESP32 cihazlarından MQTT ile veri alır, saklar, sulama kararlarını verir ve panele REST + SignalR API sunar.

- Mimari ve kararlar: [PLAN.md](PLAN.md)
- Cihaz ↔ backend sözleşmesi (firmware için): [docs/mqtt.md](docs/mqtt.md)
- Canlı veri sözleşmesi (panel için): [docs/realtime.md](docs/realtime.md)

---

## Panel geliştiren için: 5 dakikada çalıştır

Gereken tek şey **Docker** (Docker Desktop ya da Docker Engine + Compose v2). .NET kurmana gerek yok.

```bash
git clone git@github.com:fkarabacakli/rainharvest.git
cd rainharvest/backend
docker compose --profile full up -d --build
```

İlk seferde imajlar indirilip derlendiği için birkaç dakika sürer. Sonra:

| Ne | Nerede |
|---|---|
| API dokümantasyonu (Scalar) | http://localhost:5255/scalar/ |
| OpenAPI (client üretmek için) | http://localhost:5255/openapi/v1.json |
| SignalR hub | http://localhost:5255/hubs/live |
| Sağlık durumu | http://localhost:5255/health |

Dört servis çalışır: **api**, **simulator** (iki sahte cihaz: `node1`, `node2`), **db** (PostgreSQL + TimescaleDB) ve **mosquitto** (MQTT broker). Simülatör 5 saniyede bir veri gönderir ve 10 kat hızlı çalışır: hava ve toprak hızlı değişir, sulama da daha sık olur.

### Giriş bilgileri (yalnızca yerel geliştirme)

| Kullanıcı | Şifre | Ne görür |
|---|---|---|
| `admin` | `admin-dev-password` | Her şeyi; yönetim endpoint'leri dahil |
| `demo` | `demo-dev-password` | Yalnızca "Demo Ev" mahalindeki `node1` ve `node2` |

Scalar'da **Authentication → OAuth2** seçili gelir, alanlar admin ile dolu. **Authorize**'a bas; token otomatik eklenir. Normal kullanıcı görünümünü denemek için alanlara `demo` bilgilerini yaz.

### Panelden bağlanma

- **Giriş:** `POST /api/v1/auth/token`, form-urlencoded: `grant_type=password&username=…&password=…`. Yanıtta `access_token` (8 saat geçerli) ve `user` var.
- **İstekler:** `Authorization: Bearer <access_token>` başlığı.
- **Canlı veri:** SignalR, aşağıdaki bölümde.
- **CORS:** `http://localhost:5173` (Vite), `:3000` ve `:4200` açık. Başka bir port kullanıyorsan `backend/.env` dosyasına `PANEL_ORIGIN=http://localhost:8081` yazıp `docker compose --profile full up -d` ile yeniden başlat.

```ts
const body = new URLSearchParams({ grant_type: "password", username: "demo", password: "demo-dev-password" });
const res = await fetch("http://localhost:5255/api/v1/auth/token", { method: "POST", body });
const { access_token, user } = await res.json();

const devices = await fetch("http://localhost:5255/api/v1/devices", {
  headers: { Authorization: `Bearer ${access_token}` },
}).then((r) => r.json());
```

### Canlı veri (SignalR)

REST ile ekranın **ilk halini** yükle; sonraki değişiklikleri SignalR ile dinle. Sayfayı sürekli yenilemeye ya da her saniye istek atmaya (polling) gerek yok.

**Kurulum:**

```bash
npm install @microsoft/signalr
```

**Bağlanma** (girişte aldığın `access_token` ile):

```ts
import { HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";

const connection = new HubConnectionBuilder()
  .withUrl("http://localhost:5255/hubs/live", {
    accessTokenFactory: () => accessToken, // /auth/token yanıtındaki access_token
  })
  .withAutomaticReconnect() // bağlantı koparsa 0, 2, 10, 30 sn sonra tekrar dener
  .configureLogging(LogLevel.Warning)
  .build();

// Olay adları birebir böyle yazılmalı (büyük-küçük harf önemli).
connection.on("ReadingReceived", ({ deviceId, reading }) => {
  // reading: { receivedAt, levelPct, soilPct, ph, turbidityNtu, raining, flowLpm, flowTotalL, ... }
});
connection.on("DeviceStatusChanged", ({ deviceId, isOnline, at }) => { /* online rozetini güncelle */ });
connection.on("ActuatorStateChanged", ({ deviceId, pumpOn, valveOpen, irrigating }) => { /* pompa/valf ikonu */ });
connection.on("CommandUpdated", (command) => { /* command.status: Sent → Accepted → Completed (ya da Rejected/Expired) */ });
connection.on("AlertRaised", (alert) => { /* bildirim göster: alert.message, alert.severity */ });
connection.on("AlertResolved", (alert) => { /* uyarıyı listeden kaldır */ });
connection.on("AlertAcknowledged", (alert) => { /* "görüldü" olarak işaretle */ });

// Kopup geri gelince arada kaçanları tekrar gönderilmez: ekranı REST'ten tazele.
connection.onreconnected(() => reloadFromRest()); // /devices, /alerts ...

await connection.start();
```

**Olaylar:**

| Olay | Ne zaman gelir | Panelde ne yapılır |
|---|---|---|
| `ReadingReceived` | Her cihazdan ~5–10 sn'de bir | Grafiğe nokta ekle, kartlardaki değerleri güncelle |
| `DeviceStatusChanged` | Cihaz bağlandı/koptu | Online/offline rozeti |
| `ActuatorStateChanged` | Pompa ya da valf durumu değişti | Pompa/valf ikonları, "sulanıyor" göstergesi |
| `CommandUpdated` | Komut gönderildi ya da durumu değişti | "Sula" butonunun durumu: bekliyor → başladı → bitti/reddedildi |
| `AlertRaised` | Yeni uyarı | Bildirim/toast |
| `AlertResolved` | Uyarının sebebi düzeldi | Uyarıyı aktif listeden kaldır |
| `AlertAcknowledged` | Biri uyarıyı onayladı | "Görüldü" işareti |

Payload'lar REST yanıtlarıyla aynı şekilde: `CommandUpdated` = `GET .../commands/{id}` yanıtı, `Alert*` = `GET /alerts` öğesi. Alan isimleri camelCase, enum'lar metin (`"Accepted"`, `"WaterQualityBad"`). Ayrıntılı sözleşme: [docs/realtime.md](docs/realtime.md).

**Kim neyi alır:** Sunucu, kullanıcıya yalnızca üyesi olduğu mahallerin cihazlarının olaylarını gönderir; `admin` hepsini alır. Panelde ayrıca filtrelemen gerekmez. Tek bir cihazın sayfasındaysan olayları `deviceId`'ye göre süz.

**Örnek: "Sula" butonu**

```ts
const res = await fetch(`http://localhost:5255/api/v1/devices/${deviceId}/commands`, {
  method: "POST",
  headers: { Authorization: `Bearer ${accessToken}`, "Content-Type": "application/json" },
  body: JSON.stringify({ action: "irrigate", durationS: 60 }),
});
// 202: komut gönderildi. 409: cihaz offline. 400: geçersiz istek.
const command = await res.json(); // command.id, command.status === "Sent"
// Sonucu CommandUpdated olaylarından takip et (command.id ile eşleştir):
// Accepted → sulama başladı, Completed → bitti, Rejected → command.statusReason (ör. "water_quality")
```

**Denemek için:**

1. Paneli `demo` kullanıcısıyla aç ve bağlan: `ReadingReceived` olayları birkaç saniyede bir gelmeye başlar.
2. `backend/.env` içine `SIM_SCENARIO=drought` yaz, `docker compose --profile full up -d simulator` çalıştır: kısa süre sonra backend otomatik sular. `CommandUpdated` ve `ActuatorStateChanged` olayları gelir.
3. `SIM_SCENARIO=bad-water` ile `AlertRaised` (`WaterQualityBad`) gelir; `normal`'e dönünce `AlertResolved` gelir.
4. Scalar'dan bir komut gönder (`POST /devices/node1/commands`): panelde aynı anda görünür.

**Dikkat:**

- **Token süresi 8 saat.** Token yalnızca bağlanırken kontrol edilir; açık bağlantı süre dolsa da devam eder. Ama bağlantı koptuğunda yeniden bağlanma eski token ile 401 alır. Bu yüzden `accessTokenFactory` her çağrıldığında **güncel** token'ı döndürmeli (süresi dolduysa önce yenisini al).
- **Yeni mahale eklenen kullanıcı**, yeni mahalin olaylarını ancak **yeniden bağlanınca** almaya başlar.
- **CORS hatası** alırsan panelin adresi izinli listede değildir (yukarıdaki `PANEL_ORIGIN`).
- **Bağlantı ayrıntıları:** Kütüphane önce `POST /hubs/live/negotiate` yapar, sonra WebSocket'e geçer. Token URL'de `?access_token=` olarak gider; bu yalnızca `/hubs` için kabul edilir, REST isteklerinde başlık kullan.
- **Tarayıcıda kontrol:** DevTools → Network → **WS** sekmesinde `hubs/live` bağlantısını ve gelen mesajları görebilirsin.

### Senaryo değiştirmek

Simülatörün davranışı `backend/.env` dosyasıyla değişir (örnek: [.env.example](.env.example)):

```bash
SIM_SCENARIO=drought        # normal | rain | drought | bad-water | flaky
SIM_DEVICES=node1,node2
```

```bash
docker compose --profile full up -d simulator
```

| Senaryo | Panelde görmen gereken |
|---|---|
| `normal` | Ara ara yağmur, depo dolar, toprak yavaş kurur |
| `rain` | Sürekli yağmur, sulama yok |
| `drought` | Toprak hızla kurur → backend otomatik sular (`CommandUpdated`, `ActuatorStateChanged`) |
| `bad-water` | pH ~5.2, bulanıklık ~60 → `WaterQualityBad` uyarısı, sulama engellenir |
| `flaky` | Cihaz ~45 sn'de bir kopar → `DeviceOffline` uyarısı açılır, dönünce kapanır |

### Sık kullanılan komutlar

```bash
docker compose --profile full ps                # ne çalışıyor
docker compose --profile full logs -f api       # API logları
docker compose --profile full logs -f simulator # cihazların gönderdikleri
docker compose --profile full stop              # durdur (veri kalır)
docker compose --profile full down -v           # her şeyi sil, sıfırdan başla
docker compose --profile full up -d --build     # backend kodu güncellendiyse (git pull sonrası)
```

### Sorun giderme

- **`port is already allocated` (5255, 5432 ya da 1883):** O portu başka bir şey kullanıyor; muhtemelen `dotnet run` ile çalışan bir API ya da yerel bir PostgreSQL. Onu kapat ya da `.env` içinde `API_PORT=5256` gibi başka bir port seç.
- **Tarayıcıda CORS hatası:** Panelin adresi izinli listede değil. Yukarıdaki `PANEL_ORIGIN` ayarına bak.
- **`demo` kullanıcısı hiçbir cihaz görmüyor:** Simülatör çalışmıyor olabilir (`docker compose --profile full ps`). Cihazlar ilk telemetriyle online olur.
- **Eski veriden kurtulmak istiyorum:** `docker compose --profile full down -v`.

---

## Backend geliştiren için

Gereken: .NET 10 SDK ve Docker.

```bash
docker compose up -d db mosquitto                     # yalnızca altyapı
dotnet run --project src/RainHarvest.Api              # http://localhost:5255/scalar
dotnet run --project tools/RainHarvest.DeviceSimulator -- --devices node1 --scenario drought --speed 30
dotnet test                                           # entegrasyon testleri Docker ister (Testcontainers)
```

Proje yapısı, kilometre taşları ve tasarım kararları [PLAN.md](PLAN.md)'de.
