# Win11 smoke checklist (manual, post-release)

Run on a clean Windows 11 machine. Every unchecked box blocks the release.

## Install

- [ ] Copy `dist/win-x64/synchro.exe` to the machine (flash/mail — any channel).
- [ ] `synchro.exe --version` prints `synchro <version>` with no .NET runtime installed.
- [ ] `synchro.exe` with no args prints usage; exit code 2.

## Self-install and uninstall (018)

Run these from the downloaded exe (Downloads folder), NOT from the install
location — install must be able to copy the running binary.

- [ ] `.\synchro.exe install` — copies itself to
      `%LOCALAPPDATA%\Programs\synchro\synchro.exe`, prints where it copied,
      and tells you to open a new terminal.
- [ ] A NEW terminal resolves `synchro` (`where synchro` points at
      `%LOCALAPPDATA%\Programs\synchro\synchro.exe`, `synchro --version`
      prints the released version). The terminal install ran in still
      resolves nothing — expected.
- [ ] `synchro install` re-run from a newer exe — report shows
      `previous -> new` versions, and `synchro --version` in a new terminal
      answers with the new one.
- [ ] Upgrade over a locked copy: keep a synchro process running from the
      installed path (`synchro serve --root <project>`), run `install` from
      a newer exe — it succeeds through the old/new swap, reports it, and
      the stale exe disappears once the running process exits.
- [ ] `synchro uninstall` — removes the exe and the program folder, deletes
      the user PATH entry (a new terminal no longer resolves `synchro`) and
      leaves `.synchro` stores untouched.

## Git-mode channel (the slurpy flow)

- [ ] `synchro init --peer <client-name>` inside the project repo clone:
      key hex printed once, store `.synchro\` created.
- [ ] `git status` clean — `.synchro\` invisible without touching `.gitignore`.
- [ ] `.git/info/exclude` contains `/.synchro/` and `*.synchr` lines.
- [ ] Commit work, `synchro pack` → `.synchr` package created.
- [ ] `file`/hexdump on the package: no archive magic, no plaintext paths,
      no machine name/user strings.
- [ ] Package transferred to the dev side applies (dev side confirms report).

## Dev-side package inbound (Codex agent flow)

- [ ] `synchro serve --root <project>` registered as MCP server (Codex config).
- [ ] `synchro_status` / `synchro_apply_start` / `synchro_scene_*` respond via MCP.
- [ ] Conflict scene: resolve drop/replace through the agent, `scene commit`.

## Known limitations (MVP, expected)

- Key rotation absent — one static channel key.
- No TUI for the conflict dialogue on Windows — CLI `synchro scene resolve` +
  the MCP tools are the interface.
- Git-mode package applies onto plain stores work; git-mode package onto a
  plain store is rejected by design.
- Concurrent `pack` on both sides: one package in flight is the supported
  cadence; a second pack before the peer applies is rejected as out-of-order
  by design (carry the first package first).
