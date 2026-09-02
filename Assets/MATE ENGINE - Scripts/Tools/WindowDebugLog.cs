using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Helper for diagnosing window placement issues. Writes to
/// persistentDataPath/windowdebug.log. Used by AvatarHideHandler and
/// AvatarWindowHandler; safe to leave in (cheap, append-only, local).
/// </summary>
public static class WindowDebugLog
{
    // Diagnose a stale/translocated app: also write to /tmp (a stable, known
    // absolute path) so a missing log is never ambiguous. /tmp is world-writable.
    public static readonly string PersistentPath = System.IO.Path.Combine(Application.persistentDataPath, "windowdebug.log");
    public static readonly string TmpPath = "/tmp/mateengine-windowdebug.log";

    public static void Log(string msg)
    {
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\n";
        TryWrite(PersistentPath, line);
        TryWrite(TmpPath, line);
    }

    public static void StartupDiagnostics()
    {
        Log("STARTUP persistentDataPath=" + Application.persistentDataPath);
        Log("STARTUP app path=" + Application.dataPath);
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        try
        {
            var mons = MacWindowHelper.GetMonitors();
            for (int i = 0; i < mons.Count; i++)
                Log("STARTUP monitor[" + i + "]=" + mons[i] + " (primary=" + MacWindowHelper.GetPrimaryMonitorRect() + ")");
        }
        catch (System.Exception e)
        {
            Log("STARTUP monitor read error: " + e.Message);
        }
#endif
        try
        {
            var path = System.IO.Path.Combine(Application.persistentDataPath, "settings.json");
            if (System.IO.File.Exists(path))
                Log("STARTUP settings=" + System.IO.File.ReadAllText(path));
            else
                Log("STARTUP settings.json not found at " + path);
        }
        catch (System.Exception e)
        {
            Log("STARTUP settings read error: " + e.Message);
        }
    }

    static void TryWrite(string path, string line)
    {
        try
        {
            System.IO.File.AppendAllText(path, line);
        }
        catch (System.Exception)
        {
        }
    }
}