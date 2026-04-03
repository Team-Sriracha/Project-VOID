using UnityEngine;

/// <summary>
/// 플랫폼별 런타임 프레임 설정을 초기화합니다.
/// </summary>
public static class MobilePerformanceConfigurator
{
    #region Constants

    private const int MOBILE_TARGET_FRAME_RATE = 60;
    private const int DESKTOP_TARGET_FRAME_RATE = 120;

    #endregion

    #region Initialization

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyPerformanceSettings()
    {
        if (Application.isBatchMode)
        {
            return;
        }

        int targetFrameRate = Application.isMobilePlatform
            ? MOBILE_TARGET_FRAME_RATE
            : DESKTOP_TARGET_FRAME_RATE;

        // FishNet 자동 프레임 제한을 비활성화한 뒤 플랫폼별 상한을 프로젝트에서 직접 관리합니다.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFrameRate;

        Debug.Log(
            $"[MobilePerformanceConfigurator] 플랫폼 FPS 설정 적용. " +
            $"Platform={Application.platform}, TargetFrameRate={targetFrameRate}, vSyncCount={QualitySettings.vSyncCount}");
    }

    #endregion
}
