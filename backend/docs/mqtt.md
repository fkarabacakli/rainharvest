# MQTT Sözleşmesi

> Firmware (ESP32), backend ve simülatör arasındaki sözleşme.
> Kod karşılıkları `src/RainHarvest.Contracts` projesinde; JSON örnekleri birim testlerle sabitlenmiş durumda (`TelemetryPayloadJsonTests`).
> Durum: tüm topic'ler **kesin** (`telemetry`, `status`: M1; `cmd`, `state`, `config`: M3).

## Bağlantı

| Ayar | Değer |
|---|---|
| Broker | Geliştirme: yerel Mosquitto `localhost:1883` (TLS yok). Demo: HiveMQ Cloud `:8883` (TLS) |
| Kimlik | Kullanıcı adı ve şifre zorunlu, anonim bağlantı kapalı |
| Client ID | Her cihaz için benzersiz, öneri: `esp32-<deviceId>`. Aynı ID ile bağlanan ikinci istemci ilkini düşürür |
| Keep-alive | 15 sn önerilir |
| Clean session | `true` |
| **Last Will** | Topic `rainwater/<deviceId>/status`, payload `offline`, **retain = true**, QoS 1 |

Bağlandıktan hemen sonra cihaz `rainwater/<deviceId>/status` topic'ine `online` yayınlamalı (retain = true, QoS 1).

## Cihaz kimliği

`<deviceId>` yalnızca harf, rakam, `-` ve `_` içerebilir, en fazla 64 karakter (ör. `node1`, `rh-a1b2c3d4e5f6`). Backend bu kurala uymayan topic'lerden gelen mesajları atlar.

- **Her cihazın id'si benzersiz ve sabittir.** Ya fabrikada firmware'e yüklenir ya da ESP32'nin MAC adresinden türetilir (ör. `rh-` + MAC'in küçük harfli hex hali). id kutunun üzerine yazılır.
- **Tak-çalıştır:** Admin cihazı ilk bağlantıdan önce id'siyle ve mahaliyle kaydeder. Cihaz ilk mesajını gönderdiğinde doğrudan kullanıcının mahalinde görünür.
- Önceden kaydedilmemiş bir cihaz ilk mesajında mahalsiz olarak otomatik kaydedilir ve yalnızca Admin görür.
- **Güvenlik (kapsam dışı, gerekirse sonradan):** Şu an tüm cihazlar ortak MQTT hesabı kullanır. Her cihaza id'siyle birlikte kendine özel bir MQTT şifresi yüklenir. MQTT kullanıcı adı cihaz id'si olur ve broker ACL'i cihazın yalnızca `rainwater/<kendi id'si>/#` topic'ine yazmasına izin verir. Böylece id taklit edilemez.

## Topic'ler

| Topic | Yön | QoS | Retain | Gövde | Durum |
|---|---|---|---|---|---|
| `rainwater/<id>/telemetry` | cihaz → | 0 veya 1 | hayır | JSON, aşağıda | kesin |
| `rainwater/<id>/status` | cihaz → | 1 | **evet** | düz metin: `online` / `offline` | kesin |
| `rainwater/<id>/state` | cihaz → | 1 | evet | JSON, aşağıda | kesin |
| `rainwater/<id>/cmd` | → cihaz | 1 | hayır | JSON, aşağıda | kesin |
| `rainwater/<id>/config` | → cihaz | 1 | evet | JSON, aşağıda | kesin |

## `telemetry`

10 saniyede bir. JSON alan adları **camelCase**. Bilinmeyen alanlar yok sayılır, yani sözleşmeyi bozmadan alan eklenebilir.

```json
{
  "ts": 1763800000,
  "levelPct": 64.2,
  "levelCm": 38.5,
  "turbidityNtu": 12.0,
  "ph": 6.9,
  "waterTempC": 18.4,
  "raining": true,
  "soilPct": 31,
  "flowLpm": 0.0,
  "flowTotalL": 142.7,
  "rssi": -61
}
```

| Alan | Tip | Zorunlu | Geçerli aralık | Açıklama |
|---|---|---|---|---|
| `levelPct` | sayı | evet | 0–100 | Depo doluluk oranı, % |
| `levelCm` | sayı | evet | 0–1000 | Su yüksekliği, cm |
| `turbidityNtu` | sayı | evet | 0–5000 | Bulanıklık, NTU |
| `ph` | sayı | evet | 0–14 | pH |
| `raining` | bool | evet | | Yağmur sensörü ıslak mı |
| `soilPct` | sayı | evet | 0–100 | Toprak nemi, % |
| `flowLpm` | sayı | evet | 0–100 | Anlık çıkış debisi, L/dk |
| `flowTotalL` | sayı | evet | 0–1 000 000 | Cihaz açıldığından beri toplam çıkış, L |
| `rssi` | tam sayı | evet | −127–0 | Wi-Fi sinyali, dBm |
| `ts` | tam sayı | hayır | | Cihaz saati, Unix saniye. NTP senkronize değilse (2024 öncesi) yok sayılır |
| `waterTempC` | sayı | hayır | −20–80 | DS18B20 su sıcaklığı, °C |

**Doğrulama:** Zorunlu bir alan eksikse, `null` ya da yanlış tipteyse veya bir değer aralık dışındaysa backend **mesajın tamamını** atlar ve loglar. Aralık dışı bir değer kablolama, kalibrasyon ya da firmware hatası demektir; kaydedilirse ortalamaları ve kuralları bozar.

**Zaman:** Backend sunucunun alış zamanını esas alır. `ts` yalnızca bilgi amaçlı saklanır.

## `status`

Düz metin, büyük-küçük harf duyarsız: `online` veya `offline`.

- `online`: cihaz bağlanınca kendisi yayınlar.
- `offline`: Last Will olarak broker yayınlar (cihaz beklenmedik şekilde koptuğunda). Cihaz kapanırken temiz bir `DISCONNECT` gönderecekse önce kendisi yayınlamalıdır, çünkü temiz kopuşta Last Will tetiklenmez.
- Boş retained mesaj, retained durumu temizler; backend bunu yok sayar.
- Gelen her `telemetry` mesajı da cihazı online sayar.

## `cmd` (backend → cihaz, QoS 1, retain yok)

```json
{"commandId": "0193a0b4-…", "action": "irrigate", "durationS": 60}
```

| `action` | `durationS` | Cihazın yapması gereken |
|---|---|---|
| `irrigate` | zorunlu | Valf aç → pompa çalıştır → süre dolunca pompa durdur → valf kapat |
| `stop` | — | Çalışan sulamayı ya da pompayı durdur, valfi kapat |
| `pump_on` | zorunlu | Bakım: pompayı çalıştır (valf açık olmalı) |
| `pump_off` | — | Bakım: pompayı durdur |
| `valve_open` | — | Bakım: valfi aç |
| `valve_close` | — | Bakım: valfi kapat (pompa çalışıyorsa reddet) |

Süre her zaman `config.pumpMaxS` ile sınırlanır. Bilinmeyen alanlar yok sayılır.

**Cihaz şu durumlarda komutu reddeder** (`state.reason` olarak yazılır):

| `reason` | Ne zaman |
|---|---|
| `tank_empty` | Depo `config.levelMinPct` altında |
| `water_quality` | pH `config.phMin`–`phMax` dışında ya da bulanıklık `config.turbidityMaxNtu` üstünde |
| `valve_closed` | Valf kapalıyken `pump_on` |
| `pump_running` | Pompa çalışırken `valve_close` |
| `busy` | Zaten sulama ya da pompa çalışıyor |
| `invalid` | Eksik/hatalı süre, bilinmeyen `action`, henüz sensör okuması yok |

## `state` (cihaz → backend, QoS 1, **retain**)

Her değişiklikte ve her bağlantıdan sonra yayınlanır. Pompa ve valf durumunun doğruluk kaynağı cihazdır.

```json
{"pumpOn": true, "valveOpen": true, "irrigating": true,
 "lastCommandId": "0193a0b4-…", "lastCommandResult": "accepted", "reason": null}
```

| Alan | Açıklama |
|---|---|
| `pumpOn`, `valveOpen`, `irrigating` | Güncel durum (zorunlu) |
| `lastCommandId` | Bu değişikliğin yanıt verdiği komut (opsiyonel) |
| `lastCommandResult` | `accepted` (başladı), `rejected` (reddedildi), `completed` (bitti ya da durduruldu) |
| `reason` | `rejected` için yukarıdaki tablodan; `completed` için depo boşalıp erken durduysa `tank_empty` |

**Akış örneği** (`irrigate`): komut gelir → `accepted` ile `{pumpOn: true, valveOpen: true, irrigating: true}` → süre dolar → `completed` ile hepsi `false`. `stop` gelirse önce yarıda kalan komut için `completed`, sonra `stop`'un kendisi için `completed` yayınlanır.

Backend komut durumunu yalnızca ileri taşır (`Sent → Accepted → Completed`). Bu yüzden retained mesajın tekrar gelmesi sorun çıkarmaz. 30 saniye içinde yanıt alamadığı komutu `Expired` sayar; geç gelen bir yanıt bunu düzeltir.

## `config` (backend → cihaz, QoS 1, **retain**)

```json
{"phMin": 6.0, "phMax": 8.5, "turbidityMaxNtu": 25, "levelMinPct": 15, "pumpMaxS": 300}
```

- Cihazın kendi güvenlik sınırları. Backend'e ulaşılamasa bile bunları uygulamalı.
- Backend bu mesajı ayarlar değiştiğinde ve cihaz her `online` olduğunda yeniden yayınlar.
- Cihaz bir config almadan önce yukarıdaki değerleri firmware varsayılanı olarak kullanmalı.
- Simülatördeki `DeviceController` sınıfı bu kuralların çalışan bir örneği; birim testleri (`DeviceControllerTests`) beklenen davranışı adım adım gösteriyor.
