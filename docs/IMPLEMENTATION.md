# Implementacja 0.2 — mapa kodu

To stan źródeł z 10.09.2026. Docelowe pomysły z planu produktu nie oznaczają funkcji obecnego prototypu.

| Warstwa | Pliki | Zaimplementowane |
|---|---|---|
| Model i symulacja | `apps/game/Assets/Town/Domain/World.cs` | Wersjonowany snapshot, walidacja, 12 typów, drogi BFS, koszty, rodziny, usługi, cele, undo |
| Lokalny zapis | `Domain/LocalRepository.cs` | Gzip + SHA-256, atomic replace, poprzedni plik, kolejność zapisów, trwałe żądanie uploadu, import jako kopia |
| Gra i UI | `Runtime/TownApp.cs` | Sterowanie, panele, lokalne światy, zegar, autosave, chmura, dźwięk, jakość, zdjęcia |
| Wizualizacja | `Runtime/TownView.cs` | Bryły, dachy, drzewa, siatka, współdzielone materiały, podgląd, wybór, maks. 48 widocznych mieszkańców |
| Sync klient | `Runtime/CloudSync.cs` | Identity Platform REST, odświeżanie tokenu, retry, trwale zapisany upload, CAS, historia i kopie |
| Apple | `Runtime/AppleServices.cs`, `Plugins/iOS/TownApple.mm` | Haptics, Keychain, thermal/Low Power Mode, systemowe okna plików i udostępniania |
| Przygotowanie/build | `Assets/Editor/TownBuild.cs`, `TownAppleBuild.cs` | Generowanie sceny/URP, Metal/IL2CPP/ARM64, konfiguracja klienta, pluginy i frameworki Xcode |
| Serwer domenowy | `apps/sync-api/src/core.ts` | Własność świata, walidacja gzip/JSON, rewizje, CAS, idempotencja, limity, restore, tombstone |
| Serwer HTTP | `src/http.ts`, `main.ts` | API v1, bounded body, logi bez treści zapisów/tokenów, health, bezpieczny start |
| GCP adaptery | `src/google.ts` | Admin ID token + revocation, Firestore transactions, GCS create-only i odczyt generacji |
| Test storage | `src/memory.ts` | Izolowane adaptery do testów; brak trybu pamięciowego w produkcyjnym entry poincie |
| Infrastruktura | `infra/bootstrap`, `infra/environment` | Istniejący projekt GCP, konta i uprawnienia, dane, API, buildy, alarmy i OIDC |

## Zachowanie i granice

- Świat startowy 32×32. Format akceptuje 16–128 pól w każdym wymiarze, maks. 4096 obiektów i 512 rodzin.
  To limity walidacji, a nie obietnica wydajności maksymalnej mapy na iPhonie.
- Symulacja 5 ticków na sekundę; ekonomia i potrzeby co 50 ticków. Wszystkie wartości ekonomii są całkowite.
  Seed steruje wyborem nazw; losowe UUID nie służą do deterministycznego odtwarzania poleceń.
- Jedno pole na budynek. Drogi i dostępność są uproszczone. Nie ma fizyki pojazdów, korków, wnętrz ani sterowania pojedynczym Simem.
- Gra nie nalicza nieobecności. Edycja i otwarcie panelu zatrzymują symulację; po zamknięciu panelu wraca poprzedni stan pauzy.
- Snapshot zawiera cały świat; serwer nie wykonuje symulacji ani nie weryfikuje ekonomii jak system antycheat.
- Lokalny plik jest zapisany po worldId i niesie ownerUid, gdy został przypisany do konta. Lokalna gra nie wymaga logowania.
  Przełączenie kont nie ukrywa lokalnych światów na wspólnym urządzeniu, ale blokuje upload cudzego świata bez utworzenia kopii.
- Synchronizacja nie jest multiplayerem. Każde urządzenie gra lokalnie. Serwer uzgadnia rewizje i zachowuje konflikty.
- Zakończenie uploadu starszej generacji nie oznacza zapisania późniejszych lokalnych zmian.
  Wczytanie chmury zachowuje kopię brudnego lokalnego świata, również gdy otwarty był inny świat.
- Rewizje historyczne otwierają się w kliencie jako nowy świat. API obsługuje również przywrócenie jako nową rewizję tego samego świata.
- Klient nie archiwizuje jeszcze świata w UI; endpoint DELETE jest zaimplementowany i testowany.
- Autosave lokalny co 30 s oraz po edycji; sync co 120 s podczas aktywności. Przy zawieszeniu zapis lokalny bez czekania na sieć.
- iPhone: Core Haptics; Mac: wspierany trackpad, bez sterowania jego fizyczną intensywnością przez API Apple.
  Suwak na Macu wyłącza efekt przy zerze, ale nie zapewnia stopniowania siły tak jak iPhone.
- Fallback bez natywnego Keychain przechowuje token tylko w pamięci sesji. Dane logowania nie trafiają do PlayerPrefs.
- Tryb oszczędzania/thermal serious obniża render scale, dystans cieni i limit do 30 FPS. Powrót po 20 s dobrego stanu.
- Zdjęcia renderuje URP SingleCameraRequest; systemowy share sheet nie wymaga zapisu do biblioteki zdjęć.
- Unity generuje scenę, materiały, pełne ustawienia projektu i package lock przy pierwszym przygotowaniu.
  Projekt źródłowy nie zawiera zmyślonego lockfile ani eksportu Xcode.

## Co trzeba zrobić na prawdziwych urządzeniach

1. Import i kompilacja w przypiętym Unity. Wygenerowanie Town.unity i sprawdzenie Play.
2. Natywny build Mac oraz iOS: IL2CPP, linkowanie frameworków, podpis, uruchomienie po kablu.
3. Sprawdzenie dotyku, klawiatury logowania, safe area, obrotu telefonu, share/import i Core Haptics.
4. Profiling po 20–30 minutach na iPhonie 16 i Macu M2: frame time, alokacje, temperatura, bateria.
5. Wdrożenie dev GCP: prawdziwe IAM, wyłączony/revoked użytkownik, koszty, Firestore Rules, równoczesne zapisy obu urządzeń.
6. Zamknięcie aplikacji podczas zapisu/uploadu, utrata sieci, wylogowanie, konflikt oraz odzyskanie świata z historii.

## Kolejny zakres produktu

Najpierw test z Agą i poprawa czytelności budowania. Następnie docelowe UI, lepsze animacje,
profile rodzin i ich prośby, dekorowanie, art direction, dostępność oraz audio współistniejące z muzyką.
Przed szerszym wydaniem: migracje formatów, polityka retencji, GC osieroconych plików, kopie metadata/PITR,
usuwanie kont, diagnostyka błędów i pomiary na sprzęcie. Te elementy nie są przedstawiane jako wykonane.

## Przypięte elementy i źródła

Unity 6000.3.23f1, rewizja 09d2ecc7fb28; URP 17.3.0; firebase-admin 14.3.0; Node >=22.
Backend ma `package-lock.json`. Override uuid 11.1.1 usuwa podatność starszej zależności;
sprawdzone użycia zależne to CJS `v4`. Import adapterów GCP i testy przeszły po zmianie.
Terraform constraint i lockfile znajdują się w `infra/environment`.

Źródła użyte do doboru API:

- [Unity Editor releases API](https://services.api.unity.com/unity/editor/release/v1/releases?version=6000.3&limit=1)
- [Universal Render Pipeline 17.3](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.3/manual/index.html)
- [URP SingleCameraRequest](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.3/api/UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest.html)
- [RenderPipeline.SubmitRenderRequest](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.RenderPipeline.SubmitRenderRequest.html)
- [Apple Core Haptics](https://developer.apple.com/documentation/corehaptics)
- [Firebase Admin ID token verification](https://firebase.google.com/docs/auth/admin/verify-id-tokens)
