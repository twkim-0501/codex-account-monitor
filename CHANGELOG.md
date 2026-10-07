# Changelog

## 1.4.3

- Clarified the special-reset card title and both horizon labels with “리셋 확률” so its percentages are distinguishable from remaining account quota.

## 1.4.2

- Reorganized README around a direct executable download and first-run installation, keeping advanced setup and credits in dedicated documents. Added version-specific release notes.
- Replaced the text-heavy forecast with a compact card: 24h/48h circular gauges, an evidence-status badge, timing, and time since the latest reset.
- Displayed CodexReset's current experimental probabilities with source attribution, independently of the app's Tibo-post judgment. Rejected stale, malformed, or inconsistent values rather than inventing percentages.
- Moved explanations behind “근거 보기”: a brief calculation explanation, up to three evidence summaries, and optional source links.
- Added source-parser/freshness checks and visual checks for compact defaults, expandable reasons, stale-value suppression, and state changes after opposing poll votes.

## 1.4.1

- Displayed an always-visible forecast in the existing UI: next-24-hour outlook, timing, evidence strength, reasoning, and conditions that would change the assessment.
- Combined explicit promises, structured poll votes, ongoing conditional promises, and current completion/improvement context. Marked inferred windows as estimates and kept poll vote share separate from reset probability.
- Made collected original text and parent/quoted context selectable inside the app; source links are optional.
- Kept public collection on its own timer so longer account refresh settings do not delay forecast updates.
- Added forecast and UI checks for opposing votes, missing/stale context, expired forecast windows, and reading evidence without opening websites.

## 1.4.0

- Integrated banked reset counts and per-credit expiry dates into existing account rows, preserving unknown/capped detail semantics.
- Added a future-only special-reset outlook with explicit promises, conditional promises, and contextual poll/reply hints. Completed one-off signals retire; continuing promises remain until their validity ends.
- Read public announcements and reply context every ten minutes, enriching relevant posts with X's public poll cards. Show missing/stale coverage and source links; do not treat community forecast percentages as calibrated probabilities.
- Added queued, restart-deduplicated notifications for new future signals, observed account quota recovery, reset-time changes, credit increases, and credit expiry within three days/24 hours.
- Preserved account connections and the taskbar widget. Deferred measurement after refreshed WPF templates so wrapped new content correctly resizes the panel.
- Added deterministic judgment/parser/notification checks and layout checks for expanded reset evidence.

## 1.3.1

- Sized the detail panel to measured content, including every expanded account and wrapped text, instead of estimated per-account heights and a fixed 680-pixel cap.
- Recomputed height on open, expand/collapse, refresh, and width changes; kept the panel within the available desktop work area.
- Used scrolling only when the content exceeds the available screen height.
- Added deterministic WPF layout checks for three/eight accounts, same-count refresh, reopen after manual resizing, narrow widths, and constrained-height overflow.

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
