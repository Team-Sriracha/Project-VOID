using UnityEngine;

namespace ProjectVoid.Map
{
    /// <summary>
    /// 맵 레이아웃 템플릿을 정의하는 ScriptableObject
    /// Unity 에디터에서 2D 그리드로 맵 구조를 미리 정의합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "MapLayoutTemplate", menuName = "Project VOID/Map/Layout Template")]
    public class MapLayoutTemplate : ScriptableObject
    {
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
