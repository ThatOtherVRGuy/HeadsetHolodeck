# Plans and specs: how to read these

The files in `plans/` and `../specs/` are **historical design records** written before or during
implementation. They are kept for the reasoning, the provider/format details, and the interface
decisions they capture. They are not a live task list.

Read them with these rules:

- **Unchecked boxes are not a backlog.** A `- [ ]` box means the plan was not updated, not that the
  work is outstanding. Most plans here shipped and were never re-ticked.
- **They are not authoritative about current behaviour.** Where a plan and the code disagree, the
  code plus [`docs/stabilization/STATUS.md`](../../stabilization/STATUS.md) and
  [`docs/stabilization/handoff-06.md`](../../stabilization/handoff-06.md) win.
- **Nothing here is a commitment.** Order work comes from
  [`docs/stabilization/WORK-ORDERS.md`](../../stabilization/WORK-ORDERS.md); deferred ideas live in the
  "remaining gaps" section of the current handoff.
- **One plan is generally one coherent change.** When reusing a plan, check whether its files still
  exist at the paths it names before building on it; paths and namespaces have moved.

Verified-versus-planned status is maintained only in `docs/stabilization/`. If a plan's status
matters, record it there rather than editing the plan.
