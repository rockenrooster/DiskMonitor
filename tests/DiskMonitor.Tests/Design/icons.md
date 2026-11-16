# Icon geometry

Copied unchanged from MediaVerifier (`..\MediaVerifier\docs\design\icons.md`, commit `5240516`,
2026-11-16). Disk Monitor's additions are at the end: severity shapes and state icons restated from
MediaVerifier's subplan 08, one extra icon, the app icon, and where each icon is used.

Every icon is drawn by the app from the primitives below. No icon font and no image files are used. Coordinates are on a 16 x 16 grid with the origin at the top left; scale them by size / 16.

Stroke: 1.5 px at 16 px size, round caps, round joins, no fill unless a primitive says "filled". At other sizes the stroke is: 12 px icon 1.5, 16 px 1.5, 20 px 1.75, 24 px 1.75, 32 px 2.2, 48 px 2.5. Icons take the colour of the text they sit beside. Paths use only the SVG commands M, L, H, V, C and Z (absolute coordinates).

| Icon | Primitives |
| --- | --- |
| `dashboard` | rect x=2.5 y=2.5 w=4.5 h=5 radius=1<br>rect x=9 y=2.5 w=4.5 h=3 radius=1<br>rect x=9 y=7.5 w=4.5 h=6 radius=1<br>rect x=2.5 y=9.5 w=4.5 h=4 radius=1 |
| `issues` | polygon (8, 2.5) (14, 13) (2, 13)<br>line (8, 6.5) to (8, 9.5)<br>circle centre (8, 11.25) r=0.5, filled |
| `library` | line (3.5, 2.5) to (3.5, 13.5)<br>line (7, 2.5) to (7, 13.5)<br>line (10, 3.5) to (13, 13.5) |
| `scans` | polyline (1.5, 8) (4.5, 8) (6.5, 3.5) (9.5, 12.5) (11.5, 8) (14.5, 8) |
| `schedule` | rect x=2.5 y=3.5 w=11 h=10 radius=1.5<br>line (2.5, 6.5) to (13.5, 6.5)<br>line (5.5, 2) to (5.5, 4.5)<br>line (10.5, 2) to (10.5, 4.5)<br>circle centre (5.5, 9.75) r=0.5, filled<br>circle centre (8, 9.75) r=0.5, filled<br>circle centre (10.5, 9.75) r=0.5, filled |
| `tools` | rect x=2.5 y=5 w=11 h=8.5 radius=1.5<br>polyline (6, 5) (6, 3) (10, 3) (10, 5)<br>line (8, 7.5) to (8, 11)<br>line (6.25, 9.25) to (9.75, 9.25) |
| `settings` | line (2.5, 4.5) to (8, 4.5)<br>line (12, 4.5) to (13.5, 4.5)<br>circle centre (10, 4.5) r=1.75, stroked<br>line (2.5, 11.5) to (4, 11.5)<br>line (8, 11.5) to (13.5, 11.5)<br>circle centre (6, 11.5) r=1.75, stroked |
| `search` | circle centre (7, 7) r=4.5, stroked<br>line (10.4, 10.4) to (14, 14) |
| `bell` | path `M4 11.5 V7.5 C4 5 5.8 3 8 3 C10.2 3 12 5 12 7.5 V11.5`, stroked<br>line (2.75, 11.5) to (13.25, 11.5)<br>line (6.75, 13.75) to (9.25, 13.75) |
| `filter` | polygon (2.5, 3.5) (13.5, 3.5) (9.5, 8.5) (9.5, 12.5) (6.5, 13.5) (6.5, 8.5) |
| `refresh` | path `M13 8 C13 10.76 10.76 13 8 13 C5.24 13 3 10.76 3 8 C3 5.24 5.24 3 8 3 C9.6 3 11 3.75 11.9 4.9`, stroked<br>polyline (12.25, 2.25) (12.25, 5.25) (9.25, 5.25) |
| `play` | polygon (5, 3.5) (12.5, 8) (5, 12.5) |
| `pause` | line (5.5, 3.5) to (5.5, 12.5)<br>line (10.5, 3.5) to (10.5, 12.5) |
| `stop` | rect x=4 y=4 w=8 h=8 radius=1 |
| `close` | line (4, 4) to (12, 12)<br>line (12, 4) to (4, 12) |
| `add` | line (8, 3) to (8, 13)<br>line (3, 8) to (13, 8) |
| `check` | polyline (3, 8.5) (6.5, 12) (13, 4.5) |
| `chevron-right` | polyline (6, 3.5) (10.5, 8) (6, 12.5) |
| `chevron-down` | polyline (3.5, 6) (8, 10.5) (12.5, 6) |
| `chevron-left` | polyline (10, 3.5) (5.5, 8) (10, 12.5) |
| `chevron-up` | polyline (3.5, 10) (8, 5.5) (12.5, 10) |
| `more` | circle centre (3.5, 8) r=0.6, filled<br>circle centre (8, 8) r=0.6, filled<br>circle centre (12.5, 8) r=0.6, filled |
| `folder` | path `M2 4.5 H6 L7.5 6 H14 V12.5 H2 Z`, stroked |
| `folder-add` | path `M2 4.5 H6 L7.5 6 H14 V12.5 H2 Z`, stroked<br>line (8, 7.75) to (8, 10.75)<br>line (6.5, 9.25) to (9.5, 9.25) |
| `photo` | rect x=2.5 y=3.5 w=11 h=9 radius=1.5<br>circle centre (5.75, 6.75) r=1, stroked<br>polyline (2.5, 11) (6, 8) (8.5, 10.25) (10.5, 8.5) (13.5, 11) |
| `video` | rect x=2 y=4 w=8.5 h=8 radius=1.5<br>polyline (10.5, 6.9) (14, 4.9) (14, 11.1) (10.5, 9.1) |
| `music` | polyline (6, 11.5) (6, 4) (12.5, 3) (12.5, 10.5)<br>circle centre (4.5, 11.5) r=1.5, stroked<br>circle centre (11, 10.5) r=1.5, stroked |
| `clock` | circle centre (8, 8) r=5.5, stroked<br>polyline (8, 4.75) (8, 8) (10.25, 9.5) |
| `shield` | path `M8 2 L13 4 V8 C13 10.9 10.9 13.2 8 14 C5.1 13.2 3 10.9 3 8 V4 Z`, stroked |
| `shield-check` | path `M8 2 L13 4 V8 C13 10.9 10.9 13.2 8 14 C5.1 13.2 3 10.9 3 8 V4 Z`, stroked<br>polyline (5.75, 8) (7.4, 9.6) (10.25, 6.5) |
| `undo` | path `M3.5 6.5 H10 C12 6.5 13.5 8 13.5 10 C13.5 12 12 13.5 10 13.5 H6.5`, stroked<br>polyline (6, 4) (3.5, 6.5) (6, 9) |
| `quarantine` | rect x=2.5 y=3 w=11 h=3 radius=0.75<br>polyline (3.5, 6) (3.5, 12.5) (12.5, 12.5) (12.5, 6)<br>line (6.5, 8.75) to (9.5, 8.75) |
| `trash` | line (3, 4.5) to (13, 4.5)<br>polyline (6, 4.5) (6, 3) (10, 3) (10, 4.5)<br>polyline (4.25, 4.5) (5, 13.5) (11, 13.5) (11.75, 4.5)<br>line (6.75, 7) to (6.75, 11)<br>line (9.25, 7) to (9.25, 11) |
| `open` | polyline (7, 3.5) (3.5, 3.5) (3.5, 12.5) (12.5, 12.5) (12.5, 9)<br>polyline (9.5, 3.5) (12.5, 3.5) (12.5, 6.5)<br>line (12.5, 3.5) to (7.5, 8.5) |
| `copy` | rect x=5.5 y=5.5 w=8 h=8 radius=1.5<br>polyline (10.5, 3.5) (10.5, 2.5) (2.5, 2.5) (2.5, 10.5) (3.5, 10.5) |
| `download` | line (8, 2.5) to (8, 10.5)<br>polyline (4.5, 7.5) (8, 11) (11.5, 7.5)<br>line (3, 13.5) to (13, 13.5) |
| `report` | path `M4 2.5 H9.5 L12 5 V13.5 H4 Z`, stroked<br>polyline (9.5, 2.5) (9.5, 5) (12, 5)<br>line (6, 8) to (10, 8)<br>line (6, 10.5) to (10, 10.5) |
| `info` | circle centre (8, 8) r=5.5, stroked<br>line (8, 7.25) to (8, 11)<br>circle centre (8, 5.1) r=0.5, filled |
| `ignore` | circle centre (8, 8) r=5.5, stroked<br>line (4.1, 4.1) to (11.9, 11.9) |
| `drive` | rect x=2.5 y=4 w=11 h=8 radius=1.5<br>line (2.5, 9) to (13.5, 9)<br>circle centre (11, 10.5) r=0.4, filled |
| `update` | circle centre (8, 8) r=5.5, stroked<br>polyline (5.5, 8) (8, 5.5) (10.5, 8)<br>line (8, 5.5) to (8, 11) |
| `menu` | line (2.5, 4.5) to (13.5, 4.5)<br>line (2.5, 8) to (13.5, 8)<br>line (2.5, 11.5) to (13.5, 11.5) |
| `database` | path `M3 4.5 C3 3.4 5.2 2.5 8 2.5 C10.8 2.5 13 3.4 13 4.5 V11.5 C13 12.6 10.8 13.5 8 13.5 C5.2 13.5 3 12.6 3 11.5 Z`, stroked<br>path `M3 4.5 C3 5.6 5.2 6.5 8 6.5 C10.8 6.5 13 5.6 13 4.5`, stroked<br>path `M3 8 C3 9.1 5.2 10 8 10 C10.8 10 13 9.1 13 8`, stroked |
| `engine` | rect x=4.5 y=4.5 w=7 h=7 radius=1<br>line (6.5, 2) to (6.5, 4.5)<br>line (9.5, 2) to (9.5, 4.5)<br>line (6.5, 11.5) to (6.5, 14)<br>line (9.5, 11.5) to (9.5, 14)<br>line (2, 6.5) to (4.5, 6.5)<br>line (2, 9.5) to (4.5, 9.5)<br>line (11.5, 6.5) to (14, 6.5)<br>line (11.5, 9.5) to (14, 9.5) |
| `sort-down` | polyline (4.5, 6.5) (8, 10) (11.5, 6.5) |
| `sort-up` | polyline (4.5, 9.5) (8, 6) (11.5, 9.5) |

Total: 46 icons, 119 primitives.

## Disk Monitor additions

### Severity shapes (from MediaVerifier subplan 08, unchanged)

Filled shapes with a glyph knocked out in the colour of the surface behind them. Coordinates on a
16 × 16 grid; scale for 12, 14, 16, 20 and 24 px. Shape colour: the tier's `*Text` token. Glyph:
knock-out colour, 1.7 px round stroke.

| Tier | Shape | Glyph |
| --- | --- | --- |
| Critical | Octagon `(5,1.5) (11,1.5) (14.5,5) (14.5,11) (11,14.5) (5,14.5) (1.5,11) (1.5,5)`, plus a 1 px same-colour stroke | Cross: `(5.75,5.75)–(10.25,10.25)` and `(10.25,5.75)–(5.75,10.25)` |
| High | Triangle `(8,2.25) (14.5,13.5) (1.5,13.5)`, plus a 1.5 px same-colour round-join stroke | Line `(8,6.25)–(8,9.5)`; dot centre `(8,11.6)` r 0.95 |
| Medium | Diamond `(8,1.5) (14.5,8) (8,14.5) (1.5,8)`, plus a 1.2 px same-colour stroke | Line `(8,4.9)–(8,8.6)`; dot centre `(8,10.9)` r 0.95 |
| Low | Circle centre `(8,8)` r 6.75 | Dot centre `(8,5)` r 0.95; line `(8,7.4)–(8,11.2)` |
| OK | Circle centre `(8,8)` r 6.75 | Check `(4.9,8.2) (7.2,10.4) (11.1,5.9)` |

### State icons (from MediaVerifier subplan 08, unchanged)

1.5 px stroke in `Text3`, 16 px.

| State | Geometry | Disk Monitor use |
| --- | --- | --- |
| `pending` | Circle r 5.75 with dash pattern 2.2 on, 2.3 off | Volume never scanned |
| `skipped` | Circle r 5.75; line `(5.25,8)–(10.75,8)` | Drive with no health data; snoozed tray badge |
| `missing` | Circle r 5.75; question mark: path `M6.4 6.5 C6.4 5.5 7.1 4.9 8 4.9 C8.9 4.9 9.6 5.5 9.6 6.4 C9.6 7.5 8 7.6 8 8.9`; dot centre `(8,11)` r 0.5 | Disk named by an event that is no longer connected |
| `error` | Rounded square `x 2.5, y 2.5, 11 × 11`, radius 2; line `(8,5.25)–(8,8.75)`; dot centre `(8,10.9)` r 0.5 | A CHKDSK run that could not run |

### Extra icon

| Icon | Primitives |
| --- | --- |
| `bell-off` | path `M4 11.5 V7.5 C4 5 5.8 3 8 3 C10.2 3 12 5 12 7.5 V11.5`, stroked<br>line (2.75, 11.5) to (13.25, 11.5)<br>line (6.75, 13.75) to (9.25, 13.75)<br>line (2.5, 2.5) to (13.5, 13.5) |

### App icon

Disk Monitor's mark follows MediaVerifier's rules (two versions, the same blues, rasterised from
geometry by a script) with its own subject: a drive with a pulse line. Geometry on a 256 × 256 grid;
the files are [app-icon.svg](app-icon.svg) and [app-icon-small.svg](app-icon-small.svg) (reviewed as
renders at 16 to 256 px on dark and light backgrounds, 2026-11-16).

| Part | Standard version (32 px and larger) | Small version (24 px and smaller) |
| --- | --- | --- |
| Body | Rounded square `x 24, y 24, 208 × 208`, radius 40, vertical gradient `#6ED3FF` (top) to `#1F6FE0` (bottom) | Same square, flat `#2B8CEB` |
| Drive | White rounded rectangle outline `x 48, y 68, 160 × 120`, radius 20, stroke 14 | `x 50, y 66, 156 × 124`, radius 20, stroke 22 |
| Band | White line `(48,148)–(208,148)`, stroke 14 | `(50,150)–(206,150)`, stroke 22 |
| LED | White disc, centre `(176,168)`, radius 9 | Centre `(174,170)`, radius 12 |
| Pulse | White polyline `(68,108) (98,108) (114,82) (136,130) (152,108) (188,108)`, stroke 14 | None |

All strokes have round caps and joins.

`DiskMonitor.ico` holds 16, 20, 24 (small) and 32, 40, 48, 64, 128, 256 (standard). The small
version at 24 px also marks the top of the navigation rail, and at 16 px it is the tray icon.

### Where each icon is used

| Place | Icon |
| --- | --- |
| Navigation: Overview, Events, Drives, Check Disk, Settings, Update available | `dashboard`, `issues`, `drive`, `scans`, `settings`, `update` |
| Search boxes, filter summary | `search`, `filter` |
| Refresh, reload history | `refresh` |
| Scan (read-only), stop a scan | `play`, `stop` |
| Repair menu items | `tools` |
| Administrator hints, prompt-free scans | `shield`, `shield-check` |
| Mark as read | `check` |
| Ignore a kind of event | `ignore` |
| Undo in a toast | `undo` |
| Event Viewer, GitHub, external links | `open` |
| Copy | `copy` |
| Export CSV | `report` |
| Download update | `download` |
| Open log or data folder | `folder` |
| Snooze | `bell-off`; the notifications section uses `bell` |
| Times and schedules | `clock` |
| Info bars | The tier's shape; informational bars use the Low shape, as MediaVerifier's do |
| Help text beside a control | `info` |
| Drawer close, dismiss | `close` |
| More actions | `more` |
| Expanders, drop-downs, links | `chevron-right`, `chevron-down`, `chevron-up` |
| Empty "no problems" state | `check` at 48 px, as MediaVerifier |
