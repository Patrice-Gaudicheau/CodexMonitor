# Localization — v1.1.0

User controls are described in the [README](../README.md#language-support--v110) and [installation guide](INSTALLATION.md#first-use). This document covers implementation and verification.

## Language selection

`Localization.Initialize()` captures `CultureInfo.CurrentUICulture` at startup. Automatic mode selects `fr-FR` for the ISO language `fr` and `en-US` for all other Windows UI languages. Region, keyboard layout, and IP address play no part in selection.

The original Windows UI culture is kept separately from the active language. A manual French or English choice overrides it; returning to Automatic uses the original Windows UI culture rather than the last manually selected language. New threads use the selected default UI culture. `CurrentCulture` and Windows settings are not changed. Formatting explicitly uses the interface culture, and dates use `DateTimeOffset.ToLocalTime()` to preserve local timezone semantics.

## Live switching and persistence

The **Langue / Language** submenu offers **Automatic (Windows)**, **Français**, and **English**, with exactly one checked option. Switching updates the existing details form, accessibility labels and descriptions, menus, tooltip, dates, cached quota notes, and errors immediately. The quota values, refresh deadline, and server process remain unchanged; no restart or network refresh is required.

The selection is saved before it is applied. If saving fails, a localized warning is shown and the previous preference remains active.

`Settings.Language` stores `automatic`, `fr`, or `en`, with Automatic as the default. Missing, null, or unknown values resolve to Automatic while retaining `SelectedQuota`. The existing JSON file at `%LOCALAPPDATA%\CodexMonitor\settings.json` and its atomic write mechanism are reused. v1.0.0 settings remain compatible.

Language state is shared across the process. An asynchronous continuation that captured an older `CurrentUICulture` cannot restore old-language text after switching. Quota notes retain facts and are formatted at presentation time. Server errors retain their code, including an absent code, and are translated when displayed. External names and internal quota identities are unchanged.

## Resources

| File or component | Purpose |
| --- | --- |
| [StringsFrench.resx](../src/CodexMonitor.Core/Resources/StringsFrench.resx) | French reference wording. |
| [StringsEnglish.resx](../src/CodexMonitor.Core/Resources/StringsEnglish.resx) | English translations. |
| [Localization.cs](../src/CodexMonitor.Core/Localization.cs) | Language selection, `ResourceManager` access, and value/date formatting. |
| [UiText.cs](../src/CodexMonitor.Core/UiText.cs) | Named properties for the 91 resource keys, checked by the compiler. |
| `DetailsViewModel`, `AppPresentation`, `TrayMenu` | Localized presentation, errors, and menus, separate from quota calculations and freshness rules. |

Both `.resx` filenames are deliberately culture-neutral from the .NET build system's perspective, with `WithCulture="false"`. Their two `.resources` manifests are embedded in `CodexMonitor.Core.dll`, which is included in the self-contained executable bundle. No application satellite such as `fr/CodexMonitor.Core.resources.dll` is needed at runtime. Standard .NET resources are used without a third-party localization framework.

When adding a string, add the same key to both `.resx` files and a named property to `UiText`. Tests require identical key sets, nonempty values, matching formatting arguments, and matching properties. Missing translations cannot silently pass. Do not translate protocol identifiers, JSON fields, quota keys, configuration identifiers, or externally supplied names.

## Rendering and verification

Cards measure labels, category names, status text, dates, and times. Long text wraps and cards grow as needed. The French design's indicator, mint/orange/red colors, Segoe UI fonts, and spacing are preserved. The window still uses `PerMonitorV2`, fits its content within the available working area, and scrolls when screen height requires it.

The matrix opens real WinForms forms at **96, 120, and 144 DPI (100%, 125%, and 150%)**, with logical widths of 520 and 440 pixels. It covers 11 states: normal, multiple quotas, missing data, stale data, error, refreshing, individual budget, and 100/50/9/0% remaining. That gives 66 renderings per culture.

DPI changes are injected through `WM_DPICHANGED`, with child-control DPI cache simulation confined to the test process. UI cultures are also overridden only in test processes. Permanent Windows language and display settings are not changed.

Checks cover text bounds, unnecessary scrolling, access to the last line under height constraints, button actions, refresh-in-progress state, and accessibility descriptions. The real context menu and native About dialog are opened, their contents verified, and their renderings saved.

The isolated-EXE check repeats the matrix for French, English, and German fallback to English from a folder containing only the EXE, with external runtime discovery redirected to nonexistent paths. Persistence tests use isolated temporary settings files and the application's actual load/save methods. Live switching tests cover cached notes and errors, menu checks, tooltips, accessibility, and asynchronous continuations started before switching.

Reproduction commands, interactive prerequisites, and diagnostic arguments are maintained in [DEVELOPMENT.md](DEVELOPMENT.md#verify-the-standalone-executable). Dated results and hardware limitations are in [TEST-RESULTS.md](TEST-RESULTS.md).

JSON reports and PNG captures are local, ignored artifacts under `artifacts/verification/localization`, `artifacts/verification/single-file-localization`, and `artifacts/verification/manual-language/final-single-file`. Fixture dates are illustrative, not readings from the connected account.

## Delivery

A shared build version supplies the assembly, About dialog, details window, and App Server handshake version. Release binaries are built from the annotated version tag and distributed through [GitHub Releases](https://github.com/Patrice-Gaudicheau/CodexMonitor/releases/latest), with SHA-256 checksums and accompanying licenses. The previous v1.0.0 release is retained unchanged.
