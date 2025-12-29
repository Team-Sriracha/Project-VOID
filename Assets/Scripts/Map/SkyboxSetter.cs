using Fusion;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 세션(씬)이 시작될 때 서버에서 랜덤한 Skybox를 결정하고 모든 클라이언트에 동기화합니다.
/// Multi-Peer 환경 대응을 위해 RenderSettings API 대신 각 카메라의 Skybox 컴포넌트를 제어합니다.
/// NetworkMapManager 등 네트워크 객체에 부착하여 사용합니다.
/// </summary>
public class SkyboxSetter : NetworkBehaviour
{
    #region Serialized Fields
    
    [Header("Skybox 설정")]
    [Tooltip("랜덤으로 선택될 Skybox Material 목록")]
    [SerializeField] private List<Material> _skyboxMaterials = new List<Material>();

    [Tooltip("Main Camera를 찾지 못했을 때 경고 로그 출력 여부")]
    [SerializeField] private bool _showDebugLog = true;

    #endregion

    #region Networked Properties

    // Why: 모든 클라이언트가 동일한 Skybox를 보게 하기 위해 인덱스를 동기화
    [Networked] private int _skyboxIndex { get; set; } = -1;

    #endregion

    #region Private Fields

    private ChangeDetector _changeDetector;
    private bool _isSkyboxApplied = false;

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

        // 1. 서버(State Authority)에서만 랜덤 인덱스 결정
        if (HasStateAuthority && _skyboxMaterials.Count > 0)
        {
            _skyboxIndex = Random.Range(0, _skyboxMaterials.Count);
            // Why: 호스트(Server+Client)인 경우 자신의 화면에도 바로 적용 필요
            ApplySkybox(_skyboxIndex);
        }
        // 2. 클라이언트는 이미 설정된 값이 있다면 적용
        else if (_skyboxIndex >= 0)
        {
            ApplySkybox(_skyboxIndex);
        }
    }

    public override void Render()
    {
        // Why: 변경 사항 감지 및 아직 적용 안 된 경우 처리
        foreach (var change in _changeDetector.DetectChanges(this))
        {
            if (change == nameof(_skyboxIndex))
            {
                ApplySkybox(_skyboxIndex);
            }
        }

        // 안전장치: 인덱스는 유효한데 아직 적용 안 됐으면 재시도 (카메라가 늦게 생길 수 있음)
        if (!_isSkyboxApplied && _skyboxIndex >= 0)
        {
            ApplySkybox(_skyboxIndex);
        }
    }

    #endregion

    #region Private Methods

    private void ApplySkybox(int index)
    {
        if (_skyboxMaterials == null || _skyboxMaterials.Count == 0) return;
        if (index < 0 || index >= _skyboxMaterials.Count) return;

        Material selectedSkybox = _skyboxMaterials[index];
        if (selectedSkybox == null) return;

        // Why: 사용자의 요청대로 카메라별 컴포넌트가 아닌 씬(전역) 설정을 변경
        RenderSettings.skybox = selectedSkybox;
        DynamicGI.UpdateEnvironment(); // 조명 갱신
            
        _isSkyboxApplied = true;

        if (_showDebugLog)
        {
            Debug.Log($"[SkyboxSetter] 씬 Skybox 변경 완료: Index {index}, {selectedSkybox.name}");
        }
    }



    #endregion
}
