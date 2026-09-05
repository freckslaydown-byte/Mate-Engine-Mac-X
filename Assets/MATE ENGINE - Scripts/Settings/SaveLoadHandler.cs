using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Text;
using UnityEngine.Networking;

public class SaveLoadHandler : MonoBehaviour
{
    public static SaveLoadHandler Instance { get; private set; }

    public SettingsData data;

    // Multi-Instance Variablen
    private static string fileName = "settings.json";
    private static string customDataDir = null;

    private string BaseDir => string.IsNullOrEmpty(customDataDir)
        ? Application.persistentDataPath
        : Path.Combine(Application.persistentDataPath, customDataDir);

    private string FilePath => Path.Combine(BaseDir, fileName);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Kommandozeilen-Argumente lesen
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--savefile", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                fileName = args[i + 1].Trim('"');

            if (args[i].Equals("--datadir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                customDataDir = args[i + 1].Trim('"');
        }

        LoadFromDisk();
        ApplyAllSettingsToAllAvatars();

        var theme = FindAnyObjectByType<ThemeManager>();
        if (theme != null)
        {
            theme.SetHue(data.uiHueShift);
            theme.SetSaturation(data.uiSaturation);
        }


        var limiters = FindObjectsByType<FPSLimiter>();
        foreach (var limiter in limiters)
        {
            limiter.targetFPS = data.fpsLimit;
            limiter.ApplyFPSLimit();
        }

#if UNITY_STANDALONE_OSX
        // 启动时不再把窗口强制设为显示器原生像素分辨率（Retina 下 Display.main.systemWidth/
        // systemHeight 是像素、窗口却按点缩放，会导致开屏窗口高度超出屏幕）。改为延迟到首帧
        // 把窗口调整到主显示器可见工作区大小：宽度保持全屏、高度自适应可见区域。
        StartCoroutine(FitWindowToVisibleScreen());
#endif
    }

#if UNITY_STANDALONE_OSX
    // Sizes the window to the primary display's visible work area (in points) so
    // the startup window/popup never overflows past the macOS menu bar or dock.
    // Waits up to two seconds for UniWindowController to report a real window size.
    private System.Collections.IEnumerator FitWindowToVisibleScreen()
    {
        Kirurobo.UniWindowController uwc = null;
        Vector2 size = Vector2.zero;
        for (int i = 0; i < 120; i++)
        {
            uwc = Kirurobo.UniWindowController.current;
            if (uwc != null)
            {
                size = uwc.windowSize;
                if (size.x > 0f && size.y > 0f) break;
            }
            yield return null;
        }
        if (uwc == null || size.x <= 0f || size.y <= 0f) yield break;

        RectInt primary = MacWindowHelper.GetPrimaryMonitorRect();
        var monitors = MacWindowHelper.GetMonitors();
        int idx = monitors != null ? monitors.IndexOf(primary) : -1;
        if (idx < 0) idx = 0;
        int vx = primary.x, vy = primary.y, vw = primary.width, vh = primary.height;
        try { MacSystemBridge.MacSys_GetScreenVisibleRect(idx, out vx, out vy, out vw, out vh); }
        catch (System.Exception) { }
        if (vw <= 0 || vh <= 0) { vw = primary.width; vh = primary.height; }
        vw = Mathf.Min(vw, primary.width);
        vh = Mathf.Min(vh, primary.height);

        uwc.windowSize = new Vector2(vw, vh);
        float screenH = MacWindowHelper.GetGlobalScreenHeight();
        // AppKit origin is bottom-left, Y up: place the window's top-left at the
        // visible area's top-left.
        uwc.windowPosition = new Vector2(vx, screenH - (vy + vh));
    }
#endif

    // Speichern
    public void SaveToDisk()
    {
        try
        {
            string dir = Path.GetDirectoryName(FilePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(FilePath, json);
            Debug.Log("[SaveLoadHandler] Saved settings to: " + FilePath);
        }
        catch (Exception e)
        {
            Debug.LogError("[SaveLoadHandler] Failed to save: " + e);
        }
    }

    // Laden
    public void LoadFromDisk()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                string json = File.ReadAllText(FilePath);
                data = JsonConvert.DeserializeObject<SettingsData>(json);
            }
            catch
            {
                data = new SettingsData();
            }
        }
        else
        {
            data = new SettingsData();
        }
        MigrateAfterLoad();
    }


    [Serializable]
    public class SettingsData
    {
        public enum WindowSizeState { Normal, Big, Small }
        public WindowSizeState windowSizeState = WindowSizeState.Normal;

        public float soundThreshold = 0.1f;
        public float idleSwitchTime = 10f;
        public float idleTransitionTime = 1f;
        public bool enableDanceSwitch = true;
        public float danceSwitchTime = 15f;
        public float danceTransitionTime = 2f;

        // ── 舞蹈选择 ──────────────────────────────────────────────────────────
        // Female Animator blend tree 共有 20 个舞蹈动作（threshold 0-19）
        // danceClipCount : 自动循环时使用的舞蹈数量上限，范围 1-20
        // pinnedDanceIndex : -1 = 自动循环，0-19 = 固定到指定编号的舞蹈
        // ─────────────────────────────────────────────────────────────────────
        public int danceClipCount = 20;
        // -1 = auto cycle, 0~(danceClipCount-1) = pin to specific dance
        public int pinnedDanceIndex = -1;
        public float avatarSize = 1.0f;
        public bool enableDancing = true;
        // true = dance while a system player outputs audio (macOS SCK capture);
        // false = manual, enableDancing on = dance immediately.
        public bool followMusic = true;
        public bool enableMouseTracking = true;
        public int fpsLimit = 60;
        public bool isTopmost = false;

        public List<string> allowedApps = new();
        public bool bloom = true;
        public bool dayNight = true;

        public bool enableParticles = true;
        public float petVolume = 1f;
        public float effectsVolume = 1f;
        public float menuVolume = 1f;
        public float ttsVolume = 1f;

        public float headBlend = 0.7f;
        public float eyeBlend = 1f;
        public float spineBlend = 0.5f;

        public bool enableHandHolding = true;
        public bool enableWindowSitting = true;
        // "auto" = snap to both edges, "up" = top edge only, "down" = bottom edge only
        public string windowSitEdge = "auto";
        public bool ambientOcclusion = true;

        public float uiHueShift = 0f;
        public float uiSaturation = 1.0f;

        public bool enableDiscordRPC = true;

        public bool tutorialDone = false;

        public string selectedLocaleCode = "en";
        public bool enableIK = true;

        public int bigScreenScreenSaverTimeoutIndex = 0;
        public bool bigScreenScreenSaverEnabled = false;
        public float windowSitYOffset = -0.02f;
        // Runtime-tunable cliff occluder depth (⌘+[ / ⌘+]). offsetSet distinguishes
        // "never tuned" (use the Inspector value) from an explicit saved value.
        public bool windowSitCliffOffsetSet = false;
        public float windowSitCliffOffset = 0f;

        public Dictionary<string, float> lightIntensities = new();
        public Dictionary<string, float> lightSaturations = new();
        public Dictionary<string, float> lightHues = new();
        public Dictionary<string, bool> groupToggles = new();

        public Dictionary<string, bool> modStates = new();
        public int graphicsQualityLevel = 2;
        public Dictionary<string, bool> accessoryStates = new();

        public bool startWithWindows = false;
        public bool enableRandomMessages = false;

        public string selectedModelPath = "";
        public int contextLength = 4096;
        public bool enableHusbandoMode = false;
        public bool enableAutoMemoryTrim = false;

        // Anthropic LLM settings
        public string llmBaseUrl = "";
        public string llmAuthToken = "";
        public string llmModel = "claude-sonnet-4-6";
        public string llmSystemPrompt = "你是一个简洁、自然的对话助手。回答尽量直接、清楚，适合朗读。";
        public int llmMaxMessages = 20;
        public int llmMaxTokens = 1024;

        // SuperClaw daemon handshake: when enabled, the program pushes a handshake
        // payload (program name, hostname, loaded model info) to the daemon at
        // startup and whenever the model/endpoint configuration changes.
        // daemonUrl is the daemon base address, e.g. "http://192.168.1.50:8080";
        // the handshake is POSTed to {daemonUrl}/handshake.
        public bool daemonEnabled = false;
        public string daemonUrl = "";
        // Optional shared secret for the command channel. When non-empty, the app
        // sends it as X-SuperClaw-Token on command polls/acks, and the daemon
        // requires it (HS_TOKEN). Leave empty for no auth (LAN trust).
        public string daemonToken = "";
        // Independent sub-switches surfaced in the settings UI. Both default
        // true so existing saves that only set daemonEnabled=true keep working.
        public bool daemonHandshakeEnabled = true;
        public bool daemonCommandPollingEnabled = true;

        // GPT-SoVITS TTS settings
        public string ttsApiUrl = "http://100.75.53.37:9880/tts";
        public string ttsRefAudioPath = "/media/zichen/E/workspace/GPT-SoVITS/参考音频/yanami1.mp3";
        public string ttsPromptText = "物申す必要が生じただけなの。ほら、うちのクラスのツワブキ祭の企画、準備が始まったでしょ?";
        public string ttsPromptLang = "ja";
        public string ttsTextLang = "ja";
        public int ttsTopK = 15;
        public float ttsTopP = 1f;
        public float ttsTemperature = 1f;
        public string ttsTextSplitMethod = "cut0";
        public bool ttsEnabled = true;

        public int settingsVersion = 0;
        public bool alarmsEnabled = true;
        public bool enableMinecraftMessages = false;

        public string selectedParticleTheme = "Standard";
        public bool enableFeedSystem = false;
        public bool enableRandomAvatar = false;

        public bool enableLocomotion = false;


        //ALARM
        [Serializable]
        public class AlarmEntry
        {
            public string id;
            public bool enabled;
            public int hour;
            public int minute;
            public byte daysMask;
            public string text;
            public long lastTriggeredUnixMinute;
        }

        public List<AlarmEntry> alarms = new List<AlarmEntry>();

        //Timer
        [Serializable]
        public class TimerEntry
        {
            public string id;
            public bool enabled;
            public int hours;
            public int minutes;
            public int presetSeconds;
            public bool running;
            public long targetUnix;
            public string text;
        }

        public List<TimerEntry> timers = new List<TimerEntry>();


    }
    //ALARM
    void MigrateAfterLoad()
    {
        if (data.timers == null) data.timers = new List<SettingsData.TimerEntry>();
        if (string.IsNullOrEmpty(data.selectedParticleTheme)) data.selectedParticleTheme = "Standard";
        if (data == null) data = new SettingsData();
        if (data.alarms == null) data.alarms = new List<SettingsData.AlarmEntry>();
        if (data.settingsVersion < 1)
        {
            data.settingsVersion = 1;
            SaveToDisk();
        }
        if (data.settingsVersion < 2)
        {
            data.settingsVersion = 2;
            SaveToDisk();
        }
        if (data.settingsVersion < 3)
        {
            data.settingsVersion = 3;
            SaveToDisk();
        }
    }

    // ── SuperClaw daemon handshake ────────────────────────────────────────────
    // Reports program name, hostname and loaded-model info to a daemon on the
    // LAN (e.g. running on the SuperClaw box). Only active when daemonEnabled is
    // true and daemonUrl is set. Pushes on startup and whenever the model /
    // endpoint / config signature changes; retries until the daemon acks.
    private const float DaemonPollInterval = 2f;
    private float daemonPollTimer;
    private string lastHandshakeSignature;
    private bool commandPollInFlight;

    void Update()
    {
        if (data == null) return;
        daemonPollTimer -= Time.unscaledDeltaTime;
        if (daemonPollTimer > 0f) return;
        daemonPollTimer = DaemonPollInterval;
        TryPushDaemonHandshake();
        TryPollDaemonCommand();
    }

    string DaemonHandshakeSignature()
    {
        return (data.daemonEnabled ? "1" : "0") + "|" + (data.daemonHandshakeEnabled ? "1" : "0")
            + "|" + data.daemonUrl + "|" + data.llmModel + "|" + data.llmBaseUrl;
    }

    void TryPushDaemonHandshake()
    {
        if (!data.daemonEnabled || !data.daemonHandshakeEnabled || string.IsNullOrEmpty(data.daemonUrl))
        {
            lastHandshakeSignature = null;
            return;
        }

        string sig = DaemonHandshakeSignature();
        if (sig == lastHandshakeSignature) return;
        StartCoroutine(SendDaemonHandshakeCoroutine(sig));
    }

    System.Collections.IEnumerator SendDaemonHandshakeCoroutine(string signature)
    {
        var model = new Dictionary<string, object>
        {
            ["name"] = string.IsNullOrEmpty(data.llmModel) ? "unknown" : data.llmModel,
            ["endpoint"] = data.llmBaseUrl,
            ["maxTokens"] = data.llmMaxTokens
        };

        var payload = new Dictionary<string, object>
        {
            ["protocol"] = "mate-engine.daemon.handshake",
            ["protocolVersion"] = 1,
            ["programName"] = string.IsNullOrEmpty(Application.productName) ? "Mate Engine" : Application.productName,
            ["hostname"] = GetHostname(),
            ["model"] = model,
            ["sentAtUtc"] = DateTime.UtcNow.ToString("o")
        };

        string json = JsonConvert.SerializeObject(payload);
        string url = data.daemonUrl.TrimEnd('/') + "/handshake";

        using (var req = new UnityWebRequest(url, "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 3;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success &&
                req.responseCode >= 200 && req.responseCode < 300)
            {
                lastHandshakeSignature = signature;
                Debug.Log("[SaveLoadHandler] Daemon handshake sent: " + json);
            }
            else
            {
                // Retry on the next poll; signature is only remembered on success.
                Debug.LogWarning("[SaveLoadHandler] Daemon handshake failed (" + req.responseCode + " " + req.error + "), will retry.");
            }
        }
    }

    static string GetHostname()
    {
        try { return Dns.GetHostName(); } catch { }
        try { return Environment.MachineName; } catch { }
        return "unknown";
    }

    // ── SuperClaw daemon command channel ────────────────────────────────────
    // Polls the daemon for commands (e.g. "speak") and executes them. This lets
    // SuperClaw drive the app to talk via its configured TTS. Data stays local;
    // only the command payload travels over the LAN daemon link.
    string ProgramNameForDaemon()
    {
        return string.IsNullOrEmpty(Application.productName) ? "MateEngineX" : Application.productName;
    }

    string DaemonAuthToken() => string.IsNullOrEmpty(data.daemonToken) ? null : data.daemonToken;

    void TryPollDaemonCommand()
    {
        if (!data.daemonEnabled || !data.daemonCommandPollingEnabled || string.IsNullOrEmpty(data.daemonUrl)) return;
        if (commandPollInFlight) return;
        commandPollInFlight = true;
        StartCoroutine(PollDaemonCommandCoroutine());
    }

    System.Collections.IEnumerator PollDaemonCommandCoroutine()
    {
        string url = data.daemonUrl.TrimEnd('/') + "/commands/poll?client=" + Uri.EscapeDataString(ProgramNameForDaemon());
        using (var req = UnityWebRequest.Get(url))
        {
            if (DaemonAuthToken() != null) req.SetRequestHeader("X-SuperClaw-Token", DaemonAuthToken());
            req.timeout = 3;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                commandPollInFlight = false;
                yield break; // daemon may be temporarily unreachable; retry next poll
            }

            try
            {
                var obj = Newtonsoft.Json.Linq.JObject.Parse(req.downloadHandler.text);
                var cmd = obj["command"];
                if (cmd != null && cmd.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                {
                    string id = (string)cmd["id"];
                    string type = (string)cmd["type"];
                    string text = (string)cmd["text"];
                    string status = "failed";
                    if (type == "speak" && !string.IsNullOrEmpty(text))
                    {
                        status = ExecuteSpeakCommand(text) ? "done" : "failed";
                    }
                    StartCoroutine(SendDaemonCommandAck(id, status));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveLoadHandler] Could not parse daemon command: " + e.Message);
            }
        }
        commandPollInFlight = false;
    }

    bool ExecuteSpeakCommand(string text)
    {
        if (data != null && !data.ttsEnabled)
        {
            Debug.LogWarning("[SaveLoadHandler] Daemon speak ignored: ttsEnabled is false.");
            return false;
        }
        var tts = UnityEngine.Object.FindAnyObjectByType<SoVITSTTSHandler>();
        if (tts == null)
        {
            // Fallback: create a handler so the command still works even if the
            // scene's component is inactive/missing at runtime. SoVITSTTSHandler.Awake
            // adds an AudioSource, and DontDestroyOnLoad keeps it alive across scenes.
            Debug.LogWarning("[SaveLoadHandler] SoVITSTTSHandler not found in scene; creating one.");
            var go = new GameObject("SuperClawTTS");
            tts = go.AddComponent<SoVITSTTSHandler>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }
        Debug.Log("[SaveLoadHandler] Daemon speak: " + text);
        tts.Speak(text);
        return true;
    }

    System.Collections.IEnumerator SendDaemonCommandAck(string id, string status)
    {
        string url = data.daemonUrl.TrimEnd('/') + "/commands/ack";
        string json = JsonConvert.SerializeObject(new { id = id, status = status });
        byte[] body = Encoding.UTF8.GetBytes(json);
        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (DaemonAuthToken() != null) req.SetRequestHeader("X-SuperClaw-Token", DaemonAuthToken());
            req.timeout = 3;
            yield return req.SendWebRequest();
        }
    }

    public static void SyncAllowedAppsToAllAvatars()
    {
        var allAvatars = Resources.FindObjectsOfTypeAll<AvatarAnimatorController>();
        var list = new List<string>(Instance.data.allowedApps);

        foreach (var avatar in allAvatars)
            avatar.allowedApps = list;
    }

    public static void ApplyAllSettingsToAllAvatars()
    {
        var data = Instance.data;
        var avatars = Resources.FindObjectsOfTypeAll<AvatarAnimatorController>();

        foreach (var avatar in avatars)
        {
            avatar.SOUND_THRESHOLD = data.soundThreshold;
            avatar.IDLE_SWITCH_TIME = data.idleSwitchTime;
            avatar.IDLE_TRANSITION_TIME = data.idleTransitionTime;
            avatar.enableDancing = data.enableDancing;
            avatar.followMusic = data.followMusic;
            avatar.allowedApps = new List<string>(data.allowedApps);
            avatar.transform.localScale = Vector3.one * data.avatarSize;
            avatar.DANCE_SWITCH_TIME = data.danceSwitchTime;
            avatar.DANCE_TRANSITION_TIME = data.danceTransitionTime;
            avatar.enableDanceSwitch = data.enableDanceSwitch;
            avatar.DANCE_CLIP_COUNT = Mathf.Clamp(data.danceClipCount, 1, 20);
            avatar.pinnedDanceIndex = data.pinnedDanceIndex;
            avatar.enableHusbandoMode = data.enableHusbandoMode;

            foreach (var tracker in avatar.GetComponentsInChildren<AvatarMouseTracking>(true))
            {
                tracker.enableMouseTracking = data.enableMouseTracking;
                tracker.headBlend = data.headBlend;
                tracker.spineBlend = data.spineBlend;
                tracker.eyeBlend = data.eyeBlend;
            }

            foreach (var ik in avatar.GetComponentsInChildren<IKFix>(true))
                ik.enableIK = data.enableIK;

            foreach (var handler in avatar.GetComponentsInChildren<AvatarParticleHandler>(true))
            {
                handler.featureEnabled = data.enableParticles;
                handler.enabled = data.enableParticles;
                handler.selectedTheme = data.selectedParticleTheme;
                try { handler.SetTheme(data.selectedParticleTheme); } catch { }
            }

            foreach (var holder in avatar.GetComponentsInChildren<HandHolder>(true))
                holder.enableHandHolding = data.enableHandHolding;

            if (avatar.animator != null &&
                avatar.animator.isActiveAndEnabled &&
                avatar.animator.runtimeAnimatorController != null)
            {
                avatar.animator.SetBool("isDancing", false);
                avatar.animator.SetBool("isDragging", false);
                avatar.isDancing = false;
                avatar.isDragging = false;
            }

            foreach (var food in Resources.FindObjectsOfTypeAll<AvatarFoodController>())
                food.SetFeatureEnabled(Instance.data.enableFeedSystem);

            foreach (var handler in Resources.FindObjectsOfTypeAll<AvatarWindowHandler>())
            {
                handler.windowSitYOffset = data.windowSitYOffset;
                handler.windowSitEdge = data.windowSitEdge;
            }

            foreach (var loco in Resources.FindObjectsOfTypeAll<AvatarLocomotionController>())
                loco.EnableLocomotion = data.enableLocomotion;

        }
    }
}
