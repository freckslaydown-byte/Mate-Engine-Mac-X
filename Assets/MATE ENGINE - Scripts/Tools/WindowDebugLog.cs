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
    public static void Log(string msg)
    {
        try
        {
            string p = Path.Combine(Application.persistentDataPath, "windowdebug.log");
            File.AppendAllText(p, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\n");
        }
        catch (Exception)
        {
        }
    }
}