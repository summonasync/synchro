# MCP registration snippets

The server binds to ONE target directory (the synced project). Register it
per project on each side. The binary must be on PATH (or use an absolute
path).

## Claude Code

```bash
claude mcp add synchro -- synchro serve --root /absolute/path/to/project
```

or `~/.claude.json`:

```json
{
  "mcpServers": {
    "synchro": {
      "command": "synchro",
      "args": ["serve", "--root", "/absolute/path/to/project"]
    }
  }
}
```

## Codex CLI (`~/.codex/config.toml`)

```toml
[mcp_servers.synchro]
command = "synchro"
args = ["serve", "--root", "/absolute/path/to/project"]
```

## Notes

- transport: stdio (default; no port, no network — the protocol runs over
  the process pipes, matching the zero-trace contract).
- one server instance per project directory.
- tool names are fixed: synchro_status, synchro_pack_draft,
  synchro_pack_declare, synchro_pack_seal, synchro_apply_start,
  synchro_scene_status, synchro_scene_read, synchro_scene_resolve,
  synchro_apply_commit, synchro_log.
- diagnostics: set `SYNCHRO_MCP_DEBUG=1` in the environment to mirror host
  logs to stderr (never stdout — that is the protocol channel).
