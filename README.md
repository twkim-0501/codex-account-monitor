# Codex Account Monitor

> 기존 **[Codex Pulse](https://github.com/pjhun0412/codex-pulse)**를 참고하여 **OpenAI Codex로 다중 계정·SSH 서버 모니터링 용도에 맞게 개조·확장한 개인용 프로그램**입니다. 원작의 아이디어와 UI에 대한 크레딧은 **[pjhun0412](https://github.com/pjhun0412)**에게 있습니다. 이 저장소의 구현은 C#/WPF로 재작성했습니다. 자세한 출처와 제작 방식은 아래에 명시했습니다.

A native Windows widget for monitoring multiple Codex accounts, including accounts signed in on remote SSH servers. Built with WPF, a Win32/GDI taskbar mini widget, and .NET 10. No Electron runtime, telemetry, cloud dashboard, API key, or inference requests.

An adaptation and extension of the Codex Pulse widget concept, developed using OpenAI Codex for personal multi-account and SSH monitoring. Original concept and UI credit: **[pjhun0412/codex-pulse](https://github.com/pjhun0412/codex-pulse)**. The implementation here was rewritten in C#/WPF.

![Widget preview with fictional accounts and example values](docs/preview.png)

![Taskbar mini widget with fictional accounts and example remaining percentages](docs/mini-preview.png)

**[Download the latest Windows executable](https://github.com/twkim-0501/codex-account-monitor/releases/latest)**

## 한국어 · 사용 방법

1. 릴리스에서 `CodexAccountMonitor.exe`를 다운로드하고 실행합니다. Windows 10/11 x64용이며 .NET 런타임을 포함합니다. Codex 앱 또는 Codex CLI는 별도로 설치되어 있어야 합니다.
2. 실행하면 **왼쪽 작업표시줄에 접힌 미니 위젯**이 표시됩니다. 현재 PC의 Codex 로그인 계정을 자동으로 조회합니다. 미니 위젯을 클릭하면 상세 창이 열리고, 다시 클릭하거나 상세 창의 `−`를 누르면 접힙니다. 접어도 미니 위젯은 남아 계속 갱신됩니다.
3. 상세 창의 **+ 연결 → SSH**에서 기존 SSH 별명(예: `research-server`)을 선택하면 서버에 로그인된 Codex 계정을 함께 조회합니다. `ssh research-server`가 키 인증으로 연결되어 있어야 합니다. 비밀번호 입력이나 새 호스트 키 확인은 터미널에서 먼저 완료하세요.
4. 새 계정은 **+ 연결 → 로컬 · 별도 계정으로 로그인**에서 이름을 입력하고 로그인합니다. 현재 Codex 계정과 다른 `CODEX_HOME`을 사용합니다. 새 프로필은 Windows 자격 증명 저장소를 사용합니다.
5. 각 카드의 `···`로 연결을 편집하거나 제거합니다. 연결 제거는 Codex 인증 정보나 원격 파일을 삭제하지 않습니다.

상세 창은 미니 위젯 바로 위에 열리며 드래그와 크기 조절이 가능합니다. `−`와 창 닫기는 상세 창을 접습니다. 오른쪽 아래 알림 영역 아이콘으로도 상세 창을 열고 접을 수 있습니다. 미니 위젯이나 알림 영역 아이콘을 우클릭하면 메뉴가 열리며, 완전히 종료하려면 **종료**를 선택합니다.

설정에서 갱신 간격(기본 60초), 상세 창을 항상 위에 표시, 잔여 한도 10% 이하 알림, 미니 위젯 표시·도킹, Windows 로그인 시 접힌 상태로 시작을 변경할 수 있습니다. 자동 시작은 기본적으로 꺼져 있습니다. 미니 위젯만 숨겼을 때는 알림 영역 우클릭 → **왼쪽 미니 위젯 표시**로 복구할 수 있습니다.

미니 위젯은 앞의 두 연결을 표시합니다. 계정에 한도 창이 여러 개 있으면 그중 가장 낮은 **잔여 비율**을 표시합니다. 연결이 더 많으면 두 번째 줄에 `+개수`를 표시하며 모든 연결은 상세 창에서 볼 수 있습니다. 이전 조회값에는 `이전`, 조회할 수 없는 값에는 `—`를 표시합니다.

작업표시줄 내부 도킹은 **Windows 11의 가운데 정렬된 가로 작업표시줄**을 대상으로 합니다. Windows 10, 왼쪽 정렬, 세로 작업표시줄 또는 도킹 실패 시에는 시작·검색 버튼을 가리지 않도록 작업표시줄 바로 위에 표시합니다. 설정에서 도킹을 해제하면 이 배치를 직접 선택할 수도 있습니다. Explorer 내부 창 구조는 공식 확장 API가 아니므로 Windows 업데이트에 따라 달라질 수 있습니다. 도킹된 미니 위젯에는 항상 위에 표시를 강제하지 않으며 작업표시줄의 숨김 상태를 따릅니다. Explorer가 다시 시작돼 창이 사라지면 재연결을 시도합니다.

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
Remote Codex ── SSH (existing key) ── stdio ────┼── local cache ── Win32 mini widget
Additional accounts ──────────────────────────┘             └── WPF details
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
# Native widget check on an interactive Windows desktop:
.\dist\CodexAccountMonitor.exe --demo --widget-check C:\Temp\monitor-widget-check
```

`--open` opens the detail panel immediately; normal launch starts with the mini widget. `--settings <absolute-path>` loads an alternative settings file for controlled checks. `--live-screenshot <absolute-path>` queries configured accounts, saves the rendered details and a neighboring `mini-preview.png`, and exits. **Live screenshots are private data and should not be committed.** Demo/screenshot modes do not save connection changes or enable startup registration.

The deterministic checks cover multiple quota buckets, nullable data, account/workspace identity, invalid timestamps, backend-blocked states, safe SSH command construction, out-of-order JSON-RPC responses, RPC errors, and cancellation. CI builds the widget and checks those behaviors. Version tags build a self-contained executable and publish a GitHub release.

The local native-widget check verifies actual visible mint pixels, pointer hit testing, menu and detail-opening handlers, collapse/close behavior, overlay mode, and disabling/restoring the mini widget. It records parent/style diagnostics and cropped widget images. Test clicks send messages only to this process's own widget; they do not inject physical mouse input. Verified on the development PC: an actual Explorer child, no topmost style, 10 widget checks and 17 parser/transport checks passed. Physical mouse use, Explorer restart, full-screen applications, and multiple-monitor/DPI changes still need separate manual verification. CI renders both previews without requiring an interactive Explorer taskbar.

## 출처와 제작 방식 · Credits

- **원작 및 UI·사용 방식 참고:** [Codex Pulse — pjhun0412/codex-pulse](https://github.com/pjhun0412/codex-pulse). Windows 작업표시줄에서 Codex 사용량과 시스템 상태를 확인하는 원작 위젯과, 사용자가 제공한 해당 프로그램의 스크린샷을 참고했습니다. 원본의 프로그램 이름, 버전 `0.1.0`, 식별자 `com.codexpulse.widget`는 [원본 설정 파일](https://github.com/pjhun0412/codex-pulse/blob/main/src-tauri/tauri.conf.json)에서 확인할 수 있습니다.
- **개발 도구:** OpenAI Codex를 사용하여 설계, 코드 작성, 테스트, 빌드 및 문서를 작성했습니다.
- **개조·확장 내용:** 여러 Codex 계정의 동시 표시, SSH 서버 계정 조회, 별도 로컬 계정 프로필, 연결별 상태 및 캐시, 잔여 한도 알림을 추가했습니다.
- **작업표시줄 동작 참고:** 원작의 [작업표시줄 자식 창 문제 해결 기록](https://github.com/pjhun0412/codex-pulse/blob/main/docs/incidents/2026-09-08-taskbar-child-window.md)을 참고하여 미니 위젯은 Win32/GDI로, 상세 창은 WPF로 분리해 구현했습니다.

여기서 "개조·확장"은 원작 위젯의 아이디어와 사용 경험을 개인 용도에 맞게 확장했다는 뜻입니다. 이 저장소의 코드는 C#/WPF/.NET 10으로 재작성한 구현이며, 원본 저장소를 직접 수정한 Git fork는 아닙니다. 원본 코드·아이콘·바이너리를 이 저장소에 포함하지 않았습니다. 원작의 아이디어와 UI를 본 프로젝트의 독창적인 창작으로 주장하지 않습니다.

This project credits **pjhun0412's Codex Pulse** for the original widget concept and UI reference. OpenAI Codex was used to adapt that experience for multiple accounts and SSH hosts, including design, implementation, tests, builds, and documentation. This repository contains a C#/WPF rewrite rather than a direct source-code fork; it does not redistribute the original code, icons, or binaries.

### Technical references

- [Official Codex app-server protocol](https://learn.chatgpt.com/docs/app-server)
- [Official Codex authentication and credential storage](https://learn.chatgpt.com/docs/auth)

This is a community utility and is not affiliated with or endorsed by OpenAI or the original Codex Pulse author.

## License

MIT for the implementation in this repository. The original Codex Pulse project remains the work of its respective author; this license does not relicense that project.
