using UnityEngine;

namespace ProjectVoid.Map
{
    /// <summary>
    /// LootBox 스폰 포인트에 붙이는 컴포넌트입니다.
    /// </summary>
    public class LootBoxSpawnPoint : MonoBehaviour
    {
        #region Serialized Fields

        [Header("스폰 설정")]
        [Tooltip("이 위치에 스폰할 LootBox 프리팹")]
        [SerializeField] private GameObject _lootBoxPrefab;

        [Header("개별 설정 (선택)")]
        [Tooltip("이 스폰 포인트의 개별 리스폰 시간 (0이면 매니저 기본값 사용)")]
        [SerializeField] private float _customRespawnTime = 0f;

        [Tooltip("활성화 여부 (비활성화 시 스폰되지 않음)")]
        [SerializeField] private bool _isEnabled = true;

        #endregion

        #region Properties

        public GameObject LootBoxPrefab => _lootBoxPrefab;
        public float CustomRespawnTime => _customRespawnTime;
        public bool IsEnabled => _isEnabled;
        public bool IsValid => _lootBoxPrefab != null && _isEnabled;

        #endregion

        #region Public Methods

        public float GetRespawnTime(float defaultTime)
        {
            return _customRespawnTime > 0f ? _customRespawnTime : defaultTime;
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmos()
        {
            if (!_isEnabled)
            {
                Gizmos.color = Color.gray;
            }
            else if (_lootBoxPrefab != null)
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
            Gizmos.color = _isEnabled ? Color.yellow : Color.gray;
            Gizmos.DrawCube(transform.position, new Vector3(0.3f, 0.3f, 0.3f));

#if UNITY_EDITOR
            string label = _customRespawnTime > 0f 
                ? $"LootBox (Respawn: {_customRespawnTime}s)" 
                : "LootBox (Default)";
            
            if (!_isEnabled) label = "LootBox (Disabled)";
            if (_lootBoxPrefab == null) label = "LootBox (No Prefab!)";
            
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, label);
#endif
        }

        #endregion
    }
}
