# The loopback boundary, from the shell's side

`docs/STUDIO_SHELL_BOUNDARIES.md` states the contract and
`internal/frontend/serve/loopback.go` enforces it. This file records what the C#
shell owes that contract, and what it does not — because a boundary that is
assumed is worse than one that is absent.

## Who owns which half

| Step | Owner | Where |
| --- | --- | --- |
| `tcp4 127.0.0.1:0` | kernel | `ListenLoopback` |
| credential minted per launch | kernel | `crypto/rand`, 32 bytes, hex |
| host / origin / token checks | kernel | `NewLoopbackGate`, outermost first |
| handing the credential over | kernel → shell | one `handshake` line on stdout |
| refusing a handshake that is not one | **shell** | `LaunchHandshake.TryParse` |
| carrying the credential on every request | **shell** | `CredentialHandler` |
| naming the exact origin on every write | **shell** | `CredentialHandler` |
| never sending the credential elsewhere | **shell** | `CredentialHandler` |
| putting a lost cookie back | **shell** | `CredentialKeeper` |

The kernel mints the credential; the shell never chooses one. A shell that minted
its own could be made to mint one a configuration file already knows, which is
exactly what "never read from config" exists to prevent.

## What the shell refuses

`LaunchHandshake` mirrors the kernel's own reading of the authority
(`netip.ParseAddrPort`) rather than a looser one:

- **Not JSON, or not an object.** A line that is not a handshake is a kernel that
  did not start, and reading it as an empty handshake would open a window on
  nothing.
- **Version other than 1.** A newer kernel may have changed what the fields mean.
- **A credential shorter than 32 characters.** The kernel writes 32 random bytes
  hex-encoded, so a short one is a truncated read, not a token.
- **A scheme other than `http`.** This shell loads no other.
- **A host that is not an IP literal on the loopback interface.** `localhost` is
  refused on purpose: `Uri.IsLoopback` answers true for it, and a name is exactly
  what a resolver gets to answer for. This is why `Uri.IsLoopback` is not used.
- **An origin that named no port.** The kernel listens on an ephemeral port, so a
  missing one means the line is not one of its handshakes — and it is why the
  parser reads the authority text instead of letting `Uri` fill in port 80.

## What the shell does not do

- **It does not re-implement the gate.** Host, origin and token are checked by the
  kernel on every request, outermost first. Duplicating that check in the shell
  would give two answers to one question, and the one that drifts is the one
  nobody looks at.
- **It does not send the credential to a second origin.** `CredentialHandler`
  refuses the request before it leaves. The alternative — sending it unauthenticated
  and letting the kernel answer 403 — reads like a kernel fault and hides the leak
  that actually happened.
- **It does not treat 401 as a lost cookie.** A 401 belongs to a provider the
  person configured; restoring a cookie in answer to it would hide the failure they
  need to see. Only 403 is the gate refusing this launch's credential.

## The sender check has no counterpart here

Electron verifies `event.sender === win.webContents` on every IPC handler because
its renderer is a web page in another process: a page that ran someone else's
script could otherwise ask for OS capability. The C# shell has no such edge —

- the window is AXAML in this process, so *there is no IPC carrying capability*;
- the page-reachable surface is the kernel's HTTP API, which the gate already
  guards;
- agent-authored UI (an extension surface) reaches the shell only as data the
  kernel rendered, never as a call into `IHostPort`.

So the row is **not covered by a check; it is covered by not having the edge.**
The condition that would create it is recorded here so it is not rediscovered as a
surprise: **the moment a web page runs in this shell** — the agent's browser view
host in `plan_step_25` — that page is another origin's content in this process,
and every capability it can reach needs a sender check of its own. That step
inherits this paragraph.
