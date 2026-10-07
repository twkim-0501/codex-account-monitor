# 빌드·설정·개인정보 안내

[설치 안내로 돌아가기](../README.md) · [SSH 설정](SSH-SETUP.md) · [리셋 예측 판단 기준](RESET-MONITOR.md)

일반 사용자는 README의 실행 파일을 사용하면 됩니다. 이 문서는 실행파일 경로를 직접 지정하거나 소스를 빌드할 때 참고합니다.

## Codex 실행파일 경로

기본값이면 데스크톱 앱에 포함된 Codex, Windows PATH의 `codex.exe`, npm으로 설치한 Codex의 실제 실행 파일을 순서대로 찾습니다. WSL 안에만 설치된 Codex는 Windows의 로컬 연결에서 직접 찾지 않습니다.

자동으로 찾지 못하면 계정의 `···` → 연결 편집 → **고급 설정 → Codex 실행파일**에 실제 `codex.exe`의 전체 경로를 입력하고 **연결 확인 → 변경 저장**을 누릅니다. 예: `C:\Tools\Codex\codex.exe`. `.cmd`나 `.ps1` 실행 스크립트 대신 실제 실행 파일을 지정해야 합니다.

SSH 연결에서는 서버의 실제 실행 파일 경로를 입력합니다. 로그인 셸의 `codex`가 정상 동작하면 비워두세요. `CODEX_HOME`은 Codex 로그인 프로필 폴더이며 프로젝트 경로가 아닙니다. SSH의 실행파일·프로필 경로를 직접 입력한다면 서버의 절대 경로를 사용합니다.

## 설정과 로그인 프로필

Windows 파일 탐색기의 주소창에 `%LOCALAPPDATA%\CodexAccountMonitor`를 입력하면 저장 폴더가 열립니다.

| 파일·폴더 | 내용 |
| --- | --- |
| `settings.json` | 연결 이름·경로와 화면·알림 설정 |
| `cache.json` | 마지막 조회의 계정 식별 정보, 한도, 토큰 사용량 |
| `reset-state.json` | 현재 유효한 공개 리셋 신호와 중복 알림 방지 정보 |
| `profiles` | 모니터에서 생성한 별도 로컬 로그인 프로필 |

설정 예시는 [settings.example.json](settings.example.json)에 있습니다. 일반적으로는 프로그램 화면에서 설정하고 파일을 직접 수정할 필요가 없습니다.

파일은 암호화되지 않은 로컬 사용량 정보입니다. 설정·캐시와 실제 계정 스크린샷은 공개 저장소에 올리지 마세요. 로그인 자격 증명 저장과 갱신은 Codex가 처리합니다. 별도 로컬 로그인은 Windows 자격 증명 저장소를 사용하고, 원격 로그인은 서버에 유지됩니다.

모니터의 **연결 삭제**는 목록에서 연결만 제거합니다. 기본 Codex 로그인과 원격 서버 파일을 지우지 않습니다. 앱을 제거하면서 저장 폴더까지 직접 삭제하면 연결·캐시와 모니터가 생성한 프로필 폴더도 사라집니다. Windows 자격 증명 저장소 항목은 별도로 남을 수 있습니다.

## 다운로드한 파일 확인

릴리스의 `SHA256SUMS.txt`는 실행 파일의 SHA-256 체크섬입니다. 파일이 내려받는 과정에서 바뀌지 않았는지 비교할 수 있습니다. 현재 릴리스에는 코드 서명이 없습니다.

Windows PowerShell에서 **실행 파일을 저장한 폴더**로 이동한 뒤 실행합니다.

```powershell
Get-FileHash .\CodexAccountMonitor.exe -Algorithm SHA256
```

출력의 `Hash`가 같은 릴리스의 `SHA256SUMS.txt`에 적힌 값과 같으면 일치합니다. 대소문자는 무시해도 됩니다.

## 조회하는 정보와 동작

각 연결에서 `codex app-server --listen stdio://`를 실행하고 JSON-RPC로 `account/read`, `account/rateLimits/read`, `account/usage/read`를 조회합니다. 계정 한도 창과 기간은 서버 응답을 따르며 5시간·7일 등으로 고정하지 않습니다.

모니터는 모델 작업을 시작하거나 대화를 읽거나 초기화권을 소비하지 않습니다. 다른 로컬 계정에 사용자가 직접 로그인할 때만 `account/login/start`와 `cancel`을 사용합니다. 인증 토큰, SSH 개인 키, `auth.json`의 내용을 읽어 화면이나 로그에 내보내지 않습니다.

SSH 서버에는 별도 수집기나 공개 포트를 설치하지 않습니다. 기존 키 인증으로 app-server를 시작하고 모니터 종료 시 정리합니다. 최대 50개 연결을 등록할 수 있고, 동시에 조회하는 연결은 최대 3개입니다. 서비스에서 조회를 제한한다면 갱신 간격을 늘리세요.

일별 토큰 날짜는 서버의 집계 날짜입니다. 사용자 지역의 자정 기준이라고 보장하지 않습니다. 남은 한도 비율을 정확한 잔여 토큰 수나 구독 비용으로 환산하지 않습니다. API 키·Bedrock 계정은 ChatGPT 구독 사용량 지표가 없을 수 있습니다. app-server 프로토콜과 제공 지표는 Codex 버전에 따라 바뀔 수 있습니다.

공개 리셋 소식 수집에는 계정 식별자·이메일·인증 토큰을 보내지 않습니다. 현재 수집 경로와 실험적 확률의 한계는 [리셋 모니터 문서](RESET-MONITOR.md)에 설명했습니다.

## 소스 빌드

Windows와 .NET 10 SDK가 필요합니다. 별도 NuGet 패키지는 사용하지 않습니다. 저장소 루트에서 Windows PowerShell로 실행합니다.

```powershell
dotnet build src/CodexAccountMonitor/CodexAccountMonitor.csproj -c Release
dotnet run --project tests/CodexAccountMonitor.Tests -c Release
dotnet publish src/CodexAccountMonitor/CodexAccountMonitor.csproj -c Release -r win-x64 --self-contained true -o dist
```

빌드와 검사는 종료 코드 0이 정상입니다. 검사에는 `PASS`가 표시되며 실행 파일은 `dist\CodexAccountMonitor.exe`에 생성됩니다.

## 개발용 검증과 예시 화면

같은 저장소 루트에서 Windows PowerShell로 실행합니다. UI 검증에는 가상 계정을 사용합니다.

```powershell
.\dist\CodexAccountMonitor.exe --demo
.\dist\CodexAccountMonitor.exe --demo --screenshot C:\Temp\monitor-preview.png
.\dist\CodexAccountMonitor.exe --layout-check C:\Temp\monitor-layout-three
.\dist\CodexAccountMonitor.exe --demo-accounts 8 --layout-check C:\Temp\monitor-layout-eight
# Explorer 작업표시줄이 있는 대화형 Windows 데스크톱에서만 실행:
.\dist\CodexAccountMonitor.exe --demo --widget-check C:\Temp\monitor-widget-check
```

`--layout-check`는 계정 펼침·접힘, 새로고침, 너비 변경, 화면 높이 제한, 간결한 리셋 카드와 오래된 확률 숨김을 검사합니다. 결과의 모든 항목이 `true`이고 종료 코드가 0이면 통과입니다. `--widget-check`는 자체 위젯에만 테스트 메시지를 보내며 물리 마우스 입력을 주입하지 않습니다.

실제 계정·공개 소식의 조회 검증은 다음과 같습니다. 모델 작업을 시작하지 않습니다.

```powershell
dotnet run --project tests/CodexAccountMonitor.Tests -c Release -- --live local research-server
dotnet run --project tests/CodexAccountMonitor.Tests -c Release -- --feed-live
```

`research-server`는 실제 SSH 별명으로 바꿉니다. `--live-screenshot <전체 경로>`는 설정된 실제 계정을 조회하고 화면을 저장한 뒤 종료합니다. 실제 스크린샷과 설정 파일은 비공개 자료입니다. 데모·스크린샷 모드에서는 연결 변경과 자동 시작을 저장하지 않습니다.

2026-10-07 개발 PC에서 1.4.2의 코어 검사 93개, 3개·8개 가상 계정의 화면 검사 각 19개, 실제 계정 3개의 위젯·연결 검사 18개를 통과했습니다. 실제 마우스 사용, Explorer 재시작, 전체 화면 앱, 여러 모니터·DPI 변경은 별도 수동 확인 대상입니다.

GitHub Actions는 코어 검사, 자체 포함 실행 파일 빌드, 가상 화면 생성과 3개·8개 계정 화면 검사를 수행합니다. `v*` 버전 태그에서는 실행 파일, 문서 ZIP, 체크섬을 릴리스로 게시합니다.
