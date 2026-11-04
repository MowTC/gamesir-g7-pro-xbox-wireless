# Changelog

## 0.1.1 — community preview

- Keep Restart as administrator available after loading a saved backup. Restarting requires selecting the firmware and loading the backup again; no partial conversion state is carried across processes.

## 0.1.0 — community preview

- Separate Windows app for the tested standard G7 Pro receiver 1.46 to Wuchang dongle 5.12 conversion.
- Exact official-package fingerprinting and unchanged UFW payload validation.
- Restricted receiver discovery and two-pass full SPI NOR backup, including reserved tail.
- Replug and durable-backup checks before opening a locally prepared vendor updater.
- No bundled GameSir firmware, private dumps, telemetry, controller update, or restore operation.
- Forty offline checks, plus optional integration checks against a user-supplied official updater.

The underlying method was verified for wireless input on one Xbox Series S setup. End-to-end conversion through the new app interface has not yet been repeated on stock hardware; broader compatibility is unverified.
