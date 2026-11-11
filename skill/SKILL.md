---
name: synchro
description: Operate the Synchro encrypted package sync — pack committed features, apply incoming packages, resolve conflict scenes. Use when the user asks to sync, pack a snapshot, apply a package, or when a .synchr file arrives.
---

Read `SYNCHRO.md` (next to this file) and follow it exactly for every
Synchro operation: packing, applying, conflict scenes and the key ritual.

Quick command map (full procedures in SYNCHRO.md):

| intent | command |
|--------|---------|
| build a package draft | `synchro pack_draft` (MCP: synchro_pack_draft) |
| declare a feature | `synchro_pack_declare --name ... --description ... --files ...` |
| seal the package | `synchro_pack_seal` |
| apply an incoming package | `synchro apply <file.synchr>` (MCP: synchro_apply_start) |
| inspect a conflict | `synchro scene` / `synchro scene read <path>` |
| resolve a conflict | `synchro scene resolve <path> <drop\|replace\|merge\|resolved>` |
| finalize a scene | `synchro scene commit` |
