# Gerçek Zamanlı Sözleşme (SignalR)

> Panel için canlı veri. REST API'nin tamamlayıcısıdır: ilk durumu REST'ten al, sonraki değişiklikleri buradan dinle.
> Kod karşılığı: `src/RainHarvest.Api/Features/Live/` (`ILiveClient` olay listesinin kaynağıdır).

## Bağlantı

| | |
|---|---|
| URL | `http://localhost:5255/hubs/live` |
| Kimlik | `POST /api/v1/auth/token` ile alınan token. SignalR istemcisi bunu `access_token` query parametresiyle gönderir (WebSocket'te başlık gönderilemediği için). |
| Yön | Yalnızca sunucu → istemci. Hub'da çağrılacak bir metot yok. |
| JSON | camelCase; enum'lar metin (`"Rule"`, `"Accepted"`, `"WaterQualityBad"`) |

Token yoksa ya da geçersizse bağlantı 401 ile reddedilir. Token süresi dolunca (8 saat) yeni token alıp yeniden bağlanmak gerekir.

## Kim neyi alır

- **Kullanıcı:** Bağlandığı anda üyesi olduğu her mahalin cihaz olaylarını alır. Başka mahallerin olayları ona hiç gönderilmez.
- **Admin:** Tüm cihazların olaylarını alır; mahalsiz cihazlar dahil.
- Üyelik değişirse yeni durum **yeniden bağlanınca** geçerli olur.

Filtreleme sunucuda yapılır. İstemcinin abone olması gerekmez; belirli bir cihazı göstermek istiyorsa gelen olaylardaki `deviceId` alanına göre filtreler.

## Olaylar

| Olay | Ne zaman | Payload |
|---|---|---|
| `ReadingReceived` | Her geçerli telemetri (≈10 sn'de bir, cihaz başına) | `{ deviceId, reading: ReadingDto }` |
| `DeviceStatusChanged` | Cihaz online/offline oldu | `{ deviceId, isOnline, at }` |
| `ActuatorStateChanged` | Cihaz pompa/valf durumunu bildirdi | `{ deviceId, pumpOn, valveOpen, irrigating, at }` |
| `CommandUpdated` | Komut oluşturuldu ya da durumu değişti | `CommandDto` (REST'teki ile aynı) |
| `AlertRaised` | Yeni uyarı açıldı | `AlertDto` |
| `AlertResolved` | Uyarının sebebi ortadan kalktı | `AlertDto` (`resolvedAt` dolu) |
| `AlertAcknowledged` | Biri uyarıyı onayladı | `AlertDto` (`acknowledgedAt` dolu) |

`ReadingDto`, `CommandDto` ve `AlertDto` şemaları OpenAPI dokümanında (`/openapi/v1.json`) tanımlı; REST yanıtlarıyla birebir aynı.

### Örnek: manuel sulama akışı

```
POST /devices/node1/commands {action:"irrigate", durationS:60}  → 202 { id, status:"Sent" }
CommandUpdated        { id, status:"Sent" }
CommandUpdated        { id, status:"Accepted" }
ActuatorStateChanged  { pumpOn:true, valveOpen:true, irrigating:true }
ReadingReceived       { reading: { flowLpm: 4, ... } }   (sulama süresince)
CommandUpdated        { id, status:"Completed" }
ActuatorStateChanged  { pumpOn:false, valveOpen:false, irrigating:false }
```

Olayların sırası cihazın mesaj sırasını izler. İstemci, her zaman en son gelen `status`'u esas almalı.

## Uyarılar

| `type` | `severity` | Açılır | Kendiliğinden kapanır |
|---|---|---|---|
| `WaterQualityBad` | `Warning` | pH ayar aralığı dışında ya da bulanıklık limit üstünde | pH aralığın 0.1 içinde ve bulanıklık limitin %90'ının altında |
| `TankLow` | `Warning` | Depo `levelMinPct` altında | Depo `levelMinPct + 5` üstüne çıkınca |
| `DeviceOffline` | `Critical` | Cihaz offline oldu | Cihaz yeniden online oldu |

- Bir cihazda aynı tipte en fazla bir açık uyarı olur. Sorun sürdükçe yeni uyarı açılmaz.
- Kapanma eşiği açılma eşiğinden biraz daha sıkı (histerezis). Böylece eşik etrafında gidip gelen bir değer uyarıyı sürekli açıp kapatmaz.
- **Onaylamak** (`POST /alerts/{id}/ack`) uyarıyı kapatmaz; yalnızca birinin gördüğünü kaydeder. Uyarı, sebebi ortadan kalkınca kapanır.

## Örnek istemci (TypeScript)

```ts
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";

const connection = new HubConnectionBuilder()
  .withUrl("http://localhost:5255/hubs/live", {
    accessTokenFactory: () => auth.accessToken, // /auth/token yanıtındaki access_token
  })
  .withAutomaticReconnect()
  .configureLogging(LogLevel.Warning)
  .build();

connection.on("ReadingReceived", ({ deviceId, reading }) => updateChart(deviceId, reading));
connection.on("CommandUpdated", (command) => updateCommand(command));
connection.on("AlertRaised", (alert) => showToast(alert.message));

await connection.start();
```

**Yeniden bağlanınca** arada kaçan olaylar tekrar gönderilmez. `onreconnected` içinde ekranın ihtiyaç duyduğu verileri REST'ten yeniden yükle: `/devices`, `/alerts`, açık komutlar.

## CORS

İzinli origin'ler `Cors:AllowedOrigins` ayarından okunur. Development'ta `http://localhost:5173` (Vite), `:3000` (React/Next) ve `:4200` (Angular) açık. Başka bir adres gerekirse `appsettings.Development.json`'a eklenir. SignalR JS istemcisi negotiate isteğini varsayılan olarak `withCredentials: true` ile gönderir; backend izinli origin'ler için buna izin verir, istemcide ek ayar gerekmez.
