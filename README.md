# Karobar-App-Windows — Native Windows Desktop ERP (`.NET 9` + `WPF-UI`)

100% Native Windows Desktop ERP (`Karobar.exe`) built with **WPF on .NET 9**, **C# 13**, **EF Core 9 (SQLite WAL)**, **Dapper**, **QuestPDF**, and **NSec Ed25519 Hardware Licensing** — with **zero Electron, zero Chromium, and zero bundled PHP servers**.

---

## Quick Start on Your Windows VM

1. Clone this repository on your Windows VM:
   ```powershell
   git clone https://github.com/shzdasim/Karobar-App-Windows.git C:\dev\Karobar-App-Windows
   cd C:\dev\Karobar-App-Windows
   ```
2. Run the 1-click PowerShell runner (automatically checks `.NET 9 SDK`, runs `git pull`, builds the solution, executes the `xUnit` test suite, and launches the native WPF app):
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\win-pull-and-run.ps1
   ```
3. To publish a standalone, zero-dependency `Karobar.exe` binary:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\win-publish.ps1
   ```

---

## Phase 1 Features Ready for Testing

- **All 58 Domain Entities / 56 SQLite WAL Tables**: Mapped 1:1 with exact snake_case table and column names so legacy Karobar backups import natively.
- **Automated Seeding (`KarobarDatabaseInitializer`)**: Seeds all **145 Spatie Permissions**, 7 default Roles, Default Super Admin (`admin@karobar.app` / `password`), `WALK-IN-CUSTOMER`, 3 Storage Locations (`SHOP-COUNTER`, `MAIN-GODOWN`, `COLD-FRIDGE`), 14 Chart of Accounts (`1010`–`5300`), 5 Tax Rates, 11 Expense Categories, Themes, and Barcode Templates.
- **One-Click Legacy Karobar Importer (`LegacyKarobarImporterService`)**:
  - Drag & drop or browse for **Full Backups (`.zip`)**, **Database Dumps (`.sql.gz` / `.sql`)**, **Raw SQLite files (`.sqlite` / `.db`)**, or **Settings (`.json`)**.
  - Or connect directly to a **Local MySQL / MariaDB (Laragon / XAMPP)** database (`127.0.0.1:3306`) in 1 click.
  - Preserves your local Windows machine's `KRBL.` hardware license across imports and rebuilds Customer & Supplier running ledgers automatically.
- **Hardware-Bound Offline Licensing (`LicenseService`)**:
  - Computes the exact `XXXX-XXXX-XXXX-XXXX` Windows Machine ID (`MachineGuid` + WMI UUID + BaseBoard + CPU) and verifies `KRBL.` Ed25519 signatures offline.
