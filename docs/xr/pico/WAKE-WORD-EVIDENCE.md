# Wake word — what the recogniser actually hears

Evidence from captured hardware and emulator logs (`Logs/*.log`, 31 files). The wake trigger logs every
attempt as `[WakeDiag] heard='…' nearest='…' similarity=…`, which makes the failure measurable instead of
anecdotal.

## The tally

**16 distinct utterances, 1 match.**

| Heard | Times | Useful as an alias? |
|---|---|---|
| `WELL` | 24 | **No** — a common word; aliasing it would wake on ordinary speech |
| `IMPEWTER` | 1 | Already an alias, and it matched (similarity 1.00) |
| `LOUISE` | 1 | No — not a phonetic neighbour of "computer" |
| `DUDLE` | 1 | No |
| `ODE` | 1 | No |
| `NEIGHBOR` | 1 | No |
| `MISTER JONESON CUT TWO WORD` | 1 | No — the recogniser produced an unrelated phrase |
| `NO`, `WHAT`, `I`, `THERE`, `HE SAID`, `I ASKED`, `WEBS SOMETHING`, `THROUGH A LIGHT SHADE`, `LOAD MARTIAN OUTPOST` | 1 each | No |

## What this means

The failures are overwhelmingly **not near-misses**. Aliases rescue a *misheard version of the same word*
(`pewter`, `impewter`); they cannot rescue `LOUISE` or `MISTER JONESON CUT TWO WORD`, because the model did
not produce anything resembling "computer" — it transcribed different speech.

So the alias list is near the limit of what it can do. The two levers that address the real cause:

1. **Hotwords** — sherpa-onnx decodes against a phrase list with a boost, so the acoustic model produces
   "computer" more often in the first place. This fixes the cause rather than rescuing the output.
2. **A better or keyword-spotting model** — a dedicated KWS model is designed for exactly this task;
   full ASR plus string matching is a workaround. `KwsWakeTrigger` exists in the project as a placeholder.

Secondary: the command window is a fixed 7 s (`commandListenTimeoutSeconds`), which compounds the
problem — a missed wake costs the whole command, not just the word.

## Alias list after this session

`computer, puter, pewter, pudor, peter, impewter, pumpor, pooter, commuter`

`puter` was missing and is the most likely tail capture (the VAD/ASR frequently clips the first syllable).
`pooter` and `commuter` are phonetic neighbours that were not previously covered. No alias was added from
the observed failures because none of them resemble the word — adding them would buy false wakes, not
recall.

## Next measurement

The same `[WakeDiag]` line is the metric: count `matched=True` against total attempts over a fixed set of
ten deliberate utterances, before and after enabling hotwords. Anything that does not move that ratio is
not an improvement, whatever it looks like in isolation.
