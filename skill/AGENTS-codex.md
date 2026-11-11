# Synchro (Codex agent section)

When the user asks to sync, pack a snapshot, apply a `.synchr` package, or
work a conflict scene — read `SYNCHRO.md` (next to this file) and follow it
exactly for every Synchro operation.

Command map (full procedures in SYNCHRO.md):

| intent | command |
|--------|---------|
| pack (plain-mode: seals directly) | `synchro pack` / `synchro pack --full` |
| build a draft (MCP, git-mode declarations) | synchro_pack_draft |
| declare a feature (MCP) | synchro_pack_declare |
| seal the draft (MCP) | synchro_pack_seal |
| apply an incoming package | `synchro apply <file.synchr>` (MCP: synchro_apply_start) |
| inspect a conflict | `synchro scene` / `synchro scene read <path>` |
| resolve a conflict | `synchro scene resolve <path> <drop\|replace\|merge\|resolved>` |
| finalize a scene | `synchro scene commit` |
