---
name: issue-report-breakage
description: File a de-duped bug story about a broken issue-flow skill or workflow — search open issue-flow-bug issues first and recur on a match instead of creating a duplicate. The manual entry point to github.md § Reporting a broken skill. Part of the issue-planning flow.
user_invocable: true
---

# issue-report-breakage

Files (or recurs) a bug story when the issue-flow **machinery** breaks — a skill or workflow
defect, distinct from the *work* failing. This is the manual entry point to the
shared self-healing procedure; the skills and workflows run the same steps automatically on
an unexpected error.

**Read first:** `.claude/rules/github.md` (§ Reporting a broken skill, § Intake,
§ Label inventory).

Arguments: `<skill-or-workflow>` and a short description of what broke (paste the error if
you have it).

## Steps

1. **Fingerprint** the breakage per github.md § Reporting a broken skill — the named skill/workflow + a **normalized** error signature (strip issue-specific numbers, paths, and ids so the same breakage fingerprints the same).
2. **Search first.** `gh issue list --label issue-flow-bug --state open --json number,title,body` and judge whether any open bug is the **same** breakage (same skill + same signature).
3. **Match → recur:** append a `## 🔁 Recurred — {date}` comment (occurrence context + **sanitized** error) and bump the occurrence count in the body. Stop.
4. **No match → file:** `gh issue create` titled `issue-flow bug: {skill} — {signature}`, labelled `issue-flow-bug` + a severity, body = skill, failing step/command, **sanitized** error (never secrets — `NUGET_API_KEY`/`GITHUB_TOKEN`/`*_TOKEN`/`*_SECRET`/`*_KEY`/bearer tokens), the work-issue cross-ref, and the date. Intake (§ Intake — driver reconcile or `/issue-triage`) stamps it `status:inbox`.
5. **Report** whether you filed (#) or recurred (#), and the fingerprint used.

## Notes

- **Sanitize, always.** Secrets and raw payloads never go in the title or body.
- **No recursion.** If filing itself fails, surface the error to the operator and stop — never report the report.
