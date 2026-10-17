# StarGate / Frameless Migrator

A single Windows migration installer for older **StarGateWebView** and **Framelesswindow** installations.

[Download v0.1.0](https://github.com/matelv-x/StarGate-Frameless-Migrator/releases/tag/v0.1.0) · [Full migration instructions](docs/Migration-guide.txt)

## Usage

1. Download `StarGate-Frameless-Migrator.exe` from Releases and save it outside the old application folder.
2. Close the old application and its launcher. Run the migrator under the Windows account that used the old application.
3. Select the detected installation. If both applications are installed, choose which one to migrate. Use the folder selection button if automatic detection does not find it.
4. Start migration and complete the new application's installation wizard, choosing a new empty folder.

The migrator downloads the latest published installer from the selected application's GitHub repository, validates the update manifest, size and SHA-256, backs up address settings, and moves the old installation to a sibling `.legacy-…` folder. It imports older Frameless address settings into the current profile format without overwriting existing profiles. After confirming the installed application hash, it updates matching desktop and startup shortcuts.

The old installation backup is retained. On cancellation or error, the migrator attempts to restore it when the original location is free. Recovery information and settings backups are saved under `%LOCALAPPDATA%\StarGate-Frameless-Migration`.

## Requirements and scope

Windows x64, internet access, and access to the relevant GitHub repository. The executable includes .NET. On Windows ARM64 it requires x64 emulation and downloads the current x64 application release.

This migrator repository is public. **Framelesswindow remains private** and requires an authorized Git Credential Manager account. No credentials are embedded in this project or release. Browser sign-in alone may not provide repository access.

**v0.1.0 is a prerelease.** Fixture-based tests passed, and live release metadata access was checked for both applications. Full migration of a real legacy installation has not been tested.

## Build and test

On Windows with .NET SDK 8 or newer:

```powershell
dotnet publish src/Migration.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o artifacts
& ./artifacts/StarGate-Frameless-Migrator.exe --self-test
Get-Content ./artifacts/self-test-results.txt
```

The self-test uses temporary fixtures only. The optional `--check-releases` switch checks release metadata without installing; it is excluded from public CI because Framelesswindow requires private repository access.


