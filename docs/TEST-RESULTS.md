# Verification results — 13 October 2026

This record preserves the validation performed during implementation. The English documentation and presentation reviews did not change the implementation. Historical artifact hashes identify those local builds; use the checksum file from the intended [GitHub Release](https://github.com/Patrice-Gaudicheau/CodexMonitor/releases/latest) for download verification.

Implementation and reproduction instructions: [LOCALIZATION.md](LOCALIZATION.md) and [DEVELOPMENT.md](DEVELOPMENT.md).

## Publication preflight

The final v1.1.0 publication preflight reran `scripts/Test.ps1 -NoRestore -Live`: **42 groups passed**, including native French/English forms at 100/125/150% DPI, automatic detection, manual overrides, live switching, settings compatibility, quota calculations, simulated transport failures, reconnection, process cleanup, and a real authenticated quota read.

The production x64 single-file build passed. Isolated localization verification confirmed **91 embedded resources per language** and **198 native renderings** across French, English, and German fallback, with external runtime discovery redirected to nonexistent paths. Release assets are rebuilt from the exact annotated tag and checked again before publication; detailed reports remain local artifacts.

## v1.1.0 localization and manual language override

Validation ran on Windows x64 with .NET SDK **10.0.401**.

### Build and automated tests

- Release solution build: **passed, zero warnings and zero errors**.
- `scripts/Test.ps1 -NoRestore -Live`: **42 groups passed**, including a real quota read and server shutdown. Four new groups cover manual selection and return to the original Windows UI culture, persistence/backward compatibility, retranslation of the same form with cached notes/errors, and an asynchronous continuation started before switching.
- Cultures covered: `fr-FR`, `fr-CA`, `fr-BE`, `fr-CH`, `fr`, `en-US`, `en-GB`, `de-DE`, `es-ES`, `ja-JP`, and invariant culture. Regional culture was deliberately set to `es-ES` independently of UI culture to verify that selection and formatting do not depend on regional settings.
- **91 keys per language**: identical nonempty embedded sets, matching format arguments and `UiText` properties. Both `.resources` manifests were present in the core assembly.
- Dates, times, fractional values, missing/disappeared/stale quotas, translated errors and authentication warnings, core notes, tooltips, actual menus, and callbacks: **passed**. Neither language exposed the simulated server error's fake secret.
- **132 native form renderings** for French and English: 11 states × 2 widths × 3 DPI values (96/120/144). No detected text overflow, visually observed overlap, or unnecessary scrolling with sufficient height. The button retained its action and disabled state during refresh. Accessibility descriptions were translated; scrolling and access to the final line were verified under height constraints.
- Native About dialogs: text and version **1.1.0** verified in both languages, with renderings inspected.
- Persistence used temporary files with the real load/save methods. v1.0.0 settings without a language field, missing/null/unknown values, and all three saved preferences were loaded correctly; `SelectedQuota` was retained. User settings were not modified.

### Final standalone candidate

| Property | Recorded value |
| --- | --- |
| Local file | `dist/v1.1.0/win-x64/CodexMonitor.exe` |
| File version | `1.1.0.0` |
| Size | 116,255,512 bytes |
| SHA-256 | `E9945E113F876E7B056B0936E555EEC3561D2F4137B21943DAD6A4609ACC0DB6` |

This hash identifies the tested local candidate. Download integrity checks must use `SHA256SUMS.txt` from the chosen GitHub Release.

`Verify-Localization.ps1` passed on this candidate for **French, English, and German → English**. Each execution produced 66 renderings, for **198 total**. The isolated folder contained only the executable; external runtime discovery pointed to nonexistent paths. Both resource manifests were confirmed, with no required application satellite DLL.

### Live Windows interface

- French `Verify-Published.ps1 -UICulture fr-FR`: **passed**. Four real reads, including two automatic and two manual actions; reconnection, gray error/stale presentation, `TaskbarCreated` icon recovery, duplicate-instance rejection, GUI PE subsystem `2`, and no orphaned server. The real French menu and details panel were shown and captured.
- English `Verify-Published.ps1 -UICulture en-US`: **passed with the same checks**, including four reads, two automatic and two manual. Final report: `artifacts/verification/published-en-isolated`.
- Windows UI Automation on the real English panel: **passed**. Opened through the Explorer icon, invoked **Refresh**, and verified the updated last reading and English accessibility description. Content height was 599 pixels in a 601-pixel viewport, without a scrollbar. Evidence: `artifacts/verification/published-en-ui`.
- Manual switching with a French Windows UI culture: **passed**, English → French → Automatic in the same open form, including retranslation of a cached 401 error. Tooltips, credits, dates, accessibility descriptions, and menu check state were correct. Server process, current reading, and refresh deadline stayed unchanged. Evidence: `artifacts/verification/manual-language/published-fr`.
- UI Automation of the **actual language submenu** with an English Windows UI culture: **passed**, English → French → Automatic. Title, button, and accessibility descriptions changed in the same native window without an additional read. All three observations retained the same window handle, 99% quota, and refresh deadline. The final live cycle also completed the four expected reads, two automatic and two manual, with duplicate rejection and no orphaned server. Evidence: `artifacts/verification/manual-language/interactive-switch` and `artifacts/verification/manual-language/published-en-ui-cycle`.

### Test execution notes

The first live attempt inside the restricted execution sandbox could not open the connection. The check passed when run with access to the native CLI outside that sandbox.

An initial English timed check received an extra UI Automation refresh click. That moved the next automatic reading by 60 seconds, preventing the expected count within 75 seconds. The timed check was rerun without concurrent interaction and passed.

An initial UI Automation attempt could not find the hidden tray-host window by title. The script now uses the handle recorded in `session.json` and verifies ownership by the target process before interaction.

The already-running local v1.0.0 instance was temporarily stopped for interactive checks and restarted after each sequence, including the interrupted attempt. Final verification confirmed its restart from the original path. Diagnostic choices were not saved and startup registration was unchanged. Settings-file hashes matched before/after, or the file remained absent.

The local v1.0.0 executable was retained. During that implementation-test stage, no commit, push, tag, GitHub Release change, or v1.1.0 publication was performed.

### Evidence and limitations

Reports and captures are local, ignored artifacts:

- `artifacts/verification/localization/{fr-FR,en-US}`
- `artifacts/verification/manual-language/final-single-file/{fr-FR,en-US,de-DE}`

Fixture dates are deterministic examples, not reset dates from the connected account.

DPI checks use `WM_DPICHANGED` and simulated child-control DPI caches, without changing display settings. Physical moves between monitors with different DPI, ARM64 hardware, and a separate PC without .NET were not tested. UI culture overrides affected only test processes; Windows language and persistent preferences were unchanged.

Online NuGet audit was unreachable from the restricted sandbox. A one-time restore used `NuGetAudit=false`, followed by `--no-restore` builds. No permanent audit setting or third-party dependency was added.

## Historical v1.0.0 validation

The following results describe the **initial version before the details-window redesign**. The later 22- and 23-group redesign checks are archived separately in [UI-REDESIGN.md](UI-REDESIGN.md).

### Initial local build

- `dist/win-x64/CodexMonitor.exe`, version **1.0.0**, Windows x64, .NET 10 self-contained single file.
- Recorded SHA-256: `328DD6F4D3195B24916B45B6E8B1423E3EA85D2B01978AEBEF901D734305EE14`.
- SDK **10.0.401**, publishing runtime **10.0.12**, native Codex CLI **0.159.0**.
- Distribution folder contained one file, about **111 MiB**. Windows GUI PE subsystem `2` confirmed. No external runtime was required to launch.

### Automated tests

**20 groups passed** with `scripts/Test.ps1 -Live`, without a third-party test framework:

- Used-to-remaining conversion for 100/87/50/9/0%, color boundaries, and conservative rounding.
- Missing/null values, multiple categories, five-hour preference, weekly-only data, disappeared selection, authoritative multi-category view, and legacy fallback.
- Three-minute staleness, reached reset time, backward clock movement, failure, and recovery.
- Unknown fields, out-of-range numbers, unexpected types, invalid timestamps, individual budgets, and separate credit information.
- Tooltip capped at 127 characters, including reset time.
- Badge rendering at 16/20/24/32/40/48 pixels for 100/87/50/9/0% and unavailable data, with visual inspection of the contact sheet. No GDI/HICON leak after 1,500 creation/destruction cycles.
- Simulated transport: normal response, malformed JSON, authentication error, timeout, server exit, and rejection of an unexpected token callback. The server error's fake secret was not propagated.
- Forced server termination followed by reconnection; forced owner-process termination without `Kill(entireProcessTree)`, proving the Job Object terminated its server.
- Real quota read through the C# client, followed by confirmed App Server shutdown.

### Published executable in Windows

The 75-second `scripts/Verify-Published.ps1` check on the initial build above **passed**:

- Four real reads: two automatic and two initiated through menu actions. Reconnected with a new server process and temporarily displayed a gray badge.
- Final reading: **86% remaining**, weekly window of **10,080 minutes**, reset supplied as **18 October 2026 at 08:28:15, Europe/Paris**. No five-hour window was supplied for that account during this test.
- An earlier check observed a real change from 87% to 86% during automatic refresh.
- Icon registered in Explorer, rectangle obtained through `Shell_NotifyIconGetRect`, with a 16-pixel rendering in that configuration. Actual menu/panel display, selection, and refresh actions were exercised.
- Icon registration recovered after removing **the application's own icon** and simulating the documented `TaskbarCreated` message. Explorer was not restarted.
- No console or main window; second instance rejected with exit code `10`. Repeated launches succeeded and all test-server processes exited.
- Launch succeeded with `DOTNET_ROOT` pointing to a nonexistent folder and `DOTNET_MULTILEVEL_LOOKUP=0`. These checks and the published contents support use of the bundled runtime; a different PC without .NET was not tested.
- Approximately **71 MiB** for the monitor and **0.80 CPU seconds over 75 seconds**, including test captures/interactions. These measurements exclude the App Server process.

A separate Windows UI Automation check found CodexMonitor in Explorer's notification overflow, captured its **actual mint-green “86” badge**, and invoked a left-click through Explorer. The French details panel opened and was closed; the monitor was left running normally.

Local evidence:

- `artifacts/verification/final/verification.json` and `duplicate-instance.json`
- `artifacts/verification/final/context-menu.png` and `details-panel.png`
- `artifacts/verification/interactive/actual-tray-badge.png` and `shell-interaction.json`
- `artifacts/verification/icons/contact-sheet.png`

`scripts/Verify-ShellBadge.ps1` reproduces the Explorer visual check with the monitor already running.

### Historical limitations

Windows initially placed the icon in the notification overflow. To read it continuously, users must move it into the visible area or change visibility in Windows settings. CodexMonitor does not force that preference or replace the taskbar. While overflow is closed, `Shell_NotifyIconGetRect` may return the overflow-arrow rectangle; actual badge evidence was therefore captured with overflow open.

Badge sizes corresponding to 100/125/150/200/250/300% scaling were rendered and inspected without changing system display settings. Actual transitions between those display configurations remain untested. A real Explorer restart and a prolonged external network outage were not imposed; recovery mechanisms were exercised through controlled scenarios.

Startup remained **disabled during this initial validation**: state was read, with no registry writes. User-enabled startup and ARM64 publishing were not validated at this stage. Startup had subsequently been enabled before the redesign stage, as recorded in its historical report.

## Documentation screenshots

The README uses three unmodified, verified PNGs, totaling **63,105 bytes**:

| Documentation image | Verification source | Context |
| --- | --- | --- |
| [tray-badge.png](images/tray-badge.png) | `artifacts/verification/interactive/actual-tray-badge.png` | Actual numeric icon in Explorer during v1.0.0 validation; badge design retained in v1.1.0. |
| [details-en.png](images/details-en.png) | `artifacts/verification/manual-language/final-single-file/en-US/window-96-520-normal.png` | Native v1.1.0 WinForms rendering with deterministic sample quota data at 100% DPI. |
| [details-fr.png](images/details-fr.png) | `artifacts/verification/manual-language/final-single-file/fr-FR/window-96-520-normal.png` | The same native verification scenario in French. |

The details images were captured from actual Windows Forms rendering, not generated mockups. Their title-bar appearance reflects the verification capture method. Captions and descriptions are in English; French text inside the French interface is preserved.
