# Mate Engine Mac-X — Roadmap

**Editing restricted to the project maintainers** — currently `freckslaydown-byte` and SuperClaw. Changes land via a reviewed `docs/*` PR only. See [VERSIONING.md](VERSIONING.md) for the branching / backout strategy this roadmap runs on.

---

## Vision

> **Make Mate Engine a first-class macOS citizen** — a desktop companion that stays current, capable, and trustworthy as the platform, the technology, and the people around it change.

The roadmap is not a fixed goal; it is guided by three pillars:

1. **macOS currency** — track every major macOS release. Fix what breaks, adopt what improves: windowing, permissions, Retina / Apple silicon, AppKit & SwiftUI.
2. **Advancing technology** — keep the AI / voice stack current: on-device LLMs, TTS, input methods. Upgrade deliberately, never blindly.
3. **Changing use cases** — follow how people actually use a desk companion: convenience, companionship, accessibility, and shifting social norms.

**Non-negotiables (ground rules):**

- **Privacy first** — never send screen / mouse / keyboard data anywhere. The daemon handshake sends only program name, hostname, and model info.
- **Honor the lineage** — stay mergeable with upstream [`CJackHwang/Mate-Engine-Mac-X`](https://github.com/CJackHwang/Mate-Engine-Mac-X); credit `BNDSer/Mate-Engine-Mac` and `shinyflvre/Mate-Engine`.
- **Everything revertible** — features land as milestones, corrections land as patches, and each one can be backed out with a single `git revert`.
- **Localization is preserved, never regressed** — the app ships 13 locales (Unity Localization), with Japanese and Chinese (simplified + traditional) as priority audiences. Every user-facing change ships with all maintained locales updated; a feature that breaks an existing translation does not land. README stays trilingual (EN / JA / ZH).

---

## Milestones (from the fork, Aug 2026)

The fork starts at `v1.0.0` (2026-08-02). Every milestone lives on its own branch.

| ID | Milestone | Branch | Status |
|----|-----------|--------|--------|
| M1 | SuperClaw daemon handshake — report program + hostname + model on startup and model change | `feature/daemon-handshake` | **DONE** (2026-09-01) |
| M2 | SuperClaw daemon command channel — remote speak via GPT-SoVITS TTS | `feature/daemon-commands` | **CLEANUP NEEDED** — branch still carries the backed-out drag commits; must be cleaned before merge |
| M3 | macOS dev loop — `update_macos.sh` pull / build / kill / install | `main` | **DONE** (2026-09-02) |
| M4 | Licensing & third-party compliance — NOTICE.txt, license index, folder fixes | `docs/licensing` | **DONE** (2026-09-02) |
| M5 | Roadmap & versioning governance — this file + VERSIONING.md | `docs/roadmap` | **IN PROGRESS** |
| M6 | Land the chat/drag revert (`ede57bf5`) so no milestone branch carries backed-out work | `fix/revert-drag-chat` | **QUEUED** |

### Backlog (proposed, unstarted)

| ID | Proposal | Pillar |
|----|----------|--------|
| B1 | macOS version-currency pass — verify and fix on the current macOS release | macOS currency |
| B2 | AI / voice stack modernization — LLM model upgrade path, TTS quality | Advancing technology |
| B3 | Convenience / companionship review — multi-monitor placement memory, accessibility | Use cases |
| B4 | Upstream sync cadence — periodic merge from upstream master | Lineage |
| B5 | Localization preservation pass — audit all 13 locale tables for completeness, QA the core flows (settings, chat, daemon, window controls) in JA + ZH, and stand up a feedback channel for those audiences | Use cases |

### Backed-out changes (logged for history)

| Change | Commits | Backed out | Reason | Lesson |
|--------|---------|------------|--------|--------|
| Chat widget side-follow (bubbles hug model side) | `c837be7c..0de914e3` | `ede57bf5` (2026-09-03) | Diluted milestone work; a behavior change should have been its own fix branch | Experimental UX changes go on `fix/` or `experiment/` branches, never milestone branches |
| Raw drag-to-cursor model placement | `af241665..38cac2ce` | `ede57bf5` (2026-09-03) | Same | Same |

---

## Stay-on-track checklist

Before any change, answer:

1. **Which lane?** A roadmap feature goes to `feature/<milestone>`. A correction to shipped behavior goes to `fix/<slug>`. A "see if it feels better" experiment goes to `experiment/<name>`.
2. **Which pillar?** macOS currency / advancing technology / use cases / lineage. If none of these, defer.
3. **Revertible?** Can it be undone with one `git revert`? If not, split it up.
4. **Documented?** Milestones update this file when they ship. Reverted fixes get logged in the table above.
5. **Localized?** Any user-facing string change updates all 13 locale tables (JA + ZH priority). If a translation can't be provided for a new string, the feature waits — localization is a ship blocker, not a follow-up.