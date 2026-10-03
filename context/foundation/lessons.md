# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Call curl.exe, never bare curl, in PowerShell commands

- **Context**: Any command handed to the user to run on this Windows machine (primary shell is PowerShell): live-verification steps, manual success criteria in plans, runbook snippets in `context/deployment/`, and resume/verification instructions during `/10x-implement`.
- **Problem**: In Windows PowerShell, `curl` is an alias for `Invoke-WebRequest`, so curl flags (`-sS`, `-w`, `-f`, `--max-time`, `--retry`) fail or bind to the wrong parameters (e.g. `-w "\nHTTP %{http_code}\n"` bound to `-WebSession` and errored), and `\n` is not an escape in PowerShell strings. It recurred several times in persistence-wiring-smoke Phase 5 live verification, costing a round trip each time.
- **Rule**: Any command handed to the user to run in PowerShell must call `curl.exe` explicitly (with backtick-n, not \n, inside double-quoted -w strings) or use native Invoke-WebRequest/Invoke-RestMethod.
- **Applies to**: plan, plan-review, implement, impl-review
