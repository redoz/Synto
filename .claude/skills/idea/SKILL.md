---
name: idea
description: Live front door to the issue-planning flow — hash out a raw idea WITH you right now, right-sized to the ask (a quick confirm + lean spec for a clear directive, the full superpowers:brainstorming for an open-ended idea), write the spec, file a fresh GitHub issue, and land it at status:spec-review-queued. Use when you have a new idea (no issue exists yet) and want to talk it through in-session rather than async-in-the-issue. Part of the issue-planning flow.
user_invocable: true
---

# idea

The **live, in-conversation** front door to the issue-planning flow. You have a raw idea
and no issue yet; `/idea` hashes it out **with you right now** (not async-in-the-issue),
writes the resulting spec, files the GitHub issue, and advances it to
`status:spec-review-queued` — handing it straight to the async review team.

An "idea" is **either an open-ended design idea or a bounded directive** ("fix/change X
please"). `/idea` **right-sizes itself** to which one it faces — a quick confirm + lean
spec for a clear directive, the full Socratic brainstorm for an open-ended idea — mirroring
rigor.md's **brainstorm → quick** knob applied at the *live* front door. Assume the author
knows what they're asking: don't grind a clear directive through a multi-round design
conversation, but never guess past a real design question either (step 2 sizes it; step 3's
upshift catches a small-sounding ask that proves large).

This is the bootstrap that `issue-brainstorm`'s "Live alternative" assumes but doesn't
provide: that path needs an issue to already exist; `/idea` starts from nothing.

- **`/idea` vs `issue-triage` / `issue-brainstorm`:** those carry the dialogue
  **asynchronously in issue comments** (for the driver and unattended `/loop` runs).
  `/idea` carries it **live in this session** — for when you're here and want to think it
  through together. All three end inside the same flow.

**Read first:** `.claude/rules/github.md` (the `set_status` procedure, the Spec comment
template, § Artifact storage & promotion, § Working-tree hygiene, § Comment templates
plain-telegraph style), `.claude/rules/architecture.md` (domain context for the
brainstorm), and `.claude/rules/rigor.md` (the **brainstorm → quick** knob and its
heuristic, § Upshift, § The floor, and the conservatism rule — the quick-vs-full lever this
skill applies live).

**On tooling failure** (a `gh`/git/tool error, exception, or broken reference — *not* the
brainstorm or the work itself failing): follow github.md § Reporting a broken skill
(search-first; file/recur a de-duped `issue-flow-bug`).

## Steps

1. **Read context.** `.claude/rules/architecture.md`, recent commits, and any source the
   idea touches — the standard brainstorming context-gathering, grounded in Synto's
   domain. Gather enough to *judge the ask* before deciding how much process it needs.
2. **Assess — directive or open-ended idea? (the rigor decision).** An idea reaching this
   front door is one of two shapes; **right-size the process to which one you face**
   (rigor.md, the **brainstorm → quick** knob applied to the *live* front door):
   - **Directive** — the ask already specifies the change **unambiguously** and is
     **bounded**, with **no** open design or behavioral choice ("fix/change X please", a
     CI-image bump, a label rename, a doc/comment fix, a dependency bump, a config-default
     flip). Assume the author knows what they're asking → the **Quick path** (step 3).
   - **Open-ended idea** — a genuine design question, a user-facing / behavioral choice, or
     scope ambiguity. → the **Full path** (step 4).
   - **When in doubt, Full.** Quick is for the *clearly*-trivial; a merely-probably-clear
     ask stays Full (rigor.md conservatism rule — never grind, but never guess).
3. **Quick path (directive).** Skip the full Socratic clarifying-Q&A + 2–3-approaches
   design exploration entirely — the author has already made the call. Instead:
   - **One lightweight confirm — not an interrogation.** State, in a sentence or two, your
     understanding of the ask **and the concrete change you intend** ("here's what I'll
     spec — *X* in `path/to/file`; ok?"), and get the author's OK. **One** check, not a
     round of clarifying questions. This is the right-sized stand-in for brainstorming's
     approval gate — `/idea` never implements, but it still gets a lightweight OK before it
     writes the spec and files the issue.
   - **Verify before going further (upshift).** If the "small" ask turns out **bigger than
     it looked** — real design ambiguity, a decision the author *hasn't* made, schema /
     migration, or it decomposes into several plannable units — **STOP and verify with the
     author before proceeding.** Do **not** silently grind a small ask into a large change
     (rigor.md § Upshift — a relaxation is a hypothesis; contradicting evidence forces
     escalation). On a confirmed design question → fall back to the **Full path** (step 4);
     on genuine multi-unit scope → the **scope guard** (step 5).
   - On the author's OK, continue to the shared tail (steps 6–9). The spec is **lean** and
     carries the **Rigor (adaptive)** note (step 7).
4. **Full path (open-ended idea).** Invoke **superpowers:brainstorming** and run its full
   conversation: clarifying questions one at a time → 2–3 approaches with a recommendation
   → present the design → **your approval** → the spec self-review pass. **Override two of
   its defaults** to fit this flow:
   - its terminal step is **this skill's tail (steps 6–9), not `writing-plans`** — `/idea`
     stops at a reviewable spec and hands off to the issue flow;
   - the spec is written to the flow's **`drafts/`** location (step 7), **not** top-level
     `specs/` (top-level is the home of *approved* specs; the spec-review LGTM promotes it
     there later).
   On your explicit approval of the design, continue to the shared tail (steps 6–9). The
   spec is **full** and carries **no** Rigor (adaptive) note (an open-ended idea is not a
   skip-plan / one-shot candidate).
5. **Scope guard — one issue ↔ one spec.** If the idea (either path) turns out to be
   several independent plannable units, **narrow to one cohesive unit** — recommend
   re-running `/idea` per unit (or `/issue-split` once it's in the flow). Do **not**
   auto-decompose here; decomposition has a dedicated home.
6. **Create the issue.** `gh issue create` with a clear title and a tight body summary in
   **plain telegraph English** (github.md § Comment templates style — name the concrete
   thing, drop the throat-clearing). This yields the tracking number `#N`. (Created here,
   at the end — only after understanding is settled, on either path — so an abandoned
   conversation leaves no orphan stub.) A fresh issue carries no `status:*`; step 8 stamps
   it.
7. **Write & commit the spec.** Write `docs/superpowers/specs/drafts/{YYYY-MM-DD-slug}.md`
   using the superpowers:brainstorming spec conventions — **lean** on the Quick path (relax
   the *depth*, never the *existence*: a spec always exists, even a short one, per
   rigor.md), **full** on the Full path. Immediately after the standard header add
   `> **Tracking issue:** #N`. **On the Quick path only,** add a one-line advisory note
   flagging downstream downscaling — the change is trivial and fully diff-reviewable:
   ```
   > **Rigor (adaptive):** trivial, fully diff-reviewable — skip-plan + one-shot candidate.
   ```
   This is an **advisory signal** to `issue-spec-review` (which still makes its own
   skip-plan call per its heuristic) — **not** the binding `> **Rigor:** one-shot`
   *plan-file* tag, which lives in the plan and is read by `implement-plan` (rigor.md
   § The tag). Omit the note on the Full path. Authoring is **read the code + write the one
   `.md`** — never edit source to "try it" (any tracked-file trial belongs in a throwaway
   `git worktree`, never the primary checkout — github.md § Working-tree hygiene). Stage
   **only that one file**, assert `git status --porcelain` shows nothing but it (stop and
   report if anything else appears — never `git add -A`/`.`/`-u`/`commit -a`), then commit
   + push to main (primary checkout):
   `git add docs/superpowers/specs/drafts/{file}; git commit -m "docs(spec): draft for #N"; git push`
8. **Comment + advance.** Post the **Spec comment** (github.md template — 3–6 bullet
   summary + the permalink to the committed draft) on `#N`, then advance via the
   set-status script (github.md § Setting state — does the label swap *and* the board card
   move in one command), no `blocked`:
   `bash .claude/scripts/set-status.sh N spec-review-queued`  (or the `.ps1` twin from PowerShell)
9. **Report** the issue URL, the spec path, the path taken (Quick / Full), and the final
   state (`spec-review-queued`).

## Notes

- **One front door, two right-sized paths.** Quick (directive) and Full (open-ended idea)
  share the same tail (steps 6–9) and the same terminal state — they differ only in *how
  much process* reaches the spec, mirroring rigor.md's **brainstorm → quick** knob. The
  Quick path is an **added** branch, **not** a replacement: when the ask isn't clearly a
  bounded directive, stay Full.
- **Upshift is self-correcting.** The Quick path is ridden only *while the evidence
  supports it* — the moment the ask proves larger than it looked, it stops and verifies
  with you, then climbs to Full (a real design question) or the scope guard (multiple
  units). Escalating is correct, never a failure (rigor.md § Upshift). This is the guard
  against grinding a 2-line fix into a multi-round design conversation **and** against
  silently spec'ing a large change off a small-sounding ask.
- **Terminal state is the flow, not `writing-plans`.** superpowers:brainstorming normally
  ends by invoking `writing-plans`; here the spec is instead handed to the async flow
  (`issue-spec-review` reviews it, and `issue-plan` writes the plan after the spec is
  approved — unless spec-review *skip-plans* it, which the Quick path's Rigor note invites).
  Do **not** invoke `writing-plans` on either path.
- **Deliberately out of scope (YAGNI for the POC front door):** auto-decomposition into
  epics + children (use `/issue-split`), the `manual` brake (a brand-new issue has no
  labels, so there is nothing to brake — maximum rigor is reached instead by *staying
  Full*), and async comment Q&A (that's `issue-brainstorm`). Keep `/idea` single-purpose.
- **Dialogue handler.** Like the other front-door skills, `/idea` *is* the conversation —
  the github.md unaddressed-comment guard does not apply (there is no prior issue thread to
  step past; the issue doesn't exist until step 6).
