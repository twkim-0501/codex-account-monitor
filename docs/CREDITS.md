# 출처와 제작 방식

[README로 돌아가기](../README.md)

- **원작 및 UI·사용 방식 참고:** [Codex Pulse — pjhun0412/codex-pulse](https://github.com/pjhun0412/codex-pulse). Windows 작업표시줄에서 Codex 사용량과 시스템 상태를 확인하는 원작 위젯과, 사용자가 제공한 해당 프로그램의 스크린샷을 참고했습니다. 원본의 프로그램 이름, 버전 `0.1.0`, 식별자 `com.codexpulse.widget`는 [원본 설정 파일](https://github.com/pjhun0412/codex-pulse/blob/main/src-tauri/tauri.conf.json)에서 확인할 수 있습니다.
- **개발 도구:** OpenAI Codex를 사용하여 설계, 코드 작성, 테스트, 빌드 및 문서를 작성했습니다.
- **개조·확장 내용:** 여러 Codex 계정의 동시 표시, SSH 서버 계정 조회, 별도 로컬 계정 프로필, 연결별 상태 및 캐시, 잔여 한도 알림을 추가했습니다.
- **작업표시줄 동작 참고:** 원작의 [작업표시줄 자식 창 문제 해결 기록](https://github.com/pjhun0412/codex-pulse/blob/main/docs/incidents/2026-09-08-taskbar-child-window.md)을 참고하여 미니 위젯은 Win32/GDI로, 상세 창은 WPF로 분리해 구현했습니다.

여기서 "개조·확장"은 원작 위젯의 아이디어와 사용 경험을 개인 용도에 맞게 확장했다는 뜻입니다. 이 저장소의 코드는 C#/WPF/.NET 10으로 재작성한 구현이며, 원본 저장소를 직접 수정한 Git fork는 아닙니다. 원본 코드·아이콘·바이너리를 이 저장소에 포함하지 않았습니다. 원작의 아이디어와 UI를 본 프로젝트의 독창적인 창작으로 주장하지 않습니다.

This project credits **pjhun0412's Codex Pulse** for the original widget concept and UI reference. OpenAI Codex was used to adapt that experience for multiple accounts and SSH hosts, including design, implementation, tests, builds, and documentation. This repository contains a C#/WPF rewrite rather than a direct source-code fork; it does not redistribute the original code, icons, or binaries.

### Technical references

- [Official Codex app-server protocol](https://learn.chatgpt.com/docs/app-server)
- [Codex Resets public API](https://codex-resets.com/api/docs) · community announcement collection, credited in the widget
- [CodexReset](https://codexreset.org/) · community collection of monitored posts and reply context
- [Official Codex authentication and credential storage](https://learn.chatgpt.com/docs/auth)
- [Microsoft layered-window API and transparency](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow)

This is a community utility and is not affiliated with or endorsed by OpenAI or the original Codex Pulse author.
