using UnityEngine;
using UnityEditor;
using ProjectVoid.Map;

namespace ProjectVoid.Editor.Map
{
    /// <summary>
    /// MapLayoutTemplate의 Custom Inspector
    /// Unity 에디터에서 그리드를 시각적으로 편집할 수 있습니다.
    /// </summary>
    [CustomEditor(typeof(MapLayoutTemplate))]
    public class MapLayoutTemplateEditor : UnityEditor.Editor
    {
        #region Constants

        private const float DEFAULT_CELL_SIZE = 20f;
        private const float CELL_SPACING = 2f;
        private const float GRID_PADDING = 10f;
        private const int MAX_SPAWN_POINTS = 8;
        private const float MIN_ZOOM = 0.5f;
        private const float MAX_ZOOM = 3f;
        private const float ZOOM_STEP = 0.1f;

        // 스폰 포인트 색상 (플레이어 1~8)
        private static readonly Color[] SPAWN_POINT_COLORS = new Color[]
        {
            new Color(1f, 0f, 0f, 0.8f),       // 1: 빨강
            new Color(0f, 0f, 1f, 0.8f),       // 2: 파랑
            new Color(0f, 1f, 0f, 0.8f),       // 3: 초록
            new Color(1f, 1f, 0f, 0.8f),       // 4: 노랑
            new Color(1f, 0f, 1f, 0.8f),       // 5: 마젠타
            new Color(0f, 1f, 1f, 0.8f),       // 6: 시안
            new Color(1f, 0.5f, 0f, 0.8f),     // 7: 주황
            new Color(0.5f, 0f, 1f, 0.8f)      // 8: 보라
        };

        #endregion

        #region Private Fields

        private MapLayoutTemplate _template;
        private GridCell _selectedCellType = GridCell.Normal;
        private int _selectedGroupId = 0;
        private bool _isGroupIdMode = false;
        private bool _isSpawnPointMode = false;
        private int _selectedSpawnPointIndex = 0;
        private bool _isPainting = false;
        private Vector2 _scrollPosition;
        private float _zoomLevel = 1f;
        private float _previousZoom = 1f;
        private const float SCROLL_VIEW_MAX_HEIGHT = 800f;

        // 현재 셀 크기 (줌 적용)
        private float CellSize => DEFAULT_CELL_SIZE * _zoomLevel;
        private float CellSpacing => CELL_SPACING * _zoomLevel;

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            _template = (MapLayoutTemplate)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 기본 정보
            DrawBasicInfo();

            EditorGUILayout.Space(10);

            // 그리드 크기 설정
            DrawGridSizeSettings();

            EditorGUILayout.Space(10);

            // 편집 모드 선택
            DrawEditModeToggle();

            EditorGUILayout.Space(10);

            // 셀 타입 또는 그룹 ID 선택
            if (_isSpawnPointMode)
            {
                DrawSpawnPointPalette();
            }
            else if (_isGroupIdMode)
            {
                DrawGroupIdPalette();
            }
            else
            {
                DrawCellTypePalette();
            }

            EditorGUILayout.Space(10);

            // 그리드 에디터
            DrawGridEditor();

            EditorGUILayout.Space(10);

            // 유틸리티 버튼
            DrawUtilityButtons();

            EditorGUILayout.Space(10);

            // 디버그 정보
            DrawDebugInfo();

            if (GUI.changed)
            {
                EditorUtility.SetDirty(_template);
                serializedObject.ApplyModifiedProperties();
            }
        }

        #endregion

        #region Drawing Methods

        /// <summary>
        /// 기본 정보 표시
        /// </summary>
        private void DrawBasicInfo()
        {
            EditorGUILayout.LabelField("맵 레이아웃 템플릿", EditorStyles.boldLabel);

            SerializedProperty templateName = serializedObject.FindProperty("_templateName");
            SerializedProperty description = serializedObject.FindProperty("_description");

            EditorGUILayout.PropertyField(templateName, new GUIContent("템플릿 이름"));
            EditorGUILayout.PropertyField(description, new GUIContent("설명"));
        }

        /// <summary>
        /// 그리드 크기 설정
        /// </summary>
        private void DrawGridSizeSettings()
        {
            EditorGUILayout.LabelField("그리드 크기", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            SerializedProperty width = serializedObject.FindProperty("_width");
            SerializedProperty height = serializedObject.FindProperty("_height");

            EditorGUILayout.PropertyField(width, new GUIContent("너비"));
            EditorGUILayout.PropertyField(height, new GUIContent("높이"));

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                // 그리드 크기가 변경되면 OnValidate가 자동으로 호출됨
            }
        }

        /// <summary>
        /// 편집 모드 토글 (셀 타입 / 그룹 ID / 스폰 포인트)
        /// </summary>
        private void DrawEditModeToggle()
        {
            EditorGUILayout.LabelField("편집 모드", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Toggle(!_isGroupIdMode && !_isSpawnPointMode, "셀 타입 편집", "Button", GUILayout.Height(30)))
            {
                _isGroupIdMode = false;
                _isSpawnPointMode = false;
            }

            if (GUILayout.Toggle(_isGroupIdMode && !_isSpawnPointMode, "그룹 ID 편집", "Button", GUILayout.Height(30)))
            {
                _isGroupIdMode = true;
                _isSpawnPointMode = false;
            }

            if (GUILayout.Toggle(_isSpawnPointMode, "스폰 포인트 편집", "Button", GUILayout.Height(30)))
            {
                _isGroupIdMode = false;
                _isSpawnPointMode = true;
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 셀 타입 팔레트 (브러시 선택)
        /// </summary>
        private void DrawCellTypePalette()
        {
            EditorGUILayout.LabelField("셀 타입 브러시", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            // Empty
            if (GUILayout.Toggle(_selectedCellType == GridCell.Empty,
                new GUIContent("Empty", "빈 공간"),
                "Button", GUILayout.Height(30)))
            {
                _selectedCellType = GridCell.Empty;
            }

            // Central
            if (GUILayout.Toggle(_selectedCellType == GridCell.Central,
                new GUIContent("Central", "중앙 청크"),
                "Button", GUILayout.Height(30)))
            {
                _selectedCellType = GridCell.Central;
            }

            // Normal
            if (GUILayout.Toggle(_selectedCellType == GridCell.Normal,
                new GUIContent("Normal", "일반 청크"),
                "Button", GUILayout.Height(30)))
            {
                _selectedCellType = GridCell.Normal;
            }

            // Special
            if (GUILayout.Toggle(_selectedCellType == GridCell.Special,
                new GUIContent("Special", "스페셜 청크"),
                "Button", GUILayout.Height(30)))
            {
                _selectedCellType = GridCell.Special;
            }

            EditorGUILayout.EndHorizontal();

            // 선택된 셀 타입 표시
            EditorGUILayout.HelpBox($"선택된 브러시: {_selectedCellType}", MessageType.Info);
        }

        /// <summary>
        /// 그룹 ID 팔레트 (0~9)
        /// </summary>
        private void DrawGroupIdPalette()
        {
            EditorGUILayout.LabelField("청크 그룹 ID", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            // 그룹 ID 0 (그룹 없음)
            if (GUILayout.Toggle(_selectedGroupId == 0,
                new GUIContent("0", "그룹 없음"),
                "Button", GUILayout.Height(30)))
            {
                _selectedGroupId = 0;
            }

            // 그룹 ID 1~9
            for (int i = 1; i <= 9; i++)
            {
                int groupId = i;
                if (GUILayout.Toggle(_selectedGroupId == groupId,
                    new GUIContent(groupId.ToString(), $"그룹 {groupId}"),
                    "Button", GUILayout.Height(30)))
                {
                    _selectedGroupId = groupId;
                }
            }

            EditorGUILayout.EndHorizontal();

            // 선택된 그룹 ID 표시
            string groupIdInfo = _selectedGroupId == 0 ? "그룹 없음 (독립 청크)" : $"그룹 {_selectedGroupId} (같은 그룹 내부에는 벽이 생성되지 않음)";
            EditorGUILayout.HelpBox($"선택된 그룹 ID: {groupIdInfo}", MessageType.Info);
        }

        /// <summary>
        /// 스폰 포인트 팔레트 (1~8번 플레이어)
        /// </summary>
        private void DrawSpawnPointPalette()
        {
            EditorGUILayout.LabelField("플레이어 스폰 포인트", EditorStyles.boldLabel);

            var spawnPoints = _template.PlayerSpawnPoints;
            int validCount = _template.ValidSpawnPointCount;

            EditorGUILayout.LabelField($"현재 스폰 포인트: {validCount} / {MAX_SPAWN_POINTS}");

            EditorGUILayout.BeginHorizontal();

            // 스폰 포인트 1~8번 버튼
            for (int i = 0; i < MAX_SPAWN_POINTS; i++)
            {
                bool hasSpawnPoint = spawnPoints != null && i < spawnPoints.Length && spawnPoints[i].IsValid;
                Color originalBg = GUI.backgroundColor;
                
                if (hasSpawnPoint)
                {
                    GUI.backgroundColor = SPAWN_POINT_COLORS[i];
                }
                else
                {
                    GUI.backgroundColor = Color.gray;
                }

                string buttonText = $"P{i + 1}";
                if (GUILayout.Toggle(_selectedSpawnPointIndex == i, buttonText, "Button", GUILayout.Height(30), GUILayout.Width(35)))
                {
                    _selectedSpawnPointIndex = i;
                }

                GUI.backgroundColor = originalBg;
            }

            EditorGUILayout.EndHorizontal();

            // 선택된 스폰 포인트 정보
            EditorGUILayout.Space(5);
            if (spawnPoints != null && _selectedSpawnPointIndex < spawnPoints.Length && spawnPoints[_selectedSpawnPointIndex].IsValid)
            {
                var sp = spawnPoints[_selectedSpawnPointIndex];
                EditorGUILayout.HelpBox(
                    $"P{_selectedSpawnPointIndex + 1}: 그리드 ({sp.GridPosition.x}, {sp.GridPosition.y}), " +
                    $"오프셋 ({sp.LocalOffset.x:F2}, {sp.LocalOffset.y:F2}), " +
                    $"회전 {sp.SpawnRotation:F0}°", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox($"P{_selectedSpawnPointIndex + 1}: 미설정 (그리드 클릭하여 배치)", MessageType.Warning);
            }

            // 유틸리티 버튼
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("선택 스폰 포인트 삭제", GUILayout.Height(25)))
            {
                if (spawnPoints != null && _selectedSpawnPointIndex < spawnPoints.Length && spawnPoints[_selectedSpawnPointIndex].IsValid)
                {
                    Undo.RecordObject(_template, "Remove Spawn Point");
                    _template.SetSpawnPoint(_selectedSpawnPointIndex, PlayerSpawnPoint.Empty);
                    EditorUtility.SetDirty(_template);
                    Repaint();
                }
            }

            if (GUILayout.Button("모든 스폰 포인트 삭제", GUILayout.Height(25)))
            {
                if (EditorUtility.DisplayDialog("스폰 포인트 삭제",
                    "모든 스폰 포인트를 삭제하시겠습니까?", "Yes", "No"))
                {
                    Undo.RecordObject(_template, "Clear All Spawn Points");
                    for (int i = 0; i < MAX_SPAWN_POINTS; i++)
                    {
                        _template.SetSpawnPoint(i, PlayerSpawnPoint.Empty);
                    }
                    EditorUtility.SetDirty(_template);
                    Repaint();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 그리드 에디터 (클릭/드래그로 편집)
        /// </summary>
        private void DrawGridEditor()
        {
            EditorGUILayout.LabelField("그리드 에디터", EditorStyles.boldLabel);
            
            // 줌 컨트롤
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("확대/축소:", GUILayout.Width(65));
            
            if (GUILayout.Button("-", GUILayout.Width(25)))
            {
                _zoomLevel = Mathf.Max(MIN_ZOOM, _zoomLevel - ZOOM_STEP);
            }
            
            _zoomLevel = EditorGUILayout.Slider(_zoomLevel, MIN_ZOOM, MAX_ZOOM, GUILayout.Width(150));
            
            if (GUILayout.Button("+", GUILayout.Width(25)))
            {
                _zoomLevel = Mathf.Min(MAX_ZOOM, _zoomLevel + ZOOM_STEP);
            }
            
            if (GUILayout.Button("리셋", GUILayout.Width(40)))
            {
                _zoomLevel = 1f;
            }
            
            EditorGUILayout.LabelField($"{(_zoomLevel * 100):F0}%", GUILayout.Width(45));
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            if (_isSpawnPointMode)
            {
                EditorGUILayout.HelpBox("좌클릭: 스폰 포인트 배치 | 우클릭: 스폰 포인트 삭제 | Ctrl+휠: 확대/축소", MessageType.Info);
            }
            else if (_isGroupIdMode)
            {
                EditorGUILayout.HelpBox("좌클릭/드래그: 그룹 ID 설정 | 우클릭: 그룹 ID 초기화 | Ctrl+휠: 확대/축소", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("좌클릭/드래그: 셀 편집 | 우클릭: 셀 삭제 | Ctrl+휠: 확대/축소", MessageType.Info);
            }

            int width = _template.Width;
            int height = _template.Height;

            float cellSize = CellSize;
            float cellSpacing = CellSpacing;
            float totalWidth = width * (cellSize + cellSpacing) + GRID_PADDING * 2;
            float totalHeight = height * (cellSize + cellSpacing) + GRID_PADDING * 2;

            // 줌 변경 시 스크롤 위치를 중앙 기준으로 조정
            if (Mathf.Abs(_zoomLevel - _previousZoom) > 0.001f)
            {
                float zoomRatio = _zoomLevel / _previousZoom;
                
                // 현재 뷰포트의 중앙 위치 계산
                float viewportWidth = EditorGUIUtility.currentViewWidth - 40f;
                float viewportHeight = Mathf.Min(totalHeight / zoomRatio * _previousZoom, SCROLL_VIEW_MAX_HEIGHT);
                
                // 현재 스크롤 위치의 중앙점
                float centerX = _scrollPosition.x + viewportWidth / 2f;
                float centerY = _scrollPosition.y + viewportHeight / 2f;
                
                // 새로운 스크롤 위치 계산 (중앙 유지)
                _scrollPosition.x = centerX * zoomRatio - viewportWidth / 2f;
                _scrollPosition.y = centerY * zoomRatio - viewportHeight / 2f;
                
                // 스크롤 위치 클램핑
                _scrollPosition.x = Mathf.Max(0, _scrollPosition.x);
                _scrollPosition.y = Mathf.Max(0, _scrollPosition.y);
                
                _previousZoom = _zoomLevel;
            }

            // 스크롤 뷰 시작 (높이 제한 증가)
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition,
                GUILayout.MaxHeight(Mathf.Min(totalHeight, SCROLL_VIEW_MAX_HEIGHT)));

            Rect gridRect = GUILayoutUtility.GetRect(totalWidth, totalHeight);

            // 배경
            EditorGUI.DrawRect(gridRect, new Color(0.2f, 0.2f, 0.2f));

            // 그리드 그리기
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    DrawCell(gridRect, x, y);
                }
            }

            // 스폰 포인트 오버레이 그리기 (스폰 포인트 편집 모드에서만)
            if (_isSpawnPointMode)
            {
                DrawSpawnPointOverlays(gridRect);
            }

            // 마우스 입력 처리
            HandleMouseInput(gridRect, width, height);

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 스폰 포인트 오버레이 그리기
        /// </summary>
        private void DrawSpawnPointOverlays(Rect gridRect)
        {
            var spawnPoints = _template.PlayerSpawnPoints;
            if (spawnPoints == null || spawnPoints.Length == 0) return;

            float cellSize = CellSize;
            float cellSpacing = CellSpacing;

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                var sp = spawnPoints[i];
                if (!sp.IsValid) continue; // 유효하지 않은 스폰 포인트 스킵
                
                float xPos = gridRect.x + GRID_PADDING + sp.GridPosition.x * (cellSize + cellSpacing);
                float yPos = gridRect.y + GRID_PADDING + (_template.Height - 1 - sp.GridPosition.y) * (cellSize + cellSpacing);

                Rect cellRect = new Rect(xPos, yPos, cellSize, cellSize);

                // 스폰 포인트 마커 그리기
                Color spawnColor = SPAWN_POINT_COLORS[i % MAX_SPAWN_POINTS];
                
                // 선택된 스폰 포인트 강조
                if (_isSpawnPointMode && _selectedSpawnPointIndex == i)
                {
                    // 선택된 스폰 포인트: 두꺼운 흰색 테두리
                    Handles.color = Color.white;
                    float borderThickness = 3f * _zoomLevel;
                    Handles.DrawPolyLine(
                        new Vector3(cellRect.xMin - borderThickness, cellRect.yMin - borderThickness),
                        new Vector3(cellRect.xMax + borderThickness, cellRect.yMin - borderThickness),
                        new Vector3(cellRect.xMax + borderThickness, cellRect.yMax + borderThickness),
                        new Vector3(cellRect.xMin - borderThickness, cellRect.yMax + borderThickness),
                        new Vector3(cellRect.xMin - borderThickness, cellRect.yMin - borderThickness)
                    );
                }

                // 스폰 마커 배경 (검은색 원으로 대비 향상)
                Vector2 center = new Vector2(cellRect.center.x, cellRect.center.y);
                float markerRadius = cellSize * 0.4f;
                
                // 검은색 배경 원 (그림자 효과)
                Handles.color = Color.black;
                Handles.DrawSolidDisc(new Vector3(center.x, center.y, 0), Vector3.forward, markerRadius + 2f * _zoomLevel);
                
                // 흰색 테두리 원
                Handles.color = Color.white;
                Handles.DrawSolidDisc(new Vector3(center.x, center.y, 0), Vector3.forward, markerRadius + 1f * _zoomLevel);
                
                // 메인 색상 원
                Handles.color = spawnColor;
                Handles.DrawSolidDisc(new Vector3(center.x, center.y, 0), Vector3.forward, markerRadius);

                // 번호 표시 (그림자 효과 추가)
                int fontSize = Mathf.RoundToInt(11 * _zoomLevel);
                fontSize = Mathf.Clamp(fontSize, 8, 20);
                
                GUIStyle shadowStyle = new GUIStyle(GUI.skin.label);
                shadowStyle.alignment = TextAnchor.MiddleCenter;
                shadowStyle.fontSize = fontSize;
                shadowStyle.fontStyle = FontStyle.Bold;
                shadowStyle.normal.textColor = Color.black;
                
                // 그림자 (약간 오프셋)
                Rect shadowRect = new Rect(cellRect.x + 1, cellRect.y + 1, cellRect.width, cellRect.height);
                GUI.Label(shadowRect, (i + 1).ToString(), shadowStyle);

                // 메인 텍스트
                GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
                labelStyle.alignment = TextAnchor.MiddleCenter;
                labelStyle.fontSize = fontSize;
                labelStyle.fontStyle = FontStyle.Bold;
                labelStyle.normal.textColor = Color.white;

                GUI.Label(cellRect, (i + 1).ToString(), labelStyle);
            }
        }

        /// <summary>
        /// 개별 셀 그리기
        /// </summary>
        private void DrawCell(Rect gridRect, int x, int y)
        {
            float cellSize = CellSize;
            float cellSpacing = CellSpacing;
            
            float xPos = gridRect.x + GRID_PADDING + x * (cellSize + cellSpacing);
            float yPos = gridRect.y + GRID_PADDING + (_template.Height - 1 - y) * (cellSize + cellSpacing);

            Rect cellRect = new Rect(xPos, yPos, cellSize, cellSize);

            GridCell cellType = _template.GetCell(x, y);
            int groupId = _template.GetChunkGroupId(x, y);
            Color cellColor = GetCellColor(cellType);

            // 그룹 ID가 있으면 색상 조정 (그룹 ID 모드 또는 스폰 포인트 모드)
            if ((_isGroupIdMode || _isSpawnPointMode) && groupId > 0)
            {
                cellColor = GetGroupIdColor(groupId);
            }

            // 셀 그리기
            EditorGUI.DrawRect(cellRect, cellColor);

            // 테두리 (그룹 ID 모드 또는 스폰 포인트 모드일 때 강조)
            if ((_isGroupIdMode || _isSpawnPointMode) && groupId > 0)
            {
                Handles.color = Color.yellow;
            }
            else
            {
                Handles.color = Color.black;
            }
            Handles.DrawPolyLine(
                new Vector3(cellRect.xMin, cellRect.yMin),
                new Vector3(cellRect.xMax, cellRect.yMin),
                new Vector3(cellRect.xMax, cellRect.yMax),
                new Vector3(cellRect.xMin, cellRect.yMax),
                new Vector3(cellRect.xMin, cellRect.yMin)
            );

            // 텍스트 표시
            int fontSize = Mathf.RoundToInt(8 * _zoomLevel);
            fontSize = Mathf.Clamp(fontSize, 6, 16);
            
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.fontSize = fontSize;
            labelStyle.normal.textColor = Color.white;

            string cellText;
            if ((_isGroupIdMode || _isSpawnPointMode) && groupId > 0)
            {
                // 그룹 ID 모드 또는 스폰 포인트 모드: 그룹 번호 표시
                cellText = groupId.ToString();
            }
            else
            {
                // 셀 타입 모드: 셀 타입 표시
                cellText = cellType switch
                {
                    GridCell.Empty => "",
                    GridCell.Central => "C",
                    GridCell.Normal => "N",
                    GridCell.Special => "S",
                    _ => "?"
                };
            }

            GUI.Label(cellRect, cellText, labelStyle);
        }

        /// <summary>
        /// 마우스 입력 처리 (클릭/드래그/스크롤 휠)
        /// </summary>
        private void HandleMouseInput(Rect gridRect, int width, int height)
        {
            Event e = Event.current;

            // Ctrl + 스크롤 휠로 확대/축소 (마우스 위치 기준)
            if (e.type == EventType.ScrollWheel && e.control && gridRect.Contains(e.mousePosition))
            {
                float oldZoom = _zoomLevel;
                float zoomDelta = -e.delta.y * ZOOM_STEP * 0.5f;
                float newZoom = Mathf.Clamp(_zoomLevel + zoomDelta, MIN_ZOOM, MAX_ZOOM);
                
                if (Mathf.Abs(newZoom - oldZoom) > 0.001f)
                {
                    // 마우스 위치 기준으로 줌
                    Vector2 mouseRelative = e.mousePosition - new Vector2(gridRect.x, gridRect.y);
                    float zoomRatio = newZoom / oldZoom;
                    
                    // 마우스 위치가 줌 후에도 같은 그리드 위치를 가리키도록 스크롤 조정
                    _scrollPosition.x = (_scrollPosition.x + mouseRelative.x) * zoomRatio - mouseRelative.x;
                    _scrollPosition.y = (_scrollPosition.y + mouseRelative.y) * zoomRatio - mouseRelative.y;
                    
                    _scrollPosition.x = Mathf.Max(0, _scrollPosition.x);
                    _scrollPosition.y = Mathf.Max(0, _scrollPosition.y);
                    
                    _zoomLevel = newZoom;
                    _previousZoom = newZoom;
                }
                
                e.Use();
                Repaint();
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _isPainting = true;
                if (_isSpawnPointMode)
                {
                    PlaceSpawnPoint(e.mousePosition, gridRect, width, height);
                }
                else
                {
                    PaintCell(e.mousePosition, gridRect, width, height);
                }
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && _isPainting)
            {
                // 스폰 포인트 모드에서는 드래그로 연속 배치 안함
                if (!_isSpawnPointMode)
                {
                    PaintCell(e.mousePosition, gridRect, width, height);
                }
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                _isPainting = false;
            }
            // 우클릭으로 셀 삭제 (Empty로 설정)
            else if (e.type == EventType.MouseDown && e.button == 1)
            {
                _isPainting = true;
                EraseCell(e.mousePosition, gridRect, width, height);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 1 && _isPainting)
            {
                EraseCell(e.mousePosition, gridRect, width, height);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 1)
            {
                _isPainting = false;
            }
        }

        /// <summary>
        /// 셀 삭제 (Empty로 설정)
        /// </summary>
        private void EraseCell(Vector2 mousePos, Rect gridRect, int width, int height)
        {
            float cellSize = CellSize;
            float cellSpacing = CellSpacing;
            
            // 마우스 위치를 그리드 좌표로 변환
            float relativeX = mousePos.x - gridRect.x - GRID_PADDING;
            float relativeY = mousePos.y - gridRect.y - GRID_PADDING;

            int gridX = Mathf.FloorToInt(relativeX / (cellSize + cellSpacing));
            int gridY = height - 1 - Mathf.FloorToInt(relativeY / (cellSize + cellSpacing));

            if (gridX >= 0 && gridX < width && gridY >= 0 && gridY < height)
            {
                if (_isSpawnPointMode)
                {
                    // 스폰 포인트 모드: 해당 위치의 스폰 포인트 삭제
                    var spawnPoints = _template.PlayerSpawnPoints;
                    if (spawnPoints != null)
                    {
                        for (int i = 0; i < spawnPoints.Length; i++)
                        {
                            if (spawnPoints[i].IsValid && 
                                spawnPoints[i].GridPosition.x == gridX && 
                                spawnPoints[i].GridPosition.y == gridY)
                            {
                                Undo.RecordObject(_template, "Remove Spawn Point");
                                _template.SetSpawnPoint(i, PlayerSpawnPoint.Empty);
                                EditorUtility.SetDirty(_template);
                                serializedObject.Update();
                                Repaint();
                                return;
                            }
                        }
                    }
                }
                else if (_isGroupIdMode)
                {
                    // 그룹 ID 모드: 그룹 ID를 0으로 설정
                    Undo.RecordObject(_template, "Clear Group ID");
                    _template.SetChunkGroupId(gridX, gridY, 0);
                }
                else
                {
                    // 셀 타입 모드: Empty로 설정
                    Undo.RecordObject(_template, "Erase Cell");
                    _template.SetCell(gridX, gridY, GridCell.Empty);
                }

                EditorUtility.SetDirty(_template);
                serializedObject.Update();
                Repaint();
            }
        }

        /// <summary>
        /// 스폰 포인트 배치
        /// </summary>
        private void PlaceSpawnPoint(Vector2 mousePos, Rect gridRect, int width, int height)
        {
            float cellSize = CellSize;
            float cellSpacing = CellSpacing;
            
            // 마우스 위치를 그리드 좌표로 변환
            float relativeX = mousePos.x - gridRect.x - GRID_PADDING;
            float relativeY = mousePos.y - gridRect.y - GRID_PADDING;

            int gridX = Mathf.FloorToInt(relativeX / (cellSize + cellSpacing));
            int gridY = height - 1 - Mathf.FloorToInt(relativeY / (cellSize + cellSpacing));

            if (gridX >= 0 && gridX < width && gridY >= 0 && gridY < height)
            {
                // Empty 셀에는 스폰 포인트를 배치할 수 없음
                GridCell cellType = _template.GetCell(gridX, gridY);
                if (cellType == GridCell.Empty)
                {
                    Debug.LogWarning("스폰 포인트는 Empty 셀에 배치할 수 없습니다.");
                    return;
                }

                Undo.RecordObject(_template, "Place Spawn Point");

                // 선택된 인덱스에 스폰 포인트 설정
                PlayerSpawnPoint newSpawnPoint = new PlayerSpawnPoint
                {
                    GridPosition = new Vector2Int(gridX, gridY),
                    LocalOffset = new Vector2(0.5f, 0.5f),
                    SpawnRotation = 0f
                };
                
                _template.SetSpawnPoint(_selectedSpawnPointIndex, newSpawnPoint);

                EditorUtility.SetDirty(_template);
                serializedObject.Update();
                Repaint();
            }
        }

        /// <summary>
        /// 셀 페인팅
        /// </summary>
        private void PaintCell(Vector2 mousePos, Rect gridRect, int width, int height)
        {
            float cellSize = CellSize;
            float cellSpacing = CellSpacing;
            
            // 마우스 위치를 그리드 좌표로 변환
            float relativeX = mousePos.x - gridRect.x - GRID_PADDING;
            float relativeY = mousePos.y - gridRect.y - GRID_PADDING;

            int gridX = Mathf.FloorToInt(relativeX / (cellSize + cellSpacing));
            int gridY = height - 1 - Mathf.FloorToInt(relativeY / (cellSize + cellSpacing));

            if (gridX >= 0 && gridX < width && gridY >= 0 && gridY < height)
            {
                if (_isGroupIdMode)
                {
                    // 그룹 ID 모드: 그룹 ID 설정
                    Undo.RecordObject(_template, "Paint Group ID");
                    _template.SetChunkGroupId(gridX, gridY, _selectedGroupId);
                }
                else
                {
                    // 셀 타입 모드: 셀 타입 설정
                    Undo.RecordObject(_template, "Paint Cell");
                    _template.SetCell(gridX, gridY, _selectedCellType);
                }

                EditorUtility.SetDirty(_template);
                serializedObject.Update();
                Repaint();
            }
        }

        /// <summary>
        /// 유틸리티 버튼
        /// </summary>
        private void DrawUtilityButtons()
        {
            EditorGUILayout.LabelField("유틸리티", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("전체 Clear", GUILayout.Height(25)))
            {
                if (EditorUtility.DisplayDialog("전체 Clear",
                    "모든 셀을 Empty로 초기화하시겠습니까?", "Yes", "No"))
                {
                    ClearAllCells();
                }
            }

            if (GUILayout.Button("중앙 초기화", GUILayout.Height(25)))
            {
                SetCenterCell();
            }

            if (GUILayout.Button("그룹 ID 초기화", GUILayout.Height(25)))
            {
                if (EditorUtility.DisplayDialog("그룹 ID 초기화",
                    "모든 그룹 ID를 0으로 초기화하시겠습니까?", "Yes", "No"))
                {
                    ClearAllGroupIds();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 디버그 정보
        /// </summary>
        private void DrawDebugInfo()
        {
            EditorGUILayout.LabelField("디버그 정보", EditorStyles.boldLabel);

            EditorGUILayout.LabelField($"크기: {_template.Width} x {_template.Height}");
            EditorGUILayout.LabelField($"청크 수: {_template.GetFilledCellCount()}");

            if (GUILayout.Button("디버그 출력 (Console)", GUILayout.Height(25)))
            {
                Debug.Log(_template.ToDebugString());
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 셀 타입에 따른 색상 반환
        /// </summary>
        private Color GetCellColor(GridCell cellType)
        {
            return cellType switch
            {
                GridCell.Empty => new Color(0.3f, 0.3f, 0.3f),      // 어두운 회색
                GridCell.Central => new Color(1f, 0.8f, 0f),        // 노란색
                GridCell.Normal => new Color(0.3f, 0.8f, 1f),       // 하늘색
                GridCell.Special => new Color(1f, 0.3f, 0.3f),     // 빨간색
                _ => Color.magenta
            };
        }

        /// <summary>
        /// 그룹 ID에 따른 색상 반환
        /// </summary>
        private Color GetGroupIdColor(int groupId)
        {
            // 그룹 ID마다 다른 색상 (1~9)
            Color[] groupColors = new Color[]
            {
                new Color(0.3f, 0.3f, 0.3f),      // 0: 회색 (그룹 없음)
                new Color(1f, 0.5f, 0.5f),        // 1: 연한 빨강
                new Color(0.5f, 1f, 0.5f),        // 2: 연한 초록
                new Color(0.5f, 0.5f, 1f),        // 3: 연한 파랑
                new Color(1f, 1f, 0.5f),          // 4: 연한 노랑
                new Color(1f, 0.5f, 1f),          // 5: 연한 보라
                new Color(0.5f, 1f, 1f),          // 6: 연한 청록
                new Color(1f, 0.7f, 0.5f),        // 7: 연한 주황
                new Color(0.7f, 0.5f, 1f),        // 8: 연한 남색
                new Color(0.5f, 1f, 0.7f)         // 9: 연한 민트
            };

            if (groupId >= 0 && groupId < groupColors.Length)
            {
                return groupColors[groupId];
            }
            return Color.magenta;
        }

        /// <summary>
        /// 모든 셀을 Empty로 초기화
        /// </summary>
        private void ClearAllCells()
        {
            Undo.RecordObject(_template, "Clear All Cells");
            for (int y = 0; y < _template.Height; y++)
            {
                for (int x = 0; x < _template.Width; x++)
                {
                    _template.SetCell(x, y, GridCell.Empty);
                }
            }
            EditorUtility.SetDirty(_template);
            serializedObject.Update();
            AssetDatabase.SaveAssets();
            Repaint();
        }

        /// <summary>
        /// 중앙에 Central 청크 배치
        /// </summary>
        private void SetCenterCell()
        {
            Undo.RecordObject(_template, "Set Center Cell");
            Vector2Int center = _template.GetCenterPosition();
            _template.SetCell(center.x, center.y, GridCell.Central);
            EditorUtility.SetDirty(_template);
            serializedObject.Update();
            AssetDatabase.SaveAssets();
            Repaint();
        }

        /// <summary>
        /// 모든 그룹 ID를 0으로 초기화
        /// </summary>
        private void ClearAllGroupIds()
        {
            Undo.RecordObject(_template, "Clear All Group IDs");
            for (int y = 0; y < _template.Height; y++)
            {
                for (int x = 0; x < _template.Width; x++)
                {
                    _template.SetChunkGroupId(x, y, 0);
                }
            }
            EditorUtility.SetDirty(_template);
            serializedObject.Update();
            AssetDatabase.SaveAssets();
            Repaint();
        }

        #endregion
    }
}
