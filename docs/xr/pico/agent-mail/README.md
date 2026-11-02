# Agent mailbox

A file channel between the Codex session and the DeepSeek session, because they cannot talk to each other
directly and the owner should not have to relay every question.

## Files

| File | Who writes | Who reads |
|---|---|---|
| `codex-inbox.md` | DeepSeek | Codex |
| `deepseek-inbox.md` | Codex | DeepSeek |

## Protocol

1. **Read your inbox before starting work** and **write your inbox before stopping.** That way neither
   session blocks on the other.
2. **Append only.** Never edit or delete a message someone else wrote. To correct something, append a new
   message that says so — the history is the record.
3. **One message per topic.** Header format:

   ```
   ## M-0007 · DeepSeek → Codex · 2026-11-01 12:40 · STATUS: open
   **Topic:** one line
   <body: what changed, what I need, where the evidence is>
   ```

4. **Reply in the other file** with `Re: M-0007` in the topic and `STATUS: answered`. Then edit *your own*
   message's header to `STATUS: answered` so the mailbox reads cleanly at a glance.
5. **Keep it short.** Decisions, requests, results and pointers. Long analysis belongs in the topic docs
   (`INTERACTION-TEST-RESULTS.md`, `LOCAL-FIRST-TRANSCRIPTION.md`, …), referenced by path.
6. **Never put secrets in a message.** Key names are fine; values are not.
7. Number messages sequentially; check the last number before you append.

## If the mailbox is empty

Nothing is pending. Do not invent a message to fill it.

## Latency, honestly

Neither agent watches this folder. DeepSeek reads it when the owner prompts, or on a scheduled poll if one
is enabled. If something is urgent, tell the owner — a message here may sit unread, and that is a known
limitation rather than a failure.
