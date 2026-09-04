# Running Mate Engine on Low-Memory / Budget Macs

Guidance for running Mate Engine on resource-light machines — the MacBook Neo
(8 GB), 8–16 GB MacBooks, and any other Mac short on unified memory.

> **Scope note:** the daemon settings (`daemonEnabled` / `daemonHandshakeEnabled`
> / `daemonCommandPollingEnabled`) exist on the `feature/daemon-handshake` and
> `feature/daemon-commands` branches and land in `main` after the M2 cleanup.
> The TTS settings (`ttsApiUrl` etc.) are already present everywhere.
>
> **Need to stand up the TTS server itself (install, reference voice, self-host
> vs cloud)?** See [gpt-sovits-setup.md](gpt-sovits-setup.md) — this doc
> assumes the server exists and focuses on keeping the *client* Mac light; the
> setup doc covers how to get a synthesizer running and which to pick.

---

## TL;DR

- **Browsing, email, and streaming are fine on 8 GB.** The Neo's A18 Pro
  hardware-decodes video; macOS handles these workloads comfortably.
- **The one thing that starves a low-memory Mac is local speech synthesis.**
  GPT-SoVITS plus its Python runtime wants several GB. On 8 GB it starts
  swapping to SSD the moment macOS, a browser, and a synth model are all up.
- **So: keep synthesis off-box.** The app's `ttsApiUrl` is a plain URL — point
  it at a GPT-SoVITS instance running on a beefier machine (LAN or Tailscale)
  and the low-memory Mac only plays back the audio. This is the intended
  design: the shipped code default `ttsApiUrl` is already a remote host.
- **Leave the daemon features off** on light hosts (see below). Both toggles
  off means zero daemon network traffic — verified in code, no requests are
  even constructed.

---

## What's light vs heavy on a low-memory Mac

| Workload | 8 GB | Notes |
|---|---|---|
| Email, web browsing, streaming | ✅ Comfortable | Video is hardware-decoded |
| Running the pet + AI chat | ✅ Fine | Anthropic API is cloud-side; the app itself is lightweight |
| Local GPT-SoVITS synthesis | ⚠️ Rough | Model + Python runtime + macOS + browser → swap |
| Heavy dev / builds / VMs | ❌ Not recommended | 16 GB+ machine territory |

---

## Core principle: keep synthesis off-box

The app never synthesizes voice itself. It POSTs a GPT-SoVITS request to
`ttsApiUrl` and plays back the returned WAV. So the synthesis work can happen
anywhere that speaks the GPT-SoVITS `/tts` protocol:

- **Another Mac on your LAN** (e.g. a Mac Studio)
- **Another machine over Tailscale** (the pattern the shipped defaults use)
- **A cloud-hosted GPT-SoVITS instance** (same protocol, HTTP POST → WAV)

### Pointing the app at a remote TTS

Edit `settings.json` (see *Where settings live* below):

```json
{
  "ttsEnabled": true,
  "ttsApiUrl": "http://<TTS-HOST-IP>:9880/tts",
  "ttsRefAudioPath": "/path/on/the/GPT-SoVITS-host/to/ref.wav",
  "ttsPromptText": "...",
  "ttsPromptLang": "ja",
  "ttsTextLang": "ja",
  "ttsTopK": 15,
  "ttsTopP": 1,
  "ttsTemperature": 1,
  "ttsTextSplitMethod": "cut0"
}
```

**Important:** `ttsRefAudioPath` is a **server-side** path — it is sent to the
GPT-SoVITS host, which reads the reference audio from its own filesystem. The
local Mac never needs (and should not use) paths to files that only exist
locally.

### Exposing GPT-SoVITS to other machines

By default GPT-SoVITS binds to `127.0.0.1` (loopback only), so other machines
cannot reach it. To share it:

1. Start it bound to all interfaces:
   `python api_v2.py -a 0.0.0.0 -p 9880`
   (or the equivalent `--host 0.0.0.0` flag for your setup).
2. Allow it through macOS's firewall (System Settings → Network → Firewall).
3. Point clients at the machine's LAN IP, or — better for privacy — put it on
   a Tailscale network and use the Tailscale IP only.

> Prefer Tailscale over exposing the service on your LAN: the port is then
> reachable only by devices on your private tailnet, and the app's own shipped
> default `ttsApiUrl` already follows this pattern.

---

## Daemon features: leave off on light hosts

The daemon integration has two independent sub-features behind a master
switch:

| Setting | Default | Purpose |
|---|---|---|
| `daemonEnabled` | `false` | master switch |
| `daemonHandshakeEnabled` | `true` | reports program/hostname/model on startup & model change |
| `daemonCommandPollingEnabled` | `true` | polls for remote `speak` commands (2-second interval) |
| `daemonUrl` | `""` | daemon base address, e.g. `http://<DAEMON-HOST-IP>:30051` |

**Recommended for low-memory Macs:**

```json
{
  "daemonEnabled": false,
  "daemonHandshakeEnabled": false,
  "daemonCommandPollingEnabled": false
}
```

Why:

- The handshake only registers the machine; the polling loop only exists to
  receive push `speak` commands. Neither is needed to run the pet itself.
- With **both sub-switches off, the app makes zero daemon network traffic** —
  no handshake, no polling, no ack. The code returns before constructing any
  request (`SaveLoadHandler.cs`, `TryPushDaemonHandshake` /
  `TryPollDaemonCommand`).
- If you still want push-to-speak, point `daemonUrl` at a daemon on a beefier
  host and keep local synthesis off — the `speak` command is just an intent;
  the actual voice work still happens on the GPT-SoVITS host.

---

## Where settings live

- Runtime settings are persisted as `settings.json` in
  `Application.persistentDataPath` (macOS: `~/Library/Application Support/<product>/settings.json`).
- CLI overrides: `--datadir <dir>` and `--savefile <path>`.
- The settings menu can edit these at runtime; on builds where the TTS URL
  field isn't wired into the scene yet, edit `settings.json` directly and
  relaunch.

---

## Gotchas

- **120 s request timeout** — synthesis must complete within that, or the app
  drops the audio. Slow/loaded GPT-SoVITS hosts over slow links can hit this.
- **WAV response required** — response must be uncompressed WAV bytes
  (`media_type: "wav"`); the app loads it as an `AudioClip`.
- **Ref audio is server-side** — see above; the most common misconfiguration.
- **macOS Local Network permission** — macOS may prompt for local-network
  access on first connect to a LAN IP; grant it.
- **VPN/Tailscale edge cases** — the Tailscale IP pattern only works while the
  tailnet is up; a plain LAN IP is the more robust default for always-on use.

---

## FAQ

**Q: My MacBook Neo has 8 GB. Can it run the Mate Engine at all?**
Yes. The app itself is lightweight; just keep synthesis off-box as above.

**Q: Can I point the Mate Engine at the GPT-SoVITS running on my Mac Studio?**
Yes. Expose it (`-a 0.0.0.0` + firewall) or put both on Tailscale, then set
`ttsApiUrl` accordingly.

**Q: Does the app only work with GPT-SoVITS?**
The app speaks the GPT-SoVITS `/tts` protocol (JSON payload → WAV). Any server
implementing that protocol works; OpenAI TTS / ElevenLabs etc. do not, without
a provider wrapper.

**Q: With daemon features off, does anything still poll?**
No network activity at all. Unity's `Update()` still ticks a 2-second timer,
but both methods return before constructing any request.