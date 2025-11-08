using UnityEngine;

namespace ProjectVoid.Map
{
    /// <summary>
    /// 청크 프리팹과 메타데이터를 저장하는 ScriptableObject
    /// </summary>
    [CreateAssetMenu(fileName = "ChunkPrefabData", menuName = "ProjectVoid/Map/Chunk Prefab Data")]
    public class ChunkPrefabData : ScriptableObject
    {
        #region Serialized Fields

        [Header("프리팹")]
        [Tooltip("Chunk GameObject 프리팹 (Ground 타일만 있는 기본 청크)")]
        [SerializeField] private GameObject _chunkPrefab;

        [Header("3D 오브젝트 (절차적 벽 생성용)")]
        [Tooltip("벽 GameObject 프리팹 (이웃이 없는 방향에 생성)")]
        [SerializeField] private GameObject _wallPrefab;

        [Tooltip("문 GameObject 프리팹 (선택사항, null이면 벽 없음)")]
        [SerializeField] private GameObject _doorPrefab;

        [Tooltip("벽 배치 간격 (벽을 몇 유닛마다 배치할지)")]
        [SerializeField] [Range(0.1f, 10f)] private float _wallSpacing = 1f;

        [Header("기본 정보")]
        [Tooltip("청크 고유 ID")]
        [SerializeField] private string _chunkId;

        [Tooltip("청크 타입 - 시각적/게임플레이 차별화 목적 (Central=중앙허브, Normal=일반방, Special=특수방)")]
        [SerializeField] private EChunkType _chunkType = EChunkType.Normal;

        [Header("문 정보 (레거시 - 네트워크 전송용)")]
        [Tooltip("절차적 벽 생성 시스템에서는 사용되지 않음. 네트워크 전송 호환성 유지용")]
        [SerializeField] private bool _hasNorthDoor;
        [SerializeField] private bool _hasEastDoor;
        [SerializeField] private bool _hasSouthDoor;
        [SerializeField] private bool _hasWestDoor;

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
        public EChunkType ChunkType => _chunkType;

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

        /// <summary>
        /// 벽 프리팹
        /// </summary>
        public GameObject WallPrefab => _wallPrefab;

        /// <summary>
        /// 문 프리팹
        /// </summary>
        public GameObject DoorPrefab => _doorPrefab;

        /// <summary>
        /// 벽 배치 간격
        /// </summary>
        public float WallSpacing => _wallSpacing;

        /// <summary>
        /// 문 정보를 비트마스크로 반환합니다.
        /// </summary>
        public byte DoorMask
        {
            get
            {
                byte mask = 0;
                if (_hasNorthDoor) mask |= (byte)EDirection.North;
                if (_hasEastDoor) mask |= (byte)EDirection.East;
                if (_hasSouthDoor) mask |= (byte)EDirection.South;
                if (_hasWestDoor) mask |= (byte)EDirection.West;
                return mask;
            }
        }

        /// <summary>
        /// 문의 개수를 반환합니다.
        /// </summary>
        public int DoorCount
        {
            get
            {
                int count = 0;
                if (_hasNorthDoor) count++;
                if (_hasEastDoor) count++;
                if (_hasSouthDoor) count++;
                if (_hasWestDoor) count++;
                return count;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 특정 방향에 문이 있는지 확인합니다.
        /// </summary>
        public bool HasDoor(EDirection direction)
        {
            return direction switch
            {
                EDirection.North => _hasNorthDoor,
                EDirection.East => _hasEastDoor,
                EDirection.South => _hasSouthDoor,
                EDirection.West => _hasWestDoor,
                _ => false
            };
        }

        /// <summary>
        /// NetworkChunkData로 변환합니다.
        /// </summary>
        public NetworkChunkData ToNetworkData(Vector2Int position)
        {
            return new NetworkChunkData(position, _chunkType, DoorMask);
        }

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

            // 3D 오브젝트 검증
            if (_wallPrefab == null)
            {
                Debug.LogWarning($"[{_chunkId}] Wall 프리팹이 설정되지 않았습니다! 벽이 생성되지 않습니다.");
            }

            if (_wallSpacing <= 0)
            {
                Debug.LogWarning($"[{_chunkId}] Wall Spacing은 0보다 커야 합니다!");
            }

            // 청크 타입 안내
            string typeDescription = _chunkType switch
            {
                EChunkType.Central => "중앙 허브 - 주요 전투 지역 (넓고 화려한 디자인)",
                EChunkType.Normal => "일반 통로/방 - 표준 디자인",
                EChunkType.Special => "스페셜 방 - 특수 용도 (보물 상자, 특수 아이템, 보스방 등)",
                _ => "알 수 없는 타입"
            };

            // 문/벽은 절차적으로 생성되므로 문 개수 검증 불필요
        }

        #endregion
    }
}
