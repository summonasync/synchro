# Synchro — agent operating manual

Synchro syncs code between two sides that share no git: work travels as
encrypted `.synchr` packages, carried by humans. You are the agent operating
one side. Your job: pack features correctly, apply incoming packages safely,
and escalate every ambiguous decision to the human.

This manual is agent-neutral: Claude, Codex and any MCP-capable agent follow
the same steps. Commands exist as both CLI (`synchro ...`) and MCP tools
(`synchro_pack_draft`, `synchro_apply_start`, ...). The steps are identical.

## Golden rules

1. **Commit-first.** Never pack a dirty tree. Commit the work, then pack
   (git-mode; plain-mode targets have no git — skip this rule there).
2. **Never write to the peer's git.** Their repository is read-only for you.
3. **One package in flight.** After packing, wait until the peer confirms
   (their next package builds on your position) before packing again.
4. **The channel key is sacred.** It is printed once during `init` and never
   again. Never transmit it over the sync channel — the human carries it
   out-of-band, exactly like a package.
5. **Never decide conflicts alone.** Textually clean auto-merge is fine;
   every semantic decision (drop / replace / merge of overlapping intent)
   belongs to the human.

## Setup (once per side)

```bash
# side A — generates the channel key and prints the hex ONCE
synchro init --peer <name-of-side-B>

# side B — adopts the key the human carried over
synchro init --key <hex> --peer <name-of-side-A>
```

If the store already exists you will be told. Do not re-init.

## Packing a feature batch

CLI vs MCP: the draft/declare/seal three-step flow is **MCP-only**
(`synchro_pack_draft` → `synchro_pack_declare` → `synchro_pack_seal`).
The plain CLI `synchro pack` seals immediately — plain-mode names the
package itself, git-mode writes a draft JSON and expects the MCP flow for
the full declaration step.

1. **Commit-first** (git-mode): `git status` must be clean or you warn the
   human and pack committed state only.
2. **Build the draft** — MCP: `synchro_pack_draft` (add `full=true` for the
   very first baseline). Read the response: changed files, commit metadata,
   warnings. CLI: `synchro pack` (plain-mode seals directly; git-mode
   writes a draft JSON).
3. **Declare features** (MCP flow) — this is your semantic job. Group the
   changed files into features the way a reviewer would describe them. For
   each feature:
   `synchro_pack_declare --name "<short-name>" --description "<what it does, one clear sentence>" --files "<comma-separated paths>"`.
   Every changed file must belong to exactly one feature. The tool rejects
   unknown paths, duplicate feature names and double-declared files.
4. **Seal** (MCP flow): `synchro_pack_seal` — seals the draft. The output
   names the `.synchr` file. Hand it to the human for physical transfer.
5. If nothing changed, the pack fails with "nothing changed" — that is a
   normal, healthy answer. Do not retry.

## Applying an incoming package

1. `synchro apply <package.synchr>` — read the report carefully.
   - `already applied` → nothing to do.
   - errors mention "out-of-order", "lost package" or "no baseline" → tell
     the human exactly what the message says; do not improvise.
   - `applied: N files` with no conflicts → done. Report what changed.
   - `CONFLICTS: K path(s) staged` → go to the conflict procedure.
2. **Conflict procedure:**
   a. `synchro scene` — list the conflicted paths and their state.
   b. For each path: `synchro scene read <path>` shows theirs / base / ours.
      Read all three. Understand what each side intended.
   c. **Semantic matching** — before anything else, answer:
      *Is the incoming feature the same work someone already did locally?*
      Compare the incoming feature's description and files against the local
      drift. Similar intent, different code → this is a duplicate-feature
      conflict; surface it to the human explicitly.
   d. **Escalation dialogue.** Present the human: what theirs does, what ours
      does, your recommendation, and the three options —
      *drop* (keep ours, ignore incoming), *replace* (take theirs,
      deletion-conflict: delete local), *merge* (three-way draft).
      Auto-merge only when the text merges cleanly and the intent is
      obviously compatible; anything ambiguous goes to the human.
   e. Apply the human's choice: `synchro scene resolve <path> drop|replace`
      — or `merge`, then edit the markers in the working file, then
      `synchro scene resolve <path> resolved`.
   f. When every path is resolved: `synchro scene commit`.
3. **Report to the human** what was applied, what was resolved and how.

## The return trip

After applying and (git-mode) committing the merged state, pack again
(`synchro pack_draft` → declare → `synchro_pack_seal`) and hand the package
back. Plain-mode first outbound package is a full baseline; later ones are
deltas — the tool handles this, you just follow the same steps.

## If something is on fire

- wrong key / corrupted data → the human carries the wrong artifact or a
  broken transfer; ask them to re-transfer.
- pending scene blocks applies → finish the scene first (see conflict
  procedure) or, if the human wants to abandon it, that is a human decision.
- "package builds on X but this store knows the peer at Y" → packages were
  carried out of order or one was lost; the human finds the missing one.
