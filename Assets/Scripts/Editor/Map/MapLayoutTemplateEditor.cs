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

        private const float CELL_SIZE = 20f;
        private const float CELL_SPACING = 2f;
        private const float GRID_PADDING = 10f;

        #endregion

        #region Private Fields

        private MapLayoutTemplate _template;
        private EGridCell _selectedCellType = EGridCell.Normal;
        private int _selectedGroupId = 0;
        private bool _isGroupIdMode = false;
        private bool _isPainting = false;
        private Vector2 _scrollPosition;

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
            if (_isGroupIdMode)
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
        /// 편집 모드 토글 (셀 타입 / 그룹 ID)
        /// </summary>
        private void DrawEditModeToggle()
        {
            EditorGUILayout.LabelField("편집 모드", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Toggle(!_isGroupIdMode, "셀 타입 편집", "Button", GUILayout.Height(30)))
            {
                _isGroupIdMode = false;
            }

            if (GUILayout.Toggle(_isGroupIdMode, "그룹 ID 편집", "Button", GUILayout.Height(30)))
            {
                _isGroupIdMode = true;
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
            if (GUILayout.Toggle(_selectedCellType == EGridCell.Empty,
                new GUIContent("Empty", "빈 공간"),
                "Button", GUILayout.Height(30)))
            {
                _selectedCellType = EGridCell.Empty;
            }

            // Central
            if (GUILayout.Toggle(_selectedCellType == EGridCell.Central,
                new GUIContent("Central", "중앙 청크"),
                "Button", GUILayout.Height(30)))
            {
                _selectedCellType = EGridCell.Central;
            }

            // Normal
            if (GUILayout.Toggle(_selectedCellType == EGridCell.Normal,
                new GUIContent("Normal", "일반 청크"),
                "Button", GUILayout.Height(30)))
            {
                _selectedCellType = EGridCell.Normal;
            }

            // Special
            if (GUILayout.Toggle(_selectedCellType == EGridCell.Special,
                new GUIContent("Special", "스페셜 청크"),
                "Button", GUILayout.Height(30)))
            {
                _selectedCellType = EGridCell.Special;
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
        /// 그리드 에디터 (클릭/드래그로 편집)
        /// </summary>
        private void DrawGridEditor()
        {
            EditorGUILayout.LabelField("그리드 에디터", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("클릭 또는 드래그하여 셀을 편집하세요", MessageType.Info);

            int width = _template.Width;
            int height = _template.Height;

            float totalWidth = width * (CELL_SIZE + CELL_SPACING) + GRID_PADDING * 2;
            float totalHeight = height * (CELL_SIZE + CELL_SPACING) + GRID_PADDING * 2;

            // 스크롤 뷰 시작
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition,
                GUILayout.MaxHeight(Mathf.Min(totalHeight, 600)));

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

            // 마우스 입력 처리
            HandleMouseInput(gridRect, width, height);

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 개별 셀 그리기
        /// </summary>
        private void DrawCell(Rect gridRect, int x, int y)
        {
            float xPos = gridRect.x + GRID_PADDING + x * (CELL_SIZE + CELL_SPACING);
            float yPos = gridRect.y + GRID_PADDING + (_template.Height - 1 - y) * (CELL_SIZE + CELL_SPACING);

            Rect cellRect = new Rect(xPos, yPos, CELL_SIZE, CELL_SIZE);

            EGridCell cellType = _template.GetCell(x, y);
            int groupId = _template.GetChunkGroupId(x, y);
            Color cellColor = GetCellColor(cellType);

            // 그룹 ID가 있으면 색상 조정
            if (_isGroupIdMode && groupId > 0)
            {
                cellColor = GetGroupIdColor(groupId);
            }

            // 셀 그리기
            EditorGUI.DrawRect(cellRect, cellColor);

            // 테두리 (그룹 ID 모드일 때 강조)
            if (_isGroupIdMode && groupId > 0)
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
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.fontSize = 8;
            labelStyle.normal.textColor = Color.white;

            string cellText;
            if (_isGroupIdMode && groupId > 0)
            {
                // 그룹 ID 모드: 그룹 번호 표시
                cellText = groupId.ToString();
            }
            else
            {
                // 셀 타입 모드: 셀 타입 표시
                cellText = cellType switch
                {
                    EGridCell.Empty => "",
                    EGridCell.Central => "C",
                    EGridCell.Normal => "N",
                    EGridCell.Special => "S",
                    _ => "?"
                };
            }

            GUI.Label(cellRect, cellText, labelStyle);
        }

        /// <summary>
        /// 마우스 입력 처리 (클릭/드래그)
        /// </summary>
        private void HandleMouseInput(Rect gridRect, int width, int height)
        {
            Event e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _isPainting = true;
                PaintCell(e.mousePosition, gridRect, width, height);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && _isPainting)
            {
                PaintCell(e.mousePosition, gridRect, width, height);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                _isPainting = false;
            }
        }

        /// <summary>
        /// 셀 페인팅
        /// </summary>
        private void PaintCell(Vector2 mousePos, Rect gridRect, int width, int height)
        {
            // 마우스 위치를 그리드 좌표로 변환
            float relativeX = mousePos.x - gridRect.x - GRID_PADDING;
            float relativeY = mousePos.y - gridRect.y - GRID_PADDING;

            int gridX = Mathf.FloorToInt(relativeX / (CELL_SIZE + CELL_SPACING));
            int gridY = height - 1 - Mathf.FloorToInt(relativeY / (CELL_SIZE + CELL_SPACING));

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
        private Color GetCellColor(EGridCell cellType)
        {
            return cellType switch
            {
                EGridCell.Empty => new Color(0.3f, 0.3f, 0.3f),      // 어두운 회색
                EGridCell.Central => new Color(1f, 0.8f, 0f),        // 노란색
                EGridCell.Normal => new Color(0.3f, 0.8f, 1f),       // 하늘색
                EGridCell.Special => new Color(1f, 0.3f, 0.3f),     // 빨간색
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
                    _template.SetCell(x, y, EGridCell.Empty);
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
            _template.SetCell(center.x, center.y, EGridCell.Central);
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
