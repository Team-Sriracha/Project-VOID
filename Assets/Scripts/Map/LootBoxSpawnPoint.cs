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

        [Header("Gizmo 설정")]
        [Tooltip("기본 LootBox 크기 (프리팹 미설정 시 Gizmo 크기)")]
        [SerializeField] private Vector3 _defaultLootBoxSize = new Vector3(2f, 1f, 1f);

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

        private Vector3 GetLootBoxSize()
        {
            // 프리팹이 있으면 실제 크기, 없으면 기본 크기 사용
            if (_lootBoxPrefab == null)
                return _defaultLootBoxSize;

            // Renderer에서 크기 가져오기
            Renderer renderer = _lootBoxPrefab.GetComponentInChildren<Renderer>();
            if (renderer != null)
                return renderer.bounds.size;

            // Collider에서 크기 가져오기
            Collider collider = _lootBoxPrefab.GetComponentInChildren<Collider>();
            if (collider != null)
                return collider.bounds.size;

            // 둘 다 없으면 기본 크기
            return _defaultLootBoxSize;
        }

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

            // 오브젝트의 회전/스케일을 Gizmo에 반영
            Gizmos.matrix = transform.localToWorldMatrix;

            // LootBox 크기 가져오기
            Vector3 lootBoxSize = GetLootBoxSize();
            Vector3 boxCenter = Vector3.up * (lootBoxSize.y * 0.5f);

            // 박스 위치 표시 (실제 크기, 회전 반영)
            Gizmos.DrawWireCube(boxCenter, lootBoxSize);

            // 아이템 발사 방향 화살표 (로컬 forward 방향)
            Gizmos.color = Color.cyan;
            Vector3 arrowStart = boxCenter;
            float arrowLength = Mathf.Max(lootBoxSize.z, lootBoxSize.x) * 1.5f;
            Vector3 arrowEnd = arrowStart + Vector3.forward * arrowLength;
            Gizmos.DrawLine(arrowStart, arrowEnd);

            // 화살표 머리
            float arrowHeadSize = arrowLength * 0.2f;
            Vector3 arrowRight = arrowEnd - Vector3.forward * arrowHeadSize + Vector3.right * arrowHeadSize * 0.5f;
            Vector3 arrowLeft = arrowEnd - Vector3.forward * arrowHeadSize - Vector3.right * arrowHeadSize * 0.5f;
            Gizmos.DrawLine(arrowEnd, arrowRight);
            Gizmos.DrawLine(arrowEnd, arrowLeft);

            // 퍼짐 범위 표시 (±45도)
            Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
            float spreadLength = arrowLength * 0.8f;
            Vector3 spreadRight = Quaternion.Euler(0, 45f, 0) * Vector3.forward * spreadLength;
            Vector3 spreadLeft = Quaternion.Euler(0, -45f, 0) * Vector3.forward * spreadLength;
            Gizmos.DrawLine(arrowStart, arrowStart + spreadRight);
            Gizmos.DrawLine(arrowStart, arrowStart + spreadLeft);

            // Gizmos.matrix 복원
            Gizmos.matrix = Matrix4x4.identity;
        }

        private void OnDrawGizmosSelected()
        {
            // LootBox 크기 가져오기
            Vector3 lootBoxSize = GetLootBoxSize();
            Vector3 boxCenter = Vector3.up * (lootBoxSize.y * 0.5f);

            // 오브젝트의 회전/스케일을 Gizmo에 반영
            Gizmos.matrix = transform.localToWorldMatrix;

            // 선택 시 더 진한 박스 표시 (실제 크기, 회전 반영)
            Gizmos.color = _isEnabled ? Color.yellow : Color.gray;
            Gizmos.DrawCube(boxCenter, lootBoxSize);

            // 선택 시 더 진한 화살표
            Gizmos.color = Color.cyan;
            float arrowLength = Mathf.Max(lootBoxSize.z, lootBoxSize.x) * 1.5f;
            Vector3 arrowEnd = boxCenter + Vector3.forward * arrowLength;
            Gizmos.DrawLine(boxCenter, arrowEnd);

            // Gizmos.matrix 복원 (Handles.Label은 월드 좌표 사용)
            Gizmos.matrix = Matrix4x4.identity;

#if UNITY_EDITOR
            // Handles.Label은 월드 좌표 기준이므로 변환 필요
            Vector3 worldBoxCenter = transform.TransformPoint(boxCenter);
            Vector3 worldArrowEnd = transform.TransformPoint(arrowEnd);

            string label = _customRespawnTime > 0f
                ? $"LootBox (Respawn: {_customRespawnTime}s)"
                : "LootBox (Default)";

            if (!_isEnabled) label = "LootBox (Disabled)";
            if (_lootBoxPrefab == null) label = "LootBox (No Prefab!)";

            // 라벨을 박스 위에 표시
            UnityEditor.Handles.Label(worldBoxCenter + Vector3.up * (lootBoxSize.y * 0.5f + 0.5f), label);

            // 발사 방향 라벨
            UnityEditor.Handles.Label(worldArrowEnd + Vector3.up * 0.3f, "→ Item Spawn Direction");
#endif
        }

        #endregion
    }
}
