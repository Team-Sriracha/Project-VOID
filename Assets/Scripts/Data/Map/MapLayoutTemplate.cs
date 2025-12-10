using UnityEngine;

namespace ProjectVoid.Map
{
    /// <summary>
    /// 플레이어 스폰 위치 정보
    /// </summary>
    [System.Serializable]
    public struct PlayerSpawnPoint
    {
        [Tooltip("스폰 위치 (그리드 좌표)")] 
        public Vector2Int GridPosition;
        
        [Tooltip("청크 내 오프셋 (0~1, 청크 크기 기준)")]
        public Vector2 LocalOffset;
        
        [Tooltip("스폰 방향 (Y축 회전, 0=북쪽)")]
        [Range(0f, 360f)]
        public float SpawnRotation;

        /// <summary>
        /// 유효한 스폰 포인트인지 확인
        /// </summary>
        public bool IsValid => GridPosition.x >= 0 && GridPosition.y >= 0;

        /// <summary>
        /// 빈 스폰 포인트 생성
        /// </summary>
        public static PlayerSpawnPoint Empty => new PlayerSpawnPoint
        {
            GridPosition = new Vector2Int(-1, -1),
            LocalOffset = new Vector2(0.5f, 0.5f),
            SpawnRotation = 0f
        };
    }

    /// <summary>
    /// 맵 레이아웃 템플릿을 정의하는 ScriptableObject
    /// Unity 에디터에서 2D 그리드로 맵 구조를 미리 정의합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "MapLayoutTemplate", menuName = "Project VOID/Map/Layout Template")]
    public class MapLayoutTemplate : ScriptableObject
    {
        #region Constants

        public const int MAX_SPAWN_POINTS = 8;

        #endregion

        #region Serialized Fields

        [Header("그리드 크기")]
        [Tooltip("그리드 너비")]
        [SerializeField] [Range(1, 50)] private int _width = 20;

        [Tooltip("그리드 높이")]
        [SerializeField] [Range(1, 50)] private int _height = 20;

        [Header("레이아웃 데이터")]
        [Tooltip("그리드 데이터 (Width * Height 크기의 1D 배열)")]
        [SerializeField] private EGridCell[] _gridData;

        [Tooltip("청크 그룹 ID (같은 ID를 가진 청크들은 하나의 큰 청크로 취급, 0=그룹 없음)")]
        [SerializeField] private int[] _chunkGroupIds;

        [Header("템플릿 정보")]
        [Tooltip("템플릿 이름 (설명용)")]
        [SerializeField] private string _templateName = "New Layout";

        [Tooltip("템플릿 설명")]
        [SerializeField] [TextArea(3, 5)] private string _description;

        [Header("플레이어 스폰 위치")]
        [Tooltip("플레이어 스폰 위치 (최대 8개, 가장자리 청크에 배치 권장)")]
        [SerializeField] private PlayerSpawnPoint[] _playerSpawnPoints = new PlayerSpawnPoint[MAX_SPAWN_POINTS];

        #endregion

        #region Properties

        /// <summary>
        /// 그리드 너비
        /// </summary>
        public int Width => _width;

        /// <summary>
        /// 그리드 높이
        /// </summary>
        public int Height => _height;

        /// <summary>
        /// 템플릿 이름
        /// </summary>
        public string TemplateName => _templateName;

        /// <summary>
        /// 템플릿 설명
        /// </summary>
        public string Description => _description;

        /// <summary>
        /// 플레이어 스폰 위치 배열
        /// </summary>
        public PlayerSpawnPoint[] PlayerSpawnPoints => _playerSpawnPoints;

        /// <summary>
        /// 유효한 스폰 포인트 개수
        /// </summary>
        public int ValidSpawnPointCount
        {
            get
            {
                if (_playerSpawnPoints == null) return 0;
                int count = 0;
                foreach (var sp in _playerSpawnPoints)
                {
                    if (sp.IsValid) count++;
                }
                return count;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 특정 위치의 셀 타입을 반환합니다.
        /// </summary>
        public EGridCell GetCell(int x, int y)
        {
            if (!IsValidPosition(x, y))
            {
                return EGridCell.Empty;
            }

            if (_gridData == null || _gridData.Length == 0)
            {
                return EGridCell.Empty;
            }

            int index = GetIndex(x, y);
            if (index >= _gridData.Length)
            {
                return EGridCell.Empty;
            }

            return _gridData[index];
        }

        /// <summary>
        /// 특정 위치의 셀 타입을 설정합니다.
        /// </summary>
        public void SetCell(int x, int y, EGridCell cellType)
        {
            if (!IsValidPosition(x, y))
            {
                Debug.LogWarning($"잘못된 위치: ({x}, {y})");
                return;
            }

            int index = GetIndex(x, y);
            if (_gridData == null || _gridData.Length != _width * _height)
            {
                InitializeGrid();
            }

            _gridData[index] = cellType;
        }

        /// <summary>
        /// 특정 위치의 청크 그룹 ID를 반환합니다.
        /// </summary>
        public int GetChunkGroupId(int x, int y)
        {
            if (!IsValidPosition(x, y))
            {
                return 0;
            }

            if (_chunkGroupIds == null || _chunkGroupIds.Length == 0)
            {
                return 0;
            }

            int index = GetIndex(x, y);
            if (index >= _chunkGroupIds.Length)
            {
                return 0;
            }

            return _chunkGroupIds[index];
        }

        /// <summary>
        /// 특정 위치의 청크 그룹 ID를 설정합니다.
        /// </summary>
        public void SetChunkGroupId(int x, int y, int groupId)
        {
            if (!IsValidPosition(x, y))
            {
                Debug.LogWarning($"잘못된 위치: ({x}, {y})");
                return;
            }

            int index = GetIndex(x, y);
            if (_chunkGroupIds == null || _chunkGroupIds.Length != _width * _height)
            {
                InitializeChunkGroups();
            }

            _chunkGroupIds[index] = groupId;
        }

        /// <summary>
        /// 위치가 유효한지 확인합니다.
        /// </summary>
        public bool IsValidPosition(int x, int y)
        {
            return x >= 0 && x < _width && y >= 0 && y < _height;
        }

        /// <summary>
        /// 중앙 위치를 반환합니다.
        /// </summary>
        public Vector2Int GetCenterPosition()
        {
            return new Vector2Int(_width / 2, _height / 2);
        }

        /// <summary>
        /// 비어있지 않은 셀의 개수를 반환합니다.
        /// </summary>
        public int GetFilledCellCount()
        {
            if (_gridData == null)
                return 0;

            int count = 0;
            foreach (var cell in _gridData)
            {
                if (cell != EGridCell.Empty)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 특정 인덱스의 스폰 포인트를 반환합니다.
        /// </summary>
        /// <param name="index">스폰 포인트 인덱스 (0~7)</param>
        /// <returns>스폰 포인트, 없으면 Empty</returns>
        public PlayerSpawnPoint GetSpawnPoint(int index)
        {
            if (_playerSpawnPoints == null || index < 0 || index >= _playerSpawnPoints.Length)
            {
                return PlayerSpawnPoint.Empty;
            }
            return _playerSpawnPoints[index];
        }

        /// <summary>
        /// 특정 인덱스의 스폰 포인트를 설정합니다.
        /// </summary>
        /// <param name="index">스폰 포인트 인덱스 (0~7)</param>
        /// <param name="spawnPoint">스폰 포인트</param>
        public void SetSpawnPoint(int index, PlayerSpawnPoint spawnPoint)
        {
            if (index < 0 || index >= MAX_SPAWN_POINTS)
            {
                Debug.LogWarning($"잘못된 스폰 포인트 인덱스: {index}");
                return;
            }

            if (_playerSpawnPoints == null || _playerSpawnPoints.Length != MAX_SPAWN_POINTS)
            {
                InitializeSpawnPoints();
            }

            _playerSpawnPoints[index] = spawnPoint;
        }

        /// <summary>
        /// 유효한 스폰 포인트들만 반환합니다.
        /// </summary>
        /// <returns>유효한 스폰 포인트 배열</returns>
        public PlayerSpawnPoint[] GetValidSpawnPoints()
        {
            if (_playerSpawnPoints == null) return new PlayerSpawnPoint[0];

            var validPoints = new System.Collections.Generic.List<PlayerSpawnPoint>();
            foreach (var sp in _playerSpawnPoints)
            {
                if (sp.IsValid)
                {
                    validPoints.Add(sp);
                }
            }
            return validPoints.ToArray();
        }

        /// <summary>
        /// 플레이어 수에 맞는 스폰 포인트들을 반환합니다.
        /// </summary>
        /// <param name="playerCount">플레이어 수</param>
        /// <returns>스폰 포인트 배열</returns>
        public PlayerSpawnPoint[] GetSpawnPointsForPlayerCount(int playerCount)
        {
            var validPoints = GetValidSpawnPoints();
            
            if (validPoints.Length == 0)
            {
                Debug.LogWarning($"[MapLayoutTemplate] {_templateName}: 유효한 스폰 포인트가 없습니다!");
                return new PlayerSpawnPoint[0];
            }

            if (playerCount <= validPoints.Length)
            {
                // 필요한 만큼만 반환
                var result = new PlayerSpawnPoint[playerCount];
                System.Array.Copy(validPoints, result, playerCount);
                return result;
            }
            else
            {
                // 스폰 포인트가 부족하면 경고 후 있는 만큼 반환
                Debug.LogWarning($"[MapLayoutTemplate] {_templateName}: 스폰 포인트 부족 (필요: {playerCount}, 보유: {validPoints.Length})");
                return validPoints;
            }
        }

        /// <summary>
        /// 스폰 포인트가 가장자리 청크에 있는지 확인합니다.
        /// </summary>
        public bool IsEdgeSpawnPoint(PlayerSpawnPoint spawnPoint)
        {
            if (!spawnPoint.IsValid) return false;
            
            int x = spawnPoint.GridPosition.x;
            int y = spawnPoint.GridPosition.y;

            // 가장자리 여부 확인 (맵 경계 또는 주변에 빈 셀이 있는지)
            bool isOnMapEdge = x == 0 || x == _width - 1 || y == 0 || y == _height - 1;
            if (isOnMapEdge) return true;

            // 주변 8방향 중 빈 셀이 있으면 가장자리로 간주
            int[] dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
            int[] dy = { -1, -1, -1, 0, 0, 1, 1, 1 };

            for (int i = 0; i < 8; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];

                if (!IsValidPosition(nx, ny) || GetCell(nx, ny) == EGridCell.Empty)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 2D 좌표를 1D 인덱스로 변환합니다.
        /// </summary>
        private int GetIndex(int x, int y)
        {
            return y * _width + x;
        }

        /// <summary>
        /// 그리드를 초기화합니다.
        /// </summary>
        private void InitializeGrid()
        {
            _gridData = new EGridCell[_width * _height];

            // 모든 셀을 Empty로 초기화
            for (int i = 0; i < _gridData.Length; i++)
            {
                _gridData[i] = EGridCell.Empty;
            }

            // 중앙에 Central 청크 배치
            Vector2Int center = GetCenterPosition();
            SetCell(center.x, center.y, EGridCell.Central);

            // 청크 그룹도 함께 초기화
            InitializeChunkGroups();
        }

        /// <summary>
        /// 청크 그룹 배열을 초기화합니다.
        /// </summary>
        private void InitializeChunkGroups()
        {
            _chunkGroupIds = new int[_width * _height];

            // 모든 그룹 ID를 0으로 초기화 (그룹 없음)
            for (int i = 0; i < _chunkGroupIds.Length; i++)
            {
                _chunkGroupIds[i] = 0;
            }
        }

        /// <summary>
        /// 스폰 포인트 배열을 초기화합니다.
        /// </summary>
        private void InitializeSpawnPoints()
        {
            _playerSpawnPoints = new PlayerSpawnPoint[MAX_SPAWN_POINTS];

            for (int i = 0; i < MAX_SPAWN_POINTS; i++)
            {
                _playerSpawnPoints[i] = PlayerSpawnPoint.Empty;
            }
        }

        #endregion

        #region Unity Lifecycle

        private void OnValidate()
        {
            // 그리드 크기가 변경되면 재초기화
            if (_gridData == null || _gridData.Length != _width * _height)
            {
                InitializeGrid();
            }

            // 청크 그룹 배열 크기 검증
            if (_chunkGroupIds == null || _chunkGroupIds.Length != _width * _height)
            {
                InitializeChunkGroups();
            }

            // 스폰 포인트 배열 크기 검증
            if (_playerSpawnPoints == null || _playerSpawnPoints.Length != MAX_SPAWN_POINTS)
            {
                InitializeSpawnPoints();
            }

            // 템플릿 이름이 비어있으면 기본값 설정
            if (string.IsNullOrEmpty(_templateName))
            {
                _templateName = name;
            }
        }

        #endregion

        #region Debug

        /// <summary>
        /// 그리드를 문자열로 출력합니다 (디버깅용)
        /// </summary>
        public string ToDebugString()
        {
            if (_gridData == null)
                return "Grid is null";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"Template: {_templateName} ({_width}x{_height})");
            sb.AppendLine($"Filled Cells: {GetFilledCellCount()}");
            sb.AppendLine();

            for (int y = _height - 1; y >= 0; y--)
            {
                for (int x = 0; x < _width; x++)
                {
                    EGridCell cell = GetCell(x, y);
                    int groupId = GetChunkGroupId(x, y);

                    char c = cell switch
                    {
                        EGridCell.Empty => '.',
                        EGridCell.Central => 'C',
                        EGridCell.Normal => 'N',
                        EGridCell.Special => 'S',
                        _ => '?'
                    };

                    // 그룹 ID가 있으면 숫자로 표시 (1~9)
                    if (groupId > 0 && groupId <= 9)
                    {
                        c = (char)('0' + groupId);
                    }

                    sb.Append(c);
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        #endregion
    }
}
