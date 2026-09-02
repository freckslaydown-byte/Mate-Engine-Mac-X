using UnityEngine;
using NAudio.CoreAudioApi;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections;

public class AvatarAnimatorController : MonoBehaviour
{
    [Header("State Values")]
    public Animator animator;
    public float SOUND_THRESHOLD = 0.02f;
    public List<string> allowedApps = new();
    public int totalIdleAnimations = 10;
    public float IDLE_SWITCH_TIME = 12f, IDLE_TRANSITION_TIME = 3f;
    [Header("Dancing")]
    public bool enableDancing = true;
    // true = dance only while a system player is actually outputting audio
    //        (ScreenCaptureKit capture; no permission / macOS<13 = stays idle);
    // false = manual: enableDancing on = dance immediately.
    public bool followMusic = true;
    public bool enableDanceSwitch = true;
    public float DANCE_SWITCH_TIME = 15f;
    public float DANCE_TRANSITION_TIME = 2f;
    // Female blend tree 共 20 个动作（threshold 0-19），此值控制自动循环范围上限
    public int DANCE_CLIP_COUNT = 20;
    // -1 = 自动循环，0~(DANCE_CLIP_COUNT-1) = 固定到指定编号的舞蹈
    public int pinnedDanceIndex = -1;

    public bool BlockDraggingOverride = false;

    private static readonly int danceIndexParam = Animator.StringToHash("DanceIndex");
    private static readonly int isIdleParam = Animator.StringToHash("isIdle");
    private static readonly int isDraggingParam = Animator.StringToHash("isDragging");
    private static readonly int isDancingParam = Animator.StringToHash("isDancing");
    private static readonly int idleIndexParam = Animator.StringToHash("IdleIndex");

    private float cursorOutsideTimer; // macOS stuck-drag recovery

    private MMDevice defaultDevice;
    private MMDeviceEnumerator enumerator;
    private Coroutine soundCheckCoroutine, idleTransitionCoroutine, danceTransitionCoroutine;
    private float lastSoundCheckTime, idleTimer, danceTimer;
    private int idleState, danceState;
    private float dragLockTimer;
    private bool mouseHeld;
    public bool isDragging, isDancing, isIdle;
    private int _soundConfirmCount;

    [Header("Character Mode")]
    public bool enableHusbandoMode = false;
    private static readonly int isMaleParam = Animator.StringToHash("isMale");
    private static readonly int isFemaleParam = Animator.StringToHash("isFemale");


    void OnEnable()
    {
        animator ??= GetComponent<Animator>();
        Application.runInBackground = true;

#if UNITY_STANDALONE_WIN
        enumerator = new MMDeviceEnumerator();
        defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
#endif

        animator.SetFloat(isFemaleParam, enableHusbandoMode ? 0f : 1f);
        animator.SetFloat(isMaleParam, enableHusbandoMode ? 1f : 0f);

        soundCheckCoroutine = StartCoroutine(CheckSoundContinuously());

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        MacAudioMonitorBinding.Start();
        UnityEngine.Debug.Log("[AvatarAnimatorController] macOS audio monitor init. Default output device: " + MacAudioMonitorBinding.GetDefaultDeviceName());
        MacWindowFixBinding.Install();
#endif
    }

    void OnDisable() => CleanupAudioResources();
    void OnDestroy() => CleanupAudioResources();
    void OnApplicationQuit() => CleanupAudioResources();

    IEnumerator CheckSoundContinuously()
    {
        var wait = new WaitForSeconds(2f);
        while (true) { CheckForSound(); yield return wait; }
    }

    void CheckForSound()
    {
        if (MenuActions.IsMovementBlocked() || !enableDancing)
        {
            if (isDancing) SetDancing(false);
            return;
        }
#if UNITY_STANDALONE_WIN
        if (defaultDevice == null) return;
        if (!isDragging)
        {
            bool valid = IsValidAppPlaying();
            if (valid && !isDancing) StartDancing();
            else if (!valid && isDancing) SetDancing(false);
        }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        if (!isDragging)
        {
            if (followMusic)
            {
                // Music-reactive: dance only while a system player is actually
                // outputting audio. If the capture isn't available (no Screen
                // Recording permission, macOS < 13) it's treated as no music,
                // so the avatar stays idle instead of dancing unconditionally.
                bool valid = IsValidAppPlaying();
                if (valid && !isDancing) StartDancing();
                else if (!valid && isDancing) SetDancing(false);
            }
            else if (!isDancing)
            {
                // Manual mode: toggle on = dance immediately.
                StartDancing();
            }
        }
#endif
    }

    void StartDancing()
    {
        isDancing = true;
        danceTimer = 0f;
        danceState = (pinnedDanceIndex >= 0 && pinnedDanceIndex < DANCE_CLIP_COUNT)
            ? pinnedDanceIndex
            : Random.Range(0, DANCE_CLIP_COUNT);
        animator.SetBool(isDancingParam, true);
        animator.SetFloat(danceIndexParam, danceState);
    }
    void SetDancing(bool value)
    {
        isDancing = value;
        animator.SetBool(isDancingParam, value);
        if (!value && danceTransitionCoroutine != null)
        {
            StopCoroutine(danceTransitionCoroutine);
            danceTransitionCoroutine = null;
        }
    }

    bool IsValidAppPlaying()
    {
#if UNITY_STANDALONE_WIN
        if (Time.time - lastSoundCheckTime < 2f) return isDancing;
        lastSoundCheckTime = Time.time;
        try
        {
            defaultDevice?.Dispose();
            defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = defaultDevice.AudioSessionManager.Sessions;
            for (int i = 0, count = sessions.Count; i < count; i++)
            {
                var s = sessions[i];
                if (s.AudioMeterInformation.MasterPeakValue > SOUND_THRESHOLD)
                {
                    int pid = (int)s.GetProcessID;
                    if (pid == 0) continue;
                    try
                    {
                        string pname = Process.GetProcessById(pid)?.ProcessName;
                        if (string.IsNullOrEmpty(pname)) continue;
                        for (int j = 0; j < allowedApps.Count; j++)
                            if (pname.StartsWith(allowedApps[j], System.StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    catch { continue; }
                }
            }
        }
        catch { defaultDevice?.Dispose(); defaultDevice = null; }
        return false;
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        if (MacAudioMonitorBinding.OutputActivity() <= 0) return false;
        if (allowedApps == null || allowedApps.Count == 0) return true;

        var running = MacSystemBridge.GetRunningAppNames();
        for (int i = 0; i < running.Count; i++)
        {
            string appName = running[i];
            for (int j = 0; j < allowedApps.Count; j++)
                if (appName.StartsWith(allowedApps[j], System.StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        return false;
#else
        return false;
#endif
    }

    void Update()
    {
        animator.SetFloat(isFemaleParam, enableHusbandoMode ? 0f : 1f);
        animator.SetFloat(isMaleParam, enableHusbandoMode ? 1f : 0f);

        if (BlockDraggingOverride || MenuActions.IsMovementBlocked() || TutorialMenu.IsActive)
        {
            if (isDragging) SetDragging(false);
            if (isDancing) SetDancing(false);
            return;
        }
        if (Input.GetMouseButtonDown(0))
        {
            SetDragging(true);
            mouseHeld = true;
            dragLockTimer = 0.30f;
            SetDancing(false);
        }
        if (Input.GetMouseButtonUp(0)) mouseHeld = false;
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        // Stuck-drag recovery (macOS): if a drag carries the cursor outside our
        // window, the mouse-up event is delivered to the other app and Unity
        // never sees it, leaving isDragging stuck forever. The native reads work
        // even while another app has focus, so we can detect the cursor leaving
        // the window mid-drag and force-release after a short debounce.
        if (isDragging && mouseHeld &&
            MacWindowHelper.TryGetWindowRect(out RectInt winRect) &&
            MacWindowHelper.TryGetCursorPosition(out Vector2Int cur))
        {
            bool inside = cur.x >= winRect.x && cur.x <= winRect.x + winRect.width &&
                          cur.y >= winRect.y && cur.y <= winRect.y + winRect.height;
            if (inside)
            {
                cursorOutsideTimer = 0f;
            }
            else
            {
                cursorOutsideTimer += Time.unscaledDeltaTime;
                if (cursorOutsideTimer > 0.25f)
                {
                    mouseHeld = false;
                    dragLockTimer = 0f;
                    SetDragging(false);
                }
            }
        }
        else
        {
            cursorOutsideTimer = 0f;
        }
#endif
        if (dragLockTimer > 0f)
        {
            dragLockTimer -= Time.deltaTime;
            animator.SetBool(isDraggingParam, true);
        }
        else if (!mouseHeld && isDragging) SetDragging(false);

        idleTimer += Time.deltaTime;
        if (idleTimer > IDLE_SWITCH_TIME)
        {
            idleTimer = 0f;
            int next = (idleState + 1) % totalIdleAnimations;
            if (next == 0) animator.SetFloat(idleIndexParam, 0);
            else
            {
                if (idleTransitionCoroutine != null) StopCoroutine(idleTransitionCoroutine);
                idleTransitionCoroutine = StartCoroutine(SmoothIdleTransition(next));
            }
            idleState = next;
        }
        UpdateIdleStatus();

        if (isDancing && enableDanceSwitch && pinnedDanceIndex < 0)
        {
            danceTimer += Time.deltaTime;
            if (danceTimer > DANCE_SWITCH_TIME)
            {
                danceTimer = 0f;
                int nextDance = (danceState + 1) % DANCE_CLIP_COUNT;
                if (nextDance == 0) animator.SetFloat(danceIndexParam, 0);
                else
                {
                    if (danceTransitionCoroutine != null) StopCoroutine(danceTransitionCoroutine);
                    danceTransitionCoroutine = StartCoroutine(SmoothDanceTransition(nextDance));
                }
                danceState = nextDance;
            }
        }
    }
    void SetDragging(bool value)
    {
        isDragging = value;
        animator.SetBool(isDraggingParam, value);
    }

    void UpdateIdleStatus()
    {
        bool inIdle = animator.GetCurrentAnimatorStateInfo(0).IsName("Idle");
        if (isIdle != inIdle)
        {
            isIdle = inIdle;
            animator.SetBool(isIdleParam, isIdle);
        }
    }

    IEnumerator SmoothIdleTransition(int newIdle)
    {
        float elapsed = 0f, start = animator.GetFloat(idleIndexParam);
        while (elapsed < IDLE_TRANSITION_TIME)
        {
            elapsed += Time.deltaTime;
            animator.SetFloat(idleIndexParam, Mathf.Lerp(start, newIdle, elapsed / IDLE_TRANSITION_TIME));
            yield return null;
        }
        animator.SetFloat(idleIndexParam, newIdle);
    }

    IEnumerator SmoothDanceTransition(int newDance)
    {
        float elapsed = 0f, start = animator.GetFloat(danceIndexParam);
        while (elapsed < DANCE_TRANSITION_TIME)
        {
            elapsed += Time.deltaTime;
            animator.SetFloat(danceIndexParam, Mathf.Lerp(start, newDance, elapsed / DANCE_TRANSITION_TIME));
            yield return null;
        }
        animator.SetFloat(danceIndexParam, newDance);
    }

    public bool IsInIdleState() => isIdle;

    void CleanupAudioResources()
    {
        if (soundCheckCoroutine != null) { StopCoroutine(soundCheckCoroutine); soundCheckCoroutine = null; }
        if (idleTransitionCoroutine != null) { StopCoroutine(idleTransitionCoroutine); idleTransitionCoroutine = null; }
        if (danceTransitionCoroutine != null) { StopCoroutine(danceTransitionCoroutine); danceTransitionCoroutine = null; }
#if UNITY_STANDALONE_WIN
        defaultDevice?.Dispose(); defaultDevice = null;
        enumerator?.Dispose(); enumerator = null;
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        MacAudioMonitorBinding.Stop();
#endif
    }
}
