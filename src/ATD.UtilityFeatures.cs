// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/modification; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Debug/diagnostic helpers (not part of the published feature set)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mafi;
using Mafi.Core.Terrain;
using UnityEngine;
using AutoTerrainDesignations.Access;
using AutoTerrainDesignations.Access.V2;

namespace AutoTerrainDesignations;

partial class AutoDepthDesignation
{
    // Whether the cursor tile-position debug overlay is visible.
    internal static bool ShowCursorOverlay;
    // Whether the access search shows recently explored nodes.
    internal static bool ShowExperimentalAccessSearchOverlay;
    // Whether the access search shows its persistent P trace.
    internal static bool ShowExperimentalAccessPotentialOverlay;
    // Whether the latest V2 handoff candidates show their captured Mega-ground
    // connectivity. This is a session-only diagnostic overlay.
    internal static bool ShowV2PathabilityOverlay;
    // Whether the latest analyzed access clusters show their identity/state.
    // The flag is a global setting; the diagnostic records themselves remain
    // transient and are rebuilt by access analysis.
    internal static bool ShowAccessClusterOverlay;

    private const int MAX_ACCESS_SEARCH_OVERLAY_POINTS = 3000;
    private const float ACCESS_SEARCH_OVERLAY_LIFETIME_SECONDS = 3f;
    private const float ACCESS_SEARCH_OVERLAY_HEIGHT_RANGE = 5f;
    private static readonly Queue<AccessSearchOverlayPoint>
        s_accessSearchOverlayPoints =
            new Queue<AccessSearchOverlayPoint>(
                MAX_ACCESS_SEARCH_OVERLAY_POINTS);
    private static readonly List<AccessPotentialOverlayPoint>
        s_accessPotentialOverlayPoints =
            new List<AccessPotentialOverlayPoint>();
    private static Texture2D? s_accessSearchOverlayCircleTexture;
    private static readonly List<V2PathabilityOverlayPoint> s_v2PathabilityOverlayPoints =
        new List<V2PathabilityOverlayPoint>();
    private static IReadOnlyList<AccessClusterOverlayRecord>
        s_accessClusterOverlayRecords =
            new List<AccessClusterOverlayRecord>();
    private static readonly List<OreSpikeReviewMarker>
        s_oreSpikeReviewMarkers = new List<OreSpikeReviewMarker>();

    private readonly struct OreSpikeReviewMarker
    {
        public Tile2i Position { get; }
        public string Kind { get; }
        public string Label { get; }
        public float OreBottom { get; }
        public float Bedrock { get; }
        public float Cutoff { get; }

        public OreSpikeReviewMarker(Tile2i position, string kind, string label,
            float oreBottom, float bedrock, float cutoff)
        {
            Position = position;
            Kind = kind;
            Label = label;
            OreBottom = oreBottom;
            Bedrock = bedrock;
            Cutoff = cutoff;
        }
    }

    private readonly struct V2PathabilityOverlayPoint
    {
        public Tile2i Position { get; }
        public int Height2 { get; }
        public bool IsTowerReachable { get; }
        public bool IsSelectedRoute { get; }

        public V2PathabilityOverlayPoint(
            Tile2i position, int height2, bool isTowerReachable, bool isSelectedRoute)
        {
            Position = position;
            Height2 = height2;
            IsTowerReachable = isTowerReachable;
            IsSelectedRoute = isSelectedRoute;
        }
    }

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

    private sealed class AccessPotentialOverlayPoint
    {
        public Tile2i Center { get; }
        public float? GeneratedCost { get; set; }
        public float? FixedXCost { get; set; }
        public float? FixedYCost { get; set; }
        public AccessPotentialOverlayPoint(Tile2i center)
        {
            Center = center;
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
    private static GUIStyle? s_clusterOverlayStyle;
    private static GUIStyle? s_potentialOverlayStyle;
    private static GUIStyle? s_oreSpikeReviewOverlayStyle;

    internal static void DrawCursorOverlay(bool tickerActive, int worldGeneration)
    {
        if (!tickerActive || !IsWorldGenerationActive(worldGeneration)) return;

        DrawExperimentalAccessSearchOverlay();
        DrawExperimentalAccessPotentialOverlay();
        DrawV2PathabilityOverlay();
        DrawAccessClusterOverlay();
        DrawOreSpikeReviewOverlay();
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
        s_accessPotentialOverlayPoints.Clear();
    }

    internal static void RecordExperimentalAccessPotential(
        IReadOnlyList<AccessV2PotentialSample> samples)
    {
        if (!ShowExperimentalAccessPotentialOverlay || samples.Count == 0)
            return;
        var byCenter = new Dictionary<Tile2i, AccessPotentialOverlayPoint>();
        for (int index = 0; index < samples.Count; index++)
        {
            AccessV2PotentialSample sample = samples[index];
            if (!byCenter.TryGetValue(
                    sample.Center, out AccessPotentialOverlayPoint point))
            {
                if (s_accessPotentialOverlayPoints.Count
                    >= MAX_ACCESS_SEARCH_OVERLAY_POINTS)
                    continue;
                point = new AccessPotentialOverlayPoint(sample.Center);
                byCenter.Add(sample.Center, point);
                s_accessPotentialOverlayPoints.Add(point);
            }
            if (sample.IsGenerated)
                point.GeneratedCost = sample.Cost;
            else if (sample.Axis == AccessV2TravelAxis.X)
                point.FixedXCost = sample.Cost;
            else
                point.FixedYCost = sample.Cost;
        }
    }

    internal static void RecordExperimentalAccessSearchNode(
        Tile2i position, int height2, bool isGround, int? groundHeight2)
    {
        if (!ShowExperimentalAccessSearchOverlay) return;
        if (s_accessSearchOverlayPoints.Count >= MAX_ACCESS_SEARCH_OVERLAY_POINTS)
            s_accessSearchOverlayPoints.Dequeue();
        s_accessSearchOverlayPoints.Enqueue(new AccessSearchOverlayPoint(
            position, height2, isGround, groundHeight2, Time.realtimeSinceStartup));
    }

    internal static void ClearExperimentalAccessSearchOverlay()
    {
        s_accessSearchOverlayPoints.Clear();
    }

    internal static void ClearExperimentalAccessPotentialOverlay()
    {
        s_accessPotentialOverlayPoints.Clear();
    }

    internal static void RecordV2PathabilityOverlay(
        AccessSearchSnapshot snapshot, AccessSearchResult result)
    {
        if (!ShowV2PathabilityOverlay) return;
        s_v2PathabilityOverlayPoints.Clear();
        if (snapshot.V2GroundGraph == null) return;

        var selected = new HashSet<Tile2i>();
        if (result.V2Route != null)
            foreach (Access.V2.AccessV2RouteStep step in result.V2Route.RouteSteps)
                if (step.IsGround)
                    selected.Add(step.GroundCenter!.Value);

        var entries = new HashSet<Tile2i>();
        foreach (V2HandoffTrace trace in result.Diagnostics.V2HandoffTraces)
            foreach (Tile2i entry in trace.Entries)
                entries.Add(entry);
        foreach (Tile2i entry in entries)
        {
            int height2 = snapshot.TryGetGroundHeight2(entry, out int captured)
                ? captured : 0;
            s_v2PathabilityOverlayPoints.Add(new V2PathabilityOverlayPoint(
                entry, height2,
                snapshot.V2GroundGraph.TryGetGoalDistance(entry, out _),
                selected.Contains(entry)));
        }
    }

    internal static void ClearV2PathabilityOverlay()
    {
        s_v2PathabilityOverlayPoints.Clear();
    }

    internal static void RecordAccessClusterOverlay(
        IReadOnlyList<AccessOriginCluster> clusters,
        IReadOnlyDictionary<AccessOriginCluster, AccessClusterState> states)
    {
        s_accessClusterOverlayRecords =
            AccessReachability.BuildOverlayRecords(clusters, states);
    }

    internal static void ClearAccessClusterOverlay()
    {
        s_accessClusterOverlayRecords =
            new List<AccessClusterOverlayRecord>();
    }

    internal static void ClearDiagnosticOverlays()
    {
        ClearExperimentalAccessSearchOverlay();
        ClearExperimentalAccessPotentialOverlay();
        ClearV2PathabilityOverlay();
        ClearAccessClusterOverlay();
        ClearOreSpikeReviewMarkers();
    }

    internal static string LoadOreSpikeReviewMarkers()
    {
        if (string.IsNullOrWhiteSpace(s_modRootDirectoryPath))
            return "[ATD] Mod root is unavailable; marker file cannot be loaded.";
        string path = Path.Combine(s_modRootDirectoryPath!, ".scratch",
            "ore-spike-review-markers.csv");
        if (!File.Exists(path))
            return "[ATD] Ore-spike marker file not found: " + path;

        var parsed = new List<OreSpikeReviewMarker>();
        try
        {
            string[] lines = File.ReadAllLines(path);
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)
                    || line.StartsWith("x,", StringComparison.OrdinalIgnoreCase))
                    continue;
                string[] values = line.Split(',');
                if (values.Length < 7
                    || !int.TryParse(values[0], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int x)
                    || !int.TryParse(values[1], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int y)
                    || !float.TryParse(values[4], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float oreBottom)
                    || !float.TryParse(values[5], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float bedrock)
                    || !float.TryParse(values[6], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float cutoff))
                    return $"[ATD] Invalid ore-spike marker row {index + 1}.";
                string kind = values[2].Trim().ToLowerInvariant();
                if (kind != "review" && kind != "confirmed"
                    && kind != "mopup")
                    return $"[ATD] Invalid ore-spike marker kind on row {index + 1}.";
                parsed.Add(new OreSpikeReviewMarker(new Tile2i(x, y), kind,
                    values[3].Trim(), oreBottom, bedrock, cutoff));
                if (parsed.Count > 500)
                    return "[ATD] Ore-spike marker file exceeds 500 markers.";
            }
        }
        catch (Exception ex)
        {
            return "[ATD] Failed to load ore-spike markers: " + ex.Message;
        }

        s_oreSpikeReviewMarkers.Clear();
        s_oreSpikeReviewMarkers.AddRange(parsed);
        return $"[ATD] Loaded {parsed.Count} ore-spike review marker(s). "
            + "Yellow = review, green = confirmed intact spike, "
            + "cyan = confirmed mop-up-only effect.";
    }

    internal static void ClearOreSpikeReviewMarkers()
        => s_oreSpikeReviewMarkers.Clear();

    private static void DrawOreSpikeReviewOverlay()
    {
        if (s_oreSpikeReviewMarkers.Count == 0)
            return;
        Camera? camera = Camera.main;
        TerrainManager? terrain = s_desigManager?.TerrainManager;
        if (camera == null || terrain == null)
            return;

        s_oreSpikeReviewOverlayStyle ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 12,
            normal = { textColor = Color.white },
        };
        int reviewCount = s_oreSpikeReviewMarkers.Count(item => item.Kind == "review");
        int confirmedCount = s_oreSpikeReviewMarkers.Count - reviewCount;
        GUI.Box(new Rect(10f, 44f, 310f, 42f),
            $"Ore-spike review: {reviewCount} to inspect, {confirmedCount} confirmed\n"
                + "yellow = inspect   green = intact   cyan = mop-up only",
            s_oreSpikeReviewOverlayStyle);

        OreSpikeReviewMarker? hovered = null;
        Vector2 mouse = UnityEngine.Event.current.mousePosition;
        foreach (OreSpikeReviewMarker marker in s_oreSpikeReviewMarkers)
        {
            if (!terrain.IsValidCoord(marker.Position))
                continue;
            float height = terrain.GetHeight(marker.Position).Value.ToFloat();
            Vector3 screen = camera.WorldToScreenPoint(new Vector3(
                (marker.Position.X + 0.5f) * 2f, height + 1.2f,
                (marker.Position.Y + 0.5f) * 2f));
            if (screen.z <= 0f)
                continue;
            Vector2 gui = new Vector2(screen.x, Screen.height - screen.y);
            Color previous = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(gui.x - 9f, gui.y - 9f, 18f, 18f),
                GetAccessSearchOverlayCircleTexture());
            GUI.color = GetOreSpikeReviewColor(marker.Kind);
            GUI.DrawTexture(new Rect(gui.x - 6f, gui.y - 6f, 12f, 12f),
                GetAccessSearchOverlayCircleTexture());
            GUI.color = previous;
            if ((mouse - gui).sqrMagnitude <= 196f)
                hovered = marker;
        }

        if (hovered.HasValue)
        {
            OreSpikeReviewMarker marker = hovered.Value;
            GUI.Box(new Rect(10f, 90f, 355f, 48f),
                $"{marker.Label} ({marker.Position.X},{marker.Position.Y}) "
                    + $"[{marker.Kind}]\nore bottom={marker.OreBottom:0.##}  "
                    + $"bedrock={marker.Bedrock:0.##}  cutoff={marker.Cutoff:0.##}",
                s_oreSpikeReviewOverlayStyle);
        }
    }

    private static Color GetOreSpikeReviewColor(string kind)
        => kind == "confirmed" ? Color.green
            : kind == "mopup" ? Color.cyan
            : new Color(1f, 0.72f, 0.05f, 1f);

    private static void DrawExperimentalAccessSearchOverlay()
    {
        if (!ShowExperimentalAccessSearchOverlay)
            return;

        Camera? camera = Camera.main;
        if (camera == null) return;
        float now = Time.realtimeSinceStartup;
        while (s_accessSearchOverlayPoints.Count > 0
            && now - s_accessSearchOverlayPoints.Peek().RecordedAt
                >= ACCESS_SEARCH_OVERLAY_LIFETIME_SECONDS)
            s_accessSearchOverlayPoints.Dequeue();

        foreach (AccessSearchOverlayPoint point
            in s_accessSearchOverlayPoints)
        {
            float age = now - point.RecordedAt;
            Vector3 screen = camera.WorldToScreenPoint(new Vector3(
                (point.Position.X + 2f) * 2f, point.Height2 + 0.3f,
                (point.Position.Y + 2f) * 2f));
            if (screen.z <= 0f) continue;
            float alpha = 1f
                - age / ACCESS_SEARCH_OVERLAY_LIFETIME_SECONDS;
            Color previousColor = GUI.color;
            GUI.color = GetAccessSearchOverlayColor(point, alpha);
            Texture2D texture = point.IsGround
                ? GetAccessSearchOverlayCircleTexture()
                : Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(screen.x - 3f, Screen.height - screen.y - 3f, 6f, 6f), texture);
            GUI.color = previousColor;
        }
    }

    private static void DrawExperimentalAccessPotentialOverlay()
    {
        if (!ShowExperimentalAccessPotentialOverlay
            || s_accessPotentialOverlayPoints.Count == 0)
            return;
        Camera? camera = Camera.main;
        TerrainManager? terrain = s_desigManager?.TerrainManager;
        if (camera == null || terrain == null)
            return;
        s_potentialOverlayStyle ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 10,
            normal = { textColor = Color.white },
        };
        var occupiedLabels = new HashSet<long>();
        for (int index = s_accessPotentialOverlayPoints.Count - 1;
            index >= 0; index--)
        {
            AccessPotentialOverlayPoint point =
                s_accessPotentialOverlayPoints[index];
            if (!terrain.IsValidCoord(point.Center))
                continue;
            float height = terrain.GetHeight(point.Center).Value.ToFloat();
            Vector3 screen = camera.WorldToScreenPoint(new Vector3(
                point.Center.X * 2f, height + 0.8f,
                point.Center.Y * 2f));
            if (screen.z <= 0f)
                continue;
            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 0.75f, 0.1f, 1f);
            GUI.DrawTexture(new Rect(
                screen.x - 2f, Screen.height - screen.y - 2f,
                4f, 4f), Texture2D.whiteTexture);

            int bucketX = Mathf.FloorToInt(screen.x / 42f);
            int bucketY = Mathf.FloorToInt(screen.y / 18f);
            long bucket = ((long)bucketX << 32) ^ (uint)bucketY;
            if (occupiedLabels.Add(bucket))
            {
                string label = FormatPotentialOverlayLabel(point);
                Vector2 size = s_potentialOverlayStyle.CalcSize(
                    new GUIContent(label));
                GUI.Box(new Rect(
                        screen.x + 4f,
                        Screen.height - screen.y - size.y / 2f,
                        size.x + 6f, size.y),
                    label, s_potentialOverlayStyle);
            }
            GUI.color = previousColor;
        }
    }

    private static string FormatPotentialOverlayLabel(
        AccessPotentialOverlayPoint point)
    {
        var values = new List<string>(3);
        if (point.GeneratedCost.HasValue)
            values.Add($"P={point.GeneratedCost.Value:0.#}");
        if (point.FixedXCost.HasValue)
            values.Add($"FX={point.FixedXCost.Value:0.#}");
        if (point.FixedYCost.HasValue)
            values.Add($"FY={point.FixedYCost.Value:0.#}");
        return string.Join(" ", values);
    }

    private static void DrawV2PathabilityOverlay()
    {
        if (!ShowV2PathabilityOverlay || s_v2PathabilityOverlayPoints.Count == 0)
            return;

        Camera? camera = Camera.main;
        if (camera == null) return;
        foreach (V2PathabilityOverlayPoint point in s_v2PathabilityOverlayPoints)
        {
            Vector3 screen = camera.WorldToScreenPoint(new Vector3(
                (point.Position.X + 0.5f) * 2f, point.Height2 + 0.5f,
                (point.Position.Y + 0.5f) * 2f));
            if (screen.z <= 0f) continue;
            Color previousColor = GUI.color;
            GUI.color = point.IsSelectedRoute ? Color.cyan
                : point.IsTowerReachable ? Color.green : Color.red;
            GUI.DrawTexture(new Rect(screen.x - 6f, Screen.height - screen.y - 6f, 12f, 12f),
                GetAccessSearchOverlayCircleTexture());
            GUI.color = previousColor;
        }
    }

    private static void DrawAccessClusterOverlay()
    {
        if (!ShowAccessClusterOverlay
            || s_accessClusterOverlayRecords.Count == 0)
            return;
        Camera? camera = Camera.main;
        TerrainManager? terrain = s_desigManager?.TerrainManager;
        if (camera == null || terrain == null)
            return;

        s_clusterOverlayStyle ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 11,
            normal = { textColor = Color.white },
        };
        foreach (AccessClusterOverlayRecord record
            in s_accessClusterOverlayRecords)
        {
            var centerTile = new Tile2i(
                Mathf.RoundToInt(record.CenterX),
                Mathf.RoundToInt(record.CenterY));
            if (!terrain.IsValidCoord(centerTile))
                continue;
            float height = terrain.GetHeight(centerTile).Value.ToFloat();
            Vector3 screen = camera.WorldToScreenPoint(new Vector3(
                record.CenterX * 2f,
                height + 1.2f,
                record.CenterY * 2f));
            if (screen.z <= 0f)
                continue;

            Color previousColor = GUI.color;
            GUI.color = GetClusterOverlayColor(record.State);
            GUI.DrawTexture(new Rect(
                screen.x - 5f, Screen.height - screen.y - 5f,
                10f, 10f), GetAccessSearchOverlayCircleTexture());
            GUI.color = previousColor;

            string roots = string.Join("|", record.CenterRoots
                .Select(root => $"{root.X},{root.Y}"));
            GUI.Box(new Rect(
                    screen.x + 7f, Screen.height - screen.y - 14f,
                    245f, 30f),
                $"C{record.ClusterId} {record.State}  n={record.OriginCount}\n"
                    + $"center=({record.CenterX:0.#},{record.CenterY:0.#}) roots={roots}",
                s_clusterOverlayStyle);

            foreach (Tile2i root in record.CenterRoots)
            {
                Tile2i rootCenter = root + new RelTile2i(2, 2);
                if (!terrain.IsValidCoord(rootCenter))
                    continue;
                float rootHeight =
                    terrain.GetHeight(rootCenter).Value.ToFloat();
                Vector3 rootScreen = camera.WorldToScreenPoint(new Vector3(
                    rootCenter.X * 2f, rootHeight + 0.8f,
                    rootCenter.Y * 2f));
                if (rootScreen.z <= 0f)
                    continue;
                GUI.color = GetClusterOverlayColor(record.State);
                GUI.DrawTexture(new Rect(
                    rootScreen.x - 3f,
                    Screen.height - rootScreen.y - 3f,
                    6f, 6f), Texture2D.whiteTexture);
                GUI.color = previousColor;
            }
        }
    }

    private static Color GetClusterOverlayColor(AccessClusterState state)
    {
        switch (state)
        {
            case AccessClusterState.AccessibleDirect:
            case AccessClusterState.AccessibleViaProvider:
            case AccessClusterState.AccessProvided:
                return Color.green;
            case AccessClusterState.WaitingForProviderCompletion:
                return new Color(1f, 0.7f, 0.1f);
            case AccessClusterState.NeedsAccessway:
                return Color.red;
            case AccessClusterState.Blocked:
                return Color.magenta;
            default:
                return Color.white;
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
