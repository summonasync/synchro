```text
      ┌───────┐                            ┌───────┐
      │  dev  │       ▄▄▄▄▄▄▄▄▄▄▄▄▄        │  peer │
      │       ├───────▶│ .synchr │◀───────┤       │
      └───────┘        ▀▀▀▀▀▀▀▀▀▀▀▀▀        └───────┘

███████╗██╗   ██╗███╗   ██╗ ██████╗██╗  ██╗██████╗  ██████╗
██╔════╝╚██╗ ██╔╝████╗  ██║██╔════╝██║  ██║██╔══██╗██╔═══██╗
███████╗ ╚████╔╝ ██╔██╗ ██║██║     ███████║██████╔╝██║   ██║
╚════██║  ╚██╔╝  ██║╚██╗██║██║     ██╔══██║██╔══██╗██║   ██║
███████║   ██║   ██║ ╚████║╚██████╗██║  ██║██║  ██║╚██████╔╝
╚══════╝   ╚═╝   ╚═╝  ╚═══╝ ╚═════╝╚═╝  ╚═╝╚═╝  ╚═╝ ╚═════╝
```

**Synchro — sealed feature sync for machines that share no git.**  
*Work moves as encrypted, traceless `.synchr` packages carried by hand, or
flies repo-to-repo over a local path — feature-level, bidirectional, with
nothing left on the receiving machine.*

`await SummonAsync();` — part of the summoner's kit  
overmind · grimoire · squeezr · **▸ synchro** · syncrowatch · summonasync studio

---

## What it is

Synchro synchronizes code bidirectionally between two sides that share no
git and no infrastructure — a dev shop and its client, or two machines
behind air gaps. The unit of sync is a **feature**, not a diff: each side
packs its work into one encrypted, traceless `.synchr` file, a human
carries it across, and the receiving side's AI agent applies it — silently
when nothing overlaps, through a drop / replace / merge dialogue when it
does. Two repos of one project that *are* reachable by path skip the
package entirely and sync directly, master to slave — see
[two transports](#two-transports).

No shared remote. No daemon. No traces of the sending machine on the
receiving side.

## How it works

1. **`synchro init`** — creates the encrypted local store (`.synchro/`),
   detects the mode, and runs the key ritual: one side generates the channel
   key, the peer adopts it out-of-band. On git repos the store hides itself
   from `git status` via `.git/info/exclude` — `.gitignore` is never touched.
2. **`synchro pack`** — computes changes since the last common point and
   seals them into a single `.synchr` file: AES-GCM, no magic bytes, no
   headers, no machine-identifying metadata — indistinguishable from random
   data.
3. **You carry the file.** Flash drive, mail, carrier pigeon — the transport
   is deliberately human.
4. **`synchro apply`** *(in progress)* — verifies, checks for drift, applies
   non-overlapping features silently and escalates overlapping or
   semantically similar ones to the human: drop, replace, or merge. Merging
   drafts through `git merge-file`; the agent resolves what remains.

## Two transports

**Hand-carry** — `synchro pack` seals a `.synchr` file, a human moves it,
`synchro apply` lands it. For machines that share nothing at all.

**Direct master-slave** — two repos of one project reachable by path (same
machine or a mounted share). One side is the master: it holds the encrypted
store, the full sync history and the per-slave ledger; the slave is a
stateless folder. `synchro sync` exchanges feature deltas bidirectionally;
they land uncommitted on a remembered `feature-sync` branch on the receiving
side. No `.synchr` file is produced on this path — the master's encrypted
store is the only artifact Synchro writes here, and the tool never commits
in either repo (one consented exception: a plain folder joining as a slave
gets `git init` plus a single neutral bootstrap commit).

```bash
# in the master repo — once
synchro init
synchro add slave ../client-repo

# every sync — conflicts ask, Enter accepts master-wins; --yes skips the ask
synchro sync [slave] [--yes]
synchro status [slave]                # both inboxes, ledger hint, last operation
synchro log [slave]                   # sync history
synchro rollback [slave] [--master]   # discard an inbox — tree returns to the base branch
```

The human commits the inbox branches when the landed work looks right;
`synchro ui` covers the same flow in a localhost console.

## Two modes

| Mode | When | Alignment |
|------|------|-----------|
| **git** | the target directory is a real repository | sha alignment ledger — commits carry the feature semantics, diffs come from git |
| **plain** | just a folder, no repo required | hash-drift snapshot chain — zero drift applies straight, drift escalates |

Both modes run on the same encrypted store and the same package format.

## The zero-trace contract

- every byte written on the receiving machine is encrypted and
  indistinguishable from random data — no archive magic, no headers, no
  signatures;
- no metadata identifies the sending machine: no hostname, no user, no
  paths;
- the store hides from the client's `git status` without touching
  `.gitignore`;
- the tool and the agents never commit in a peer's git — the only exception
  is the consented bootstrap commit when a plain folder joins as a slave.

## Commands

- `synchro init` — create the encrypted local store and run the key ritual:
  `--peer <name>` generates the channel key, `--key <hex>` adopts the peer's.
  Bare `synchro init` declares the repo the sync **master**.
- `synchro add slave <path>` — register a slave repo for direct sync: path
  validation, sync-branch name, plain folders join via a consented bootstrap.
- `synchro pack` — seal changes since the last common point; `--full` for a
  first-transfer baseline.
- `synchro scene` — the conflict scene: inspect what overlaps, resolve
  drop / replace / merge, commit the outcome.
- `synchro serve` — stdio MCP server; coding agents drive status, scene and
  apply through it.
- `synchro ui` — localhost web console over the same state: dashboard, sync
  history, inboxes and live conflict scenes; free port by default (`--port`
  to override), loopback only.
- `synchro apply` — *(in progress)* the receiving side of the pipeline.
- `synchro sync [<slave>] [--yes]` — direct bidirectional master-slave sync:
  conflicts ask, Enter accepts master-wins; deltas land uncommitted on the
  remembered sync branch.
- `synchro rollback [<slave>] [--master]` — discard a sync inbox, tree back
  to the base branch.
- `synchro status [<slave>]`, `synchro log [<slave>]` — per-slave config,
  inbox state and sync history for the direct path.
- `synchro install` — register the running binary as the `synchro` command:
  per-user copy, PATH registration, upgrade report on re-run (`--path-fix`
  for a missing Linux PATH entry, `--force` to re-point a foreign name).
- `synchro uninstall` — remove the installed binary, the command name and
  its PATH entries; stores and settings stay untouched.

## Quick start

```bash
# side A — generates the channel key (share the hex out-of-band, once)
synchro init --peer <your-name>

# side B — adopts the peer key
synchro init --key <hex> --peer <their-name>

# pack committed work into a sealed package
synchro pack            # delta since the last common point (git-mode)
synchro pack --full     # first-transfer baseline
```

Requires: git installed (repo initialization is *not* required).
Windows and Linux, single self-contained binary.

## Status

MVP under active development.

- shipped: encrypted store + key ritual · git-mode pack (manifest drafts) ·
  plain-mode pack + `.synchr` sealing · conflict scene with
  drop / replace / merge · stdio MCP server (`serve`) · local web UI (`ui`) ·
  agent skill, E2E suite, cross-platform release builds · direct
  master-slave sync (`add slave`, `sync`, `rollback`, `status`, `log`) with
  dirty landing on a remembered sync branch
- in progress: `apply` (base protocol, drift, silent apply, conflict
  escalation)

## Building from source

Requires the .NET 10 SDK and git.

```bash
git clone https://github.com/summonasync/synchro.git
cd synchro
dotnet build Synchro.slnx
dotnet test Synchro.slnx
dotnet run --project src/Synchro.App
```

Release-style builds, from Ubuntu targeting both desktops:

```bash
scripts/release.sh                    # -> dist/{linux-x64,win-x64} + sha256 manifest
```

Install the built CLI (Linux):

```bash
chmod +x dist/linux-x64/synchro
./dist/linux-x64/synchro install        # add --path-fix when ~/.local/bin is not on PATH
```

Windows (PowerShell): `.\dist\win-x64\synchro.exe install`.

`synchro install` copies the binary into the per-user install location and
registers the `synchro` command on PATH — per-user, no admin, no network:

- Linux: a versioned copy under `~/.local/share/synchro/` behind a
  `~/.local/bin/synchro` symlink. When `~/.local/bin` is missing from PATH,
  rerun with `--path-fix` — a marked PATH block lands in `~/.profile`. Open
  a new shell (or re-source `~/.profile`) and `synchro` resolves.
- Windows: `%LOCALAPPDATA%\Programs\synchro\synchro.exe`, the user PATH
  updated in the registry. Open a new terminal and `synchro` resolves.
- Upgrade is the same command: run `install` from the newer binary and the
  report shows `previous -> new` versions.
- `synchro install --force` re-points an existing name the install did not
  create — with a warning naming where it pointed.
- `synchro uninstall` removes the binary, the command name and its PATH
  entries. `.synchro` stores are never touched.

## Docs

- [`docs/WIN11-SMOKE.md`](docs/WIN11-SMOKE.md) — manual smoke checklist for
  a clean Windows 11 machine
- [`skill/`](skill/) — the agent-neutral operating manual: how a coding
  agent drives synchro over CLI and MCP

## Pairs with syncrowatch

[`syncrowatch`](https://github.com/summonasync/synchrowatch) — a watched
folder bound to a private GitHub repository. When a `.synchr` package lands
in the repository it watches, it surfaces with the ready-to-run `synchro`
command attached. One seal, two ways in.

---

`await SummonAsync();` — part of the summoner's kit  
overmind · grimoire · squeezr · **▸ synchro** · syncrowatch · summonasync studio
