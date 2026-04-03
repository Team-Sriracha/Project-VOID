using System;

/// <summary>
/// 런타임에서 사용할 Firebase App 인스턴스 컨텍스트입니다.
/// </summary>
public sealed class FirebaseRuntimeAppContext
{
    #region Properties

    /// <summary>
    /// Firebase App 인스턴스입니다.
    /// </summary>
    public object App { get; }

    /// <summary>
    /// Firebase App 이름입니다.
    /// </summary>
    public string AppName { get; }

    /// <summary>
    /// 프로세스 전용 앱 여부입니다.
    /// </summary>
    public bool IsProcessScoped { get; }

    #endregion

    #region Constructor

    /// <summary>
    /// 컨텍스트를 생성합니다.
    /// </summary>
    public FirebaseRuntimeAppContext(object app, string appName, bool isProcessScoped)
    {
        App = app;
        AppName = appName ?? string.Empty;
        IsProcessScoped = isProcessScoped;
    }

    #endregion
}
