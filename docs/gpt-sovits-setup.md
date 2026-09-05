# GPT-SoVITS Setup for the Mate Engine (App)

How the Mate Engine (this fork) talks to GPT-SoVITS for TTS voice, how to
configure the app, and how to choose where the synthesizer runs — on the same
Mac, on a server you control, or in the cloud.

> **English-only · 英語のみ · 仅英文** — this document is written in English.
> If you are reading a translation (e.g. via browser auto-translate), copy
> **code blocks verbatim** — never translate them — or your configuration will
> break. コード部分は翻訳せず、そのままコピーしてください。代码块请勿翻译，请原样复制。

> **This guide is for everyone.** Whether you run GPT-SoVITS on the same
> machine, on a dedicated box on your network, or in the cloud, the app
> behaves identically — only the `ttsApiUrl` changes. There is a companion
> guide for low-memory / budget Macs (e.g. MacBook Neo, 8 GB machines):
> [low-resource-config.md](low-resource-config.md).

> **Known gap:** the settings-menu wiring for the TTS fields (`ttsApiUrlInput`,
> `ttsRefAudioPathInput`, `ttsPromptTextInput`, `ttsPromptLangInput`,
> `ttsTextLangInput`, `ttsTopKInput`, `ttsTopPInput`, `ttsTemperatureInput`,
> `ttsTextSplitMethodInput`, `ttsEnabledToggle`) is declared in
> `Assets/MATE ENGINE - Scripts/Settings/SettingsMenu/SettingsHandlerDropdowns.cs`
> but **not wired into the shipped scene** (serialized as `{fileID: 0}`), so
> those fields cannot be edited from the settings UI today. The **volume
> slider** *is* wired. Until the scene wiring lands, configure the endpoint by
> editing `settings.json` (see below) or passing `--datadir` / `--savefile`.

---

## How the app uses TTS

The Mate Engine never synthesizes or generates speech itself. **Voice output
is optional** — if TTS is disabled, the pet is simply silent (text chat keeps
working, since that goes to the Anthropic API directly).

When TTS is enabled, the app does *one* thing:

1. **POST a JSON request** to the endpoint in `ttsApiUrl`
2. **Expect raw uncompressed WAV bytes** back
3. **Play the WAV** through its own audio output

The request payload mirrors GPT-SoVITS's `tts.py synthesize()` exactly —
`text`, `text_lang`, `ref_audio_path`, `prompt_text`, `prompt_lang`, `top_k`,
`top_p`, `temperature`, `text_split_method`, `media_type: "wav"`.

Consequences:

- **Any server that speaks this protocol works** — locally, on another machine
  on your LAN, over Tailscale, or in the cloud. Only the endpoint URL changes.
- **Other TTS providers (OpenAI TTS, ElevenLabs, Apple Speech, ...) do not
  work out of the box** — the app speaks GPT-SoVITS's protocol, not theirs.
- **Synthesis is always external to the app.** The app delegates voice
  generation to whatever `ttsApiUrl` points at — same machine, another machine,
  or the cloud. And `ttsRefAudioPath` is a *path on the GPT-SoVITS host* (see
  below) — the app never reads audio files itself.

---

## Core settings (the ones that matter)

All TTS settings live in `settings.json` (see *Where settings live* at the
bottom). The keys the app actually uses:

| Key | Meaning | Default (code) |
|---|---|---|
| `ttsEnabled` | Master switch for voice output | `true` |
| `ttsApiUrl` | **The endpoint**: `{scheme}://{host}:{port}/tts` | `http://127.0.0.1:9880/tts` (scene) / remote Tailscale IP (class) |
| `ttsRefAudioPath` | **Server-side** path to the reference voice sample on the GPT-SoVITS host | a Linux-path sample |
| `ttsPromptText` | Transcript of the reference sample (used for zero-shot cloning) | Japanese sample text |
| `ttsPromptLang` | Language of the reference transcript | `ja` |
| `ttsTextLang` | Language of the text to synthesize | `ja` |
| `ttsTopK` / `ttsTopP` / `ttsTemperature` | Sampling parameters | `15` / `1` / `1` |
| `ttsTextSplitMethod` | Splitting behavior (`cut0` etc.) | `cut0` |

Minimal working config (same machine, LAN, or cloud — only the URL differs):

```json
{
  "ttsEnabled": true,
  "ttsApiUrl": "http://<TTS-HOST-IP>:9880/tts",
  "ttsRefAudioPath": "/abs/path/on/TTS-host/ref.wav",
  "ttsPromptText": "transcript of the reference audio",
  "ttsPromptLang": "ja",
  "ttsTextLang": "ja",
  "ttsTopK": 15,
  "ttsTopP": 1,
  "ttsTemperature": 1,
  "ttsTextSplitMethod": "cut0"
}
```

> ⚠️ **The single most common misconfiguration:** `ttsRefAudioPath` must be a
> path on the **GPT-SoVITS host's own filesystem**. The server reads it, not
> the Mac. If you point it at a path that only exists on your Mac, synthesis
> fails with a "reference audio not found"-style error even though the URL is
> fine.

---

## Where should the synthesizer run? A decision guide

The protocol is identical everywhere — the app cannot tell the difference.
The choice is really three-way: **this Mac**, **a server you control** (your
own or a rented VM), or **a hosted service**.

| | This Mac (self-host, local) | Server you control (self-host, network) | Cloud |
|---|---|---|---|
| **Where** | GPT-SoVITS on the same machine as the app (needs 16 GB+ RAM headroom) | Your GPU box / Mac Studio / Linux server / rented GPU VM | A GPT-SoVITS hosting service / API provider |
| **Latency & privacy** | Lowest; audio never leaves the machine | Low on LAN/Tailscale; audio stays in your network or your VM | Depends on provider; audio and text traverse the internet |
| **Cost** | Electricity only | Electricity only (or hourly VM cost) | Per-use API pricing, or hourly VM cost |
| **Setup effort** | Medium: install Python env + models once | High: same install, plus bind `0.0.0.0`, firewall, and URL config on every client | Low for a hosted *service*; medium for a VM |
| **Works offline** | Yes | Yes (on your network) | No |
| **Reference voice** | Yours, or any sample on the machine | Yours, or any sample on the box | Whatever the service allows (some hosts restrict cloning) |
| **Maintenance** | You (updates, model re-downloads) | You (updates, GPU drivers) | Provider, mostly — you just relaunch the VM image |
| **Best for** | One well-equipped Mac that only needs to serve itself | A Mac Studio / GPU box you already run; a household with several Macs; the "one host serves every device" pattern | No beefy hardware; occasional use; not wanting to babysit a server |

### When it makes sense

- **Your primary Mac has the headroom (16 GB+)** → run GPT-SoVITS on the same
  machine. Simplest setup, lowest latency, works offline. This is the default
  choice for a fully outfitted Mac.
- **You already run a Mac Studio / GPU box, or have several Macs** → self-host
  on the beefiest one and let every device point at it. This is the pattern the
  app was built for — its shipped defaults already expect a remote synthesizer.
- **Privacy-sensitive voice cloning** → self-host on your own network (or
  Tailscale) — audio never leaves your machines.
- **No beefy hardware anywhere (e.g. an 8 GB MacBook Neo)** → cloud, or a cheap
  rented GPU VM. Don't try to run the model set on 8 GB; see
  [low-resource-config.md](low-resource-config.md).

### The one-synthesizer-for-every-device pattern (recommended for households)

Keep GPT-SoVITS on *one* machine (desktop, Mac Studio, old gaming laptop), and
expose it only to your LAN or a Tailscale network. Every device points
`ttsApiUrl` at it and gets full voice — the synthesis work happens once, and
no other machine needs to be powerful. This is the same pattern the app's
shipped defaults assume.

---

## Standing up your own server (self-host, Unix/macOS flavored)

Full install docs live in the [RVC-Boss/GPT-SoVITS](https://github.com/RVC-Boss/GPT-SoVITS)
repo — this is the compressed path for serving the `/tts` API the app needs.

### 1. Install

Follow the official README for your OS (Python 3.9/3.10 works well; the
project installs a `conda`/`venv` with `requirements.txt` and downloads
models on first run — several GB). The Windows WebUI (`go-webui-v2.bat`) is
the easy path on PC; on macOS/Linux use the Python API entry points.

### 2. Get a reference voice

For zero-shot TTS you need one short clean audio clip (a few seconds to ~1
minute) of the voice you want, plus an exact transcript:

- Record with any phone/DAW; keep it clean (no music, no reverb).
- Trim precisely at the words in the transcript.
- Save e.g. `/srv/gpt-sovits/ref/voice.wav` and its `.txt` transcript
  alongside — you'll give both paths to the app.

### 3. Run the API server — same machine or shared?

Launch the GPT-SoVITS **API** (not just the WebUI), choosing the bind address
to match your deployment:

**Just this Mac (simplest):** bind to loopback — nothing leaves the machine.

```bash
python api_v2.py -a 127.0.0.1 -p 9880
```

**Server for other devices too:** bind to all interfaces so other machines can
reach it.

```bash
python api_v2.py -a 0.0.0.0 -p 9880
```

(Adjust the exact script / flags from the official repo for your version.)
For the shared case, also open the firewall (see *macOS specifics* below) —
and prefer Tailscale over raw LAN exposure if the machine is on one.

Then smoke-test with curl — `127.0.0.1` when on the same machine, `<HOST-IP>`
from another:

```bash
curl -X POST http://127.0.0.1:9880/tts \
  -H "Content-Type: application/json" \
  -d '{"text":"こんにちは","text_lang":"ja","ref_audio_path":"/abs/path/ref.wav","prompt_text":"...","prompt_lang":"ja","top_k":15,"top_p":1,"temperature":1,"text_split_method":"cut0","media_type":"wav"}' \
  --output /tmp/out.wav
```

If you get WAV bytes back, the server is ready. The app then just needs the
URL in `settings.json` — `http://127.0.0.1:9880/tts` for same-machine (the
scene's shipped default), or `http://<HOST-IP>:9880/tts` for network use.
Note the `/tts` suffix in either case.

### 4. macOS specifics

- **Firewall**: allow the GPT-SoVITS process/server through System Settings →
Network → Firewall (and Local-Network access if macOS prompts on first
connect).
- **LaunchAgent**: run it under `launchAgents` (e.g. `com.superclaw.gptsovits`)
so it survives reboots — see this fork's SuperClaw daemon notes on the
`feature/daemon-*` branches for the same pattern.
- **Tailscale** (recommended over raw LAN exposure): put the server and all
  clients on a tailnet; use only `100.x.y.z` Tailscale IPs in `ttsApiUrl`.

---

## Cloud options

### Hosted service

Any provider exposing a GPT-SoVITS-compatible `/tts` endpoint works — point
`ttsApiUrl` at it, put a *server-side* reference path/params the provider
accepts, done. Verify the provider's payload keys match the table above
(transcript, ref audio, sampling params) — most do, some rename fields.

### Rented GPU VM (self-managed)

1. Pick a GPU VM (RunPod/Paperspace/Vast.ai...): Tesla T4 / RTX 3090 / A10
2.0+ is plenty.
2. Install GPT-SoVITS per the official repo — as if it were your own box.
3. Upload your reference voice + transcript to the VM's filesystem; put those
   paths in `ttsRefAudioPath` (they're server-side!).
4. Run `api_v2.py -a 0.0.0.0 -p 9880`; open the VM's firewall port (or, better,
   SSH-tunnel / Tailscale to the VM).
5. Snapshot the VM so you can relaunch in minutes instead of re-installing.

> Privacy note: text and audio are sent to the VM. For sensitive use, choose
> a provider in your jurisdiction or self-host instead.

---

## Where settings live (app side)

- Runtime settings: `settings.json` in `Application.persistentDataPath`
  (macOS: `~/Library/Application Support/<product>/settings.json`).
- CLI overrides: `--datadir <dir>` and `--savefile <path>`.
- Editing: change keys in `settings.json` (TTS URL is *not* in the shipped
  settings-UI scene yet — see the known-gap note at the top) and relaunch the
  app. The app writes these fields back on exit, so edits are preserved.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| No voice at all | `ttsEnabled` false, or URL wrong | Set `true` + correct `ttsApiUrl` |
| "reference audio not found" | `ttsRefAudioPath` is a *local* Mac path | Put the path on the GPT-SoVITS host |
| Request times out (120 s) | Server slow / ref audio huge / far link | Trim ref audio; use a closer host; faster GPU |
| Connection refused | Server bound `127.0.0.1` (and you're connecting from another machine) / firewall | Run with `-a 0.0.0.0`, open firewall, use reachable IP |
| Audio plays wrong voice | Ref audio + prompt mismatch | Re-record ref; fix `prompt_text` to match it exactly |
| Works in WebUI, fails from app | Testing URL vs app URL differ | Append `/tts`; check scheme/host/port match |
| `media_type` unsupported | Server version variance | Keep `wav`; check server logs |

---

## FAQ

- **Does my Mac need to be powerful to use voice?** Not necessarily. If the
  synthesizer runs elsewhere (another machine or the cloud), any Mac — even an
  8 GB one — just plays back audio. If you run GPT-SoVITS on the same machine,
  that machine needs the headroom (16 GB+ RAM recommended). For
  budget-hardware guidance, see [low-resource-config.md](low-resource-config.md).
- **Does TTS run without internet?** Self-hosted, yes (LAN or loopback); cloud
  requires internet to the provider.
- **Can I use my LLM's voice (Anthropic) instead?** No — Anthropic only
  returns text. TTS is separate; the voice pipeline is GPT-SoVITS.
- **Is my voice data shipped anywhere?** Only to whatever `ttsApiUrl` points
  at — self-host keeps it on your network.