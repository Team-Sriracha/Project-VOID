#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 클라이언트/서버 빌드를 자동화하는 에디터 유틸리티입니다.
/// </summary>
public static class ProjectBuildAutomation
{
    #region Constants

    private const string BUILD_ROOT = "Builds";
    private const string CLIENT_PRODUCT_NAME = "ProjectVOID-Client";
    private const string SERVER_PRODUCT_NAME = "ProjectVOID-Server";
    private const string LOGIN_SCENE_PATH = "Assets/Scenes/Login.unity";
    private const string SERVER_SCENE_PATH = "Assets/Scenes/ServerScene.unity";
    private const string GAMEPLAY_SCENE_PATH = "Assets/Scenes/GamePlay.unity";
    private const string ANDROID_PRODUCT_NAME = "ProjectVOID-Android";
    private const string IOS_PRODUCT_NAME = "ProjectVOID-iOS";
    private const string DESKTOP_CANVAS_OBJECT_NAME = "Canvas";
    private const string DESKTOP_UI_MANAGER_OBJECT_NAME = "UIManager";
    private const string MOBILE_CANVAS_OBJECT_NAME = "MobileCanvas";
    private const string MOBILE_UI_MANAGER_OBJECT_NAME = "MobileUIManager";
    private const ScriptingImplementation LINUX_SERVER_SCRIPTING_BACKEND = ScriptingImplementation.Mono2x;
    private const string NO_ACTIVE_BUILD_PROFILE_LABEL = "(활성 Build Profile 없음)";
    private const string PENDING_BUILD_REQUEST_SESSION_KEY = "ProjectVOID.ProjectBuildAutomation.PendingBuildRequest";

    #endregion

    #region Types

    private enum DeferredBuildRequest
    {
        None = 0,
        AndroidClient = 1,
        IosClient = 2,
        LinuxServer = 3
    }

    private readonly struct SceneObjectActiveState
    {
        public string ObjectName { get; }
        public bool WasActive { get; }

        public SceneObjectActiveState(string objectName, bool wasActive)
        {
            ObjectName = objectName ?? string.Empty;
            WasActive = wasActive;
        }
    }

    private sealed class GameplaySceneBuildState
    {
        public SceneSetup[] SceneSetup { get; set; }
        public List<SceneObjectActiveState> ObjectStates { get; } = new();
    }

    private sealed class BuildExecutionContext
    {
        public BuildTarget ActiveBuildTarget { get; set; }
        public BuildTargetGroup ActiveBuildTargetGroup { get; set; }
        public StandaloneBuildSubtarget ActiveStandaloneBuildSubtarget { get; set; }
        public BuildProfile ActiveBuildProfile { get; set; }
        public string ActiveBuildProfileName { get; set; } = NO_ACTIVE_BUILD_PROFILE_LABEL;
        public bool UseActiveBuildProfile { get; set; }
        public bool DelayBuildUntilTargetSwitchCompletes { get; set; }
    }

    #endregion

    #region Editor Initialization

    [InitializeOnLoadMethod]
    private static void ResumeDeferredBuildOnEditorLoad()
    {
        string pendingRequestValue = SessionState.GetString(PENDING_BUILD_REQUEST_SESSION_KEY, string.Empty);
        if (string.IsNullOrWhiteSpace(pendingRequestValue))
        {
            return;
        }

        EditorApplication.delayCall -= ExecuteDeferredBuildIfReady;
        EditorApplication.delayCall += ExecuteDeferredBuildIfReady;
    }

    private static void ExecuteDeferredBuildIfReady()
    {
        string pendingRequestValue = SessionState.GetString(PENDING_BUILD_REQUEST_SESSION_KEY, string.Empty);
        if (string.IsNullOrWhiteSpace(pendingRequestValue))
        {
            return;
        }

        SessionState.EraseString(PENDING_BUILD_REQUEST_SESSION_KEY);

        if (!Enum.TryParse(pendingRequestValue, ignoreCase: false, out DeferredBuildRequest request))
        {
            Debug.LogWarning($"[ProjectBuildAutomation] 알 수 없는 보류 빌드 요청을 무시합니다: {pendingRequestValue}");
            return;
        }

        if (!IsDeferredBuildReady(request))
        {
            Debug.LogWarning(
                $"[ProjectBuildAutomation] 보류된 빌드를 재개하지 않습니다. " +
                $"Request={request}, ActiveTarget={EditorUserBuildSettings.activeBuildTarget}");
            return;
        }

        Debug.Log($"[ProjectBuildAutomation] 타겟 전환 완료. 보류된 빌드를 재개합니다: {request}");
        switch (request)
        {
            case DeferredBuildRequest.AndroidClient:
                BuildAndroidClient();
                break;
            case DeferredBuildRequest.IosClient:
                BuildIosClient();
                break;
            case DeferredBuildRequest.LinuxServer:
                BuildLinuxServer();
                break;
            default:
                Debug.LogWarning($"[ProjectBuildAutomation] 처리되지 않은 보류 빌드 요청입니다: {request}");
                break;
        }
    }

    private static bool IsDeferredBuildReady(DeferredBuildRequest request)
    {
        return request switch
        {
            DeferredBuildRequest.AndroidClient => EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android,
            DeferredBuildRequest.IosClient => EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS,
            DeferredBuildRequest.LinuxServer => EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneLinux64,
            _ => false
        };
    }

    #endregion

    #region Menu

    [MenuItem("Project VOID/Build/Build Windows Client")]
    private static void BuildWindowsClientMenu()
    {
        BuildWindowsClient();
    }

    [MenuItem("Project VOID/Build/Build Linux Server")]
    private static void BuildLinuxServerMenu()
    {
        BuildLinuxServer();
    }

    [MenuItem("Project VOID/Build/Build Android Client")]
    private static void BuildAndroidClientMenu()
    {
        BuildAndroidClient();
    }

    [MenuItem("Project VOID/Build/Build iOS Client")]
    private static void BuildIosClientMenu()
    {
        BuildIosClient();
    }

    [MenuItem("Project VOID/Build/Open Build Folder")]
    private static void OpenBuildFolder()
    {
        string buildDirectory = Path.GetFullPath(BUILD_ROOT);
        Directory.CreateDirectory(buildDirectory);
        EditorUtility.RevealInFinder(buildDirectory);
    }

    #endregion

    #region Batch Entry Points

    public static void BuildWindowsClient()
    {
        RunBuild(
            target: BuildTarget.StandaloneWindows64,
            isServerBuild: false,
            outputPath: Path.Combine(BUILD_ROOT, "Client", "Windows", $"{CLIENT_PRODUCT_NAME}.exe"),
            scenes: GetClientScenes());
    }

    public static void BuildLinuxServer()
    {
        RunBuild(
            target: BuildTarget.StandaloneLinux64,
            isServerBuild: true,
            outputPath: Path.Combine(BUILD_ROOT, "Server", "Linux", SERVER_PRODUCT_NAME),
            scenes: GetServerScenes());
    }

    public static void BuildAndroidClient()
    {
        RunBuild(
            target: BuildTarget.Android,
            isServerBuild: false,
            outputPath: Path.Combine(BUILD_ROOT, "Client", "Android", $"{ANDROID_PRODUCT_NAME}.apk"),
            scenes: GetClientScenes());
    }

    public static void BuildIosClient()
    {
        RunBuild(
            target: BuildTarget.iOS,
            isServerBuild: false,
            outputPath: Path.Combine(BUILD_ROOT, "Client", "iOS", IOS_PRODUCT_NAME),
            scenes: GetClientScenes());
    }

    #endregion

    #region Build Core

    private static void RunBuild(
        BuildTarget target,
        bool isServerBuild,
        string outputPath,
        string[] scenes)
    {
        if (scenes == null || scenes.Length == 0)
        {
            throw new InvalidOperationException("빌드에 사용할 씬 목록이 비어 있습니다.");
        }

        BuildExecutionContext buildExecutionContext = ResolveBuildExecutionContext(target, isServerBuild);
        if (buildExecutionContext.DelayBuildUntilTargetSwitchCompletes)
        {
            return;
        }

        string fullOutputPath = Path.GetFullPath(outputPath);
        PrepareBuildOutputLocation(target, fullOutputPath);

        string originalStandaloneDefines = ServerRuntimeDefineUtility.GetStandaloneDefines();
        ScriptingImplementation originalServerBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server);
        bool originalDedicatedServerOptimizations = PlayerSettings.dedicatedServerOptimizations;
        StandaloneBuildSubtarget originalStandaloneSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
        GameplaySceneBuildState gameplaySceneState = null;

        try
        {
            if (isServerBuild)
            {
                ServerRuntimeDefineUtility.SetStandaloneServerRuntimeDefine(true);
            }
            else
            {
                ServerRuntimeDefineUtility.SetStandaloneServerRuntimeDefine(false);
            }

            PlayerSettings.dedicatedServerOptimizations = isServerBuild;
            Debug.Log(
                $"[ProjectBuildAutomation] Dedicated Server 최적화 설정: " +
                $"Target={target}, Enabled={PlayerSettings.dedicatedServerOptimizations}");

            if (IsStandaloneBuildTarget(target))
            {
                StandaloneBuildSubtarget requestedStandaloneSubtarget = GetStandaloneBuildSubtarget(isServerBuild);
                if (EditorUserBuildSettings.standaloneBuildSubtarget != requestedStandaloneSubtarget)
                {
                    EditorUserBuildSettings.standaloneBuildSubtarget = requestedStandaloneSubtarget;
                    Debug.Log(
                        $"[ProjectBuildAutomation] Standalone 서브타깃 설정: " +
                        $"Target={target}, Subtarget={requestedStandaloneSubtarget}");
                }
            }

            if (!isServerBuild)
            {
                bool useMobileUI = IsMobileClientBuild(target);
                gameplaySceneState = PrepareGameplaySceneForBuild(target, useMobileUI);
            }

            if (isServerBuild)
            {
                ScriptingImplementation serverBackend = GetDesiredServerScriptingBackend(target, originalServerBackend);
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, serverBackend);

                Debug.Log(
                    $"[ProjectBuildAutomation] 서버 빌드 스크립팅 백엔드 설정: " +
                    $"Target={target}, Backend={serverBackend}");
            }

            BuildReport report = ExecuteBuild(
                target,
                isServerBuild,
                scenes,
                fullOutputPath,
                buildExecutionContext);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"빌드 실패: Result={report.summary.result}, " +
                    $"Errors={report.summary.totalErrors}, " +
                    $"Warnings={report.summary.totalWarnings}");
            }

            Debug.Log(
                $"[ProjectBuildAutomation] Build 성공: Target={target}, " +
                $"Server={isServerBuild}, Size={report.summary.totalSize} bytes, " +
                $"Output={fullOutputPath}");
        }
        finally
        {
            if (gameplaySceneState != null)
            {
                RestoreGameplaySceneAfterBuild(gameplaySceneState);
            }

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, originalServerBackend);
            PlayerSettings.dedicatedServerOptimizations = originalDedicatedServerOptimizations;
            EditorUserBuildSettings.standaloneBuildSubtarget = originalStandaloneSubtarget;
            ServerRuntimeDefineUtility.SetStandaloneDefines(originalStandaloneDefines);
            AssetDatabase.Refresh();
        }
    }

    #endregion

    #region Build Context

    private static BuildExecutionContext ResolveBuildExecutionContext(BuildTarget target, bool isServerBuild)
    {
        BuildExecutionContext context = new BuildExecutionContext
        {
            ActiveBuildTarget = EditorUserBuildSettings.activeBuildTarget,
            ActiveBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget),
            ActiveStandaloneBuildSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget,
            ActiveBuildProfile = GetActiveBuildProfileSafe()
        };

        if (context.ActiveBuildProfile != null)
        {
            context.ActiveBuildProfileName = context.ActiveBuildProfile.name;
        }

        context.UseActiveBuildProfile = !isServerBuild &&
                                        IsMobileClientBuild(target) &&
                                        context.ActiveBuildTarget == target &&
                                        context.ActiveBuildProfile != null;

        LogBuildExecutionContext(target, isServerBuild, context);
        ValidateBuildExecutionContext(target, isServerBuild, context);

        return context;
    }

    private static void ValidateBuildExecutionContext(BuildTarget target, bool isServerBuild, BuildExecutionContext context)
    {
        BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(target);
        if (!BuildPipeline.IsBuildTargetSupported(targetGroup, target))
        {
            throw new InvalidOperationException($"현재 Unity 에디터에 대상 플랫폼 모듈이 설치되지 않았습니다: Target={target}");
        }

        bool requiresActiveTargetMatch = isServerBuild || IsMobileClientBuild(target);
        if (!requiresActiveTargetMatch)
        {
            return;
        }

        if (context.ActiveBuildTarget == target)
        {
            return;
        }

        if (!Application.isBatchMode &&
            TryScheduleInteractiveBuildTargetSwitch(target, context.ActiveBuildTarget, out string switchMessage))
        {
            context.DelayBuildUntilTargetSwitchCompletes = true;
            Debug.Log(switchMessage);
            return;
        }

        string guidance = isServerBuild
            ? $"Unity 실행 인자에 `-buildTarget {target}`를 지정한 뒤 다시 실행해야 합니다."
            : $"Unity 실행 인자에 `-buildTarget {target}` 또는 `-activeBuildProfile <대상 프로필>`를 지정한 뒤 다시 실행해야 합니다.";

        throw new InvalidOperationException(
            $"현재 활성 빌드 타겟이 요청 타겟과 다릅니다. " +
            $"Requested={target}, Active={context.ActiveBuildTarget}, " +
            $"ActiveProfile={context.ActiveBuildProfileName}, " +
            $"IsServer={isServerBuild}. " +
            $"Unity 문서상 배치/자동화 빌드에서는 실행 중 타겟 전환이 신뢰되지 않으므로, 잘못된 컨텍스트에서 빌드를 진행하지 않습니다. {guidance}");
    }

    private static void LogBuildExecutionContext(BuildTarget target, bool isServerBuild, BuildExecutionContext context)
    {
        Debug.Log(
            $"[ProjectBuildAutomation] Build 컨텍스트: " +
            $"RequestedTarget={target}, " +
            $"IsServer={isServerBuild}, " +
            $"ActiveTarget={context.ActiveBuildTarget}, " +
            $"ActiveTargetGroup={context.ActiveBuildTargetGroup}, " +
            $"ActiveStandaloneSubtarget={context.ActiveStandaloneBuildSubtarget}, " +
            $"ActiveProfile={context.ActiveBuildProfileName}, " +
            $"UseActiveBuildProfile={context.UseActiveBuildProfile}, " +
            $"BatchMode={Application.isBatchMode}");
    }

    private static BuildProfile GetActiveBuildProfileSafe()
    {
        try
        {
            return BuildProfile.GetActiveBuildProfile();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ProjectBuildAutomation] 활성 Build Profile 조회 실패. 기본 빌드 경로로 진행합니다. {ex.Message}");
            return null;
        }
    }

    private static bool TryScheduleInteractiveBuildTargetSwitch(
        BuildTarget requestedTarget,
        BuildTarget activeTarget,
        out string message)
    {
        message = string.Empty;

        DeferredBuildRequest deferredBuildRequest = GetDeferredBuildRequest(requestedTarget);
        if (deferredBuildRequest == DeferredBuildRequest.None)
        {
            message = $"[ProjectBuildAutomation] 보류 빌드 요청을 생성할 수 없는 타겟입니다: {requestedTarget}";
            return false;
        }

        BuildTargetGroup requestedTargetGroup = BuildPipeline.GetBuildTargetGroup(requestedTarget);
        SessionState.SetString(PENDING_BUILD_REQUEST_SESSION_KEY, deferredBuildRequest.ToString());

        bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(requestedTargetGroup, requestedTarget);
        if (!switched)
        {
            SessionState.EraseString(PENDING_BUILD_REQUEST_SESSION_KEY);
            message =
                $"[ProjectBuildAutomation] 활성 빌드 타겟 전환에 실패했습니다. " +
                $"Requested={requestedTarget}, Active={activeTarget}";
            return false;
        }

        message =
            $"[ProjectBuildAutomation] 활성 빌드 타겟을 `{activeTarget}`에서 `{requestedTarget}`로 전환했습니다. " +
            "스크립트 리로드 후 보류된 빌드를 자동으로 재개합니다.";
        return true;
    }

    private static DeferredBuildRequest GetDeferredBuildRequest(BuildTarget target)
    {
        return target switch
        {
            BuildTarget.Android => DeferredBuildRequest.AndroidClient,
            BuildTarget.iOS => DeferredBuildRequest.IosClient,
            BuildTarget.StandaloneLinux64 => DeferredBuildRequest.LinuxServer,
            _ => DeferredBuildRequest.None
        };
    }

    private static BuildReport ExecuteBuild(
        BuildTarget target,
        bool isServerBuild,
        string[] scenes,
        string fullOutputPath,
        BuildExecutionContext buildExecutionContext)
    {
        if (buildExecutionContext != null && buildExecutionContext.UseActiveBuildProfile)
        {
            BuildPlayerWithProfileOptions profileBuildOptions = new BuildPlayerWithProfileOptions
            {
                buildProfile = buildExecutionContext.ActiveBuildProfile,
                locationPathName = fullOutputPath,
                options = BuildOptions.None
            };

            Debug.Log(
                $"[ProjectBuildAutomation] Build 시작 (활성 Build Profile 사용): " +
                $"Target={target}, " +
                $"Profile={buildExecutionContext.ActiveBuildProfileName}, " +
                $"Server={isServerBuild}, Output={fullOutputPath}");

            return BuildPipeline.BuildPlayer(profileBuildOptions);
        }

        BuildPlayerOptions buildOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            target = target,
            locationPathName = fullOutputPath,
            options = BuildOptions.None,
            subtarget = GetBuildSubtarget(target, isServerBuild)
        };

        Debug.Log(
            $"[ProjectBuildAutomation] Build 시작: Target={target}, " +
            $"Server={isServerBuild}, Output={fullOutputPath}");

        return BuildPipeline.BuildPlayer(buildOptions);
    }

    #endregion

    #region Scene Resolution

    private static string[] GetClientScenes()
    {
        List<string> scenes = new List<string>();
        TryAddScene(LOGIN_SCENE_PATH, scenes);

        EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
        for (int i = 0; i < buildScenes.Length; i++)
        {
            EditorBuildSettingsScene scene = buildScenes[i];
            if (scene == null || !scene.enabled || string.IsNullOrWhiteSpace(scene.path))
            {
                continue;
            }

            if (string.Equals(scene.path, LOGIN_SCENE_PATH, StringComparison.Ordinal))
            {
                continue;
            }

            scenes.Add(scene.path);
        }

        return scenes.ToArray();
    }

    private static string[] GetServerScenes()
    {
        List<string> scenes = new List<string>();
        TryAddScene(SERVER_SCENE_PATH, scenes);
        TryAddScene(GAMEPLAY_SCENE_PATH, scenes);
        return scenes.ToArray();
    }

    private static void TryAddScene(string scenePath, List<string> scenes)
    {
        if (string.IsNullOrWhiteSpace(scenePath) || scenes == null)
        {
            return;
        }

        for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
        {
            EditorBuildSettingsScene buildScene = EditorBuildSettings.scenes[i];
            if (buildScene == null || !buildScene.enabled)
            {
                continue;
            }

            if (string.Equals(buildScene.path, scenePath, StringComparison.Ordinal))
            {
                scenes.Add(scenePath);
                return;
            }
        }

        throw new InvalidOperationException($"빌드 설정에 필요한 씬이 없습니다: {scenePath}");
    }

    #endregion

    #region Gameplay Scene Preparation

    private static GameplaySceneBuildState PrepareGameplaySceneForBuild(BuildTarget target, bool useMobileUI)
    {
        EnsureScenesSavedBeforeTemporaryBuildChanges();

        GameplaySceneBuildState buildState = new GameplaySceneBuildState
        {
            SceneSetup = EditorSceneManager.GetSceneManagerSetup()
        };

        Scene gameplayScene = EditorSceneManager.OpenScene(GAMEPLAY_SCENE_PATH, OpenSceneMode.Single);

        SetSceneObjectActiveState(gameplayScene, DESKTOP_CANVAS_OBJECT_NAME, !useMobileUI, buildState.ObjectStates);
        SetSceneObjectActiveState(gameplayScene, DESKTOP_UI_MANAGER_OBJECT_NAME, !useMobileUI, buildState.ObjectStates);
        SetSceneObjectActiveState(gameplayScene, MOBILE_CANVAS_OBJECT_NAME, useMobileUI, buildState.ObjectStates);
        SetSceneObjectActiveState(gameplayScene, MOBILE_UI_MANAGER_OBJECT_NAME, useMobileUI, buildState.ObjectStates);

        EditorSceneManager.SaveScene(gameplayScene);

        string uiModeLabel = useMobileUI ? "모바일" : "데스크톱";
        Debug.Log($"[ProjectBuildAutomation] {uiModeLabel} 빌드용 GamePlay 씬 UI 전환 적용 완료. Target={target}");
        return buildState;
    }

    private static void RestoreGameplaySceneAfterBuild(GameplaySceneBuildState buildState)
    {
        if (buildState == null)
        {
            return;
        }

        Scene gameplayScene = EditorSceneManager.OpenScene(GAMEPLAY_SCENE_PATH, OpenSceneMode.Single);
        for (int i = 0; i < buildState.ObjectStates.Count; i++)
        {
            SceneObjectActiveState objectState = buildState.ObjectStates[i];
            GameObject targetObject = FindSceneGameObject(gameplayScene, objectState.ObjectName);
            if (targetObject != null)
            {
                targetObject.SetActive(objectState.WasActive);
            }
        }

        EditorSceneManager.SaveScene(gameplayScene);

        if (buildState.SceneSetup != null && buildState.SceneSetup.Length > 0)
        {
            EditorSceneManager.RestoreSceneManagerSetup(buildState.SceneSetup);
        }

        Debug.Log("[ProjectBuildAutomation] 빌드 후 GamePlay 씬 UI 상태 원복 완료");
    }

    private static bool IsMobileClientBuild(BuildTarget target)
    {
        return target == BuildTarget.Android || target == BuildTarget.iOS;
    }

    private static int GetBuildSubtarget(BuildTarget target, bool isServerBuild)
    {
        if (!IsStandaloneBuildTarget(target))
        {
            return 0;
        }

        return isServerBuild
            ? (int)StandaloneBuildSubtarget.Server
            : (int)StandaloneBuildSubtarget.Player;
    }

    private static StandaloneBuildSubtarget GetStandaloneBuildSubtarget(bool isServerBuild)
    {
        return isServerBuild
            ? StandaloneBuildSubtarget.Server
            : StandaloneBuildSubtarget.Player;
    }

    private static bool IsStandaloneBuildTarget(BuildTarget target)
    {
        return target == BuildTarget.StandaloneWindows ||
               target == BuildTarget.StandaloneWindows64 ||
               target == BuildTarget.StandaloneLinux64 ||
               target == BuildTarget.StandaloneOSX;
    }

    private static void PrepareBuildOutputLocation(BuildTarget target, string fullOutputPath)
    {
        if (string.IsNullOrWhiteSpace(fullOutputPath))
        {
            throw new InvalidOperationException("빌드 출력 경로가 비어 있습니다.");
        }

        if (UsesDirectoryOutput(target))
        {
            DeleteFileSystemEntryIfExists(fullOutputPath);
            Directory.CreateDirectory(fullOutputPath);
            Debug.Log($"[ProjectBuildAutomation] 빌드 출력 폴더 초기화: {fullOutputPath}");
            return;
        }

        string outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException($"빌드 출력 폴더를 확인할 수 없습니다: {fullOutputPath}");
        }

        DeleteFileSystemEntryIfExists(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        Debug.Log($"[ProjectBuildAutomation] 빌드 출력 폴더 초기화: {outputDirectory}");
    }

    private static bool UsesDirectoryOutput(BuildTarget target)
    {
        return target == BuildTarget.iOS;
    }

    private static void DeleteFileSystemEntryIfExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
            return;
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static ScriptingImplementation GetDesiredServerScriptingBackend(BuildTarget target, ScriptingImplementation fallbackBackend)
    {
        if (target == BuildTarget.StandaloneLinux64)
        {
            return LINUX_SERVER_SCRIPTING_BACKEND;
        }

        return fallbackBackend;
    }

    private static void EnsureScenesSavedBeforeTemporaryBuildChanges()
    {
        if (Application.isBatchMode)
        {
            EditorSceneManager.SaveOpenScenes();
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            throw new OperationCanceledException("빌드 전 씬 저장이 취소되었습니다.");
        }
    }

    private static void SetSceneObjectActiveState(
        Scene scene,
        string objectName,
        bool activeState,
        List<SceneObjectActiveState> objectStates)
    {
        GameObject targetObject = FindSceneGameObject(scene, objectName);
        if (targetObject == null)
        {
            throw new InvalidOperationException($"GamePlay 씬에서 오브젝트를 찾지 못했습니다: {objectName}");
        }

        objectStates?.Add(new SceneObjectActiveState(objectName, targetObject.activeSelf));
        targetObject.SetActive(activeState);
    }

    private static GameObject FindSceneGameObject(Scene scene, string objectName)
    {
        if (!scene.IsValid() || string.IsNullOrWhiteSpace(objectName))
        {
            return null;
        }

        GameObject[] rootObjects = scene.GetRootGameObjects();
        for (int i = 0; i < rootObjects.Length; i++)
        {
            GameObject foundObject = FindChildRecursive(rootObjects[i].transform, objectName);
            if (foundObject != null)
            {
                return foundObject;
            }
        }

        return null;
    }

    private static GameObject FindChildRecursive(Transform current, string objectName)
    {
        if (current == null)
        {
            return null;
        }

        if (string.Equals(current.name, objectName, StringComparison.Ordinal))
        {
            return current.gameObject;
        }

        for (int i = 0; i < current.childCount; i++)
        {
            GameObject foundObject = FindChildRecursive(current.GetChild(i), objectName);
            if (foundObject != null)
            {
                return foundObject;
            }
        }

        return null;
    }

    #endregion
}
#endif
