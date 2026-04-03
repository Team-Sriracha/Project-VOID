using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// UI 요소에 드롭 섀도우 효과를 적용하는 Mesh Effect입니다.
/// </summary>
[AddComponentMenu("UI/Effects/UIDropShadow")]
public class UIDropShadow : BaseMeshEffect
{
    #region Serialized Fields

    [SerializeField] private Color _color = new Color(0f, 0f, 0f, 0.5f);
    [SerializeField, Range(-180f, 180f)] private float _angle = -45f;
    [SerializeField] private float _distance = 5f;
    [SerializeField, Range(0f, 50f)] private float _spread = 0f;
    [SerializeField, Range(0, 10)] private float _size = 0f;

    #endregion

    #region Core Functionality

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;
        
        List<UIVertex> originalVerts = new List<UIVertex>();
        vh.GetUIVertexStream(originalVerts);

        ApplyShadow(originalVerts, vh);
    }

    #endregion

    #region Helper Methods

    private void ApplyShadow(List<UIVertex> verts, VertexHelper vh)
    {
        // 1. 그림자 오프셋 방향 계산
        float rad = _angle * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        Vector2 move = dir * _distance;

        // 2. Prepare for shadow generation
        // Clear vh to rebuild: Shadow first, then Original
        vh.Clear();

        // 2. 블러 레이어 수 계산
        int steps = Mathf.CeilToInt(_size);
        if (steps == 0) steps = 1;
        if (steps > 20) steps = 20;

        // Shadow processing
        // We want the shadow to fade out at the edges (Blur).
        // Central layers are opaque, outer layers transparent?
        // Or accumulated transparency?
        
        // Simple approach: Stacking semi-transparent layers? No, that causes overdraw darkening.
        // Better approach:
        // Render 1 shadow layer?
        // If "Size" (Blur) is requested, we spread copies around?
        // Let's start with a simpler interpretation:
        // "Size" -> affects the expansion/scale of a simplistic blur.
        
        // Let's implement the loop such that:
        // We draw 'steps' copies.
        // Each copy is expanded slightly more and has lower alpha?
        // That creates a gradient look.
        
        // Start Alpha = m_Color.a
        // We divide Alpha by steps?
        
        float startAlpha = _color.a;
        float stepAlpha = (startAlpha) / steps; // Simple additive approximation, though not physically correct blending
        // Correct blending of alpha is Hard.
        
        // Let's assume user wants a stacked look. 
        // Actually, "Shadow" component just appends.
        // Order: Shadow(s) -> Original.
        
        int vertCount = verts.Count;
        
        // Calculate center for expansion
        // For text or multiple quads, we might want to expand per-quad or global?
        // Global center is safer for "Drop Shadow".
        Vector3 center = Vector3.zero;
        if (vertCount > 0)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < vertCount; i++)
            {
                Vector3 p = verts[i].position;
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0);
        }

        // Generate Shadow Layers
        // We iterate from "Core" (Hard) to "Edge" (Soft).
        // Or just one big loop.
        
        // If Size=0, steps=1.
        for (int s = 1; s <= steps; s++)
        {
            // Calculate expansion for this step
            // Spread is the base expansion.
            // Size adds blur radius.
            // Current Expansion = Spread + (Size * (s / steps))?
            
            float blurOffset = (_size > 0) ? ((float)s / steps) * _size : 0;
            float currentExpand = _spread + blurOffset;

            // Alpha for this layer
            // If we stack them, the alpha accumulates.
            // We want the total visual alpha to roughly match m_Color.a
            // A simple hack is to use a very low alpha for blur layers.
            // Or just draw ONE layer if steps=1.
            
            Color col = _color;
            if (steps > 1)
            {
                // Quadratic falloff or linear?
                float t = (float)s / steps;
                 // Outer layers fainter?
                col.a = (startAlpha / steps); 
            }

            // Create verts for this layer
            for (int i = 0; i < vertCount; i++)
            {
                UIVertex vt = verts[i];
                Vector3 origPos = vt.position;
                
                // 1. Move
                Vector3 newPos = origPos + (Vector3)move;
                
                // 2. Expand (Dilation) relative to center
                Vector3 fromCenter = newPos - center;
                // Avoid zero vector
                if (fromCenter.sqrMagnitude > 0.001f)
                {
                    // Normalize and add expansion
                    // This works well for convex shapes (Rect).
                    // For concave, it might look weird.
                    // Doing simple scaling is better?
                    // newPos = center + fromCenter * (1 + expansion/radius)
                    // But expansion is in pixels.
                    
                    // Direction
                    Vector3 expandDir = fromCenter.normalized;
                    newPos += expandDir * currentExpand;
                }
                
                vt.position = newPos;
                vt.color = col;
                vh.AddVert(vt);
            }
            
            // Add Triangles for this layer
            // Assuming quads (4 verts per quad)
            // Or if standard mesh stream, triangles are defined by index.
            // ModifyMesh(VertexHelper) handles the stream. 
            // We pushed `vertCount` vertices.
        }

        // Add Indices for Shadows
        // vh.AddVert adds them sequentially.
        // We need to add triangles connecting them.
        // Wait, VertexHelper stream usually comes as a list of Triangles?
        // GetUIVertexStream gets a list of triangles (vertices repeated).
        // So for every 3 verts, it's a triangle.
        // So we just iterate verts and they form tris naturally if we preserve order.
        
        // Shadow Triangles
        int totalShadowVerts = vertCount * steps;
        for (int i = 0; i < totalShadowVerts; i += 3)
        {
            vh.AddTriangle(i, i + 1, i + 2);
        }

        // Add Original
        int startObj = totalShadowVerts;
        for (int i = 0; i < vertCount; i++)
        {
            vh.AddVert(verts[i]);
        }
        for (int i = 0; i < vertCount; i += 3)
        {
            vh.AddTriangle(startObj + i, startObj + i + 1, startObj + i + 2);
        }
    }

    #endregion
}
