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

- `main` is the only line that receives tags: `vX.Y.Z` (currently `v1.0.0`).
- Urgent fixes to a released version: `hotfix/<slug>` → merge to `main` → tag the patch.

## Docs governance

ROADMAP.md and this file are the steering documents. Editing is restricted to the maintainers (`freckslaydown-byte` and SuperClaw) and lands only through a reviewed `docs/*` PR. Enforced socially today; can be hard-enforced later via GitHub branch protection / CODEOWNERS if needed.