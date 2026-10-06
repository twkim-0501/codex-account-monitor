# SSH 서버의 Codex 계정 연결하기

[README로 돌아가기](../README.md) · [English guide](USAGE.en.md)

이미 VSCode나 Codex 앱에서 서버를 사용하고 있다면 기존 SSH 별명을 그대로 쓸 수 있습니다. **이 모니터에는 별도로 등록해야 합니다.** 서버에 새 프로그램이나 수집기를 설치할 필요는 없습니다.

## 1. Windows에서 SSH 접속 확인

Windows 터미널 또는 PowerShell에서 실행합니다. `research-server`를 실제 사용 중인 SSH 별명으로 바꾸세요.

```powershell
ssh research-server
```

서버의 터미널이 열리면 SSH 접속이 되는 상태입니다. 처음 접속할 때는 서버의 호스트 키를 확인하고, 서버 비밀번호를 매번 입력해야 한다면 기존 SSH 키 인증을 먼저 설정하세요. 키 설정은 서버 관리자나 기존 접속 안내를 따릅니다. 모니터는 비밀번호 입력 창을 띄우지 않습니다.

SSH 별명이 없다면 `%USERPROFILE%\.ssh\config`에 등록할 수 있습니다. 다음은 가상의 예시입니다. 주소·사용자·키 경로는 본인의 환경에 맞게 바꿉니다.

```sshconfig
Host research-server
  HostName 203.0.113.20
  User researcher
  IdentityFile ~/.ssh/id_ed25519
```

모니터는 이 파일의 `Host` 별명을 추가 화면의 후보로 보여줍니다. 후보에 보인다고 등록된 것은 아닙니다. `Include` 파일의 별명 등 목록에 없는 이름은 직접 입력할 수 있습니다.

## 2. 서버에서 Codex 설치와 로그인 확인

아래 명령은 **SSH로 접속한 서버 터미널**에서 실행합니다.

```bash
codex --version
codex login status
```

Codex가 설치되어 있고 원하는 ChatGPT 계정으로 로그인돼 있어야 합니다. 설치가 필요하다면 [공식 시작 안내](https://learn.chatgpt.com/docs/quickstart)를 따릅니다.

로그인이 필요한 원격 환경에서는 [공식 인증 안내](https://learn.chatgpt.com/docs/auth#login-on-headless-devices)의 기기 코드 방식을 사용할 수 있습니다.

```bash
codex login --device-auth
```

안내된 주소를 브라우저에서 열어 로그인하고 코드를 입력합니다. 기기 코드 로그인이 비활성화돼 있으면 ChatGPT 보안 설정 또는 워크스페이스 설정에서 허용해야 할 수 있습니다.

**이미 쓰는 프로필에서 다른 계정으로 로그인하면 그 프로필의 기존 계정도 바뀝니다.** 여러 계정을 유지할 때는 서로 다른 서버 사용자 또는 별도의 Codex 로그인 프로필을 사용하세요. 이 모니터의 SSH 연결 확인은 기존 로그인 상태를 조회하며 로그인 계정을 바꾸지 않습니다.

서버 확인을 마쳤다면 `exit`로 Windows 터미널로 돌아옵니다. 비밀번호 입력 없이 접속되는지도 확인할 수 있습니다.

```powershell
ssh -o BatchMode=yes research-server "codex --version"
```

서버의 로그인 셸에서만 Codex 경로가 설정되는 경우 위 명령은 실패할 수 있습니다. 모니터는 `bash -lc` 로그인 셸을 사용합니다. 필요하면 모니터 고급 설정에 실제 Codex 실행파일 경로를 입력하세요.

## 3. 모니터에 등록

1. 미니 위젯 또는 알림 영역 아이콘을 눌러 상세 창을 엽니다.
2. **+ 계정**을 누르고 표시 이름을 입력합니다.
3. **SSH · 원격 서버의 Codex**를 선택합니다.
4. `research-server` 등 준비한 SSH 별명을 선택합니다.
5. **연결 확인**에서 이메일·플랜·한도를 확인합니다.
6. **모니터에 추가**를 눌러 완료합니다.

![SSH connection form with fictional values](add-ssh-preview.png)

프로젝트 경로를 등록할 필요는 없습니다. VSCode/Codex 앱의 프로젝트 연결과 모니터의 계정 조회는 각각 설정합니다.

## 다른 계정이 보일 때

| 이름 | 의미 |
| --- | --- |
| 모니터 표시 이름 `Research` | 화면에서 구별하기 위한 이름 |
| SSH 별명 `research-server` | Windows SSH 설정의 접속 이름 |
| 서버 사용자 `researcher` | 해당 서버의 Linux 사용자 |
| 연결 확인에 나온 이메일 | 그 사용자의 Codex 프로필에 로그인된 실제 ChatGPT 계정 |

SSH 별명 또는 표시 이름을 바꿔도 ChatGPT 로그인은 바뀌지 않습니다. 서로 다른 별명이 같은 서버 사용자와 같은 Codex 프로필을 가리키면 같은 계정이 조회될 수 있습니다.

별도의 서버 프로필을 이미 사용하는 경우 **고급 설정 → Codex 프로필 폴더 (CODEX_HOME)**에 해당 서버의 절대 경로를 지정합니다. 예: `/home/researcher/.codex-work`. 기본 프로필이면 비워둡니다. 실행파일 경로도 서버의 경로여야 합니다. 프로젝트 경로를 프로필 경로로 입력하지 마세요.

## 추가 도움

연결 확인이 실패하면 [README의 문제 해결 표](../README.md#자주-묻는-질문--문제-해결)를 확인하세요. 문의할 때는 오류 문구·Windows 버전·Codex 버전만 알려주고, 인증 파일·개인 키·토큰은 공유하지 마세요.
