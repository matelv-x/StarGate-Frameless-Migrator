# StarGate / Frameless Migrator

Jeden instalator migracyjny dla starszych instalacji **StarGateWebView** i **Framelesswindow** na Windows.

**[Pobierz instalator EXE — v0.1.0](https://github.com/matelv-x/StarGate-Frameless-Migrator/releases/tag/v0.1.0)** · [Pełna instrukcja po polsku](docs/Instrukcja-migracji.txt) · [English](README.en.md)

## Jak użyć

1. Pobierz `StarGate-Frameless-Migrator.exe` z Releases. Zapisz go poza katalogiem starego programu, np. w Pobranych.
2. Zamknij stary program i jego launcher. Uruchom migrator na tym samym koncie Windows, na którym korzystasz z programu.
3. Wybierz wykrytą instalację. Gdy zainstalowane są oba programy, wybierz, który chcesz zaktualizować. Jeśli nie ma go na liście, kliknij **Wskaż stary folder…**.
4. Kliknij **Pobierz i migruj**. Program pobierze aktualny instalator z właściwego repozytorium GitHub i sprawdzi jego rozmiar oraz SHA-256.
5. Dokończ standardowy kreator nowej wersji. Wybierz nowy, pusty katalog. Nie wybieraj folderu kopii `.legacy-…`.

Jedna operacja aktualizuje jeden wybrany program. Aby przenieść drugi, uruchom migrator ponownie.

## Co dzieje się ze starą wersją i adresami

- Rozpoznany stary katalog jest **przenoszony do folderu obok z końcówką `.legacy-…`**, a nie automatycznie kasowany.
- Pliki JSON zapisanych adresów i profili są zabezpieczane w `%LOCALAPPDATA%\StarGate-Frameless-Migration\[sesja]\settings`; oryginały pozostają na miejscu.
- Starszy pojedynczy adres Frameless jest importowany do obecnego formatu `Profiles`, bez nadpisywania istniejącego profilu.
- Po potwierdzeniu nowej instalacji migrowane są skróty pulpitu i autostartu prowadzące do wybranej starej wersji. Ich kopie również są zachowywane.
- Po anulowaniu lub błędzie program próbuje przywrócić stary katalog, jeśli jego poprzednia ścieżka jest wolna. Przy konflikcie zachowuje kopię i podaje jej lokalizację.

Szczegóły odzyskiwania, uprawnień i obsługiwanych lokalizacji są w [instrukcji](docs/Instrukcja-migracji.txt).

## Wymagania i dostęp GitHub

- Windows x64; środowisko .NET jest zawarte w EXE. Na Windows ARM64 wymagane jest uruchamianie aplikacji x64 przez emulację; pobierana nowa wersja również jest x64.
- Internet do pobrania nowego wydania.
- Repozytorium migratora jest publiczne. **Framelesswindow pozostaje prywatne** — jego aktualizacja wymaga dostępu przez Git Credential Manager. Migrator nie zawiera tokenów ani haseł i nie zapisuje poświadczeń. Sama sesja w przeglądarce nie zastępuje dostępu Git.
- Nie uruchamiaj migratora na innym koncie administratora: ma ono inne `%LOCALAPPDATA%`.

## Status wydania i testów

To pierwsze wydanie testowe **v0.1.0**. Testy na sztucznych katalogach obejmują wykrywanie obu aplikacji, niestandardowe lokalizacje, zachowanie i import adresów, konflikty profili, odrzucanie katalogów źródłowych i niewłaściwego pakietu oraz ograniczenia adresów pobierania.

Sprawdzono odczyt manifestów obu wydań z GitHuba. **Nie wykonano pełnej migracji rzeczywistej starej instalacji.** Folder kopii jest zachowywany, aby umożliwić odzyskanie danych.

## Kompilacja i testy

Wymagany .NET SDK 8 lub nowszy na Windows:

```powershell
dotnet publish src/Migration.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o artifacts
& ./artifacts/StarGate-Frameless-Migrator.exe --self-test
Get-Content ./artifacts/self-test-results.txt
```

`--self-test` używa wyłącznie sztucznych katalogów tymczasowych. Nie migruje zainstalowanych programów. Opcjonalne `--check-releases` sprawdza metadane wydań z GitHuba bez instalacji; nie jest uruchamiane w publicznym CI, ponieważ Framelesswindow wymaga prywatnego dostępu.

## Powiązane programy

- [StarGateWebView](https://github.com/matelv-x/StarGateWebView)
- [Framelesswindow](https://github.com/matelv-x/Framelesswindow) — dostęp do repozytorium prywatnego wymagany

