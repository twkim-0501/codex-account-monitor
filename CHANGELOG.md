# Changelog

## 1.3.0

- Added a connection check showing the actual signed-in account, plan, and returned quota before registration.
- Clarified that VSCode/Codex SSH setup and monitor registration are separate; SSH aliases do not identify ChatGPT accounts.
- Renamed the new-connection action to “모니터에 추가” and kept the completion button visible when scrolling.
- Moved optional profile/binary/short-name fields into advanced options; hid SSH inputs for local connections.
- Added in-app help and opened the detail panel on first launch.
- Added beginner installation, Free-account FAQ, SSH setup, update/removal instructions, and an English guide. Public examples use fictional accounts.
- Preserved saved connections and the three-account taskbar layout. Enforced the existing 50-connection limit before saving.

## 1.2.0

- Redesigned the widget as translucent, single-row taskbar chips and a light expandable account list.
- Displayed up to three accounts with a bounded `+N` overflow button and optional short labels.

## 1.1.0

- Added a collapsible left taskbar mini widget with a separate WPF detail panel.

## 1.0.0

- Initial multi-account local/SSH monitor, isolated local login profiles, quota/token parsing, alerts, and portable Windows releases.
