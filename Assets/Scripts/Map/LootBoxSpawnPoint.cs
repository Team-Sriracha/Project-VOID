using UnityEngine;
using Fusion;
using ExitGames.Client.Photon.StructWrapping;

namespace ProjectVoid.Map
{
    /// <summary>
    /// LootBox 스폰 포인트에 붙이는 컴포넌트입니다.
    /// 이 위치에 어떤 LootBox를 스폰할지 지정합니다.
    /// 태그 기반 대신 컴포넌트 기반으로 더 유연한 설정이 가능합니다.
    /// </summary>
    public class LootBoxSpawnPoint : MonoBehaviour
    {
        #region Serialized Fields

        [Header("스폰 설정")]
        [Tooltip("이 위치에 스폰할 LootBox 프리팹 (NetworkObject 포함)")]
        [SerializeField] private NetworkPrefabRef _lootBoxPrefab;

        [Header("개별 설정 (선택)")]
        [Tooltip("이 스폰 포인트의 개별 리스폰 시간 (0이면 매니저 기본값 사용)")]
        [SerializeField] private float _customRespawnTime = 0f;

        [Tooltip("활성화 여부 (비활성화 시 스폰되지 않음)")]
        [SerializeField] private bool _isEnabled = true;

        #endregion

        #region Properties

        /// <summary>
        /// 스폰할 LootBox 프리팹 참조
        /// </summary>
        public NetworkPrefabRef LootBoxPrefab => _lootBoxPrefab;

        /// <summary>
        /// 개별 리스폰 시간 (0이면 매니저 기본값 사용)
        /// </summary>
        public float CustomRespawnTime => _customRespawnTime;

        /// <summary>
        /// 스폰 포인트가 활성화되어 있는지 여부
        /// </summary>
        public bool IsEnabled => _isEnabled;

        /// <summary>
        /// 유효한 스폰 포인트인지 확인 (프리팹이 설정되어 있고 활성화됨)
        /// </summary>
        public bool IsValid => _lootBoxPrefab.IsValid && _isEnabled;

        #endregion

        #region Public Methods

        /// <summary>
        /// 리스폰 시간을 반환합니다 (커스텀 설정이 있으면 커스텀, 없으면 매니저 기본값)
        /// </summary>
        /// <param name="defaultTime">매니저의 기본 리스폰 시간</param>
        /// <returns>적용할 리스폰 시간</returns>
        public float GetRespawnTime(float defaultTime)
        {
            return _customRespawnTime > 0f ? _customRespawnTime : defaultTime;
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmos()
        {
            // 에디터에서 스폰 포인트 시각화
            if (!_isEnabled)
            {
                Gizmos.color = Color.gray;
            }
            else if (_lootBoxPrefab.IsValid)
            {
                Gizmos.color = Color.yellow;
            }
            else
            {
                Gizmos.color = Color.red;
            }

            Gizmos.DrawWireCube(transform.position + Vector3.up, new Vector3(4.0f, 2.0f, 2.0f));
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.5f);
        }

        private void OnDrawGizmosSelected()
        {
            // 선택 시 더 진한 표시
            Gizmos.color = _isEnabled ? Color.yellow : Color.gray;
            Gizmos.DrawCube(transform.position, new Vector3(0.3f, 0.3f, 0.3f));

            // 리스폰 시간 표시 (Scene 뷰에서)
#if UNITY_EDITOR
            string label = _customRespawnTime > 0f 
                ? $"LootBox (Respawn: {_customRespawnTime}s)" 
                : "LootBox (Default)";
            
            if (!_isEnabled) label = "LootBox (Disabled)";
            if (!_lootBoxPrefab.IsValid) label = "LootBox (No Prefab!)";
            
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, label);
#endif
        }

        #endregion
    }
}
