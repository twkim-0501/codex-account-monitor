# Codex Account Monitor: getting started

[한국어 README](../README.md) · [Download the Windows executable](https://github.com/twkim-0501/codex-account-monitor/releases/latest/download/CodexAccountMonitor.exe) · [Release and ZIP](https://github.com/twkim-0501/codex-account-monitor/releases/latest)

A Windows taskbar widget for checking remaining Codex quota and token activity across local and SSH accounts. Inspired by [pjhun0412's Codex Pulse](https://github.com/pjhun0412/codex-pulse), adapted and rewritten in C#/WPF using OpenAI Codex. See the [full credits](CREDITS.md).

SSH servers need bash and Codex installed; Linux servers have been verified.

The **초기화권 지급** and **다음 특별 리셋** cards put expected payout/reset times first and show source links directly, without explanatory paragraphs or expanders. Confirmed credit receipts replace pending deadlines with receipt time, incoming amount, and account names. The 24h/48h gauges are experimental community estimates from [CodexReset](https://codexreset.org/). A confirmed reset retires old one-off forecasts and probability snapshots from before that reset. Separately scheduled future resets and ongoing programs remain. Unknown times are marked as unknown, and unavailable probabilities show a dash. Held-credit counts and expiry dates remain in account details.

Public sources are checked every ten minutes without an API key. Coverage comes from community collectors and X's public embed cards and can be delayed or incomplete. Hints are never treated as guaranteed resets or calibrated probabilities. New-signal, quota-recovery/reset-time-change, credit-increase, and three-day/24-hour credit-expiry notifications can be disabled in settings. Monitoring does not run model turns or redeem credits. See the [implementation and judgment rules](RESET-MONITOR.md) (Korean).

## Install and start

1. Use Windows 10/11 x64. Install the [official Windows desktop app](https://learn.chatgpt.com/docs/windows/windows-app) or [Codex CLI](https://learn.chatgpt.com/docs/codex/cli), and sign in with your ChatGPT account. If you already use Codex on this PC, skip this preparation step.
2. [Download **CodexAccountMonitor.exe** directly](https://github.com/twkim-0501/codex-account-monitor/releases/latest/download/CodexAccountMonitor.exe). The `Source code` archives and `Code → Download ZIP` are for developers. A ZIP containing the executable and documentation is available on the release page.
3. Keep the executable in a folder of your choice and run it. The .NET runtime is included; no SDK, installer, or administrator access is required.
4. On first launch, the detail panel opens and reads the current PC's Codex login. For a login-required message, sign in through Codex and click `↻`.
5. Click `−` to collapse the panel. The mini widget continues refreshing. If it is missing, look in the notification area, including the hidden icons menu. Right-click the icon and enable **왼쪽 미니 위젯 표시** (show mini widget).

The interface currently uses Korean labels. The `?` button opens a short usage guide. Start with these labels:

| Label | Meaning |
| --- | --- |
| + 계정 | Add account |
| 연결 확인 | Check connection and account identity |
| 모니터에 추가 | Add to monitor; completes registration |
| 변경 저장 | Save changes to an existing connection |
| 고급 설정 | Advanced options, usually optional |
| 종료 | Quit the monitor |

## Add an SSH account

**VSCode/Codex remote connections do not automatically register accounts in this monitor.** The SSH config provides choices, and you explicitly choose which accounts to monitor.

1. In Windows Terminal, run `ssh research-server`, replacing the example with your SSH alias. Complete the initial host-key check and configure key authentication. The monitor cannot prompt for a password.
2. On the server, check `codex --version` and `codex login status`. For a new headless login, follow the [official authentication guide](https://learn.chatgpt.com/docs/auth#login-on-headless-devices).
3. Click **+ 계정**, enter a display name, and choose **SSH · 원격 서버의 Codex**.
4. Select the SSH alias or type `user@host`.
5. Click **연결 확인**. Check that the masked email and plan belong to the intended account.
6. Click **모니터에 추가**. The dialog closes and the account appears in the list.

An SSH alias, the Linux user, and the ChatGPT account are different identities. Renaming a connection does not change its login. Different aliases pointing to the same server user/profile can report the same account. For multiple logins, use separate server users or existing separate Codex profiles.

No project directory is required. In advanced options, `CODEX_HOME` means the Codex login profile, not the project folder. Leave it empty for the default. Remote binary/profile paths must refer to the server. SSH config aliases are read from `%USERPROFILE%\.ssh\config`; aliases from included files can be typed manually.

## Add another local account

Choose **로컬 · 별도 계정으로 로그인**, enter a display name, and click **이 계정으로 로그인**. Complete the browser login with the desired account, check the connection, then click **모니터에 추가**. Browser login alone does not finish monitor registration.

The monitor creates a separate local profile and uses Windows credential storage, preserving the current Codex login. These profiles do not automatically change the account list in other Codex apps.

The default local connection follows the current Codex login on this PC; its display name does not pin an account. To keep monitoring an account after switching the desktop login, use a separate local login profile or an SSH connection already signed into that account. Local credential-file changes reload the monitor connection on its next read. Authentication failures also trigger one reconnect; persistent failures remain failed reads and do not replace the quota cache with an empty successful snapshot.

## Read the widget

- Percentages show **remaining quota**, not percentage used or exact remaining token counts. With several quota windows, the mini widget shows the lowest remaining percentage.
- Up to three accounts appear in one row. The current desktop login appears first with a soft blue chip and dot; the detail panel also shows a **PRIMARY** badge. The remaining accounts keep their registration order. Switching the desktop login updates both views. Matching registered local/SSH accounts are reused; an unregistered desktop login appears automatically as **현재 계정** without creating a saved connection. Unknown identity removes the primary designation. For more accounts, `+N` opens the complete list. Up to 50 connections can be saved.
- Click an account row to expand its token activity and reset times. Dates follow the server's daily bucket, not necessarily your local midnight.
- Each account also shows weekly remaining quota, time until its regular reset, a daily quota budget, and a pacing badge. For example, 40% remaining over four days allows 10 percentage points of the full weekly quota per day. Expanded accounts compare quota used with time elapsed in two bars. This evenly distributed reference is not an additional usage limit or an exact token estimate; five-hour limits apply separately.
- Weekly budgets use fresh server-provided seven-day windows and regular reset times, independently of special-reset forecasts. Updated quota/reset data after a reset or credit redemption replaces the old budget. Under 24 hours, the UI shows the total allowance until reset. Missing, failed, expired, or more-than-ten-minute-old reads show **확인 필요** (needs checking).
- The detail panel automatically fits its content when opened, expanded/collapsed, refreshed, or made narrower. Scrollbars appear only when the content exceeds the available screen height. Reopening restores the content height after manual shortening.
- `이전` means the last successful reading is stale. `—` means unknown/unavailable, not zero. `제한` means the backend reports ordinary usage is blocked.
- Free accounts are not filtered out. A Free account has been verified with this app, but availability and returned metrics depend on the account and Codex version. Quota periods are reported by Codex and are not hardcoded.
- `−` and closing the window collapse the details. To quit, right-click the widget or notification icon and select **종료**.

Taskbar docking targets Windows 11's centered horizontal taskbar. Other layouts use a small overlay just above it. Explorer's internal structure can change with Windows updates.

## Update, privacy, and support

Quit the monitor before replacing the executable with a newer release. Settings are stored in `%USERPROFILE%\.codex-account-monitor` from v1.8.1, so Codex-launched and Windows-startup runs share one account list. Existing Windows and Codex-package settings are recovered automatically; configured connections take precedence over the initial This PC account. Original files remain intact. Keep the same executable path if you enabled startup. To uninstall, turn off startup in settings, quit, and remove the executable. Removing the data folder also removes profiles created there; credential-store entries and profiles at legacy paths can remain separately.

There is no code-signing certificate. Check that the executable came from this repository's release; a SHA-256 checksum is provided. Only demo images and fictional config examples belong in public reports. Settings/cache contain private account metadata and should not be uploaded.

No model turns are started by the monitor. Remote credentials stay on the server. Connection removal removes the monitor entry, not the server login or remote files.

For help, [open an issue](https://github.com/twkim-0501/codex-account-monitor/issues) with your Windows/app/Codex versions, connection type, and the displayed error. Redact emails and server addresses. Never attach authentication files, private keys, or tokens.
