# Changelog

## Unreleased

- Add upcoming user-visible changes here before tagging a release.

## 2.0.0.2 - 2026-11-16

- Menus, drop-down lists and tooltips show crisp text again; it came out heavy and darker than intended.
- Filter chips and drop-downs on the Events page resize as soon as their count, label or selection changes. Before, their text could be cut off or sit oddly in the button until you switched pages.
- The window keeps itself up to date when it was opened while Disk Monitor was still starting. Before, it could stay on what it first showed until you switched pages.
- The drive filter on Events shows "All drives" even when no event names a drive.
- Settings shows the full four-part version, for example 2.0.0.1.

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

