using UnityEngine;

namespace ProjectVoid.Map
{
    /// <summary>
    /// 게임 모드별 템플릿 세트
    /// </summary>
    [System.Serializable]
    public struct MapTemplateSet
    {
        public GameMode GameMode;
        public int MinPlayers;
        public int MaxPlayers;
        public MapLayoutTemplate[] Templates;
    }

    /// <summary>
    /// 맵 생성 설정을 저장하는 ScriptableObject
    /// </summary>
    [CreateAssetMenu(fileName = "MapGenerationSettings", menuName = "Project VOID/Map/Generation Settings")]
    public class MapGenerationSettings : ScriptableObject
    {
        #region Serialized Fields

        [Header("청크 기본 설정")]
        [Tooltip("청크 하나의 크기 (타일 개수)")]
        [SerializeField] private int _chunkSize = 10;

        [Header("중앙 클러스터")]
        [Tooltip("중앙 클러스터용 청크 프리팹 데이터")]
        [SerializeField] private ChunkPrefabData[] _centralChunks;

        [Header("청크 타입")]
        [Tooltip("일반 청크 프리팹 데이터 풀")]
        [SerializeField] private ChunkPrefabData[] _normalChunks;

        [Tooltip("스페셜 청크 프리팹 데이터 풀")]
        [SerializeField] private ChunkPrefabData[] _specialChunks;

        [Header("게임 모드별 템플릿")]
        [Tooltip("게임 모드 및 인원수에 따른 템플릿 세트")]
        [SerializeField] private MapTemplateSet[] _templateSets;

        [Tooltip("이웃 청크가 있을 때 문 생성 확률 (0-1, 낮을수록 막힌 벽이 많음)")]
        [SerializeField] [Range(0f, 1f)] private float _doorGenerationProbability = 0.7f;

        [Header("벽/문 프리팹")]
        [Tooltip("벽 GameObject 프리팹 (이웃이 없는 방향에 생성)")]
        [SerializeField] private GameObject _wallPrefab;

        [Tooltip("문 GameObject 프리팹 (이웃이 있는 방향에 생성, 선택사항)")]
        [SerializeField] private GameObject _doorPrefab;

        [Tooltip("벽 배치 간격 (벽을 몇 유닛마다 배치할지)")]
        [SerializeField] [Range(0.1f, 10f)] private float _wallSpacing = 1f;

        [Header("시드")]
        [Tooltip("랜덤 시드 사용 (체크 해제 시 고정 시드 사용)")]
        [SerializeField] private bool _useRandomSeed = true;

        [Tooltip("고정 시드 값 (디버깅용)")]
        [SerializeField] private int _fixedSeed = 12345;

        #endregion

        #region Properties

        /// <summary>
        /// 청크 크기 (타일 개수)
        /// </summary>
        public int ChunkSize => _chunkSize;

        /// <summary>
        /// 중앙 청크 배열
        /// </summary>
        public ChunkPrefabData[] CentralChunks => _centralChunks;

        /// <summary>
        /// 일반 청크 배열
        /// </summary>
        public ChunkPrefabData[] NormalChunks => _normalChunks;

        /// <summary>
        /// 스페셜 청크 배열
        /// </summary>
        public ChunkPrefabData[] SpecialChunks => _specialChunks;

        /// <summary>
        /// 랜덤 시드 사용 여부
        /// </summary>
        public bool UseRandomSeed => _useRandomSeed;

        /// <summary>
        /// 고정 시드 값
        /// </summary>
        public int FixedSeed => _fixedSeed;

        /// <summary>
        /// 게임 모드별 템플릿 세트 배열
        /// </summary>
        public MapTemplateSet[] TemplateSets => _templateSets;

        /// <summary>
        /// 맵 레이아웃 템플릿 배열 (첫 번째 템플릿 세트에서 가져옴, 호환성 유지용)
        /// </summary>
        public MapLayoutTemplate[] MapTemplates => _templateSets != null && _templateSets.Length > 0 ? _templateSets[0].Templates : null;

        /// <summary>
        /// 문 생성 확률 (0-1)
        /// </summary>
        public float DoorGenerationProbability => _doorGenerationProbability;

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

        #endregion

        #region Public Methods

        /// <summary>
        /// 현재 설정에 따라 시드를 생성합니다.
        /// </summary>
        public int GenerateSeed()
        {
            return _useRandomSeed ? Random.Range(int.MinValue, int.MaxValue) : _fixedSeed;
        }

        /// <summary>
        /// 가중치를 고려하여 일반 청크를 랜덤하게 선택합니다.
        /// </summary>
        public ChunkPrefabData GetRandomNormalChunk()
        {
            return GetWeightedRandomChunk(_normalChunks, "Normal");
        }

        /// <summary>
        /// 가중치를 고려하여 스페셜 청크를 랜덤하게 선택합니다.
        /// </summary>
        public ChunkPrefabData GetRandomSpecialChunk()
        {
            return GetWeightedRandomChunk(_specialChunks, "Special");
        }

        /// <summary>
        /// 가중치를 고려하여 중앙 청크를 랜덤하게 선택합니다.
        /// </summary>
        public ChunkPrefabData GetRandomCentralChunk()
        {
            return GetWeightedRandomChunk(_centralChunks, "Central");
        }

        /// <summary>
        /// 게임 모드와 플레이어 수에 맞는 템플릿 배열을 반환합니다.
        /// </summary>
        /// <param name="mode">게임 모드</param>
        /// <param name="playerCount">플레이어 수</param>
        /// <returns>해당 조건에 맞는 템플릿 배열, 없으면 기본 템플릿</returns>
        public MapLayoutTemplate[] GetTemplatesForMode(GameMode mode, int playerCount)
        {
            if (_templateSets == null || _templateSets.Length == 0)
            {
                Debug.LogError("[MapGenerationSettings] 템플릿 세트가 설정되지 않았습니다!");
                return null;
            }

            // 게임 모드와 플레이어 수에 맞는 템플릿 찾기
            foreach (var set in _templateSets)
            {
                if (set.GameMode == mode && 
                    playerCount >= set.MinPlayers && 
                    playerCount <= set.MaxPlayers &&
                    set.Templates != null && set.Templates.Length > 0)
                {
                    return set.Templates;
                }
            }

            // 랭크 템플릿이 별도로 없으면 일반 8인 템플릿으로 폴백
            if (mode == GameMode.Ranked)
            {
                foreach (var set in _templateSets)
                {
                    if (set.GameMode == GameMode.EightPlayer &&
                        playerCount >= set.MinPlayers &&
                        playerCount <= set.MaxPlayers &&
                        set.Templates != null && set.Templates.Length > 0)
                    {
                        Debug.LogWarning("[MapGenerationSettings] Ranked 템플릿이 없어 EightPlayer 템플릿으로 대체합니다.");
                        return set.Templates;
                    }
                }
            }

            // 매칭되는 세트가 없으면 첫 번째 템플릿 세트 반환 (fallback)
            Debug.LogWarning($"[MapGenerationSettings] {mode} ({playerCount}명)에 맞는 템플릿을 찾을 수 없어 첫 번째 템플릿 세트 사용");
            return _templateSets[0].Templates;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 가중치 기반 랜덤 선택
        /// </summary>
        private ChunkPrefabData GetWeightedRandomChunk(ChunkPrefabData[] chunks, string chunkType)
        {
            if (chunks == null || chunks.Length == 0)
            {
                Debug.LogError($"[{chunkType}] 청크 배열이 비어있습니다! MapGenerationSettings에서 {chunkType} Chunks를 설정해주세요.");
                return null;
            }

            // null이 아닌 청크만 필터링
            var validChunks = new System.Collections.Generic.List<ChunkPrefabData>();
            foreach (var chunk in chunks)
            {
                if (chunk != null && chunk.ChunkPrefab != null)
                {
                    validChunks.Add(chunk);
                }
                else if (chunk != null && chunk.ChunkPrefab == null)
                {
                    Debug.LogError($"[{chunkType}] ChunkPrefabData '{chunk.name}'의 ChunkPrefab 필드가 null입니다!");
                }
            }

            if (validChunks.Count == 0)
            {
                Debug.LogError($"[{chunkType}] 유효한 청크가 없습니다! 모든 ChunkPrefabData에 프리팹을 할당해주세요.");
                return null;
            }

            // 전체 가중치 합계 계산
            float totalWeight = 0f;
            foreach (var chunk in validChunks)
            {
                totalWeight += chunk.SpawnWeight;
            }

            // 랜덤 값 선택
            float randomValue = Random.Range(0f, totalWeight);

            // 가중치에 따라 청크 선택
            float currentWeight = 0f;
            foreach (var chunk in validChunks)
            {
                currentWeight += chunk.SpawnWeight;
                if (randomValue <= currentWeight)
                {
                    return chunk;
                }
            }

            // Fallback
            return validChunks[0];
        }

        #endregion

        #region Unity Lifecycle

        private void OnValidate()
        {
            // 유효성 검사
            if (_centralChunks == null || _centralChunks.Length == 0)
            {
                Debug.LogWarning("중앙 청크가 설정되지 않았습니다!");
            }

            if (_normalChunks == null || _normalChunks.Length == 0)
            {
                Debug.LogWarning("일반 청크가 설정되지 않았습니다!");
            }

            if (_specialChunks == null || _specialChunks.Length == 0)
            {
                Debug.LogWarning("스페셜 청크가 설정되지 않았습니다!");
            }

            if (_templateSets == null || _templateSets.Length == 0)
            {
                Debug.LogWarning("맵 템플릿 세트가 설정되지 않았습니다!");
            }
        }

        #endregion
    }
}
