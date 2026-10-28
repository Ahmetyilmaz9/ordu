# Ordu — Online Bölük Savaşı Oyunu

> Bu dosya projenin tasarım ve mimari hafızasıdır. Claude Code bunu okuyarak
> projeye sıfırdan dahil olabilmeli. Kararlar değiştikçe bu dosya güncellenmeli.

**Motor:** Unity 6 LTS · **Render:** URP · **Dil:** C#
**Durum:** M8a tamamlandı (2026-10-28). Sıradaki: M8b — görsel geçiş.

---

## 1. Oyun Nedir

İki oyuncunun eşleşip ordularıyla gerçek zamanlı savaştığı online strateji oyunu.
Total War tarzı muharebe hissi, ama kampanya yok — sadece sahra savaşı.

**Dönem:** Osmanlı, 1500–1550 (Çaldıran / Mohaç penceresi).
Bu pencere mekanik olarak seçildi: ok ve tüfek aynı anda sahada, süvari hâlâ
belirleyici ama ölümlü, topçu sahra savaşına girmiş durumda. Daha erken dönem
sıradan bir ortaçağ oyunu olur, daha geç dönemde süvari anlamsızlaşır.

**Kritik kural:** Oyuncu tek tek askere komut vermez. Komut birimi **bölüktür**.
Bölük bir bütün olarak hareket eder, döner, formasyon değiştirir ve savaşır.
Bu kural tüm mimarinin temelidir — değişirse her şey yeniden tasarlanır.

---

## 2. Kesinleşmiş Tasarım Kararları

### Ordu kurma
- Oyuncu başına **10 slot**, maç öncesi doldurulur
- Her slot = **tek birim tipi**, sabit asker sayısı (karışık bölük yok)
- Tip başına slot limiti ile denge (puan bütçesi değil — v1 için basitlik)
- İki oyuncu da **aynı havuzdan** seçer (simetrik). Fraksiyonlar v2'de.
- **Ordu gizli:** rakip senin ordunu sadece görüş alanına giren bölükler kadar bilir.
  Keşif (hızlı sipahiyle öne çıkmak) gerçek bir hamle olur. Sis sistemi bunu bedavaya sağlar.

### Maç akışı ve zafer
- **Hazırlık ~2,5 dk** (savaş süresinden ayrı): ordu kurma ~90 sn + yerleştirme ~60 sn.
  İki oyuncu da hazırsa süre beklenmeden geçilir.
- **Yerleştirme fazı:** her oyuncu sadece kendi bölgesine dizer, rakibin dizilişini görmez.
- **Savaş 15 dk.**
- **Zafer:** rakibin sahada savaşabilir bölüğü kalmayınca kazanırsın. İmha olan ya da
  kaçarken harita kenarından çıkan bölük sayılmaz. Kaçan ama hâlâ haritada olan bölük
  sayılır (toparlanma şansı var).
- **Süre dolarsa:** sahada kalan (kaçmayan) asker sayısı çok olan kazanır.
- Harita boyutu: bir uçtan öbürüne yürüyüş ≈ 2 dk hedefi → harita = bölük hızı × süre.
  Somut değer M1'de hız ölçülünce hesaplanır.

### Birim seti

| Tip | Slot başı | Rol |
|---|---|---|
| Azap / piyade | 200 | Ucuz hat tutucu, dayanıklı, vurucu gücü düşük |
| Kargılı | 180 | Süvariye duvar, tüfekliyi korur |
| Tüfekli yeniçeri | 100 | Yıkıcı salvo, uzun doldurma, yakın dövüşte çöker |
| Okçu | 120 | Sürekli ateş, doldurma boşluğu yok, zırha karşı zayıf |
| Sipahi (süvari) | 50 | Şarj darbesi + manevra, kargıya karşı intihar |
| Topçu | 8–10 top | Uzun menzil, sabit, kurulum gerektirir |

Sayılar başlangıç değeri, dengelemeye açık. **Worst case: 10 × 200 × 2 = 4000 asker.**
Render bütçesi bu sayı üzerine kurulacak.

Kapalı karşı-taş döngüsü: kargı → süvari → okçu/topçu → piyade → kargı.
Her birimin hem bir avı hem bir avcısı var. Bu döngü bozulmamalı.

### Ana savaş ritmi: doldurma penceresi
Tüfekli bölük ateş ettikten sonra **15–20 saniye savunmasızdır**. Oyunun merkezi
kararı bu: salvoyu erken mi kullanırsın, temasa kadar mı beklersin? Rakip ateş
ettiği anda süvari gönderilir. Bu yüzden oyuncular bölüklerini kademeli ateşe
geçirmek zorunda kalır.

Bu mekanik "şarj butonu"nun yerine geçiyor — Total War'dan kasıtlı ayrışma noktası.

### Şarj = hızın sonucu (ayrı buton yok)
- Bölüğe "koş" emri verilir. **İlk temas hasarı ve moral etkisi temas anındaki hıza göre**
  ölçeklenir. Sipahi en hızlısı olduğu için en sert vuranı.
- Koşmak **yorgunluk** harcar (bölük başına tek float; yürürken/dururken dolar).
  Yorgun bölük zayıf çarpar. Doğru oyun: yürüyerek yaklaş, son 30–40 m koş.
- Kargı hattına **önden** çarpan süvari şarj hasarını kendisi yer → döngü kendiliğinden çıkar.
- Yeni kaynak/UI yok; sunucu temas hızını zaten biliyor.

Okçu/tüfekli ayrımı: okçu **sürekli ama zayıf**, tüfekli **aralıklı ama yıkıcı**.
Aynı rolün iki risk profili.

### Moral sistemi
Total War hissinin asıl kaynağı kayıp değil, bölüklerin kırılıp kaçmasıdır.

Moral kaynakları: alınan kayıp oranı · kanattan/arkadan saldırı · yakındaki dost
bölüğün kaçması · **sancaktarın ölmesi** · yorgunluk · tüfek salvosu (barut kuralı).

Akış: normal → sarsılma (daha zayıf dövüşür) → **rout** (bölük kontrolden çıkar,
en yakın harita kenarına kaçar) → toparlanma şansı.

**Toparlanma:**
- Peşinde kimse yoksa moral yavaş yavaş dolar
- Yakında morali yüksek, savaşan dost bölük varsa daha hızlı dolar
- Eşik geçilince bölük durur, toparlanır, kontrol oyuncuya döner
- **Her toparlanmadan sonra moral tavanı düşer** → ikinci kaçış birinciden kolay,
  sonsuz kaç-dön döngüsü olmaz

**Sancaktar (komutan yerine):** her bölükte ön safın ortasındaki slot. Ölürse bölük
moral şoku yer, ön saftan biri sancağı devralır (şok tek seferlik). Bit maskesinde
sadece özel anlamlı bir indeks — ek sistem yok. Ordu seviyesinde general yok (v1).

**Barut kuralı:** Ateşli silahlar orantısız moral hasarı verir. Tüfek salvosu 30
kayıp verir ama 60 kayıp kadar moral kırar. Tüfek, kayıp tablosunda değil kaçan
bölüklerde kazanır.

### Formasyon
- Hat / kolon / kare / kama — oyuncu komutu
- **Geçiş anlık değil, 4–5 saniye sürer.** Bu süre boyunca bölük yavaş ve savunmasız.
  "Yanlış zamanda formasyon değiştirdim" bir hata olarak var olmalı.
- **Dönüş hızı formasyondan türetilir:**
  `maks dönüş (rad/sn) = asker koşu hızı ÷ merkezden en uzak slotun mesafesi`.
  Uçtaki asker asla koşu hızını aşmaz. Geniş hat yavaş döner (kanattan yakalanma riski),
  kolon çeviktir. Değer elle girilmez; formasyon genişliği + `UnitDefinition` hızından gelir.
- Askerler slota ışınlanmaz, sınırlı hızla **takip eder** (sadece istemci görseli).

### Savaş çözümü (bölük seviyesinde)
Tek tek düello simüle edilmez. Sunucu bölük-vs-bölük çözer:
- **Temas cephesi:** çakışan genişlik kaç askerin gerçekten dövüştüğünü belirler.
  30 genişliğindeki bölük, 10 genişliğindekiyle temas ederse sadece ~10 asker savaşır.
- **Kayıp oranı** = f(saldırı, savunma, temas cephesi, arazi, kuşatma açısı)
- **Kanat/arka çarpanı:** saldıranın, savunanın yön vektörüne göre açısı.
  Taktik derinliğin yarısı burada.

### Savaş sisi
Var. **Sunucu, oyuncunun göremediği bölüklerin verisini o istemciye hiç göndermemeli.**
Paketi gönderip istemcide gizlemek = paket sniffer ile maphack. Bu tür oyunlarda
en yaygın hile budur.

### Tekil asker ölümü
Rastgele asker öldürme YOK. Sunucu her bölük için bir **"hayatta" bit maskesi**
tutar (200 asker ≈ 25 byte). Ölen asker indeksi **hasarın geldiği yöne göre** seçilir:
cepheden dövüşüyorsa ön saflardan, kanattan saldırı yiyorsa o kanattaki slotlardan.

Ağdan sadece indeks geçer (2 byte/ölüm). Bu detay oyunun **okunabilirliğini** belirler:
ön saf eriyorsa "cephem çöküyor", kanat eriyorsa "kuşatılıyorum". Rastgele ölümde
hiçbir şey anlaşılmaz.

Sis notu: bölük görüş alanına geri girdiğinde sunucu bit maskesini bir kez tam
gönderir, sonra tekrar delta.

### Görsel yön
**Minyon tarzı stilize askerler** — gerçekçi değil, çocuksu/blob. Gerekçeler:
- Düşük kemik sayısı (15–20 vs 60–80); skinning maliyeti doğrudan buna bağlı
- **Ragdoll yok** — 4000 ragdoll motoru öldürür. Canned düşme / "poof" ölüm.
- Kan/decal yok → decal maliyeti + yaş derecelendirmesi birden çözülür
- Flat/toon shading, PBR yok
- Üretim maliyeti: gerçekçi asker + zırh varyantları + mocap aylar alır

**En önemli gerekçe:** stratejik kamera mesafesinden gerçekçi askerler gri lapaya
dönüşür. Basit siluet + güçlü renk kodu uzaktan *okunur*. Kargılı mavi ve dikdörtgen,
okçu sarı ve ince, süvari kırmızı ve uzun. Stilize burada sadece ucuz değil, **daha iyi**.

Uyarı: stilize ≠ otomatik ucuz. Pürüzsüz blob yüzeyler fazlalaşmayı önlemek için
beklenenden çok üçgen ister; outline shader bedava değil.

### Barut dumanı
En büyük görsel fırsat ve tek ciddi perf riski. 100 tüfeklinin aynı anda ateşlemesi,
dikkatsiz yapılırsa 4000 askerin tamamından pahalıya patlar (particle overdraw).

**Kural: asker başına duman YOK. Bölük seviyesinde tek bir duman hacmi.**
Gerçek bir salvo da zaten tek bulut üretir. Düşük çözünürlüklü, seyrek, opak-ish
kartlar — yüzlerce yarı saydam katman değil.

Yan fayda: duman ateş eden bölüğün kendi görüşünü kapatır (mekanik) ve arkasındaki
askerleri çizmeye gerek kalmaz (optimizasyon). Doğru yazılırsa duman perf *kazandırır*.

---

## 3. Mimari (Unity)

### Replikasyon sınırı — projenin temel fikri

```
SUNUCU (ağda replike edilen)          İSTEMCİ (replike EDİLMEYEN)
─────────────────────────────         ──────────────────────────
Bölük entity × 20                     4000 asker
  pozisyon, yön                         slot pozisyonu (hesaplanır)
  formasyon tipi/genişliği               animasyon, LOD
  kalan asker, moral                     ölüm efekti
  durum, hedef                           yerel çarpışma/kaçınma
  hayatta bit maskesi
```

**Askerler ağda var olmaz.** Pozisyonları her istemcide bölüğün transform'u +
formasyon parametrelerinden *hesaplanır*. Bu yüzden 100 asker de 500 asker de
ağ maliyeti açısından aynıdır. ~20 networked entity, ~50 byte × 20 Hz.

Bu sayede deterministic lockstep yazma zorunluluğu yok — normal sunucu-otoriter
model yeterli.

### Netcode seçimi

**Netcode for GameObjects (NGO) 2.x + Unity Transport.**
Kurulu: NGO 2.13.3, Transport 2.7.4, Multiplayer Play Mode 2.0.2. (NGO 3.x Unity 6.7 istiyor, 6.3 LTS ile çalışmaz.)

Gerekçe: UE'nin replikasyon modeline en yakın paradigma (`NetworkBehaviour`,
`NetworkVariable`, `ServerRpc`/`ClientRpc`), Unity resmi, Relay/Lobby ile aynı
ekosistemde. 20 entity için fazlasıyla yeterli.

Savaş sisi için kritik: `NetworkObject.CheckObjectVisibility` ve
`NetworkShow`/`NetworkHide` ile per-client görünürlük. Maphack koruması buradan gelir.

Alternatifler ve neden seçilmedi:
- **Mirror** — olgun ve ücretsiz, NGO'ya çok yakın. Geçerli bir B planı.
- **Photon Quantum** — deterministic ECS, RTS için teknik olarak mükemmel, ama
  ücretli ve tamamen farklı paradigma. v1 için aşırı.

> Unity'de UE'nin yerleşik replikasyonu yok. UE'de bedava gelen şey (bReplicates,
> otomatik transform senkronu) burada paket kurularak elde edilir. M0'ın varlık
> sebebi bu.

### Sunucu modeli: dedicated, host modu YOK

**Karar:** M0'dan itibaren sunucu ayrı bir süreç (Dedicated Server build, geliştirmede
lokal headless). İki istemci ona bağlanır. `StartHost()` hiç kullanılmaz.

Neden: Relay+host modelinde oyunculardan biri sunucudur — düşmanın tüm verisi onun
RAM'inde, sis o oyuncuyu hiç korumaz (bellekten maphack). Üstüne host'un emirleri 0 ms,
misafirinki ping kadar gecikir; 1v1 rekabette adaletsiz.

**Barındırma kararı (2026-10-28):** Unity'nin Multiplay barındırması **Mart 2026'da kapandı**
(Lobby/Relay/Matchmaker/NGO çalışmaya devam ediyor). Şimdilik **IP ile bağlanma** (oyun içi
kutu, `-ip`); genel teste geçerken **Edgegap** (yönetilen, kullandıkça öde; Linux + Docker,
NGO rehberi var; eşleştirme ~22 $/ay ya da UGS Matchmaker). B planı: kendi VPS (Hetzner ~4–5 €/ay)
+ maç başına sunucu süreci açan küçük başlatıcı. Bir maç sunucusu ~%6 çekirdek, ~1 KB/sn/istemci.
Dedicated sunucuda Relay gereksiz.

### Render stratejisi
- **URP** (HDRP değil) — stilize tarza uygun, düşük sistem gereksinimi hedefiyle uyumlu
- Askerler: **VAT (Vertex Animation Texture)** + `Graphics.RenderMeshIndirect` /
  `RenderMeshInstanced`. 4000 animasyonlu asker için pratikte tek yol.
  Animasyon çeşitliliği sınırlı olacak (yürü, koş, saldır, öl, ateş et — 6-8 klip).
- **Askerler GameObject DEĞİL — M1'den itibaren.** Her bölük askerlerinin pozisyonunu
  bir dizide tutar (slot matematiği doldurur), çizim bölük başına tek
  `Graphics.RenderMeshInstanced` çağrısı. M1'de mesh küre; M8'de sadece mesh+shader
  değişir (VAT), dizi/slot/ölüm mantığı aynen kalır.
- LOD: yakın plandaki ~30 asker gerçek SkinnedMeshRenderer olabilir, gerisi VAT

### Bölük etiketi (okunabilirlik)
Her bölüğün üstünde kameraya dönük etiket, milestone'larla büyür:
- **M1:** tip ikonu + asker sayısı, takım rengi
- **M4:** moral çubuğu + durum (normal / sarsılmış / kaçıyor — kaçarken yanıp söner)
- **M5:** doldurma halkası — **rakibin tüfeklisinde de görünür** (görüş alanındaysa).
  "Ateş etti, şimdi süvari gönder" kararı buradan okunur.

Etiket sadece istemcinin zaten aldığı veriyi gösterir; görünmeyen bölüğün etiketi yok.
- DOTS/Entities **gerekli değil** — bölük seviyesinde zaten 20 entity var, asker
  tarafı saf instancing. Gerekirse sonra eklenir.

### Pathfinding
Bölük başına **tek yol**, asker başına değil. Unity NavMesh + `NavMeshAgent`
(bölük merkezi için) yeterli. Flow field gerekmiyor. Askerler sadece kendi slot
pozisyonlarını takip eder + minimal yerel kaçınma.

### Slot matematiği (projenin kalbi)

Hiçbir asker pozisyonu ağdan gelmez, hepsi buradan çıkar:

```csharp
public Vector3 GetSlotLocalOffset(int slotIndex)
{
    int rank = slotIndex / formationWidth;   // 0 = ön saf
    int file = slotIndex % formationWidth;   // 0 = sol kanat
    int rankCount = Mathf.CeilToInt((float)soldierCount / formationWidth);

    // Unity'de +Z ileri, +X sağa
    float z = -(rank - (rankCount - 1) * 0.5f) * slotSpacing;
    float x =  (file - (formationWidth - 1) * 0.5f) * slotSpacing;
    return new Vector3(x, 0f, z);
}

public Vector3 GetSlotWorldPosition(int slotIndex)
    => transform.TransformPoint(GetSlotLocalOffset(slotIndex));
```

> UE'den taşınırken dikkat: UE'de ileri +X, Unity'de +Z.

### Veri yapısı
Birim istatistikleri **hardcode edilmeyecek.** ScriptableObject (`UnitDefinition`)
olarak tutulacak: `unitId`, `factionId`, asker sayısı, saldırı, savunma, moral,
menzil, doldurma süresi, hız, mesh, animasyon seti.

Bölük kodu asla `if (unitType == Sipahi)` yazmamalı — hep tanımdan okumalı.

**Fraksiyon kavramı baştan var olsun**, şimdilik iki oyuncu da `Faction_Default`'a
işaret etsin. Maliyeti bugün bir gün, sonra bir ay.

---

## 4. Henüz Karar Verilmedi

- Kuşatma savaşı olacak mı, yoksa sadece sahra mı
- Tip başına slot limitlerinin tam değerleri
- Harita boyutunun somut değeri (yöntem belli, bkz. "Maç akışı" — M1'de ölçülür)
- **Tabur cengi** — *M4 playtest'inden sonra karar.* v1 hali: yerleştirme fazında
  konan hareketsiz engel hattı, süvari şarjını durdurur, arkasındaki tüfekliyi korur.
  Topçu sistemi gerektirmez. Risk: fazla güçlüyse herkes saklanır, oyun pasifleşir.
- **Mehter** — *M4 sonrası.* 10 slottan birini yiyen destek birimi: savaşmaz, moral
  aurası verir, toparlanmayı hızlandırır; düşerse tüm ordu moral kaybeder.

### v1'e GİRMEYECEKLER
Replay/izleyici (mimari sayesinde sonra kolay) · ranked/ELO · hava durumu/gün-gece ·
2v2 ve üstü · topçu (M9+) · ordu seviyesinde general

---

## 5. Yol Haritası

**Altın kural 1: networking'i sonraya bırakma.** Singleplayer yapıp sonra multiplayer
eklemek, bu tür oyunlarda "baştan yazmak" demektir.

**Altın kural 2: görselle başlama.** İlk milestone'larda askerler küre/kapsül,
bölükler renkli dikdörtgen. Asker modeli aramaya başlamak en yaygın tuzak.

### M0 — Ağ boru hattı ✅
Unity 6 + URP projesi, NGO kurulumu, Dedicated Server build target,
**Multiplayer Play Mode** paketi (editörde 1 sunucu + 2 istemci tek Play ile).
İki istemci **dedicated sunucuya** bağlanıyor ve sunucudaki bir küpün hareketini görüyor.
Host modu yok. Başka hiçbir şey yok.

> **Yapıldı:** `NetworkBootstrap` rolü kendisi seçer (dedicated build / `-server` → sunucu;
> editörde MPPM ana editör = sunucu, sanal oyuncular `-name Player2..4` → istemci).
> Sahne ve build'ler elle değil menüden: **Ordu → Savaş Sahnesini Kur**, **Ordu → Build → ...**
> M0 test küpü M1'de kaldırıldı.
> **Gerçek build testi (2026-10-28):** `OrduServer.exe -bot` + `Ordu.exe` ile tam maç oynandı.
> Sunucu headless'ta 60 FPS ile sınırlı (sınırsızken bir çekirdeği yer; böyle ~%6).
> İstemci başka makineye `Ordu.exe -ip <sunucu IP>` ile bağlanır (UnityTransport argümanı
> kendisi okur; denenmedi). Uyarı: build alırken MPPM sanal oyuncuları kapalı olsun —
> açıkken istemci build'i shader derlemesinde kilitlendi.

### M1 — Bölük hareketi ✅
`Battalion : NetworkBehaviour` — replike transform, sol tık seç / sağ tık hareket
ettir (ServerRpc), slot matematiği, debug küreleri (`RenderMeshInstanced`, GameObject
değil), formasyondan türeyen dönüş hızı, bölük etiketi (ikon + sayı).
**Başarı kriteri:** iki pencerede de 100 küre formasyonu koruyarak ilerliyor;
istemciden verilen emir sunucuya ulaşıp sonuç geri replike oluyor.

> **Yapıldı ve test edildi.** Dosyalar: `Formation` (slot matematiği + dönüş
> hızı, saf statik), `Battalion` (ağ + sunucu simülasyonu, `MoveRpc` sahiplik kontrolü),
> `BattalionView` (instanced küreler + OnGUI etiket), `CommandInput` (seç/emir),
> `BattalionSpawner` (bağlanan oyuncuya 2 bölük), `UnitDefinition` (`Test_Piyade`, 100 asker).
> Kestirmeler: düz çizgi hareket (NavMesh engel gelince), etikette ikon yerine metin.
> Testler: **Ordu → Test → Slot Matematiği**.

> Seçim istemci tarafı bir kavramdır, replike edilmez. Sunucuya sadece emir gider.
> Simülasyon SADECE sunucuda koşmalı — `IsServer` kontrolü unutulursa sessiz
> desync olur ve bulması işkencedir.

### M2 — Formasyon ✅
Formasyon tipleri, süreli geçiş, sağ tıkı sürükleyerek hem konum hem yön verme.

> **Yapıldı ve test edildi.** Kontroller (M8a'da değişti, bkz. orası): Hat/Kolon/Kare/Kama ·
> **sağ tık** git (varışta yön değişmez) · **sağ tık sürükle** hat çiz: konum + yön +
> genişlik (çoklu seçimde çizgi bölüklere eşit bölünür). Şekil/genişlik değişince
> `formationChangeTime` (4,5 sn) boyunca hız × `transitionSpeedFactor` (0,4); bitiş
> zamanı sunucu saatiyle replike (`TransitionEndTime`). Görsel geçiş bedava: askerler
> yeni slotlarına yürür. Kestirme: kare dolu (içi boş kare M3'te).

### M3 — Temas ve savaş ✅
Bölük-vs-bölük çarpışma çözümü, temas cephesi, kanat çarpanı, tekil asker ölümü
(yöne göre indeks seçimi + bit maskesi senkronu), hıza bağlı şarj + yorgunluk.
**Basit bot:** sunucuda, oyuncuyla aynı emir yolundan "en yakın düşmana yürü-saldır".

> **Yapıldı ve test edildi.** `Combat/`: `AliveMask` (256 bit), `CombatMath` (saf:
> dikdörtgen teması, vurulan kenar, temas cephesi, yöne göre ölüm, kanat/arka çarpanı),
> `CombatSystem` (sunucu: temas → kayıp/sn = cephedeki asker × lethality × saldırı/savunma ×
> yorgunluk × kenar çarpanı × geçiş cezası; temasın ilk karesinde şarj darbesi = asker × hız ×
> `chargeImpact`, kargı önden `antiChargeFront` oranında geri yansıtır), `BotCommander`.
> Temasta düşmanın içine yürünemez, geri çekilmek serbest. Kontroller: **R** koş/yürü.
> Bot: **Ordu → Bota Karşı Oyna** (build'de `-bot`). Testler: **Ordu → Test → Savaş Matematiği**.
> Dokümandan sapma: ölümde indeks deltası değil, maskenin tamamı (32 byte) replike edilir —
> sis/geç bağlanma aynı yoldan çözülür; bant genişliği sorun olursa deltaya geçilir.
> Kestirme: kare görselde dolu ama savaşta her yön cephe.

### M4 — Moral ✅
Sarsılma, rout, toparlanma (düşen moral tavanı), sancaktar, zafer koşulu + 15 dk süre,
etikette moral çubuğu. **M4 bittiğinde oyun oynanabilir demektir.**
Sonrasında: grafiksiz bot-vs-bot denge testi (yüzlerce maç, `UnitDefinition` ayarı),
Tabur cengi ve Mehter kararları.

> **Yapıldı ve test edildi.** `Combat/Morale` (saf kurallar + sabitler),
> `Combat/MatchManager` (sahneye yerleşik `Match` objesi: saat + zafer, `MatchRules` saf).
> Moral 0–100: kayıp (bölüğün tamamı = 150), kanat 4/sn, arka 8/sn (kare ve cephe 0),
> yakında dost kaçarsa −15, sancaktar ilk düşüşte −25, yorgunken savaşta −2/sn;
> `discipline` kayıpları böler. <40 sarsıldı (saldırı ×0,75), <10 kaçış (emir yok, saldırı 0,
> en yakın kenara koşar, kenarda sahadan çıkar). Kaçarken 25 m içinde düşman yoksa +4/sn
> (+4 yakında sağlam dost), 45'te toparlanır. Her kaçış tavanı 25 düşürür → üçüncü kaçış kalıcı.
> Sancaktar = ön safın ortası, büyük çizilir; ölünce ön safın ortasındaki devralır.
> Saat iki takım sahaya çıkınca başlar (`matchDurationSeconds`, sahnede `Match` objesinde).

### M5 — Menzilli birimler ✅
Okçu ve tüfekli. Doldurma penceresi mekaniği, etikette doldurma halkası.
> **Okları replike etme.** Sunucu salvoyu bölük seviyesinde çözer ("hedef bölük
> 14 kayıp verdi, cepheden"), istemci tamamen kozmetik ok spawn eder. Ok fiziği
> ve çarpışması istemcide yok.

> **Yapıldı ve test edildi.** Birim tipleri artık `UnitRoster` asset'inde
> (Piyade, Okçu, Tüfekli); bölük ağda sadece `UnitIndex` taşır. Sunucu (`CombatSystem.TryFire`):
> duran, ateşi serbest, dolu, yakın dövüşte olmayan atıcı önündeki 60° içinde menzildeki en yakın
> düşmana salvo atar → öldürme = atıcı × `killsPerShot` × mesafe düşüşü (menzil sonunda %40) ×
> kenar çarpanı; ölüm yöne göre, tüfekte moral kaybı × `firearmMoraleMultiplier` (2). Doldurma
> bitişi sunucu saatiyle replike (`ReloadEndTime`). Okçu: 70 m, 3 sn, hepsi atar, zayıf.
> Tüfekli: 50 m, 18 sn, 2 saf, yıkıcı, yakında zayıf. Kontrol: **F** ateşi tut/serbest.
> Etikette doldurma çubuğu (rakipte de görünür). Kestirmeler: halka yerine çubuk; salvo
> efekti çizgi (`VolleyRpc` herkese gider — M7'de görenlere sınırlanacak); zırh yok;
> hedef seçme yok (en yakın), odak ateşi sonra.

### M5.5 — Kargılı ve sipahi ✅ *(yol haritasında boşluktu, M6'dan önce eklendi)*
Sadece veri: `Kargili` (180, savunma 14, `antiChargeFront` 0,7 → önden şarjın %70'i
saldırana döner) ve `Sipahi` (50, koşu 9 m/sn, `chargeImpact` 0,15, seyrek dizilim).
Mekanikler M3'ten hazırdı. Her oyuncu 5 bölükle doğar (aralık 38 m, kargılı en dışta).
> **Yapıldı ve test edildi:** sipahi okçuyu ezer, kargılıya önden çarpan sipahi erir.

### M6 — Ordu kurma ekranı + eşleştirme ✅
Hazırlık akışı: ordu kurma (~90 sn) → gizli yerleştirme (~60 sn) → savaş.
10 slot, birim seçimi, Lobby ile eşleştirme, dedicated sunucu barındırma kararı.

> **M6a — yapıldı ve test edildi.** `MatchManager` faz akışı:
> Waiting → ArmySelect (90 sn) → Deploy (60 sn) → Battle (15 dk) → Ended; iki taraf hazırsa süre
> beklenmez, süre biterse varsayılan ordu. `ArmyRules` (saf): 10 slot, tip başına `maxSlots`
> (şimdilik hepsi 3), sunucu orduyu doğrular. Yerleştirme: bölükler sadece kendi takımına
> görünür (`CheckObjectVisibility`, sunucu rakibe göndermez), savaşta FogSystem görüşe göre açar (M7);
> emirler bölge içinde anında konumlar, dizilim geçişi süresiz. Savaş/bot sadece Battle fazında.
> `MatchUI` (IMGUI): ordu paneli, yerde bölge çizgisi, Hazır, saat, sonuç. Kestirmeler: 3. oyuncu
> izleyici, kopan oyuncunun takımı boşalmaz (yeniden bağlanma M6b), IMGUI arayüz.
> **M6b — yapıldı ve test edildi** (build'de kutudan 127.0.0.1 ile bağlanıldı). Karar: şimdilik IP ile bağlanma (bkz. "Sunucu modeli").
> İstemci: editörde ve `-ip` ile kendiliğinden bağlanır, yoksa IP kutusu (IP ya da IP:port,
> son adres hatırlanır); bağlantı kurulamazsa/koparsa kutu geri gelir. **Ordu → Build →
> Dedicated Server (Linux)**. Dışarıdan bağlanmak için sunucuda 7777/UDP açık olmalı.
> Tuzak (çözüldü): `BuildPlayer` aktif platformu değiştirir; Linux sunucu build'inden sonra MPPM
> sanal oyuncuları sunucu platformunda açılıp 7777 için çakıştı. Build menüsü artık eski platforma
> döner, editörde rol tespiti `UNITY_SERVER`'a bakmaz. Linux build Unity'ye toolchain paketleri ekledi.

### M7 — Savaş sisi ✅
Per-client görünürlük (`NetworkShow` / `NetworkHide`).

> **Yapıldı ve test edildi.** `Match/FogSystem` (sunucu, 0,2 sn'de bir): takım, kendi
> bölüklerinden birinin `sightRange`'indeki (varsayılan 60 m, sipahi 90 m) düşmanı görür; gizlemek
> için 10 m fazlası gerekir (sınırda titreme olmasın). Görülmeyen bölük o oyuncuya `NetworkHide`
> ile hiç gönderilmez; yeniden görününce NGO baştan doğurur (konum, maske, moral tam gelir).
> `CheckObjectVisibility` da aynı kuralı söyler (NGO, kural false iken `NetworkShow`'u reddeder).
> Görmediğine ateş yok (`FogSystem.TeamSees`), salvo efekti sadece atanı görenlere, bot da sise
> uyar (kimseyi görmezse rakip tarafa ilerler). Maç bitince herkes her şeyi görür.
> Not: okçu menzili (70) varsayılan görüşten (60) uzun — tam menzil için başka bölük görmeli.

### M8a — Total War tarzı kontroller ✅ *(kullanıcı isteğiyle eklendi, M8b'den önce)*
> **Yapıldı ve test edildi.**
> - **Kamera** (`RtsCamera`): WASD/oklar/ekran kenarı kaydırır, Q/E ya da orta tuş döndürür,
>   tekerlek yakınlaştırır; kırmızı takım kendi arkasından başlar.
> - **Seçim** (`CommandInput`): sol tık seç (Shift ekle/çıkar), sol sürükle kutu, çift tık ekrandaki
>   aynı tip, Ctrl+A hepsi, **Ctrl+0–9** grup ata, **0–9** grubu seç (iki kez: kamera gider).
> - **Emir:** sağ tık zemine git · **düşmana sağ tık = saldır** (sunucu `AttackRpc`: hedefi takip eder,
>   sis/ölüm olunca durur; atıcı menzilin %85'inde durup döner ve **önce hedefe ateş eder** — odak ateşi)
>   · sağ sürükle hat çiz + **hayalet önizleme** (varış slotları) · **Backspace** dur (`HaltRpc`).
> - **Formasyon tuşları Z/X/C/V** (rakamlar artık grup) · R koş · F ateş.
> - **Birim kartları** (`UnitCards`): altta kendi bölüklerin, grup no, sayı, durum, moral/doldurma;
>   tıkla seç, çift tıkla kamera. Kartların üstündeki tık sahaya geçmez.
> - Bot artık `AttackRpc` kullanır.

### M8b — Görsel geçiş
VAT pipeline, minyon modelleri, duman, LOD.

### M9+ — Topçu
**Ayrı bir sistem, v1'e girmemeli.** Sabit konum, kurulum süresi, minimum menzil,
balistik yay, yeniden doldurma, dost ateşi, ayrı mürettebat modeli. Diğer dört
formasyon birimiyle hiç kod paylaşmıyor. Savaş sistemi oturmadan buna girilirse
hiçbir sistem bitmez.

> Topçu mermileri okların aksine **gerçek ve replike** olmalı — oyuncu nişan alıyor,
> düşüş noktasını görmesi gerek, ve sayı az (2 slot × 10 top).

---

## 6. Çalışma Şekli

- Türkçe, doğrudan ve resmi olmayan dil
- Adım adım ilerle, her adımda **çalışan bir şey** çıksın
- Her milestone sonunda dur, test edilsin, sonra devam
- Manuel/tekrarlayan işten kaçın — editör UI'ından 50 değer girmek yerine script yaz
- Scope creep'e karşı sert ol: "bunu da ekleyelim" gelirse hangi milestone'a ait
  olduğunu sor
