# G7 Pro Xbox Wireless

A separate, open-source Windows app that helps convert a **supported standard GameSir G7 Pro receiver** using the official **Wuchang dongle 5.12 firmware**. This is independent community software, not a GameSir or Microsoft product.

**Hardware result:** one standard receiver on firmware **1.46** was backed up, converted to Wuchang **5.12**, and tested successfully for wireless input on **Xbox Series S**. The standard controller reconnected after a receiver cold boot; **its firmware was not changed**. Other receiver revisions, Xbox models, audio features, and long-term compatibility are unverified.

**[Download v0.1.1 for Windows x64](https://github.com/MowTC/gamesir-g7-pro-xbox-wireless/releases/download/v0.1.1/G7ProXboxWireless-0.1.1-win-x64.zip)** · [Releases and checksums](https://github.com/MowTC/gamesir-g7-pro-xbox-wireless/releases/tag/v0.1.1) · [How the conversion works](docs/PROTOCOL.md)

This is a **preview**. The conversion method was tested on real hardware using the research implementation. The packaged app has automated tests and offline verification against the actual vendor package; a full conversion through this new interface has not been repeated on a stock receiver. Firmware conversion can make a receiver unusable. The required backup captures SPI flash, but restoration has not been tested and no restore operation is included.

## Supported configuration

| Component | Accepted in this release |
|---|---|
| Host | Windows 10/11 x64, .NET Framework 4.8 |
| Receiver update mode | USB `4C4A:2342`, BR23 `UBOOT1.00` |
| Flash | 512 KiB SPI NOR, JEDEC `856013` |
| Original receiver firmware | `JS_3103_DG`, **1.46**, validated flash header at `0x1000` |
| Conversion package | Original Wuchang **dongle 5.12 PID Protected** updater, exact SHA-256 below |
| Controller | Existing standard G7 Pro, already paired wirelessly; no controller firmware change |

An unsupported receiver may be readable, but conversion stays blocked. This app is **not** a controller pairing utility. If you need to pair a replacement controller first, use the separate [G7 Pro Pairing project](https://github.com/MowTC/gamesir-g7-pro-pairing).

## Use the app

1. Extract the Windows ZIP and open **G7ProXboxWireless.exe**. Use **Restart as administrator** before making a backup; Windows asks for elevation. No installer or driver replacement is involved.
2. Use **Get official firmware** to open the [Wuchang package hosted by GameSir](https://drive.google.com/file/d/1jHdA1OJnj-lCQrfHqujVzdLEVh6yPcp-/view), linked from [GameSir's support manual](https://gamesir.com/support/manuals/gamesir-g7prozzz). Download and extract the archive. Select **G7 Pro WFF Dongle 5.12 Flash Tool - PID Protected.exe**, not the controller updater. The app rejects other files and versions.
3. Close GameSir software. Connect **only the receiver you intend to convert** to the PC. Connect the controller wirelessly in 2.4 GHz mode, with its USB cable disconnected. Hold **Xbox + Menu (≡)** to put the receiver into update mode.
4. Select **Scan receiver**, then **Back up receiver**. On first use, the app downloads an 11 KiB reader from a pinned upstream GitHub commit and verifies its SHA-256. It runs that reader in RAM and reads **all 512 KiB twice**, including the reserved final region. Both dumps are flushed to disk and must match byte for byte before conversion is unlocked. Keep a separate, private copy of the backup folder.
5. **Keep this app open.** Unplug the receiver for at least three seconds, reconnect to the **same USB port**, let the controller reconnect, then hold **Xbox + Menu** again. This clears the temporary backup reader so the vendor updater can detect the receiver. The app must observe the disconnect and return on the saved USB connection.
6. Read the conversion acknowledgment and select **Open conversion updater**. The app rechecks the saved backup files and the receiver, builds a private copy of the verified vendor updater, and launches it. It changes **only product matching** in the configuration and regenerates the original-format integrity records. **Both UFW firmware payloads remain byte-identical.** Whole-chip erase, authentication-code rewriting, and automatic upgrade remain off.
7. In the vendor window, choose **Install Firmware**. **Do not unplug during writing.** Wait for green **Upgrade SUCCESS**, receiver product `JS_3103_U_DG`, firmware **5.12**, and checksum `4938-FD1F1A2B` (Key variant) or `A7D9-AA06E6C9` (No-Key variant). A closed updater or a full progress bar alone does not mean success.
8. Replug the receiver and test wireless input on the PC. Move it to the Xbox and test dashboard navigation and A **without a controller USB cable**. Power-cycle the controller and console to check reconnection. The Windows name “Xbox Gaming Device” alone does not establish console compatibility.

If the original updater reports a PID mismatch, you opened the unprepared original file. Launch the conversion updater through this app after completing the backup and replug steps. Do not enable whole-chip erase as a troubleshooting step.

If you close the app between steps, use **Load verified backup…** to reopen that receiver's `manifest.json`. Both complete dump files are rechecked, and another observed replug in the same port is required. The USB path is a connection check, not a unique cryptographic identity: keep only the intended receiver attached and do not substitute another device after backup.

## What is public, and what stays local

This repository and its release ZIP contain **our source, app, documentation, and license notices**. They do not contain GameSir executables, firmware images, firmware captures, provisioning material, or anyone's device backup. You supply the original official updater locally. The prepared vendor executable is generated locally and is not published by this project.

Local files live under `%LOCALAPPDATA%\G7ProXboxWireless\`:

- `backups/`: two full flash dumps and a JSON manifest with SHA-256, firmware identity, and USB connection information.
- `cache/`: the hash-pinned RAM reader.
- `prepared/`: the locally prepared vendor updater and a text preparation record.
- `activity.log`: operation status and local backup paths.

**Keep these files private.** Dumps can contain device-specific data. Do not attach them to public issues. There is no telemetry or upload feature. The app opens GitHub/GameSir links on request and downloads the pinned reader from `raw.githubusercontent.com` when needed; those sites receive ordinary network request metadata. Backup image data is never sent to them by the app.

The SPI backup excludes chip OTP/eFuses and separate chips. It is a verified capture, **not a proven recovery procedure**. The app does not offer flashing arbitrary raw images or restoration. If an update fails, preserve the logs and original backup, stop repeated flashing, and report the non-sensitive device/firmware versions and exact error.

## Build and test

On Windows x64 with .NET Framework 4.8:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Package
```

No NuGet packages, Python runtime, vendor firmware, or network access are needed to build. The build runs **40 offline checks** for CRCs, container integrity, bounds, flash-command rejection, stock-firmware identification, and backup verification. `bin/` contains the app and test executable; `dist/` contains the portable app ZIP, source ZIP, and checksums. `G7ProXboxWireless.csproj` also supports MSBuild with the .NET Framework 4.8 targeting pack.

For the optional real-package offline integration check, supply a legally obtained original updater and an output file **outside the repository**:

```powershell
.\bin\CoreTests.exe 'C:\firmware\G7 Pro WFF Dongle 5.12 Flash Tool - PID Protected.exe' 'C:\firmware\prepared-test.exe'
```

That performs three additional checks against the real package and prepares a test copy without launching it or sending hardware commands. Vendor firmware is deliberately absent from CI fixtures.

## Auditing and fingerprints

- [FirmwarePackage.cs](src/FirmwarePackage.cs): exact package allowlist, bounded decompression, embedded-file CRCs, configuration change, unchanged UFW hashes, and integrity-record regeneration.
- [ReceiverBackup.cs](src/ReceiverBackup.cs): Windows device discovery, SCSI transport, backup-only command allowlist, two-pass readback, firmware-header validation, and manifest validation.
- [MainWindow.cs](src/MainWindow.cs): downloads, local file operations, replug gating, and explicit vendor-updater launch. Closing an updater is never treated as successful installation.
- [Protocol notes](docs/PROTOCOL.md): command formats and limits of the observed results.

Original official updater SHA-256:

```text
5040fe4c38311fc7971ff390a331d1b38b9ecd537075619c396278e7be381eb1
```

RAM reader SHA-256:

```text
7384429e33ee1f340fbffe46aa928a0099c163e1374a40f6f9f27468a6f16ddf
```

Our code is MIT licensed. Protocol research and header decoding draw on kagaimiq's MIT-licensed tools; see [third-party notices](THIRD_PARTY_NOTICES.md). GameSir's firmware and executable are not covered by our MIT license. GameSir and Xbox names identify compatible products; no endorsement is implied.
