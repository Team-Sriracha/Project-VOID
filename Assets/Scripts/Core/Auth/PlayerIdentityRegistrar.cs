using System;
using System.Threading.Tasks;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// Why: FishNet.Managing.NetworkManager와 커스텀 NetworkManager 이름 충돌을 방지합니다.
using CustomNetworkManager = global::NetworkManager;

/// <summary>
/// 로컬 인증 정보를 서버에 등록하고 표시 이름을 동기화합니다.
/// </summary>
public class PlayerIdentityRegistrar : NetworkBehaviour
{
    #region SyncVars

    public readonly SyncVar<string> PlayerDisplayName = new();
    public readonly SyncVar<string> PlayerGuestId = new();

    #endregion

    #region Fish-Net Lifecycle

    public override void OnStartServer()
    {
        base.OnStartServer();
        TryApplyIdentityFromNetworkManager();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!IsOwner)
        {
            return;
        }

        RunRegisterIdentityAsync();
    }

    #endregion

    #region Registration

    private async void RunRegisterIdentityAsync()
    {
        try
        {
            await RegisterIdentityAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlayerIdentityRegistrar] 신원 등록 예외: {ex}");
        }
    }

    private async Task RegisterIdentityAsync()
    {
        if (!ServiceLocator.TryGet<IAuthService>(out IAuthService authService) || !authService.IsSignedIn)
        {
            Debug.LogWarning("[PlayerIdentityRegistrar] AuthService가 없거나 로그인 상태가 아닙니다.");
            return;
        }

        string idToken = await authService.GetIdTokenAsync();
        if (string.IsNullOrWhiteSpace(idToken))
        {
            Debug.LogWarning("[PlayerIdentityRegistrar] ID Token이 비어 있어 신원 등록을 건너뜁니다.");
            return;
        }

        ServerRpc_RegisterIdentity(idToken);
    }

    [ServerRpc]
    private void ServerRpc_RegisterIdentity(string idToken)
    {
        RunRegisterIdentityOnServerAsync(idToken);
    }

    private async void RunRegisterIdentityOnServerAsync(string idToken)
    {
        try
        {
            await RegisterIdentityOnServerAsync(idToken);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlayerIdentityRegistrar] 서버 신원 등록 예외: {ex}");
        }
    }

    private async Task RegisterIdentityOnServerAsync(string idToken)
    {
        if (CustomNetworkManager.Instance == null)
        {
            return;
        }

        bool isVerified = await CustomNetworkManager.Instance.TryVerifyAndRegisterIdentityAsync(Owner, idToken);
        if (!isVerified)
        {
            return;
        }

        if (CustomNetworkManager.Instance.TryGetPlayerIdentity(Owner, out PlayerIdentity identity))
        {
            ApplyServerIdentity(identity);
        }
    }

    private void TryApplyIdentityFromNetworkManager()
    {
        if (!IsServerInitialized || CustomNetworkManager.Instance == null)
        {
            return;
        }

        if (CustomNetworkManager.Instance.TryGetPlayerIdentity(Owner, out PlayerIdentity identity))
        {
            ApplyServerIdentity(identity);
        }
    }

    private void ApplyServerIdentity(PlayerIdentity identity)
    {
        if (identity == null)
        {
            return;
        }

        PlayerDisplayName.Value = string.IsNullOrWhiteSpace(identity.DisplayName)
            ? string.Empty
            : identity.DisplayName.Trim();
        PlayerGuestId.Value = string.IsNullOrWhiteSpace(identity.GuestId)
            ? string.Empty
            : identity.GuestId.Trim();
    }

    #endregion
}
