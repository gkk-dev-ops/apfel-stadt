# Nasze Miasteczko — prototyp 0.2

Kod osobistej gry dla Agi: Unity, Mac M2 i iPhone 16. Stan: 10.09.2026.

**W pakiecie jest implementacja gry, klienta synchronizacji, API i infrastruktury.**
To źródła prototypu, jeszcze bez wykonanego buildu Unity/Xcode i testu na sprzęcie Apple.
Czysta domena C# oraz backend TypeScript przeszły testy opisane w [VALIDATION.md](docs/VALIDATION.md).

## Uruchomienie na Macu

Zainstaluj przez Unity Hub **6000.3.23f1** z modułami macOS IL2CPP i iOS oraz Xcode.
Dodaj istniejący folder `apps/game` w Hub — nie twórz nowego projektu w jego miejsce.
Projekt przypina URP **17.3.0**. Pierwszy import pobierze pakiety; wygenerowany `packages-lock.json`
i pozostałe ProjectSettings zapisz później w git. Zamknij Editor przed użyciem skryptów.

```bash
cp .env.example .env.local
# Ustaw własny BUNDLE_ID; ścieżka UNITY_EDITOR jest już wpisana dla wskazanej wersji.
# APPLE_TEAM_ID jest potrzebny do podpisania aplikacji na iPhone.
set -a
source .env.local
set +a
./scripts/unity.sh prepare
./scripts/unity.sh macos
# Otwórz Town.app ze ścieżki wypisanej po udanym buildzie.
```

`prepare` tworzy scenę `Assets/Scenes/Town.unity`, materiały i konfigurację URP z kodu.
Zachowuje istniejącą scenę Town. Można też otworzyć projekt, wybrać **Town → Prepare playable project**,
otworzyć tę scenę i nacisnąć Play. Funkcje Apple są aktywne w natywnym buildzie; w Editorze
brakujący mostek nie blokuje gry, a token sesji pozostaje tylko w pamięci.

Build Mac automatycznie kompiluje mostek Objective-C++ do ARM64. iOS:

```bash
./scripts/unity.sh ios
./scripts/ios-device.sh list
./scripts/ios-device.sh build-and-install DEVICE_UDID
```

Xcode musi mieć zalogowane konto, a iPhone włączony Developer Mode i zaufanie do Maca.
Szczegóły, konfiguracja chmury oraz ograniczenia Personal Team: [BUILD-AND-DEPLOY.md](docs/BUILD-AND-DEPLOY.md).

## Co zawiera prototyp

- Miasto 32×32, 12 rodzajów budynków i dekoracji z proceduralną geometrią.
- Budowanie, obracanie, przenoszenie, burzenie z częściowym zwrotem i cofanie na pauzie.
- Drogi połączone z wjazdem na zachodniej krawędzi, rodziny, dostępność usług, zadowolenie i ekonomia.
- Dziesięć celów z nagrodami, tryb swobodnego budowania, pauza i tempo 1×/2×/4×.
- Kamera na dotyk/mysz, animowani mieszkańcy, polski interfejs i proste generowane dźwięki.
- Wiele lokalnych światów, autosave, kopia bezpieczeństwa, import/eksport `.town`, zdjęcie miasta.
- Core Haptics na iPhonie, haptyka trackpada na Macu, regulacja siły, Keychain, odczyt temperatury i Low Power Mode.
- Klient logowania i synchronizacji: chmura, historia, ponowienia i zachowanie obu wersji przy konflikcie.
- API TypeScript z prawdziwymi adapterami Identity Platform, Firestore i GCS; Dockerfile i Cloud Build.
- Terraform: stan, IAM, auth, baza, pliki, Artifact Registry, Cloud Run, alarmy, opcjonalne GitHub OIDC.
- Skrypty buildów, eksport Xcode, instalacja po kablu, CI i testy.

Grafika jest robocza, interfejs używa IMGUI, a rodziny mają proste potrzeby. To baza do pierwszej
rozgrywki i oceny pomysłu; dekorowanie wnętrz, rozbudowane osobowości, fabuła i docelowa oprawa
pozostają następnymi etapami. Parametry 30/60 FPS są ustawieniami, **nie wynikiem benchmarku**.

## Jak grać i synchronizować

Wybierz obiekt na dole i dotknij wolnego pola. Edycja zatrzymuje czas; **Graj** go wznawia.
W trybie wskazywania wybierz dom, aby zobaczyć jego rodzinę. Budynki usługowe wymagają dostępu
do połączonej drogi. Na Macu: przeciąganie, kółko zoom, Q/E obrót kamery, R obrót budynku,
spacja pauza, ⌘Z cofanie. Po pauzie nie ma kar za nieobecność ani postępu podczas zamknięcia gry.

Bez ustawień API działa lokalnie. Po wdrożeniu GCP ustaw `API_BASE_URL`, `AUTH_API_KEY`,
`AUTH_PROJECT_ID` w `.env.local` i przebuduj oba klienty. Zaloguj się tym samym kontem testowym.
Panel **Chmura** pozwala wysłać świat i wczytać go na drugim urządzeniu. Auto-upload działa co
120 sekund aktywnej gry; przejście na drugi sprzęt warto poprzedzić ręcznym wysłaniem.
Nowszy świat wczytuje się jawnie z panelu. Konflikt wymaga wyboru; żadna wersja nie jest automatycznie scalana.

## Testy i dokumentacja

```bash
# Node 22+, .NET SDK 8, Python 3; opcjonalnie Terraform 1.9.8.
npm ci --prefix apps/sync-api --ignore-scripts
./scripts/check.sh
```

- [IMPLEMENTATION.md](docs/IMPLEMENTATION.md) — mapa kodu, zakres wykonania i brakujące testy.
- [SYNC-PROTOCOL.md](docs/SYNC-PROTOCOL.md) — dokładny protokół obecnej implementacji.
- [BUILD-AND-DEPLOY.md](docs/BUILD-AND-DEPLOY.md) — pełna procedura Apple i GCP.
- [VALIDATION.md](docs/VALIDATION.md) — wykonane sprawdzenia i ograniczenia środowiska.
- [PLAN-APLIKACJI.md](docs/PLAN-APLIKACJI.md) — docelowy plan produktu i rozwoju.

Nie wdrożono zasobów GCP ani nie wykonano podpisywania Apple. Dane kont i projektów uzupełniasz lokalnie.
