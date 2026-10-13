# Details window redesign — 13 October 2026

This is a historical record of the **v1.0.0** French details-window redesign and its final height correction. It describes those stages, not the later v1.1.0 localization changes or a current GitHub Release. Current language behavior is documented in [LOCALIZATION.md](LOCALIZATION.md).

## Design

Clicking the tray badge opens a native Windows Forms window inspired by Windows 11: a light background, rounded white cards, mint/turquoise accents, a circular quota indicator with a central percentage, the selected quota title, and a large Refresh button at the bottom.

Reset and last-reading cards show the supplied dates and times in French and the local Windows timezone. A quieter information area explains the refresh interval and stale-data rule. Other quotas and credit information remain available in the scrollable content. The Refresh button stays accessible when content overflows; Esc closes the window.

The window retains its native border and close button. Rounded window corners use the documented Windows 11 DWM attribute. GDI+ draws the cards and ring without image assets, WebView, or new dependencies. The button is a real Windows Forms control accessible by keyboard and Windows accessibility APIs. Cards expose their content to screen readers.

## Scope of the redesign

- Added `DetailsForm.cs` and `DetailsViewModel.cs`; replaced the details TextBox in `MonitorContext.cs`.
- Reused the exact selection and freshness rules from `CodexMonitor.Core`. A failure or stale reading displays a dash with no percentage arc.
- Quota calculation, App Server transport, badge rendering, tray integration, settings, entry point, and publishing configuration were unchanged during this stage, verified by before/after hashes.
- Refresh intervals, App Server methods, the tray menu, and distribution format were unchanged. The local self-contained x64 executable at `dist/win-x64/CodexMonitor.exe` was replaced and restarted.
- Startup had already been enabled by the user at this stage. Its registry command and executable path stayed unchanged.

## Initial redesign verification

- **22 test groups passed**, including the 20 existing groups and checks for the new panel's selected/missing/stale states, dates, and native button.
- Card renderings were inspected at 100%, 125%, 150%, and 200%, with 100/87/50/9/0% remaining and missing data. The actual Windows window was also verified at 100%. Windows display settings were not changed.
- Native `AutoScaleMode.Dpi`, drawing in logical coordinates, height bounded by the screen working area, scrollable content, and a fixed button were verified. The 100% value was not clipped.
- A 75-second local published-executable check passed: live quotas, automatic/manual refresh, reconnection, badge recovery, duplicate-instance rejection, and server shutdown.
- After replacement, the actual tray icon opened the window. Windows UI Automation invoked its real Refresh button; a successful new reading and updated time were observed, then the panel closed normally. The monitor remained active in the tray.
- The final reading for this stage was **86% remaining, weekly quota**, with a supplied reset time of **18 October 2026 at 08:28 local time**.

Local evidence:

- `artifacts/verification/details-redesign/installed/details-window.png`
- `artifacts/verification/details-redesign/installed/ui-interaction.json`

Recorded local v1.0.0 build before the final height correction, SHA-256:

`609821028383F11E2771EBE13E5FD5D24D744DC952BF5B2A5B7ECDBB1591B53E`

The controls were reproduced with `scripts/Test.ps1 -Live` and `scripts/Verify-DetailsWindow.ps1` while CodexMonitor was already running. The latter inspects and interacts with the application's notification-area icon and details window without changing settings.

## Final correction: unnecessary scrollbar

Window height was adjusted to use the content measured at full width, the button footer, and the available screen working area. Margins, typography, cards, ring, and button design stayed unchanged. Scrolling remains available when the screen is too short and disappears when enough height becomes available.

**23 test groups passed.** The Windows Forms `WM_DPICHANGED` path was exercised at 96/120/144 DPI, with child-control DPI cache simulation only inside the test process. Global Windows settings were unchanged. Captures and measurements showed all content visible without a scrollbar:

| Scale | Content height | Viewport height | Vertical scrollbar |
| --- | --- | --- | --- |
| 100% | 585 px | 587 px | Absent |
| 125% | 732 px | 735 px | Absent |
| 150% | 878 px | 881 px | Absent |

A simulated 720-pixel screen height retained scrolling, allowed the last line to be reached, and kept the button visible. Restoring sufficient height removed the scrollbar again.

The replaced executable was restarted and its actual 100% window checked through Windows UI Automation and the native viewport styles: no vertical scrollbar, with all 585 pixels of content inside the 587-pixel viewport. The native Refresh button triggered another successful live reading. This correction increased client height by only 5 pixels in that configuration.

Quota reading, badge rendering, transport, refresh behavior, presentation model, and publishing configuration remained unchanged, verified by hashes. Startup remained enabled at the same path.

Local evidence:

- `artifacts/verification/ui-polish/installed/details-window.png`
- `artifacts/verification/ui-polish/dpi-layout.json`

Recorded final local v1.0.0 UI-polish build, SHA-256:

`364B3BAFE2F3D17A018AAFB47C6302404DB71147058900CAF0C8FC745D0218B3`

These hashes identify historical local builds, not the current download. Use the checksum file from the intended GitHub Release to verify a downloaded executable. Local reports and captures are ignored artifacts; current reproduction instructions are in [DEVELOPMENT.md](DEVELOPMENT.md).