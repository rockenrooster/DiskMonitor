# Changelog

## Unreleased

- Add upcoming user-visible changes here before tagging a release.

## 2.0.0.1 - 2026-11-16

- Uninstalling now also removes Disk Monitor's notification and link registration, so nothing is left pointing at the removed program.

## 2.0.0.0 - 2026-11-16

- First public release of Disk Monitor 2.0, rebuilt from scratch.
- Watches the System and Application event logs in real time for physical and file system disk errors.
- Reads S.M.A.R.T. and NVMe health without administrator rights and alerts when it gets worse.
- Four severity tiers with per-tier notification options and per-event tier overrides.
- Toast notifications that lead straight to the event or to a scan of the affected volume, with quiet hours and snooze.
- CHKDSK from the app: read-only scans with live output, repairs with confirmation.
- Proactive scans after file system errors and on a schedule.
- Tray icon that shows the current state; start with Windows.
- Signed updates from GitHub Releases.

