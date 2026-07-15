// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/modification; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Debug/diagnostic helpers (not part of the published feature set)
using System.Collections.Generic;
using Mafi;
using UnityEngine;

namespace AutoTerrainDesignations;

partial class AutoDepthDesignation
{
    // Whether the cursor tile-position debug overlay is visible.
    internal static bool ShowCursorOverlay;
    // Whether the experimental access search shows recently explored nodes.
    internal static bool ShowExperimentalAccessSearchOverlay;

    private const int MAX_ACCESS_SEARCH_OVERLAY_POINTS = 3000;
    private const float ACCESS_SEARCH_OVERLAY_LIFETIME_SECONDS = 3f;
    private const float ACCESS_SEARCH_OVERLAY_HEIGHT_RANGE = 5f;
    private static readonly List<AccessSearchOverlayPoint> s_accessSearchOverlayPoints =
        new List<AccessSearchOverlayPoint>();
    private static Texture2D? s_accessSearchOverlayCircleTexture;

    private readonly struct AccessSearchOverlayPoint
    {
        public Tile2i Position { get; }
        public int Height2 { get; }
        public bool IsGround { get; }
        public int? GroundHeight2 { get; }
        public float RecordedAt { get; }

        public AccessSearchOverlayPoint(
            Tile2i position, int height2, bool isGround, int? groundHeight2, float recordedAt)
        {
            Position = position;
            Height2 = height2;
            IsGround = isGround;
            GroundHeight2 = groundHeight2;
            RecordedAt = recordedAt;
        }
    }

    // Returns the terrain tile currently under the mouse cursor.
    internal static bool TryGetCursorTile(out Tile3f tile)
    {
        tile = default;
        if (s_terrainCursor == null) return false;
        return s_terrainCursor.TryComputeTerrainPosition(Input.mousePosition, out tile);
    }

    private static GUIStyle? s_tileOverlayStyle;

    internal static void DrawCursorOverlay(bool tickerActive, int worldGeneration)
    {
        if (!tickerActive || !IsWorldGenerationActive(worldGeneration)) return;

        DrawExperimentalAccessSearchOverlay();
        if (!ShowCursorOverlay || !TryGetCursorTile(out Tile3f pos)) return;

        s_tileOverlayStyle ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 13,
            normal = { textColor = Color.white },
        };

        Tile2i xy = pos.Xy.Tile2i;
        int z = pos.Z.ToIntRounded();
        GUI.Box(new Rect(10f, Screen.height - 36f, 190f, 26f),
            $"  ({xy.X}, {xy.Y}, {z})", s_tileOverlayStyle);
    }

    internal static void BeginExperimentalAccessSearchOverlay()
    {
        s_accessSearchOverlayPoints.Clear();
    }

    internal static void RecordExperimentalAccessSearchNode(
        Tile2i position, int height2, bool isGround, int? groundHeight2)
    {
        if (!ShowExperimentalAccessSearchOverlay) return;
        if (s_accessSearchOverlayPoints.Count >= MAX_ACCESS_SEARCH_OVERLAY_POINTS)
            s_accessSearchOverlayPoints.RemoveAt(0);
        s_accessSearchOverlayPoints.Add(new AccessSearchOverlayPoint(
            position, height2, isGround, groundHeight2, Time.realtimeSinceStartup));
    }

    internal static void ClearExperimentalAccessSearchOverlay()
    {
        s_accessSearchOverlayPoints.Clear();
    }

    private static void DrawExperimentalAccessSearchOverlay()
    {
        if (!ShowExperimentalAccessSearchOverlay || s_accessSearchOverlayPoints.Count == 0)
            return;

        Camera? camera = Camera.main;
        if (camera == null) return;
        float now = Time.realtimeSinceStartup;
        for (int index = s_accessSearchOverlayPoints.Count - 1; index >= 0; index--)
        {
            AccessSearchOverlayPoint point = s_accessSearchOverlayPoints[index];
            float age = now - point.RecordedAt;
            if (age >= ACCESS_SEARCH_OVERLAY_LIFETIME_SECONDS)
            {
                s_accessSearchOverlayPoints.RemoveAt(index);
                continue;
            }

            Vector3 screen = camera.WorldToScreenPoint(new Vector3(
                (point.Position.X + 2f) * 2f, point.Height2 + 0.3f,
                (point.Position.Y + 2f) * 2f));
            if (screen.z <= 0f) continue;
            float alpha = 1f - age / ACCESS_SEARCH_OVERLAY_LIFETIME_SECONDS;
            Color previousColor = GUI.color;
            GUI.color = GetAccessSearchOverlayColor(point, alpha);
            Texture2D texture = point.IsGround
                ? GetAccessSearchOverlayCircleTexture()
                : Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(screen.x - 3f, Screen.height - screen.y - 3f, 6f, 6f), texture);
            GUI.color = previousColor;
        }
    }

    private static Color GetAccessSearchOverlayColor(
        AccessSearchOverlayPoint point, float fadeAlpha)
    {
        float heightOffset = point.GroundHeight2.HasValue
            ? (point.Height2 - point.GroundHeight2.Value) / 2f
            : 0f;
        Color color = heightOffset < 0f
            ? Color.Lerp(Color.white, Color.red,
                Mathf.Clamp01(-heightOffset / ACCESS_SEARCH_OVERLAY_HEIGHT_RANGE))
            : Color.Lerp(Color.white, Color.blue,
                Mathf.Clamp01(heightOffset / ACCESS_SEARCH_OVERLAY_HEIGHT_RANGE));
        color.a = 0.2f + 0.8f * fadeAlpha;
        return color;
    }

    private static Texture2D GetAccessSearchOverlayCircleTexture()
    {
        if (s_accessSearchOverlayCircleTexture != null)
            return s_accessSearchOverlayCircleTexture;

        var texture = new Texture2D(6, 6, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < 6; y++)
        for (int x = 0; x < 6; x++)
        {
            float dx = x - 2.5f;
            float dy = y - 2.5f;
            texture.SetPixel(x, y, dx * dx + dy * dy <= 7.25f ? Color.white : Color.clear);
        }
        texture.Apply(false, true);
        s_accessSearchOverlayCircleTexture = texture;
        return texture;
    }
}
