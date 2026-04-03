using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 요소에 그라디언트 효과를 적용하는 Mesh Effect입니다.
/// </summary>
[AddComponentMenu("UI/Effects/UIGradient")]
public class UIGradient : BaseMeshEffect
{
    #region Serialized Fields

    [SerializeField] private Color _color1 = Color.white;
    [SerializeField] private Color _color2 = Color.black;
    [SerializeField, Range(-180f, 180f)] private float _angle = -90f;
    [SerializeField, Range(-1f, 1f)] private float _offset = 0f;

    #endregion

    #region Core Functionality

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive())
            return;

        int count = vh.currentVertCount;
        if (count == 0)
            return;

        UIVertex vertex = new UIVertex();

        // 1. Calculate bounds
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        for (int i = 0; i < count; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            Vector3 pos = vertex.position;
            if (pos.x < minX) minX = pos.x;
            if (pos.x > maxX) maxX = pos.x;
            if (pos.y < minY) minY = pos.y;
            if (pos.y > maxY) maxY = pos.y;
        }

        // 2. 방향 벡터 계산
        float rad = _angle * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

        // 3. Project corners onto direction to find the range (min/max dot product)
        float dotMin = float.MaxValue;
        float dotMax = float.MinValue;

        Vector2[] corners = new Vector2[]
        {
            new Vector2(minX, minY),
            new Vector2(maxX, minY),
            new Vector2(maxX, maxY),
            new Vector2(minX, maxY)
        };

        for (int i = 0; i < corners.Length; i++)
        {
            float d = Vector2.Dot(corners[i], dir);
            if (d < dotMin) dotMin = d;
            if (d > dotMax) dotMax = d;
        }

        float range = dotMax - dotMin;
        if (range <= 0) range = 0.0001f; // Prevent division by zero

        // 4. Apply Color to vertices
        for (int i = 0; i < count; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            
            // Project vertex position onto direction
            float dot = Vector2.Dot(new Vector2(vertex.position.x, vertex.position.y), dir);
            
            float t = (dot - dotMin) / range;
            t -= _offset;

            Color finalColor = Color.Lerp(_color1, _color2, Mathf.Clamp01(t));

            vertex.color = finalColor * vertex.color;
            vh.SetUIVertex(vertex, i);
        }
    }

    #endregion
}
