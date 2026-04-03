using UnityEngine;

namespace ProjectVoid.Map
{
    /// <summary>
    /// 청크 프리팹과 메타데이터를 저장하는 ScriptableObject
    /// </summary>
    [CreateAssetMenu(fileName = "ChunkPrefabData", menuName = "Project VOID/Map/Chunk Prefab Data")]
    public class ChunkPrefabData : ScriptableObject
    {
        #region Serialized Fields

        [Header("프리팹")]
        [Tooltip("Chunk GameObject 프리팹 (Ground 타일만 있는 기본 청크)")]
        [SerializeField] private GameObject _chunkPrefab;



        [Header("기본 정보")]
        [Tooltip("청크 고유 ID")]
        [SerializeField] private string _chunkId;

        [Tooltip("청크 타입 - 시각적/게임플레이 차별화 목적 (Central=중앙허브, Normal=일반방, Special=특수방)")]
        [SerializeField] private ChunkType _chunkType = ChunkType.Normal;

        [Header("생성 확률")]
        [Tooltip("이 청크가 선택될 가중치 (높을수록 자주 등장)")]
        [SerializeField] [Range(0f, 10f)] private float _spawnWeight = 1.0f;

        [Header("청크 크기")]
        [Tooltip("청크 크기 (타일 개수, 위치 계산용)")]
        [SerializeField] private int _chunkSize = 10;

        [Tooltip("청크가 차지하는 그리드 너비 (1칸, 2칸 등)")]
        [SerializeField] [Range(1, 4)] private int _chunkWidth = 1;

        [Tooltip("청크가 차지하는 그리드 높이 (1칸, 2칸 등)")]
        [SerializeField] [Range(1, 4)] private int _chunkHeight = 1;

        #endregion

        #region Properties

        /// <summary>
        /// 청크 프리팹
        /// </summary>
        public GameObject ChunkPrefab => _chunkPrefab;

        /// <summary>
        /// 청크 고유 ID
        /// </summary>
        public string ChunkId => _chunkId;

        /// <summary>
        /// 청크 타입
        /// </summary>
        public ChunkType ChunkType => _chunkType;

        /// <summary>
        /// 생성 가중치
        /// </summary>
        public float SpawnWeight => _spawnWeight;

        /// <summary>
        /// 청크 크기 (위치 계산용)
        /// </summary>
        public int ChunkSize => _chunkSize;

        /// <summary>
        /// 청크가 차지하는 그리드 너비
        /// </summary>
        public int ChunkWidth => _chunkWidth;

        /// <summary>
        /// 청크가 차지하는 그리드 높이
        /// </summary>
        public int ChunkHeight => _chunkHeight;



        #endregion

        #region Unity Lifecycle

        private void OnValidate()
        {
            // 프리팹 검증
            if (_chunkPrefab == null)
            {
                Debug.LogWarning($"[{_chunkId}] Chunk 프리팹이 설정되지 않았습니다!");
                return;
            }


        }

        #endregion
    }
}
