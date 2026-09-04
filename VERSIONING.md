# Mate Engine Mac-X — Versioning & Backout Strategy

Editing restricted to the project maintainers (`freckslaydown-byte` + SuperClaw); changes land via a reviewed PR. The roadmap this strategy serves lives in [ROADMAP.md](ROADMAP.md).

---

## Why

Milestones keep the repo's direction legible; fixes keep it healthy. Mixing the two — like the Sep 2026 chat-widget and drag-to-cursor changes that landed on top of daemon work — dilutes both. Every merged change must therefore be **revertible in one step**, which is what the Git-Revert strategy below guarantees.

## Branch lanes

| Lane | Branch | Purpose | Merges to |
|------|--------|---------|-----------|
| Release | `main` | Stable line; release tags `vX.Y.Z` | — |
| Milestone | `feature/<milestone>` | One roadmap milestone per branch, carrying only that milestone's work | `main`, `--no-ff` |
| Fix | `fix/<slug>` | Corrective patch to already-shipped behavior; one concern, one commit | `main`, `--no-ff` |
| Hotfix | `hotfix/<slug>` | Urgent patch to a released tag | `main` + back-port to tag |
| Docs | `docs/<topic>` | Documentation-only work (licensing, roadmap, strategy) | `main` |
| Experiment | `experiment/<name>` | Throwaway prototypes; never merged | deleted when decided |

## Classifying a change

| It's a... | Ask | Lane |
|-----------|-----|------|
| Feature | Adds or extends a capability from the roadmap | `feature/<milestone>` |
| Fix / hotfix | Corrects something already shipped that is wrong | `fix/<slug>` / `hotfix/<slug>` |
| Experiment | "Try this, maybe it feels better" (chat hugs, drag-to-cursor) | `experiment/<name>` |
| Docs | Changes documentation only | `docs/<topic>` |

**Cardinal rule:** milestone branches carry *only* their milestone. Fixes, hotfixes, and experiments never ride along on a milestone branch — and features never sneak into fix branches.

## Supported platforms

| Dimension | Supported | Supported until |
|-----------|-----------|-----------------|
| Architecture | Apple Silicon (M1 and newer) | — |
| macOS | 26 (Tahoe) and newer | Next major + 1, per release review |

Intel Macs are **not** supported, from the first release on. Rationale: macOS 27 (Golden Gate) is Apple Silicon-only — Intel Macs end with Tahoe, and Rosetta 2 is slated for removal in macOS 28 — so "Intel Mac" and "current macOS" will no longer coexist. The fork has no published release yet, so dropping Intel now costs nothing and avoids forever carrying an architecture with no test hardware to verify against. The bug-report template's macOS dropdown mirrors this support list.

## Issues & triage

**Tracker:** all issues live on the fork's own tracker — [`freckslaydown-byte/Mate-Engine-Mac-X/issues`](https://github.com/freckslaydown-byte/Mate-Engine-Mac-X/issues). Bugs in this fork are never filed on the upstream trackers. `CJackHwang/Mate-Engine-Mac-X` remains the fork source and lineage reference, but its Issues/Releases pages are *not* used by this project's users.

**Discussions:** questions, help, and rough ideas go to [`Discussions`](https://github.com/freckslaydown-byte/Mate-Engine-Mac-X/discussions) first; only confirmed defects and roadmap-backed items become issues. `config.yml` routes "Question & Help" there, and the README/CONTRIBUTING point contributors the same way.

**Labels**

| Label | Meaning | Resulting lane |
|-------|---------|----------------|
| `bug` | Shipped behavior is wrong | `fix/<slug>` → revertible merge |
| `hotfix` | Urgent bug in a released tag | `hotfix/<slug>` → merge to `main` + tag patch |
| `enhancement` | New or extended capability | Roadmap backlog → `feature/<milestone>` |
| `localization` | Translation gap or locale regression | Ship blocker — must land in all 13 locales (JA/ZH priority) |
| `macos-version` | Broken/affected by a macOS release | `fix/<slug>` under the macOS-currency pillar |
| `docs` | Documentation defect | `docs/<topic>` |

**Lifecycle**

1. **Report** — user opens an issue using the template from ROADMAP.md.
2. **Triage** — a maintainer labels it, maps it to a lane and a roadmap pillar, and decides whether it is a confirmed defect or needs more info.
3. **Fix** — confirmed defect spawns `fix/<slug>` (one concern, one commit, revertible via `git revert -m 1`). Enhancements enter the roadmap backlog first.
4. **Merge** — `--no-ff` into `main`; the merge SHA is recorded so the change stays a single revertable unit.
5. **Close** — the fix/feature resolves the issue; the maintainer closes it, referencing the merge SHA.
6. **Back out (if needed)** — if the fix proves detrimental, revert the merge (see above). The issue is reopened with a note; the backout is logged in ROADMAP.md's backed-out table.

## Backout strategy: Git Revert

### Fixes and hotfixes

1. One concern, one commit, its own `fix/<slug>` branch.
2. Merge into `main` with `--no-ff` so the merge is a single revertable unit. Note the merge SHA.
3. If the fix proves detrimental, revert the *merge* — this restores the pre-fix behavior without touching anything that landed later:

   ```bash
   git revert -m 1 <merge-sha>
   ```

4. Backing out always happens as an explicit commit on `main`. Never "un-edit" files to undo silently — history stays truthful.

### Experiments

Keep them on `experiment/<name>` and never merge them. Decided against? Delete the branch — nothing ever shipped, so there is nothing to revert.

### Order and conflicts

- Back out the **newest** merged change first when reverting several.
- If a later change conflicts with the revert, keep the later change intact and re-apply the revert hunks by hand (revert-first).
- If a reverted change is later re-promoted via a new merge, revert the new commit with `git revert` again — never `reset`.

### Cheat sheet

```bash
# Back out a merged fix (merge commit)
git revert -m 1 <merge-sha>

# Back out a single direct commit
git revert <sha>

# Re-apply a reverted fix later (decision reversed)
git revert <revert-sha>

# Unpushed branch only: drop the last N commits (destroys work — careful)
git reset --hard HEAD~N
```

## Milestone lifecycle

1. **Propose** — add a backlog row to ROADMAP.md with the milestone and its pillar.
2. **Branch** — `git switch -c feature/<milestone> main`.
3. **Develop** — only that milestone's work, in reviewable commits.
4. **Review** — PR; milestone merges require a maintainer review.
5. **Merge** — `--no-ff` into `main`; record the merge SHA.
6. **Close** — mark the ROADMAP row **DONE** and remove the branch.

## Releases

- `main` is the only line that receives tags: `vX.Y.Z` (currently `v1.0.0` inherited from the fork source; **no release artifacts published on this fork yet**).
- Urgent fixes to a released version: `hotfix/<slug>` → merge to `main` → tag the patch.
- Publish a release only from `main`.

**Release checklist (M7 — first release from this fork)**

1. **Verify `main`** — merge the roadmap (M5) and daemon work (M1/M2) first so the release reflects this fork's direction (see ROADMAP.md).
2. **Tag** — `git tag vX.Y.Z` from `main` (bump from the previously released version; M7 ships `v1.1.0`).
3. **Build** — run `./Tools/build_macos.sh` on a Mac; verify the universal-binary arch check passes.
4. **Package** — `PACKAGE_DMG=1 ./Tools/build_macos.sh` produces `MateEngineX.dmg`; also zip the `.app` (e.g. `ditto -c -k --keepParent`) and name it `MateEngineX-vX.Y.Z-macOS.zip` to match the README.
5. **Document** — note the macOS version tested, the Unity version, and what changed since the previous release.
6. **Publish** — create the GitHub Release on the fork (`freckslaydown-byte/Mate-Engine-Mac-X`), attach both assets, mark **Latest**, keep drafts out.
7. **Verify** — Release page returns 200, both assets download, checksums match (`shasum -a 256`).
8. **Update** — mark M7 DONE in ROADMAP.md; update README "Download & Usage" if artifact names changed.

> Reverting a release: use the tag, not `git reset`. Fix forward with a `hotfix/<slug>` → bump to the next patch. Never delete a release tag that users may have downloaded.

## Docs governance

ROADMAP.md and this file are the steering documents. Editing is restricted to the maintainers (`freckslaydown-byte` and SuperClaw) and lands only through a reviewed `docs/*` PR. Enforced socially today; can be hard-enforced later via GitHub branch protection / CODEOWNERS if needed.