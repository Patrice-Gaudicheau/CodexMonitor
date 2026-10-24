# Installation and updates

This guide is for the Windows x64 executable distributed through [CodexMonitor GitHub Releases](https://github.com/Patrice-Gaudicheau/CodexMonitor/releases/latest). You do not need to compile the application or install .NET. Normal use does not require administrator privileges.

The v1.2.0 distribution includes `CodexMonitor.exe`, `SHA256SUMS.txt`, `LICENSE`, and `THIRD-PARTY-NOTICES.txt`. Keep the license and notices with your downloaded copy. The bilingual interface and saved language override were introduced in v1.1.0; WSL fallback is available starting with v1.2.0.

## Check the native Windows Codex CLI

CodexMonitor needs an authenticated Codex CLI that supports Codex App Server. It prefers a **native Windows `codex.exe`**, and can fall back to a WSL installation when the Windows executable is absent. The earlier native Windows validation used `codex-cli 0.159.0`.

For Codex installation instructions, use the [official Codex CLI documentation](https://learn.chatgpt.com/docs/codex/cli) and ensure you install a native Windows executable. A command wrapper named `codex.cmd` or `codex.ps1` alone is not enough for CodexMonitor's executable discovery.

CodexMonitor looks for the executable in this order:

1. The absolute `.exe` path specified by `CODEX_MONITOR_CODEX_PATH`, if set. An invalid override produces a warning rather than falling back.
2. `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe`.
3. A `codex.exe` in an absolute directory on the Windows `PATH`.
4. If **Use WSL when the Windows CLI is missing** is enabled (the default), Codex in the default WSL distribution, launched through Windows' `wsl.exe`.

An installed Windows CLI that fails to connect or authenticate does not trigger a switch to WSL. This keeps the selected account predictable.

Open **Windows PowerShell or PowerShell on Windows**, rather than a WSL terminal. Check the bundled location:

```powershell
Test-Path "$env:LOCALAPPDATA\Programs\OpenAI\Codex\bin\codex.exe"
```

If it returns `True`, check the executable and authentication:

```powershell
& "$env:LOCALAPPDATA\Programs\OpenAI\Codex\bin\codex.exe" --version
& "$env:LOCALAPPDATA\Programs\OpenAI\Codex\bin\codex.exe" login status
```

Otherwise, check for a native executable on the Windows PATH:

```powershell
Get-Command codex.exe -CommandType Application -ErrorAction SilentlyContinue
codex.exe --version
codex.exe login status
```

If `Get-Command` returns no result, install a native Windows executable or configure the custom path below before trying the following commands. A successful `codex` command alone does not prove that CodexMonitor can locate `codex.exe`.

If you are not signed in, start the normal login flow using the **same executable**:

```powershell
# For the bundled location:
& "$env:LOCALAPPDATA\Programs\OpenAI\Codex\bin\codex.exe" login

# Or, when codex.exe was found on PATH:
codex.exe login
```

Choose the command for your installation; do not run both unnecessarily. Sign in through the CLI's normal flow. Do not copy authentication tokens or credentials into CodexMonitor. API-key-only accounts may not expose the ChatGPT quota percentages the utility displays.

For a custom CLI location, check its absolute path directly, for example:

```powershell
& 'C:\Tools\Codex\codex.exe' --version
& 'C:\Tools\Codex\codex.exe' login status
```

Use your actual installation path. Set `CODEX_MONITOR_CODEX_PATH` to that absolute `codex.exe` path in your user environment and restart CodexMonitor so it receives the updated environment. If the override is already set, these checks should use its value rather than the bundled location:

```powershell
$env:CODEX_MONITOR_CODEX_PATH
& $env:CODEX_MONITOR_CODEX_PATH --version
& $env:CODEX_MONITOR_CODEX_PATH login status
```

Do not point it at a WSL path or a shell wrapper.

## WSL fallback

When no Windows CLI is found, keep **Use WSL when the Windows CLI is missing** checked in the tray menu. This choice is saved as `AllowWslFallback` in the existing settings file. Older settings enable fallback by default. This option requires v1.2.0 or later.

The monitor launches `codex app-server --listen stdio://` inside your default WSL distribution, with its home folder as the working directory. It uses the Linux login environment and the Codex authentication stored there. It does not copy Windows credentials. A Windows `CODEX_HOME` passed through WSL is cleared before loading the Linux login profile, allowing the Linux environment to choose its own Codex home.

In your WSL terminal, check:

```sh
command -v codex
codex --version
codex login status
```

If necessary, use `codex login` in that same WSL distribution. The monitor only reads quotas; it does not perform login or start a model turn. WSL must already be installed and have a usable distribution. CodexMonitor does not install or reconfigure WSL.

For multiple distributions or a CLI installed outside the login `PATH` (for example, through a shell-specific Node version manager), two optional **Windows user environment variables** are supported:

| Variable | Value |
| --- | --- |
| `CODEX_MONITOR_WSL_DISTRIBUTION` | Distribution name from `wsl.exe --list --quiet`, such as `Ubuntu`. Otherwise the WSL default is used. |
| `CODEX_MONITOR_WSL_CODEX_PATH` | Absolute Linux executable path, such as `/home/you/.local/bin/codex`. Otherwise `codex` is resolved in the Linux login environment. |

Restart CodexMonitor after changing these variables. Paths and distribution names are passed as literal arguments. `CODEX_MONITOR_CODEX_PATH` remains a Windows-only explicit override; an invalid value blocks fallback, so remove an obsolete override when moving to a WSL-only setup.

Changing the fallback checkbox reconnects and refreshes the reading. It is temporarily disabled while a refresh is running. Disabling it while using WSL clears the old reading and reports the missing Windows CLI. Enabling it can start the chosen WSL distribution if it is stopped.

## Download and run

1. Open the [latest release](https://github.com/Patrice-Gaudicheau/CodexMonitor/releases/latest). Confirm that you are in the `Patrice-Gaudicheau/CodexMonitor` repository and check the version in the release title.
2. Under **Assets**, download **CodexMonitor.exe**. The GitHub source-code archives are for developers, not the ready-to-run application.
3. Download **LICENSE** and **THIRD-PARTY-NOTICES.txt** for the accompanying license information. Optionally download **SHA256SUMS.txt** from the same release and perform the check below.
4. Create a permanent folder, for example `C:\Tools\CodexMonitor`, and copy the executable into it. If creating that folder is restricted on your PC, choose a permanent folder under your user profile instead.
5. Double-click `CodexMonitor.exe`.
6. Look for the numeric badge in the Windows notification area. There is no main window or console at startup; a gray dash may appear until the first successful reading.
7. If Windows hides the icon, open the notification overflow. Drag it to the visible notification area, or use Windows taskbar settings to keep it visible. CodexMonitor leaves that Windows preference to you.

There is no installation wizard. The executable contains its .NET runtime and both application languages. Windows may extract bundled native components into its temporary directory on first launch; no extra runtime or resource DLL needs to be copied alongside the EXE.

## Optional SHA-256 verification

Keep the executable and checksum file from the **same release** together. Open PowerShell in that folder:

```powershell
Get-FileHash .\CodexMonitor.exe -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

Compare the `Hash` value with the checksum on the line for `CodexMonitor.exe`. Letter case does not matter. If the values differ, do not run that copy; download both files again from the intended release. A matching hash confirms the file matches the release's checksum; it does not replace checking the repository and release source.

## Windows SmartScreen

The current release executable is not code-signed. Windows SmartScreen may show an **unrecognized app** warning, and the publisher may appear as unknown.

Before deciding whether to run it, confirm the repository and release URL, check the release information, and compare its SHA-256 checksum. If you decide to trust the file and Windows offers the option, **More info → Run anyway** applies to that launch. Managed PCs may not offer this option; follow your organization's policy. Do not disable SmartScreen or other Windows security protections globally.

## First use

Left-click the badge to view the selected quota, reset time, and last successful reading. Right-click for **Displayed quota**, **Refresh now**, **Start with Windows**, **About / Version**, and **Exit**. Labels are localized. Automatic startup is disabled by default; choose it only after placing the executable in its final folder.

**Langue / Language** offers **Automatic (Windows)**, **Français**, and **English**. Automatic selects French for a French Windows UI language and English for every other Windows UI language. A manual selection takes effect immediately and is saved. It does not change Windows settings or trigger a quota refresh.

## Updating

1. Download the newer `CodexMonitor.exe` from [GitHub Releases](https://github.com/Patrice-Gaudicheau/CodexMonitor/releases/latest), along with its optional checksum file.
2. Verify the downloaded file if desired.
3. Right-click the running CodexMonitor badge and choose **Exit**. Closing only the details window leaves the application running.
4. Replace the executable in its permanent folder.
5. Launch the replacement and check **About / Version**.

The selected quota, language preference, and WSL fallback choice are stored in `%LOCALAPPDATA%\CodexMonitor\settings.json`, separately from the executable. Replacing the EXE leaves these preferences intact. Older settings are compatible; absent, null, or unknown language values resolve to Automatic, and a missing WSL preference enables fallback.

Startup registration uses the executable's absolute path in the current user's Windows Run registry key. Replacing it at the same path preserves that registration. If you move it, enable **Start with Windows** again from the new location. A second launch while an instance is already running does not open another monitor.

## Removing the application

Disable **Start with Windows** from the tray menu, choose **Exit**, and delete the executable from its folder. The small settings file remains in your user profile; delete `%LOCALAPPDATA%\CodexMonitor\settings.json` only if you also want to remove your saved preferences.

## Troubleshooting

| Problem | Suggested action |
| --- | --- |
| Icon is hidden | Open the notification overflow and change its visibility in Windows taskbar settings. |
| CLI not found | Run the Windows executable checks above, or enable WSL fallback and check Codex in the selected distribution. Check any Windows override for an existing absolute `.exe` path. |
| Authentication warning | Run `login status` and, if needed, `login` in the environment in use (Windows or the selected WSL distribution). Check whether your account provides ChatGPT quotas. |
| WSL connection warning | Check that WSL and the selected distribution are available, and that the Linux login environment finds Codex. For a shell-specific installation, set `CODEX_MONITOR_WSL_CODEX_PATH` to its absolute Linux path. |
| Connection warning or gray dash | Check your network and the details window. Refresh manually or wait for the next automatic attempt. |
| Previously selected quota is unavailable | Open **Displayed quota** and choose an available quota or Automatic. A missing selected quota is not silently replaced. |
| No five-hour limit | The service may supply only a weekly window or other limits for your account. CodexMonitor displays the data it receives. |
| EXE cannot be replaced | Exit the running instance from its tray menu, then retry the replacement. |
| New launch appears to do nothing | Check for the existing instance in the notification area or overflow. Duplicate launches are rejected. |
| Language change does not persist | Check the localized save warning and your access to the settings folder. A failed save leaves the previous language active. |

A gray dash appears after a failed read, after three minutes without a successful reading, or when a reported reset time has been reached. CodexMonitor waits for fresh data rather than assuming the quota has returned to 100%.
