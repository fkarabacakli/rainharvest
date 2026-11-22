# Backend Planı — RainHarvest

> Durum: **Taslak, onay bekliyor.** Kod yazımı M0 onayıyla başlar.
> İlgili: [../architecture.md](../architecture.md) (sistem mimarisi, MQTT sözleşmesi), [../requirements.md](../requirements.md)

## 1. Amaç ve kapsam

Backend, ESP32 cihazlarından MQTT ile gelen veriyi alır, saklar, kuralları çalıştırır ve panel ekibine **REST + SignalR API** sunar.

**Kapsamda**
- MQTT üzerinden telemetri, eyleyici durumu ve cihaz durumunu (LWT) almak ve doğrulamak
- Kalıcı kayıt (PostgreSQL + TimescaleDB)
- Kural motoru: sulama, su kalitesi
- Manuel komut API'si (pompa, valf)
- Eşik ayarları ve cihaza retained `config` yayını
- Uyarılar: kayıt + SignalR
- Geçmiş sorgular ve CSV dışa aktarımı (Python analizi için)
- JWT kimlik doğrulama
- **Mahaller (site):** her kullanıcı yalnızca üyesi olduğu mahallerin (ev, ofis…) cihazlarını görür; bir kullanıcı birden fazla mahale üye olabilir
- OpenAPI + Scalar
- **Cihaz simülatörü** ve **docker-compose** (yerel Mosquitto)

**Kapsam dışında**
- Panel arayüzü (başka ekip üyesi yapacak; backend yalnızca sözleşme sunar)
- Dış bildirim kanalları (Telegram vb.). Şimdilik çıkarıldı; uyarılar panelde SignalR ile görünür. İleride `RaiseAlert` slice'ına bir bildirim adımı eklenerek kolayca geri getirilebilir.
- Firmware
- Tam OAuth2 yetkilendirme sunucusu, refresh token, kullanıcı kayıt ekranı (ilk sürümde)
- Mahal içi roller ve kullanıcıların kendi mahallerine üye eklemesi; şimdilik tüm yönetim Admin'de (bkz. §5, ileride eklemek ucuz)
- Cihazın kullanıcı tarafından eşleme koduyla (claim code) eklenmesi; cihazları Admin atar
- Cihaza özel MQTT şifresi ve broker ACL'i. Tüm cihazlar ortak bir MQTT hesabı kullanır (okul projesi; gerekirse sonradan eklenir, bkz. §13)
- Bulut deploy

## 2. Kararlar

| # | Konu | Karar | Gerekçe |
|---|---|---|---|
| D1 | Platform | .NET 10 (LTS), ASP.NET Core Minimal API | Makinede SDK 10 kurulu |
| D2 | Mimari | **Vertical slice**, tek web projesi | Katman projeleri bu boyutta yalnızca tören olur |
| D3 | Slice deseni | Feature başına bir dosya: endpoint + request/response + handler | Bir özelliği değiştirmek için tek bir yere bakılır |
| D4 | Mediator | **MediatR yok**; düz handler sınıfları | Lisans değişti, ihtiyaç yok |
| D5 | Endpoint kaydı | `IEndpoint` + assembly taraması | `Program.cs` şişmesin |
| D6 | MQTT girişi | Slice'lar `IMqttHandler` uygular, `MqttWorker` topic'e göre yönlendirir | MQTT de HTTP gibi bir giriş noktası olur |
| D7 | Slice'lar arası iletişim | Uygulama içi olaylar (`Channel<T>` tabanlı) | Kural ve uyarılar telemetri slice'ına doğrudan bağlanmaz |
| D8 | Veritabanı | **PostgreSQL 17 + TimescaleDB**, EF Core + Npgsql | Telemetri bir zaman serisi. Hypertable, zaman gruplama, continuous aggregate, sıkıştırma ve saklama politikaları hazır geliyor. Bkz. §7 |
| D9 | Auth | **API kendi JWT'sini basar** (HMAC-SHA256), doğrulama standart `AddJwtBearer` | Basit. Resource-server tasarımı sayesinde ileride Keycloak'a geçişte endpoint kodu değişmez |
| D10 | Roller ve mahaller | Global iki rol: `Admin`, `User`. Erişim **mahal üyeliğiyle** belirlenir; mahal içi rol yok | Bkz. §5. Mahal içi rol ileride `site_members`'a bir kolonla eklenir |
| D20 | Mahal atama | Cihazı mahale, kullanıcıyı mahale yalnızca Admin atar | En basit güvenli model |
| D22 | Önceden kayıt (tak-çalıştır) | Admin cihazı ilk bağlantıdan önce id + mahal ile kaydeder; cihaz bağlanınca doğrudan mahalinde görünür. id, cihaza fabrikada yüklenir ya da ESP32 MAC'inden türetilir | Kullanıcı hiçbir şey yapmadan cihazını görür |
| D21 | Topic'te mahal yok | MQTT topic'i `rainwater/<deviceId>/...` olarak kalır | Cihaz mahalini bilmez; taşıma firmware değişikliği gerektirmez |
| D11 | API dokümantasyonu | `Microsoft.AspNetCore.OpenApi` + Scalar, Bearer şemalı, yalnızca Development'ta | |
| D12 | Gerçek zamanlı veri | SignalR, sözleşmesi `docs/realtime.md` içinde | OpenAPI SignalR'ı kapsamıyor |
| D13 | Hata modeli | `ProblemDetails` (RFC 9457), doğrulama hataları alan bazında | Panel tek bir formatı işler |
| D14 | API yolu | `/api/v1/...` | |
| D15 | Zaman | Her yerde UTC, ISO 8601. Sunucu alış zamanı esas, cihaz zamanı ayrıca saklanır | ESP32 saati kayabilir |
| D16 | Dış bildirim | Şimdilik yok | Kapsamı daraltmak için çıkarıldı |
| D17 | Broker | Geliştirme: **yerel Mosquitto** (docker). Demo: HiveMQ Cloud. Fark yalnızca yapılandırmada | Geliştirme internete ve kotaya bağımlı olmaz |
| D18 | Sırlar | `dotnet user-secrets` (yerel), ortam değişkenleri (docker). Repo'ya sır girmez | |
| D19 | Doğruluk kaynağı | Eyleyici durumu için **cihazın bildirdiği `state`** esas alınır, gönderilen komut değil | Güvenlik kuralları ESP32'de; cihaz komutu reddedebilir |

## 3. Solution yapısı

```
backend/
├─ RainHarvest.slnx
├─ PLAN.md
├─ docker-compose.yml
├─ docker/
│  └─ mosquitto/
│     ├─ mosquitto.conf
│     └─ passwd                 (geliştirme kullanıcıları: api, simulator)
├─ docs/
│  ├─ realtime.md               (SignalR sözleşmesi)
│  └─ mqtt.md                   (topic ve payload sözleşmesi; architecture.md'den taşınır)
├─ src/
│  ├─ RainHarvest.Api/
│  │  ├─ Program.cs
│  │  ├─ Features/
│  │  │  ├─ Auth/          Login, GetCurrentUser
│  │  │  ├─ Users/         CreateUser, ListUsers
│  │  │  ├─ Devices/       HandleDeviceStatus (MQTT), ListDevices, GetDevice
│  │  │  ├─ Telemetry/     IngestTelemetry (MQTT), GetLatestReading, GetReadingHistory, ExportReadingsCsv
│  │  │  ├─ Actuators/     SendCommand, HandleActuatorState (MQTT), ListActuatorEvents
│  │  │  ├─ Settings/      GetSettings, UpdateSettings
│  │  │  ├─ Rules/         IrrigationRule, WaterQualityRule
│  │  │  ├─ Alerts/        RaiseAlert, ListAlerts, AcknowledgeAlert
│  │  │  └─ Live/          LiveHub
│  │  ├─ Domain/           Device, Reading, ActuatorEvent, DeviceSettings, Alert, User
│  │  ├─ Infrastructure/
│  │  │  ├─ Persistence/   AppDbContext, Migrations (Timescale SQL'i dahil), Seed
│  │  │  ├─ Mqtt/          MqttWorker, MqttPublisher, MqttOptions, TopicRouter
│  │  │  ├─ Auth/          JwtTokenIssuer, JwtOptions
│  │  │  └─ Events/        InProcessEventBus
│  │  └─ Common/           IEndpoint, IMqttHandler, ProblemDetails eşlemeleri, sayfalama
│  └─ RainHarvest.Contracts/     (yalnızca MQTT payload DTO'ları; Api ve Simulator paylaşır)
├─ tools/
│  └─ RainHarvest.DeviceSimulator/
└─ tests/
   ├─ RainHarvest.Api.UnitTests/          (kurallar, handler'lar)
   └─ RainHarvest.Api.IntegrationTests/   (WebApplicationFactory + Testcontainers: TimescaleDB + Mosquitto)
```

**Not:** `RainHarvest.Contracts` tek istisna. MQTT payload'ı backend ile simülatör arasında ortak olduğu için kopyalanmaz, paylaşılır. İçinde mantık olmaz, yalnızca record'lar bulunur.

## 4. API sözleşmesi (ilk sürüm)

Tümü `/api/v1` altında. Panel ekibi `/openapi/v1.json` üzerinden client üretebilir.

| Metot | Yol | Erişim | Açıklama |
|---|---|---|---|
| POST | `/auth/login` | anonim | `{username, password}` → `{accessToken, expiresAt, user}` |
| GET | `/auth/me` | giriş yapmış | Oturumdaki kullanıcı + üyesi olduğu mahaller |
| GET | `/users` | Admin | Kullanıcı listesi |
| POST | `/users` | Admin | Kullanıcı oluştur (`User` veya `Admin`) |
| GET | `/sites` | giriş yapmış | Admin: tüm mahaller. User: üyesi olduğu mahaller |
| POST | `/sites` | Admin | Mahal oluştur |
| GET | `/sites/{id}` | mahal üyesi | Mahal + cihazları |
| PUT | `/sites/{id}` | Admin | Mahalin adını değiştir |
| GET | `/sites/{id}/members` | Admin | Üyeler |
| PUT | `/sites/{id}/members/{userId}` | Admin | Üye ekle (idempotent) |
| DELETE | `/sites/{id}/members/{userId}` | Admin | Üyeyi çıkar |
| POST | `/devices` | Admin | Önceden kayıt: `{id, name, siteId?}`. id zaten varsa 409 |
| PUT | `/devices/{id}/name` | Admin | `{name}` ile cihazın görünen adını değiştir |
| PUT | `/devices/{id}/site` | Admin | `{siteId}` ile cihazı mahale ata, `{siteId: null}` ile mahalden çıkar |
| GET | `/devices` | giriş yapmış | Admin: tüm cihazlar (mahalsizler dahil). User: üyesi olduğu mahallerin cihazları |
| GET | `/devices/{id}` | cihaza erişimi olan | Cihaz detayı + son okuma + eyleyici durumu |
| GET | `/devices/{id}/readings/latest` | cihaza erişimi olan | Son okuma |
| GET | `/devices/{id}/readings` | cihaza erişimi olan | Ham okumalar: `from`, `to` (en fazla 24 saat), `limit`; devamı varsa `nextFrom` döner |
| GET | `/devices/{id}/readings/aggregate` | cihaza erişimi olan | Gruplanmış istatistik: `bucket` = 1m (≤7 gün), 15m (≤90 gün), 1h (≤400 gün), `from`, `to` |
| GET | `/devices/{id}/readings/export.csv` | cihaza erişimi olan | CSV |
| POST | `/devices/{id}/commands` | cihaza erişimi olan | `{action: "irrigate", durationS}` ya da bakım için `{actuator: "pump"/"valve", action}` → **202** `{commandId}` |
| GET | `/devices/{id}/actuator-events` | cihaza erişimi olan | Sayfalı geçmiş |
| GET | `/devices/{id}/settings` | cihaza erişimi olan | Eşikler |
| PUT | `/devices/{id}/settings` | Admin | Eşikleri güncelle → retained `config` yayınla |
| GET | `/alerts` | giriş yapmış | Erişebildiği cihazların uyarıları; `status=open/acknowledged`, sayfalı |
| POST | `/alerts/{id}/ack` | cihaza erişimi olan | Uyarıyı onayla |
| GET | `/health` | anonim | DB + MQTT bağlantısı |

**Neden ayrı bir `irrigate` komutu var?** Valf çıkışta, pompadan sonra duruyor. Sulama "valf aç → pompa çalıştır → pompa durdur → valf kapat" sırasıyla yapılmalı. Bu sırayı ve kilitlemeyi (kapalı valfe karşı pompa çalışmaz) **cihaz** yönetir. Backend ve panel yalnızca "şu kadar süre sula" der. Tek tek pompa ve valf komutları bakım içindir ve cihaz kilitlemeye aykırı olanları reddeder.

**"Cihaza erişimi olan"** = Admin ya da cihazın mahaline üye olan kullanıcı. Erişimi olmayan biri için yanıt **404**'tür, 403 değil; böylece başkasının cihazının var olup olmadığı anlaşılmaz.

**Komutlar neden 202 dönüyor?** Komut MQTT'ye iletilir, ama sonucunu cihaz belirler. Cihaz `state` mesajında `lastCommandId` ile yanıt verir. Panel sonucu SignalR'dan `ActuatorStateChanged` olarak görür. Cihaz offline ise backend **409** döner.

## 5. Kimlik doğrulama ve yetki

- `POST /auth/login` → HMAC-SHA256 ile imzalı JWT. Claim'ler: `sub`, `name`, `role` (`Admin`/`User`), `exp`. Süre: 8 saat (yapılandırılabilir).
- **Mahal listesi token'a konmaz.** Erişim her istekte veritabanından kontrol edilir; böylece mahalden çıkarılan kullanıcı token süresi dolmadan da erişimini kaybeder.
- Şifreler `PasswordHasher<User>` ile hash'lenir.
- İlk Admin, başlangıçta yapılandırmadan seed edilir.
- Yetki politikaları: varsayılan olarak **her endpoint giriş ister** (fallback policy); `AdminOnly` yalnızca Admin içindir.
- **Cihaz erişimi tek bir yerde kontrol edilir:** `/devices/{deviceId}/...` altındaki tüm endpoint'ler, route'taki `deviceId`'yi çözen ortak bir endpoint filtresinden geçer (Admin → izin; User → cihazın `site_id`'si üyelikleri arasında mı; değilse 404). Slice'lar bu kontrolü tekrar yazmaz, unutamaz.
- Bir entegrasyon testi uygulamadaki tüm `/devices/{deviceId}` route'larını tarar ve yabancı bir kullanıcı için hepsinin 404 döndüğünü doğrular. Yeni eklenen bir endpoint de otomatik olarak bu testin kapsamına girer.
- **Önceden kayıt:** Admin `POST /devices` ile cihazı mahaliyle birlikte kaydeder. İlk MQTT mesajındaki upsert yalnızca `is_online` ve `last_seen_at`'i günceller; `site_id` ve `name`'e dokunmaz.
- **Mahalsiz cihazlar:** Önceden kaydedilmemiş bir cihaz ilk mesajla otomatik kaydolur, `site_id`'si boştur ve yalnızca Admin görür. Rastgele bir cihaz kimsenin panelinde görünmez.
- **Mahal içi roller sonradan nasıl eklenir:** `site_members`'a `role` kolonu (varsayılan `Member`) eklenir; erişim filtresi "üye mi?" yerine "rolü yeterli mi?" diye bakar. Mevcut veri ve endpoint'ler bozulmaz.
- SignalR: token `access_token` query parametresiyle gelir; `JwtBearerEvents.OnMessageReceived` ile okunur.
- Scalar'da Bearer şeması tanımlı: login'den alınan token girilip korumalı endpoint'ler denenebilir.
- **ESP32 ve simülatör JWT kullanmaz.** Broker'a kendi MQTT kullanıcı adı ve şifresiyle bağlanırlar.
- **Keycloak'a geçiş yolu:** `JwtOptions.Authority` ayarlanır ve `Auth/Login` slice'ı kaldırılır. Diğer endpoint'ler değişmez.

## 6. Gerçek zamanlı sözleşme (SignalR)

Hub: `/hubs/live`. Bağlanan kullanıcı, üyesi olduğu **her mahalin grubuna otomatik** katılır (Admin ayrıca tüm cihazları gören `admins` grubuna). Event'ler cihazın mahal grubuna gönderilir. Üyelik değişirse istemci yeniden bağlanınca yeni gruplar geçerli olur.

| Event | Payload |
|---|---|
| `ReadingReceived` | Okuma DTO'su |
| `ActuatorStateChanged` | `{deviceId, pump, valve, lastCommandId, at}` |
| `DeviceStatusChanged` | `{deviceId, isOnline, at}` |
| `AlertRaised` | Uyarı DTO'su |

Ayrıntılar `docs/realtime.md` dosyasında yazılacak (M4).

## 7. Veri modeli

- **User:** Id, Username, PasswordHash, Role (`Admin`/`User`), CreatedAt
- **Site:** Id, Name, CreatedAt
- **SiteMember:** SiteId, UserId, AddedAt. PK `(SiteId, UserId)`. Rol kolonu şimdilik yok.
- **Device:** Id (string, ör. `node1`), Name, **SiteId?** (null = mahalsiz; mahal silinirse null olur), IsOnline, LastSeenAt
  - Cihaz başka mahale taşınırsa geçmiş verisi cihazla birlikte gider.
- **Reading** (hypertable): DeviceId, ReceivedAt, DeviceTs, LevelPct, LevelCm, TurbidityNtu, Ph, WaterTempC?, Raining, SoilPct, FlowLpm, FlowTotalL, Rssi
  - PK: `(DeviceId, ReceivedAt)`. Ayrı Id yok (bkz. TimescaleDB kullanımı).
- **ActuatorEvent:** Id, DeviceId, At, Actuator (Pump/Valve), Kind (CommandSent/StateReported), Value, Source (Manual/Rule/Device), CommandId?, Reason?, UserId?
- **DeviceSettings:** DeviceId, PhMin, PhMax, TurbidityMax, LevelMinPct, LevelMaxPct, SoilDryPct, SoilWetPct, PumpMaxS, IrrigationCooldownS, UpdatedAt, UpdatedBy
- **Alert:** Id, DeviceId, Type, Severity, Message, RaisedAt, AcknowledgedAt?, AcknowledgedBy?

**Hacim:** 10 saniyede bir okuma, cihaz başına günde yaklaşık 8.6 bin, yılda yaklaşık 3.1 milyon satır.

### TimescaleDB kullanımı

| Konu | Karar |
|---|---|
| Hypertable | **Yalnızca `readings`.** Zaman kolonu `received_at` (`timestamptz`), chunk aralığı 1 gün. Diğer tablolar küçük, normal tablo kalır. |
| Birincil anahtar | Hypertable'da benzersiz indeks zaman kolonunu içermek zorunda. PK: `(device_id, received_at)`. Ayrı `Id` kolonu yok. Aynı cihaz için birebir aynı zaman damgalı ikinci satır `ON CONFLICT DO NOTHING` ile atlanır. **Not:** Zaman damgası sunucunun alış zamanı olduğu için MQTT QoS 1 tekrarları farklı zamanla gelir ve bunu elemez. Bu nadir durum en fazla fazladan bir örnek demek; kabul edildi. |
| Yabancı anahtarlar | Başka tablolardan `readings`'e FK yok. `readings.device_id → devices.id` FK'si var. |
| Geçmiş sorguları | Ham okumalar `/readings` endpoint'inden okunur. `/readings/aggregate` için `1m` ham tablodan `time_bucket` ile, `15m` ve `1h` ise **continuous aggregate**'lerden (`readings_15m`, `readings_1h`) okunur. Her bucket'ta ortalama, min ve max; yağmur için "yağdı mı" (bool_or). |
| Continuous aggregate yenileme | `add_continuous_aggregate_policy`, birkaç dakikada bir. Gerçek zamanlı toplama (real-time aggregation) açık; son dakikalar da sonuçta görünür. |
| Sıkıştırma | 7 günden eski chunk'lar sıkıştırılır (`segmentby = device_id`). |
| Saklama | Ham veri **90 gün**, aggregate'ler **süresiz** tutulur. Uzun vadeli grafikler aggregate'lerden çizilir. Süreler yapılandırmayla değişebilir. |
| EF Core ile ilişki | EF tabloları ve normal şemayı yönetir. Hypertable, continuous aggregate ve politikalar **migration içinde ham SQL** (`migrationBuilder.Sql`) ile kurulur. Aggregate'ler EF'te keyless entity olarak view'a eşlenir. |
| Toplu sorgular | Bucket sorguları LINQ ile değil, `FromSql` / `SqlQuery` ile yazılır (`time_bucket` EF'in bildiği bir fonksiyon değil). |
| CSV dışa aktarımı | Büyük aralıklar için akış (streaming) ile yazılır, bellekte biriktirilmez. |
| Lisans | Sıkıştırma ve continuous aggregate özellikleri Timescale License kapsamında; kendi sunucumuzda kullanım ücretsiz. Docker'da tam sürümü içeren `timescale/timescaledb` imajı kullanılır (Apache-only `-oss` imajı değil). |

`actuator_events` ve `alerts` da zamana bağlı, ama hacimleri küçük. Normal tablo + `(device_id, at)` indeksi yeterli. Gerekirse ileride hypertable'a çevrilebilir.

## 8. Kurallar

Kurallar saf fonksiyon olarak yazılır: `(okuma, ayarlar, son durum, şimdi) → karar`. Testlerin büyük kısmı burada olur.

- **Sulama:** `SoilPct < SoilDryPct` ise `irrigate` komutu gönderilir (`durationS` ayardan gelir). `SoilWetPct` eşiği histerezis sağlar. `IrrigationCooldownS` dolmadan yeni sulama yapılmaz. Şu durumlarda sulama **yapılmaz** ve gerekçesi kaydedilir:
  - depo `LevelMinPct` altında (uyarı üretilir)
  - su kalitesi kötü (aşağıya bakın)
  - yağmur yağıyor (`raining = true`)
- **Su kalitesi:** pH aralık dışında ya da bulanıklık limit üstündeyse `WaterQualityBad` uyarısı üretilir ve otomatik sulama durdurulur. Cihaz da kendi tarafında sulamayı engeller (çift koruma). Kirli su depoya girebildiği için uyarı mesajı kullanıcıyı depoyu kontrol etmeye yönlendirmeli.
- **Cihaz offline:** LWT `offline` gelirse ya da 60 saniye telemetri gelmezse `DeviceOffline` uyarısı üretilir.
- **Uyarı tekrarını bastırma:** Aynı cihaz ve aynı uyarı tipi için açık bir uyarı varsa yeni uyarı açılmaz. Durum düzelince uyarı otomatik kapanır (bu karar M3'te kesinleşecek).

## 9. Cihaz simülatörü

`tools/RainHarvest.DeviceSimulator`: ESP32 gibi davranan bir .NET konsol uygulaması.

- Telemetriyi 10 saniyede bir gönderir. Aralık ayarlanabilir; hızlandırılmış mod vardır.
- Bağlanırken LWT (`offline`) tanımlar, bağlanınca `online` yayınlar.
- `cmd` mesajlarını dinler. `irrigate` geldiğinde valf aç → pompa çalıştır → süre dolunca pompa durdur → valf kapat sırasını uygular. Sulama sırasında depo seviyesi düşer, akış artar, toprak nemi yükselir. Her adımda `state` ile `lastCommandId` yayınlar.
- `config` mesajını dinler ve cihaz tarafı güvenlik kurallarını taklit eder: depo boşsa ya da su kötüyse sulamayı reddeder, kapalı valfe karşı pompa komutunu reddeder.
- **Senaryolar** (`--scenario`): `normal`, `rain` (seviye yükselir), `bad-water` (pH ve bulanıklık bozulur), `drought` (toprak kurur, depo boşalır), `flaky` (bağlantı kopar ve geri gelir)
- Birden fazla cihaz: `--devices node1,node2`
- Hem backend'in uçtan uca testinde hem de panel ekibinin donanımsız geliştirmesinde kullanılır.

## 10. Docker

`docker-compose.yml`:

| Servis | İmaj | Port | Not |
|---|---|---|---|
| `db` | `timescale/timescaledb` (PostgreSQL 17, sabit sürüm etiketiyle) | 5432 | Volume'da kalıcı. Healthcheck: `pg_isready` |
| `mosquitto` | `eclipse-mosquitto:2` | 1883 | Parola dosyalı, anonim erişim kapalı |
| `api` | yerel Dockerfile | 8080 | Profil: `full` |
| `simulator` | yerel Dockerfile | — | Profil: `full` |

- **Günlük geliştirme:** `docker compose up mosquitto` + API'yi IDE'den çalıştırmak (debug için).
- **Tam demo veya panel ekibi için:** `docker compose --profile full up` ile her şey ayağa kalkar.
- Günlük geliştirme artık `docker compose up db mosquitto` ile başlar.
- Migration'lar API başlarken **yalnızca Development'ta** otomatik uygulanır. Diğer ortamlarda `dotnet ef database update` ya da migration bundle ile uygulanır.
- `api` servisi `db` healthcheck'i geçmeden başlamaz (`depends_on: condition: service_healthy`).
- Yerel Mosquitto TLS'siz çalışır (1883). HiveMQ Cloud TLS ile (8883). Fark yalnızca `MqttOptions` ayarında.

## 11. Test stratejisi

| Seviye | Ne | Araç |
|---|---|---|
| Birim | Kurallar, payload doğrulama, rol politikaları, token üretimi | xUnit |
| Entegrasyon | HTTP endpoint'leri + gerçek TimescaleDB + gerçek Mosquitto: mesaj yayınla, API'den oku. Bucket sorguları ve continuous aggregate'ler burada test edilir (EF InMemory ya da SQLite bunları taklit edemez) | WebApplicationFactory + Testcontainers. Konteynerler test koleksiyonu başına bir kez kalkar; her test kendi rastgele cihaz id'sini kullanır, bu yüzden testler arasında temizlik gerekmez |
| Uçtan uca (elle) | Simülatör senaryoları + Scalar | docker compose |

**Hedef:** Kurallar ve MQTT handler'ları kapsamlı test edilir. Basit CRUD endpoint'leri için birer mutlu yol (happy path) testi yeterli.

## 12. Kilometre taşları

Her biri **"bitti" tanımıyla** birlikte:

| | Kapsam | Bitti sayılması için |
|---|---|---|
| **M0** İskelet | Solution, projeler, `IEndpoint`/`IMqttHandler` altyapısı, DbContext + ilk migration (readings hypertable dahil), ProblemDetails, OpenAPI + Scalar, `/health` (DB + MQTT), docker-compose (TimescaleDB + Mosquitto), CI'sız `dotnet test` | `docker compose up db mosquitto` + `dotnet run` → migration uygulanır, `readings` bir hypertable olur, Scalar açılır, `/health` yeşil döner |
| **M1** Telemetri uçtan uca | Contracts, IngestTelemetry, Devices (LWT), latest/history endpoint'leri, **simülatörün `normal` senaryosu** | Simülatör çalışırken `/devices/node1/readings/latest` canlı veri döner. Entegrasyon testi geçer |
| **M2** Auth ve mahaller | Login, me, users, seed admin, Scalar Bearer, sites, üyelikler, cihaz atama ve önceden kayıt (`POST /devices`), M1 endpoint'lerine cihaz erişim filtresi, simülatörsüz test verisi | Token'sız 401; User, Admin endpoint'inde 403. A kullanıcısı yalnızca kendi mahalinin cihazlarını görür, B'nin cihazı A'ya 404 döner; mahalsiz cihazı yalnızca Admin görür. Önceden kaydedilen cihaz ilk MQTT mesajından sonra doğrudan mahalinde görünür. Tüm `/devices/{deviceId}` route'larını tarayan test geçer |
| **M3** Komutlar, ayarlar, kurallar | SendCommand (202/409), state işleme, settings + retained config, sulama ve kalite kuralları, simülatörün cmd/config desteği ve senaryoları | `drought` senaryosunda backend pompayı otomatik çalıştırır. Kural birim testleri geçer |
| **M4** Uyarılar ve canlı veri | Alerts, tekrar bastırma, SignalR hub, `docs/realtime.md` | `bad-water` senaryosunda uyarı kaydedilir, `AlertRaised` SignalR event'i görülür ve uyarı `/alerts`'te listelenir |
| **M5** Analiz ve teslim | CSV export, `full` docker profili, README (kurulum, panel ekibi için rehber) | Panel ekibi yalnızca README ile sistemi kendi makinesinde ayağa kaldırabilir |

**Panel ekibiyle eşgüdüm:** M0 sonunda OpenAPI taslağı paylaşılır. M1'den itibaren arkadaşın simülatörle gerçek veri görebilir.

## 13. Riskler

| Risk | Etki | Önlem |
|---|---|---|
| ESP32 payload'ı plandan farklı çıkar | Ingest kırılır | `docs/mqtt.md` firmware ekibiyle erken onaylanır. Doğrulama hatası loglanır, mesaj atlanır, servis düşmez |
| Kural salınımı (pompa sürekli açılıp kapanır) | Donanım yıpranır | Histerezis + cooldown + cihaz tarafında maksimum çalışma süresi |
| Uyarı seli | Panel ve uyarı listesi gereksiz kayıtla dolar | Açık uyarı varken tekrar açılmaz |
| MQTT kopması | Veri kaybı | Otomatik yeniden bağlanma, `/health`'te görünür. Kalıcı oturum (clean session = false) M3'te değerlendirilir |
| HiveMQ ile yerel Mosquitto arasındaki farklar (TLS, kotalar) | Demo günü sürpriz | M3 sonunda bir kez HiveMQ'ya karşı uçtan uca deneme |
| EF Core Timescale nesnelerini bilmiyor | Migration'lar hypertable'ı bozabilir, bucket sorguları LINQ'e uymaz | Timescale nesneleri yalnızca ham SQL migration'larında; bucket sorguları `FromSql` ile. Entegrasyon testi `readings`'in hypertable olduğunu doğrular |
| Ekip üyelerinin ortam kurulumu | Herkese PostgreSQL kurmak zahmetli | Veritabanı yalnızca docker-compose ile çalışır; yerel kurulum gerekmez |
| Yeni bir endpoint'te erişim kontrolünün unutulması | Başka kullanıcının verisi sızar | Kontrol tek bir ortak filtrede; tüm `/devices/{deviceId}` route'larını tarayan test |
| MQTT'de cihaz taklidi: her istemci her `rainwater/<id>/...` topic'ine yazabiliyor | Bir cihaz başkasının adına veri gönderebilir | **Bilinçli olarak kabul edildi** (okul projesi). Gerekirse: her cihaza id'siyle birlikte özel şifre, MQTT kullanıcı adı = cihaz id'si, broker ACL'i `rainwater/%u/#`. Backend tarafında değişiklik gerekmez |
| JWT anahtarının sızması | Yetkisiz erişim | Anahtar user-secrets ve ortam değişkeninde tutulur, en az 256 bit |

## 14. Açık noktalar

- Firmware ekibiyle MQTT payload'ının onaylanması (özellikle `lastCommandId`, `config` alanları ve `irrigate` komutu)
- Firmware'in sulama sırasını ve pompa/valf kilitlemesini uyguladığının teyidi
- Uyarıların otomatik kapanıp kapanmayacağı (M3)
- Yeniden bağlanmada kalıcı MQTT oturumu kullanılıp kullanılmayacağı (M3)
- Cihaz id'si fabrikada mı yüklenecek, MAC'ten mi türetilecek (firmware ekibi). MAC seçilirse `esp_efuse_mac_get_default` kullanılmalı, bayt sırası kutudaki etiketle aynı olmalı

## 15. Mevcut durum

**M2 tamamlandı** (2026-11-22):
- JWT girişi (`/auth/login`, `/auth/me`), kullanıcılar, mahaller, üyelikler, cihaz önceden kaydı ve mahal ataması.
- Her endpoint varsayılan olarak giriş istiyor. Açık olanlar: `/auth/login`, `/health`, `/system/info`, Development'ta OpenAPI ve Scalar.
- Cihaz erişimi tek bir grup filtresinde: handler'ı `deviceId` parametresi alan her endpoint otomatik korunuyor. Filtre kapatılınca testlerin kırmızıya döndüğü elle doğrulandı.
- İlk Admin `Seed:AdminUsername` / `Seed:AdminPassword` ayarından oluşturuluyor (Development: `admin` / `admin-dev-password`). Herhangi bir Admin varken tekrar oluşturulmuyor.
- Kullanıcı adları büyük-küçük harf duyarsız; bilinmeyen kullanıcı ile yanlış şifre aynı süre ve aynı 401 ile yanıtlanıyor.
- Cihaz adı `PUT /devices/{id}/name` ile değiştirilebiliyor (Admin); otomatik kaydolan cihazlar da böylece adlandırılıyor.

**M1 tamamlandı** (2026-11-22):
- MQTT'den telemetri ve cihaz durumu alınıyor. Bilinmeyen cihazlar ilk mesajda otomatik kaydediliyor (ad = id).
- Bozuk JSON, eksik alan ve fiziksel aralık dışı değerler loglanıp atlanıyor; servis durmuyor.
- Cihaz zaman damgası 2024 öncesiyse (NTP senkronize değilse) yok sayılıyor.
- Endpoint'ler: `/devices`, `/devices/{id}`, `/readings/latest`, `/readings` (sayfalı), `/readings/aggregate`.
- `readings_15m` ve `readings_1h` continuous aggregate'leri, 7 günde columnstore sıkıştırması ve 90 günlük saklama politikası kuruldu. Migration geri alınıp yeniden uygulanarak test edildi.
- Simülatör (`normal` senaryosu): her cihazın kendi bağlantısı, retained "offline" LWT'si ve hızlandırılabilir saati var. Ctrl+C ve SIGTERM'de "offline" yayınlayıp temiz kapanıyor.
- MQTT sözleşmesi [docs/mqtt.md](docs/mqtt.md) dosyasında yazıldı.

**M0 iskeleti yazıldı** (2026-11-22):
- .NET 10'da kalındı (LTS). .NET 11 paketleri yüklenmesin diye EF Core, OpenAPI ve test paketleri 10.0.x'e sabitlendi; `global.json` SDK 10'u seçer.
- Şimdilik yalnızca `devices` ve `readings` tabloları var. Diğer tablolar ait oldukları kilometre taşında eklenecek.
- Kolon ve tablo isimleri `EFCore.NamingConventions` ile snake_case.

**Komutlar:**

```bash
docker compose up -d db mosquitto
dotnet run --project src/RainHarvest.Api          # http://localhost:5255/scalar
dotnet run --project tools/RainHarvest.DeviceSimulator -- --devices node1,node2 --interval 2 --speed 60
dotnet test                                        # entegrasyon testleri Docker ister
```
