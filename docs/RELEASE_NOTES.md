Community preview v0.1.1 of **G7 Pro Xbox Wireless**, a standalone app separate from the G7 Pro Pairing utility.

The underlying conversion method succeeded on one standard G7 Pro receiver running firmware 1.46: after installing Wuchang dongle 5.12, the existing controller reconnected and worked wirelessly on Xbox Series S **without changing controller firmware**.

The app verifies the exact original official updater, makes two full matching 512 KiB backups, requires a receiver replug, and prepares the vendor updater locally with product matching disabled. UFW payload bytes remain unchanged. Users supply GameSir's original package; no vendor firmware or device dumps are redistributed.

**Preview limits:** supported stock firmware is 1.46 only; other receiver revisions and Xbox models are unverified. The new interface has not yet completed a full stock-to-converted hardware run. Firmware conversion can make a receiver unusable. Backups exclude OTP/eFuses; restoration is untested and not included. The app does not claim success from an updater exit or USB name.

Download `G7ProXboxWireless-0.1.0-win-x64.zip`, extract it, and read the included README. Requires Windows 10/11 x64 with .NET Framework 4.8; backup requires administrator access. The executable is unsigned.

Build validation: 40 offline checks. The real official package also passed three optional integration checks and the C#-prepared updater opened successfully with the expected firmware CRCs, without starting another installation.

Source is in the repository and source ZIP; SHA-256 checksums accompany the downloads.

This patch keeps Restart as administrator available after loading a saved backup. After an administrator restart, select the firmware and load the backup again.
