# Codex Account Monitor

A native Windows widget for monitoring multiple Codex accounts, including accounts signed in on remote SSH servers. Built with WPF and .NET 10. No Electron runtime, telemetry, cloud dashboard, API key, or inference requests.

![Widget preview with fictional accounts and example values](docs/preview.png)

**[Download the latest Windows executable](https://github.com/twkim-0501/codex-account-monitor/releases/latest)**

## 한국어 · 사용 방법

1. 릴리스에서 `CodexAccountMonitor.exe`를 다운로드하고 실행합니다. Windows 10/11 x64용이며 .NET 런타임을 포함합니다. Codex 앱 또는 Codex CLI는 별도로 설치되어 있어야 합니다.
2. 첫 실행에서는 현재 PC의 Codex 로그인 계정을 자동으로 조회합니다.
3. **+ 연결 → SSH**에서 기존 SSH 별명(예: `research-server`)을 선택하면 서버에 로그인된 Codex 계정을 함께 조회합니다. `ssh research-server`가 키 인증으로 연결되어 있어야 합니다. 비밀번호 입력이나 새 호스트 키 확인은 터미널에서 먼저 완료하세요.
4. 새 계정은 **+ 연결 → 로컬 · 별도 계정으로 로그인**에서 이름을 입력하고 로그인합니다. 현재 Codex 계정과 다른 `CODEX_HOME`을 사용합니다. 새 프로필은 Windows 자격 증명 저장소를 사용합니다.
5. 각 카드의 `···`로 연결을 편집하거나 제거합니다. 연결 제거는 Codex 인증 정보나 원격 파일을 삭제하지 않습니다.

왼쪽 하단에 처음 열리며 드래그와 크기 조절이 가능합니다. `−`와 창 닫기는 트레이로 숨깁니다. 알림 영역 아이콘을 클릭하면 다시 열립니다. 완전히 종료하려면 아이콘 우클릭 → **종료**를 선택합니다.

설정에서 갱신 간격(기본 60초), 항상 위에 표시, 잔여 한도 10% 이하 알림, Windows 시작 시 실행을 변경할 수 있습니다. 자동 시작은 기본적으로 꺼져 있습니다.

## What it shows

- Every quota bucket/window actually returned by Codex: remaining percentage, window duration, and reset time. No hardcoded five-hour/seven-day assumptions.
- Account plan, masked email, and local/SSH connection name.
- Latest returned daily token bucket, lifetime tokens, and recent daily trend.
- Earned reset-credit count when provided. The app only displays these credits; it does not consume them.
- Last successful update and explicit stale/error states. Missing metrics remain unknown rather than zero.
- A duplicate-account notice when the backend supplies enough identity information. No account-wide percentages or totals are added together.

The displayed daily date is the server's bucket date, **not a guaranteed midnight-to-midnight total in your local timezone**. Quota percentage and token activity are separate metrics; the app does not estimate an exact remaining token count or actual subscription spending.

## Architecture and credentials

Each connection uses `codex app-server --listen stdio://` and JSON-RPC:

```text
Local Codex / isolated local profile ── stdio ──┐
Remote Codex ── SSH (existing key) ── stdio ────┼── WPF widget + local cache
Additional accounts ──────────────────────────┘
```

- Queries: `account/read`, `account/rateLimits/read`, and `account/usage/read`.
- Only explicitly initiated isolated-profile login uses `account/login/start`/`cancel`.
- No turns are started, no chats are read, and no AI tokens are spent on monitoring.
- Remote credentials stay on the remote host. No public listening port or remote collector installation is needed. A small app-server process is started through SSH and ended when the monitor closes.
- The app does not read, copy, display, or log `auth.json`, access tokens, refresh tokens, SSH private keys, or raw server diagnostics.
- Up to three sources refresh concurrently. Account services can still throttle reads; increase the refresh interval if needed.

Settings and cached **usage metadata** are local files under `%LOCALAPPDATA%\CodexAccountMonitor`. They contain connection names/paths, email/account identity, percentages, token counts, and update times. They are not encrypted. They never contain login tokens. Do not publish these files or screenshots of your real accounts.

New local login profiles live under `%LOCALAPPDATA%\CodexAccountMonitor\profiles`. Codex owns credential storage and renewal. Existing sessions can expire or be revoked; reconnect/login in the corresponding Codex environment if that happens.

Codex's app-server protocol can change. Tested with local CLI `0.160.0` and remote CLI `0.159.2`; unsupported metrics are shown explicitly. ChatGPT-backed Codex accounts are the intended target. API-key/Bedrock accounts may not provide these subscription metrics.

## Configuration

The UI writes `settings.json` outside this repository. See [settings.example.json](docs/settings.example.json) for a generic example. For SSH, `codexPath` may be an absolute native binary path and `codexHome` must be an absolute remote directory. Empty values use the remote login shell's `codex` and its default profile.

Local Codex is discovered in the desktop app's bundled binaries, native `codex.exe` on PATH, or npm's native binaries. You can also select a native executable path. `.cmd`/`.ps1` launchers are not accepted.

## Build and verify

Requires the .NET 10 SDK on Windows. No third-party NuGet packages are used.

```powershell
dotnet build src/CodexAccountMonitor/CodexAccountMonitor.csproj -c Release
dotnet run --project tests/CodexAccountMonitor.Tests -c Release
dotnet publish src/CodexAccountMonitor/CodexAccountMonitor.csproj -c Release -r win-x64 --self-contained true -o dist
```

Read-only live check (does not start model turns):

```powershell
dotnet run --project tests/CodexAccountMonitor.Tests -c Release -- --live local research-server
```

UI preview and layout verification:

```powershell
.\dist\CodexAccountMonitor.exe --demo
.\dist\CodexAccountMonitor.exe --demo --screenshot C:\Temp\monitor-preview.png
```

`--settings <absolute-path>` loads an alternative settings file for controlled checks. `--live-screenshot <absolute-path>` queries configured accounts, saves the rendered widget, and exits. **Live screenshots are private data and should not be committed.** Demo/screenshot modes do not save connection changes or enable startup registration.

The deterministic checks cover multiple quota buckets, nullable data, account/workspace identity, invalid timestamps, backend-blocked states, safe SSH command construction, out-of-order JSON-RPC responses, RPC errors, and cancellation. CI builds the widget and checks those behaviors. Version tags build a self-contained executable and publish a GitHub release.

## References

- [Official Codex app-server protocol](https://learn.chatgpt.com/docs/app-server)
- [Official Codex authentication and credential storage](https://learn.chatgpt.com/docs/auth)

This is an independent utility, not an official OpenAI application and not a modification of Codex Pulse.

## License

MIT.
