#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 서버 런타임 전용 define를 토글하는 에디터 유틸리티입니다.
/// </summary>
public static class ServerRuntimeDefineUtility
{
    #region Constants

    public const string SERVER_RUNTIME_DEFINE = "PROJECTVOID_SERVER_RUNTIME";

    #endregion

    #region Menu

    [MenuItem("Project VOID/Server Runtime/Enable Standalone Server Runtime Define")]
    private static void EnableStandaloneServerRuntimeDefine()
    {
        SetStandaloneServerRuntimeDefine(enable: true);
    }

    [MenuItem("Project VOID/Server Runtime/Disable Standalone Server Runtime Define")]
    private static void DisableStandaloneServerRuntimeDefine()
    {
        SetStandaloneServerRuntimeDefine(enable: false);
    }

    #endregion

    #region Helper Methods

    public static void SetStandaloneServerRuntimeDefine(bool enable)
    {
        string currentDefines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
        List<string> defineList = new List<string>(
            (currentDefines ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries));

        bool contains = defineList.Contains(SERVER_RUNTIME_DEFINE);
        if (enable && !contains)
        {
            defineList.Add(SERVER_RUNTIME_DEFINE);
        }
        else if (!enable && contains)
        {
            defineList.RemoveAll(define => string.Equals(define, SERVER_RUNTIME_DEFINE, StringComparison.Ordinal));
        }
        else
        {
            Debug.Log($"[ServerRuntimeDefineUtility] 변경 없음. Enabled={contains}");
            return;
        }

        string updatedDefines = string.Join(";", defineList);
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, updatedDefines);
        Debug.Log($"[ServerRuntimeDefineUtility] Standalone define 갱신: {updatedDefines}");
    }

    public static bool HasStandaloneServerRuntimeDefine()
    {
        string currentDefines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
        if (string.IsNullOrWhiteSpace(currentDefines))
        {
            return false;
        }

        string[] defineArray = currentDefines.Split(';', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < defineArray.Length; i++)
        {
            if (string.Equals(defineArray[i], SERVER_RUNTIME_DEFINE, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static string GetStandaloneDefines()
    {
        return PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone) ?? string.Empty;
    }

    public static void SetStandaloneDefines(string defines)
    {
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, defines ?? string.Empty);
    }

    #endregion
}
#endif
