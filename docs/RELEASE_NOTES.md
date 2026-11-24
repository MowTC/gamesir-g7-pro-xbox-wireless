## Stock receiver firmware 1.47 support

**G7 Pro Xbox Wireless v0.1.2** now accepts standard `JS_3103_DG` receivers on stock firmware **1.46 or 1.47** for conversion using the same pinned official Wuchang dongle **5.12** package.

Thank you to **[@baptisteba](https://github.com/baptisteba)** for testing and documenting a successful **1.47 → 5.12** conversion on **Xbox Series X** in [issue #1](https://github.com/MowTC/gamesir-g7-pro-xbox-wireless/issues/1). Their unchanged standard controller reconnected without re-pairing, and they report wireless operation after power-cycling both the controller and console. Their test used v0.1.1 built from source with only the stock-version allowlist extended to include 1.47. The original project hardware test covered 1.46 on Xbox Series S. The maintainer has not independently repeated the 1.47 hardware test.

### Changes

- Add **1.47** to the explicit stock-version allowlist. Untested versions, including **1.53**, remain blocked.
- Retain the existing product, flash-layout, JEDEC, exact official-package hash, two-pass backup, and observed-replug checks.
- Add regression checks for 1.47 acceptance, 1.53 rejection, wrong-product rejection on 1.47, and loading a verified 1.47 backup.
- Update the app's compatibility notice and documentation with credit for the community hardware report.

**Validation:** 44 offline checks, plus three optional integration checks against the real official updater. No hardware is flashed by these tests.

Download **G7ProXboxWireless-0.1.2-win-x64.zip**, extract it, and follow the included README. Requires Windows 10/11 x64 and .NET Framework 4.8; backup requires administrator access. The executable is unsigned. Source and SHA-256 checksums are also attached.

This remains a **community preview**. Other receiver versions, audio features, and long-term compatibility are unverified. Firmware conversion can make a receiver unusable. Backups exclude OTP/eFuses; restoration remains untested and is not included. Users supply GameSir's original updater locally; no vendor firmware or private device dumps are bundled.
