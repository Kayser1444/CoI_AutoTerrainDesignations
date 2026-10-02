// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mafi;
using Mafi.Core;
using Mafi.Core.GameLoop;
using Mafi.Core.Products;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Unity;
using Mafi.Unity.Terrain.Designation;
using AutoTerrainDesignations.Access;
using UnityEngine;
using UnityEngine.Rendering;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private static MiningHazardOverlayRuntime? s_miningHazardOverlay;

        internal static void InitializeMiningHazardOverlay(
            IGameLoopEvents gameLoopEvents,
            TerrainDesignationsRenderer? terrainDesignationsRenderer)
        {
            StopMiningHazardOverlay();
            if (terrainDesignationsRenderer == null)
            {
                s_log.Warning("Mining hazard overlay is unavailable because the game terrain designations renderer could not be resolved.");
                return;
            }

            try
            {
                s_miningHazardOverlay = new MiningHazardOverlayRuntime(
                    gameLoopEvents,
                    () => terrainDesignationsRenderer.IsActive);
                s_miningHazardOverlay.SetEnabled(MiningHazardOverlayEnabled);
                s_miningHazardOverlay.SetFencePointPreviewEnabled(
                    MiningHazardFencePreviewEnabled);
                LogInfo("Mining hazard overlay renderer initialized with the Standard shader.");
            }
            catch (Exception exception)
            {
                s_miningHazardOverlay?.Dispose();
                s_miningHazardOverlay = null;
                s_log.Warning(
                    "Mining hazard overlay renderer could not be initialized: "
                    + exception.Message);
            }
        }

        private static void StopMiningHazardOverlay()
        {
            s_miningHazardOverlay?.Dispose();
            s_miningHazardOverlay = null;
        }

        private static bool TryPrepareMiningCollapseEnvelope(
            out IEnumerator projectionRoutine,
            out ProjectedDesignationBuildResult projectionResult,
            out int miningDesignationCount,
            out string failureReason)
        {
            projectionRoutine = new List<object>().GetEnumerator();
            projectionResult = new ProjectedDesignationBuildResult();
            miningDesignationCount = 0;
            failureReason = string.Empty;
            TerrainDesignationsManager? designationManager = s_desigManager;
            TerrainDesignationProto? miningProto = s_miningProto;
            if (designationManager == null || miningProto == null)
            {
                failureReason = "MiningDesignationsUnavailable";
                return false;
            }

            var miningDesignations = new Dictionary<Tile2i, TerrainDesignation>();
            foreach (TerrainDesignation designation in designationManager.Designations)
            {
                if (designation.Prototype != miningProto)
                    continue;
                miningDesignations[designation.Data.OriginTile] = designation;
            }
            miningDesignationCount = miningDesignations.Count;
            if (miningDesignations.Count == 0)
            {
                projectionResult.Disturbance =
                    new ProjectedDesignationDisturbance();
                return true;
            }

            TerrainManager terrain = designationManager.TerrainManager;
            int rayMargin = AutoTerrainDesignationsMod.AccessCandidateRayMaxDistance
                + AutoTerrainDesignationsMod.AccessRayEndBuffer;
            int minX = miningDesignations.Keys.Min(origin => origin.X);
            int minY = miningDesignations.Keys.Min(origin => origin.Y);
            int maxX = miningDesignations.Keys.Max(origin => origin.X + 4);
            int maxY = miningDesignations.Keys.Max(origin => origin.Y + 4);
            var relevantMin = new Tile2i(
                Math.Max(0, minX - rayMargin),
                Math.Max(0, minY - rayMargin));
            var relevantMax = new Tile2i(
                Math.Min(terrain.TerrainSize.X - 1, maxX + rayMargin),
                Math.Min(terrain.TerrainSize.Y - 1, maxY + rayMargin));

            float fallbackMiningSlope = float.MaxValue;
            if (s_protosDb != null)
            {
                foreach (TerrainMaterialProto material in s_protosDb.All<TerrainMaterialProto>())
                    fallbackMiningSlope = Math.Min(
                        fallbackMiningSlope,
                        GetCutMaterialSlope(material));
            }
            if (fallbackMiningSlope == float.MaxValue)
                fallbackMiningSlope = 0.5f;

            var sliceControl = new ExperimentalAccessSliceControl
            {
                // Temporary high-throughput in-game experiment. At 30 FPS,
                // this can consume most of a frame; compare responsiveness
                // against the much slower 2 ms slice before choosing a default.
                SliceBudgetMilliseconds = 30,
            };
            projectionRoutine = BuildProjectedDesignationDisturbedTilesSliced(
                miningDesignations,
                terrain,
                new Dictionary<Tile2i, float>(),
                new Dictionary<Tile2i, AccessTerrainColumn>(),
                relevantMin,
                relevantMax,
                Tile2i.Zero,
                new Tile2i(terrain.TerrainSize.X - 1, terrain.TerrainSize.Y - 1),
                dumpingMaterialSlope: fallbackMiningSlope,
                fallbackMiningSlope: fallbackMiningSlope,
                vehicleDisturbanceRadius: 0,
                projectionResult,
                sliceControl);
            return true;
        }

        private sealed class MiningHazardOverlayRuntime : IDisposable
        {
            private const int ChunkSize = 64;
            private const float SurfaceOffsetTiles = 0.025f;
            private const int ChunkGroupingYieldInterval = 2048;
            private const int FencePostSpacingTiles = 4;
            private const float FencePointRadiusTiles = 0.16f;
            private const float FencePointLiftTiles = 0.12f;
            private const int FencePointLatitudeSegments = 4;
            private const int FencePointLongitudeSegments = 8;

            private sealed class ChunkView
            {
                public readonly GameObject GameObject;
                public readonly MeshFilter MeshFilter;
                public readonly MeshRenderer MeshRenderer;
                public Mesh? Mesh;

                public ChunkView(GameObject gameObject)
                {
                    GameObject = gameObject;
                    MeshFilter = gameObject.AddComponent<MeshFilter>();
                    MeshRenderer = gameObject.AddComponent<MeshRenderer>();
                }
            }

            private readonly IGameLoopEvents m_gameLoopEvents;
            private readonly Func<bool> m_isDesignationOverlayActive;
            private readonly GameObject m_root;
            private readonly Material m_material;
            private readonly GameObject m_fencePreviewRoot;
            private readonly MeshFilter m_fencePreviewMeshFilter;
            private readonly MeshRenderer m_fencePreviewMeshRenderer;
            private readonly Material m_fencePreviewMaterial;
            private readonly Dictionary<Vector2Int, ChunkView> m_chunks =
                new Dictionary<Vector2Int, ChunkView>();
            private IEnumerator? m_rebuildRoutine;
            private Mesh? m_fencePreviewMesh;
            private bool m_enabled;
            private bool m_fencePointPreviewEnabled;
            private bool m_dirty = true;
            private bool m_visibilityKnown;
            private bool m_lastOverlayVisible;
            private bool m_lastFencePreviewVisible;
            private bool m_disposed;
            private long m_lastDesignationRevision = -1;
            private string m_dirtyReason = "initial";

            public MiningHazardOverlayRuntime(
                IGameLoopEvents gameLoopEvents,
                Func<bool> isDesignationOverlayActive)
            {
                m_gameLoopEvents = gameLoopEvents;
                m_isDesignationOverlayActive = isDesignationOverlayActive;
                m_root = new GameObject("ATD Mining Hazard Overlay");
                m_root.hideFlags = HideFlags.DontSave;
                m_root.SetActive(false);

                // The game keeps Unity's Standard shader available, but strips
                // built-in shaders such as Unlit/Transparent from some players.
                Shader? shader = Shader.Find("Standard");
                if (shader == null)
                    throw new InvalidOperationException(
                        "Unity shader 'Standard' is unavailable.");
                m_material = new Material(shader)
                {
                    name = "ATD Mining Hazard Overlay Material",
                    hideFlags = HideFlags.DontSave,
                    renderQueue = 3000,
                };
                m_material.SetFloat("_Mode", 3f);
                m_material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m_material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m_material.SetInt("_ZWrite", 0);
                m_material.DisableKeyword("_ALPHATEST_ON");
                m_material.EnableKeyword("_ALPHABLEND_ON");
                m_material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m_material.mainTexture = CreateStripeTexture();

                m_fencePreviewRoot = new GameObject(
                    "ATD Mining Hazard Fence-Point Preview")
                {
                    hideFlags = HideFlags.DontSave,
                };
                m_fencePreviewRoot.SetActive(false);
                m_fencePreviewMeshFilter =
                    m_fencePreviewRoot.AddComponent<MeshFilter>();
                m_fencePreviewMeshRenderer =
                    m_fencePreviewRoot.AddComponent<MeshRenderer>();
                m_fencePreviewMeshRenderer.shadowCastingMode =
                    ShadowCastingMode.Off;
                m_fencePreviewMeshRenderer.receiveShadows = false;
                m_fencePreviewMeshRenderer.allowOcclusionWhenDynamic = false;
                m_fencePreviewMaterial = new Material(shader)
                {
                    name = "ATD Mining Hazard Fence-Point Preview Material",
                    hideFlags = HideFlags.DontSave,
                    renderQueue = 3001,
                };
                m_fencePreviewMaterial.color =
                    new Color(1f, 0.24f, 0.015f, 1f);
                m_fencePreviewMaterial.EnableKeyword("_EMISSION");
                m_fencePreviewMaterial.SetColor(
                    "_EmissionColor", new Color(1f, 0.12f, 0.005f, 1f));
                m_fencePreviewMaterial.SetFloat("_Glossiness", 0.12f);
                m_fencePreviewMeshRenderer.sharedMaterial =
                    m_fencePreviewMaterial;

                m_gameLoopEvents.RenderUpdate.AddNonSaveable(this, OnRenderUpdate);
            }

            public void SetEnabled(bool enabled)
            {
                if (m_enabled == enabled)
                    return;
                m_enabled = enabled;
                MarkDirty("setting-change");
            }

            public void SetFencePointPreviewEnabled(bool enabled)
            {
                if (m_fencePointPreviewEnabled == enabled)
                    return;
                m_fencePointPreviewEnabled = enabled;
                MarkDirty("fence-preview-setting-change");
            }

            private void OnRenderUpdate(GameTime _)
            {
                if (m_disposed)
                    return;

                bool designationOverlayActive = m_isDesignationOverlayActive();
                bool overlayVisible = m_enabled && designationOverlayActive;
                bool fencePreviewVisible = m_fencePointPreviewEnabled;
                if (!m_visibilityKnown
                    || overlayVisible != m_lastOverlayVisible
                    || fencePreviewVisible != m_lastFencePreviewVisible)
                {
                    LogInfo(
                        "Mining hazard visualization visibility state: "
                        + $"overlaySettingEnabled={m_enabled} "
                        + $"designationOverlay={designationOverlayActive} "
                        + $"overlayVisible={overlayVisible} "
                        + $"fencePreviewEnabled={fencePreviewVisible}.");
                    m_visibilityKnown = true;
                    m_lastOverlayVisible = overlayVisible;
                    m_lastFencePreviewVisible = fencePreviewVisible;
                    if (overlayVisible || fencePreviewVisible)
                        MarkDirty("visibility-enabled");
                }

                if (m_root.activeSelf != overlayVisible)
                    m_root.SetActive(overlayVisible);
                if (m_fencePreviewRoot.activeSelf != fencePreviewVisible)
                    m_fencePreviewRoot.SetActive(fencePreviewVisible);

                if (!overlayVisible && !fencePreviewVisible)
                {
                    CancelRebuild();
                    return;
                }

                if (m_lastDesignationRevision != s_terrainDesignationRevision)
                {
                    m_lastDesignationRevision = s_terrainDesignationRevision;
                    MarkDirty("designation-change");
                }

                if (m_dirty && m_rebuildRoutine == null)
                    StartRebuild();
                AdvanceRebuild();
            }

            private void MarkDirty(string reason)
            {
                if (!m_dirty || m_dirtyReason.Length == 0)
                    m_dirtyReason = reason;
                m_dirty = true;
            }

            private void CancelRebuild()
            {
                IEnumerator? routine = m_rebuildRoutine;
                m_rebuildRoutine = null;
                (routine as IDisposable)?.Dispose();
            }

            private void StartRebuild()
            {
                m_dirty = false;
                string reason = m_dirtyReason;
                m_dirtyReason = string.Empty;
                Stopwatch setupTimer = Stopwatch.StartNew();
                if (!TryPrepareMiningCollapseEnvelope(
                    out IEnumerator projectionRoutine,
                    out ProjectedDesignationBuildResult projectionResult,
                    out int miningDesignationCount,
                    out string failureReason))
                {
                    HideChunks();
                    HideFencePointPreview();
                    s_log.Warning(
                        "Mining hazard overlay projection could not start; "
                        + $"trigger={reason}, miningDesignations="
                        + $"{miningDesignationCount}, setupMs="
                        + $"{setupTimer.ElapsedMilliseconds}, "
                        + $"reason={failureReason}.");
                    return;
                }

                m_rebuildRoutine = RebuildRoutine(
                    projectionRoutine,
                    projectionResult,
                    miningDesignationCount,
                    setupTimer.Elapsed.TotalMilliseconds,
                    reason);
            }

            private void AdvanceRebuild()
            {
                IEnumerator? routine = m_rebuildRoutine;
                if (routine == null)
                    return;

                bool hasMore;
                try
                {
                    hasMore = routine.MoveNext();
                }
                catch (Exception exception)
                {
                    HideChunks();
                    HideFencePointPreview();
                    s_log.Warning(
                        "Mining hazard overlay rebuild failed; "
                        + $"error={exception}");
                    hasMore = false;
                }
                if (hasMore)
                    return;

                (routine as IDisposable)?.Dispose();
                m_rebuildRoutine = null;
            }

            private IEnumerator RebuildRoutine(
                IEnumerator projectionRoutine,
                ProjectedDesignationBuildResult projectionResult,
                int miningDesignationCount,
                double setupMilliseconds,
                string reason)
            {
                var rebuildTimer = Stopwatch.StartNew();
                double projectionMilliseconds = 0d;
                double meshMilliseconds = 0d;
                double fenceMilliseconds = 0d;
                int projectionSlices = 0;
                int meshSlices = 0;
                try
                {
                    while (true)
                    {
                        Stopwatch sliceTimer = Stopwatch.StartNew();
                        bool projectionHasMore = projectionRoutine.MoveNext();
                        sliceTimer.Stop();
                        projectionMilliseconds += sliceTimer.Elapsed.TotalMilliseconds;
                        projectionSlices++;
                        if (!projectionHasMore)
                            break;
                        yield return null;
                    }

                    if (!string.IsNullOrEmpty(projectionResult.FailureReason))
                    {
                        HideChunks();
                        HideFencePointPreview();
                        s_log.Warning(
                            "Mining hazard overlay projection failed; "
                            + $"hiding stale overlay (trigger={reason}, "
                            + $"miningDesignations={miningDesignationCount}, "
                            + $"setupMs={setupMilliseconds:0.##}, "
                            + $"projectionMs={projectionMilliseconds:0.##}, "
                            + $"reason={projectionResult.FailureReason}).");
                        yield break;
                    }

                    ProjectedDesignationDisturbance projection =
                        projectionResult.Disturbance
                        ?? new ProjectedDesignationDisturbance();
                    int safetyOnlyCutTileCount = projection.CutSafetyTiles.Count(
                        tile => !projection.CutSupportCeilings.ContainsKey(tile));
                    // CutTiles also contains support-only classifications used by
                    // access planning. The public overlay describes projected ground
                    // work, so render only tiles with an actual projected cut surface.
                    var hazardTiles = new HashSet<Tile2i>(
                        projection.CutSupportCeilings.Keys);

                    int activeChunks = 0;
                    if (m_enabled)
                    {
                        IEnumerator meshRoutine = ApplyTilesSliced(
                            hazardTiles,
                            count => activeChunks = count);
                        while (true)
                        {
                            Stopwatch sliceTimer = Stopwatch.StartNew();
                            bool meshHasMore = meshRoutine.MoveNext();
                            sliceTimer.Stop();
                            meshMilliseconds += sliceTimer.Elapsed.TotalMilliseconds;
                            meshSlices++;
                            if (!meshHasMore)
                                break;
                            yield return null;
                        }
                        (meshRoutine as IDisposable)?.Dispose();
                    }
                    else
                        HideChunks();

                    int fencePreviewPosts = 0;
                    if (m_fencePointPreviewEnabled)
                    {
                        Stopwatch fenceTimer = Stopwatch.StartNew();
                        try
                        {
                            fencePreviewPosts =
                                ApplyFencePointPreview(hazardTiles);
                        }
                        catch (Exception exception)
                        {
                            HideFencePointPreview();
                            s_log.Warning(
                                "Mining hazard fence-point preview rebuild failed; "
                                + $"trigger={reason}: {exception.Message}");
                        }
                        fenceMilliseconds = fenceTimer.Elapsed.TotalMilliseconds;
                    }
                    else
                        HideFencePointPreview();

                    LogInfo(
                        "Mining hazard overlay rebuild completed: "
                        + $"trigger={reason} "
                        + $"miningDesignations={miningDesignationCount} "
                        + $"hazardTiles={hazardTiles.Count} "
                        + $"safetyOnlyCutTiles={safetyOnlyCutTileCount} "
                        + $"fencePreviewPosts={fencePreviewPosts} "
                        + $"renderChunks={activeChunks} "
                        + $"setupMs={setupMilliseconds:0.##} "
                        + $"projectionMs={projectionMilliseconds:0.##} "
                        + $"projectionSlices={projectionSlices} "
                        + $"projectionSliceBudgetMs=30 "
                        + $"meshMs={meshMilliseconds:0.##} "
                        + $"meshSlices={meshSlices} "
                        + $"fenceMs={fenceMilliseconds:0.##} "
                        + $"wallMs={rebuildTimer.ElapsedMilliseconds}.");
                }
                finally
                {
                    (projectionRoutine as IDisposable)?.Dispose();
                }
            }

            private IEnumerator ApplyTilesSliced(
                HashSet<Tile2i> tiles,
                Action<int> completed)
            {
                var byChunk = new Dictionary<Vector2Int, List<Tile2i>>();
                int visited = 0;
                foreach (Tile2i tile in tiles)
                {
                    var chunkKey = new Vector2Int(
                        tile.X / ChunkSize,
                        tile.Y / ChunkSize);
                    if (!byChunk.TryGetValue(chunkKey, out List<Tile2i>? chunkTiles))
                    {
                        chunkTiles = new List<Tile2i>();
                        byChunk.Add(chunkKey, chunkTiles);
                    }
                    chunkTiles.Add(tile);
                    visited++;
                    if (visited % ChunkGroupingYieldInterval == 0)
                        yield return null;
                }

                foreach (KeyValuePair<Vector2Int, ChunkView> pair in m_chunks)
                    pair.Value.GameObject.SetActive(false);

                TerrainManager? terrain = s_desigManager?.TerrainManager;
                if (terrain == null)
                {
                    completed(0);
                    yield break;
                }

                int activeChunks = 0;
                foreach (KeyValuePair<Vector2Int, List<Tile2i>> pair in byChunk)
                {
                    ChunkView chunk = GetOrCreateChunk(pair.Key);
                    ReplaceChunkMesh(chunk, pair.Value, terrain);
                    chunk.GameObject.SetActive(pair.Value.Count > 0);
                    activeChunks++;
                    yield return null;
                }
                completed(activeChunks);
            }

            private int ApplyFencePointPreview(HashSet<Tile2i> hazardTiles)
            {
                TerrainManager? terrain = s_desigManager?.TerrainManager;
                if (terrain == null)
                    return HideFencePointPreview();

                List<Tile2i> anchors = BuildFencePostAnchors(hazardTiles);
                if (anchors.Count == 0)
                    return HideFencePointPreview();

                Mesh mesh = BuildFencePointMesh(anchors, terrain);
                if (m_fencePreviewMesh != null)
                    UnityEngine.Object.Destroy(m_fencePreviewMesh);
                m_fencePreviewMesh = mesh;
                m_fencePreviewMeshFilter.sharedMesh = mesh;
                m_fencePreviewMeshRenderer.enabled = true;
                return anchors.Count;
            }

            private int HideFencePointPreview()
            {
                m_fencePreviewRoot.SetActive(false);
                m_fencePreviewMeshRenderer.enabled = false;
                m_fencePreviewMeshFilter.sharedMesh = null;
                if (m_fencePreviewMesh != null)
                {
                    UnityEngine.Object.Destroy(m_fencePreviewMesh);
                    m_fencePreviewMesh = null;
                }
                return 0;
            }

            private static List<Tile2i> BuildFencePostAnchors(
                HashSet<Tile2i> hazardTiles)
            {
                var anchors = new List<Tile2i>();
                var uniqueAnchors = new HashSet<Tile2i>();
                var unvisited = new HashSet<Tile2i>(hazardTiles);
                var componentStack = new Stack<Tile2i>();

                while (unvisited.Count > 0)
                {
                    Tile2i seed = default;
                    foreach (Tile2i tile in unvisited)
                    {
                        seed = tile;
                        break;
                    }

                    unvisited.Remove(seed);
                    componentStack.Push(seed);
                    var componentCorners = new HashSet<Tile2i>();
                    while (componentStack.Count > 0)
                    {
                        Tile2i tile = componentStack.Pop();
                        componentCorners.Add(tile);
                        componentCorners.Add(tile + new RelTile2i(1, 0));
                        componentCorners.Add(tile + new RelTile2i(0, 1));
                        componentCorners.Add(tile + new RelTile2i(1, 1));

                        AddUnvisitedNeighbor(
                            tile + new RelTile2i(-1, 0),
                            unvisited, componentStack);
                        AddUnvisitedNeighbor(
                            tile + new RelTile2i(1, 0),
                            unvisited, componentStack);
                        AddUnvisitedNeighbor(
                            tile + new RelTile2i(0, -1),
                            unvisited, componentStack);
                        AddUnvisitedNeighbor(
                            tile + new RelTile2i(0, 1),
                            unvisited, componentStack);
                    }

                    List<Tile2i> hull = BuildConvexHull(componentCorners);
                    AppendHullPosts(hull, anchors, uniqueAnchors);
                }

                return anchors;
            }

            private static void AddUnvisitedNeighbor(
                Tile2i neighbor,
                HashSet<Tile2i> unvisited,
                Stack<Tile2i> componentStack)
            {
                if (unvisited.Remove(neighbor))
                    componentStack.Push(neighbor);
            }

            private static List<Tile2i> BuildConvexHull(
                HashSet<Tile2i> points)
            {
                var sorted = new List<Tile2i>(points);
                sorted.Sort((left, right) =>
                {
                    int xOrder = left.X.CompareTo(right.X);
                    return xOrder != 0 ? xOrder : left.Y.CompareTo(right.Y);
                });
                if (sorted.Count <= 2)
                    return sorted;

                var hull = new List<Tile2i>(sorted.Count * 2);
                foreach (Tile2i point in sorted)
                {
                    while (hull.Count >= 2
                        && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(point);
                }

                int lowerHullCount = hull.Count;
                for (int i = sorted.Count - 2; i >= 0; i--)
                {
                    Tile2i point = sorted[i];
                    while (hull.Count > lowerHullCount
                        && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(point);
                }
                hull.RemoveAt(hull.Count - 1);
                return hull;
            }

            private static long Cross(Tile2i origin, Tile2i a, Tile2i b)
            {
                return ((long)a.X - origin.X) * ((long)b.Y - origin.Y)
                    - ((long)a.Y - origin.Y) * ((long)b.X - origin.X);
            }

            private static void AppendHullPosts(
                List<Tile2i> hull,
                List<Tile2i> anchors,
                HashSet<Tile2i> uniqueAnchors)
            {
                if (hull.Count == 0)
                    return;
                if (hull.Count == 1)
                {
                    AddUniqueAnchor(hull[0], anchors, uniqueAnchors);
                    return;
                }

                for (int i = 0; i < hull.Count; i++)
                {
                    Tile2i start = hull[i];
                    Tile2i end = hull[(i + 1) % hull.Count];
                    float deltaX = end.X - start.X;
                    float deltaY = end.Y - start.Y;
                    float edgeLength = Mathf.Sqrt(
                        deltaX * deltaX + deltaY * deltaY);
                    int sampleCount = Math.Max(
                        1,
                        Mathf.CeilToInt(edgeLength / FencePostSpacingTiles));
                    for (int sample = 0; sample <= sampleCount; sample++)
                    {
                        float fraction = (float)sample / sampleCount;
                        var anchor = new Tile2i(
                            Mathf.RoundToInt(start.X + deltaX * fraction),
                            Mathf.RoundToInt(start.Y + deltaY * fraction));
                        AddUniqueAnchor(anchor, anchors, uniqueAnchors);
                    }
                }
            }

            private static void AddUniqueAnchor(
                Tile2i anchor,
                List<Tile2i> anchors,
                HashSet<Tile2i> uniqueAnchors)
            {
                if (uniqueAnchors.Add(anchor))
                    anchors.Add(anchor);
            }

            private static Mesh BuildFencePointMesh(
                List<Tile2i> anchors,
                TerrainManager terrain)
            {
                int verticesPerPoint =
                    (FencePointLatitudeSegments + 1)
                    * (FencePointLongitudeSegments + 1);
                int trianglesPerPoint = FencePointLatitudeSegments
                    * FencePointLongitudeSegments * 6;
                var vertices = new List<Vector3>(anchors.Count * verticesPerPoint);
                var triangles = new List<int>(anchors.Count * trianglesPerPoint);

                foreach (Tile2i anchor in anchors)
                {
                    Vector3 center = ToSurfacePoint(
                        anchor, terrain, FencePointLiftTiles);
                    int first = vertices.Count;
                    for (int latitude = 0;
                        latitude <= FencePointLatitudeSegments;
                        latitude++)
                    {
                        float phi = Mathf.PI * latitude
                            / FencePointLatitudeSegments;
                        float ring = Mathf.Sin(phi);
                        float vertical = Mathf.Cos(phi);
                        for (int longitude = 0;
                            longitude <= FencePointLongitudeSegments;
                            longitude++)
                        {
                            float theta = 2f * Mathf.PI * longitude
                                / FencePointLongitudeSegments;
                            var direction = new Vector3(
                                ring * Mathf.Cos(theta),
                                vertical,
                                ring * Mathf.Sin(theta));
                            vertices.Add(center
                                + direction * FencePointRadiusTiles);
                        }
                    }

                    int rowSize = FencePointLongitudeSegments + 1;
                    for (int latitude = 0;
                        latitude < FencePointLatitudeSegments;
                        latitude++)
                    for (int longitude = 0;
                        longitude < FencePointLongitudeSegments;
                        longitude++)
                    {
                        int topLeft = first + latitude * rowSize + longitude;
                        int topRight = topLeft + 1;
                        int bottomLeft = topLeft + rowSize;
                        int bottomRight = bottomLeft + 1;
                        triangles.Add(topLeft);
                        triangles.Add(topRight);
                        triangles.Add(bottomRight);
                        triangles.Add(topLeft);
                        triangles.Add(bottomRight);
                        triangles.Add(bottomLeft);
                    }
                }

                var mesh = new Mesh
                {
                    name = "ATD Mining Hazard Fence-Point Preview",
                    hideFlags = HideFlags.DontSave,
                    indexFormat = IndexFormat.UInt32,
                };
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0, calculateBounds: true);
                mesh.RecalculateNormals();
                return mesh;
            }

            private ChunkView GetOrCreateChunk(Vector2Int key)
            {
                if (m_chunks.TryGetValue(key, out ChunkView? existing))
                    return existing;

                var gameObject = new GameObject(
                    $"Hazard chunk {key.x},{key.y}")
                {
                    hideFlags = HideFlags.DontSave,
                };
                gameObject.transform.SetParent(m_root.transform, worldPositionStays: false);
                ChunkView chunk = new ChunkView(gameObject);
                chunk.MeshRenderer.sharedMaterial = m_material;
                chunk.MeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                chunk.MeshRenderer.receiveShadows = false;
                chunk.MeshRenderer.allowOcclusionWhenDynamic = false;
                m_chunks.Add(key, chunk);
                return chunk;
            }

            private static void ReplaceChunkMesh(
                ChunkView chunk,
                List<Tile2i> tiles,
                TerrainManager terrain)
            {
                var vertices = new List<Vector3>(tiles.Count * 4);
                var uvs = new List<Vector2>(tiles.Count * 4);
                var triangles = new List<int>(tiles.Count * 6);
                foreach (Tile2i tile in tiles)
                {
                    Tile2i east = tile + new RelTile2i(1, 0);
                    Tile2i south = tile + new RelTile2i(0, 1);
                    Tile2i southEast = tile + new RelTile2i(1, 1);

                    int first = vertices.Count;
                    vertices.Add(ToSurfacePoint(tile, terrain));
                    vertices.Add(ToSurfacePoint(east, terrain));
                    vertices.Add(ToSurfacePoint(southEast, terrain));
                    vertices.Add(ToSurfacePoint(south, terrain));
                    uvs.Add(new Vector2(tile.X, tile.Y));
                    uvs.Add(new Vector2(tile.X + 1, tile.Y));
                    uvs.Add(new Vector2(tile.X + 1, tile.Y + 1));
                    uvs.Add(new Vector2(tile.X, tile.Y + 1));
                    triangles.Add(first);
                    triangles.Add(first + 2);
                    triangles.Add(first + 1);
                    triangles.Add(first);
                    triangles.Add(first + 3);
                    triangles.Add(first + 2);
                }

                var mesh = new Mesh
                {
                    name = "ATD Mining Hazard Overlay Chunk",
                    hideFlags = HideFlags.DontSave,
                    indexFormat = IndexFormat.UInt32,
                };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0, calculateBounds: true);
                if (chunk.Mesh != null)
                    UnityEngine.Object.Destroy(chunk.Mesh);
                chunk.Mesh = mesh;
                chunk.MeshFilter.sharedMesh = mesh;
                chunk.MeshRenderer.enabled = triangles.Count > 0;
            }

            private static Vector3 ToSurfacePoint(
                Tile2i corner,
                TerrainManager terrain,
                float additionalHeightTiles = 0f)
            {
                Tile2i sample = new Tile2i(
                    Math.Max(0, Math.Min(terrain.TerrainSize.X - 1, corner.X)),
                    Math.Max(0, Math.Min(terrain.TerrainSize.Y - 1, corner.Y)));
                float height = terrain.GetHeight(sample).Value.ToFloat()
                    + SurfaceOffsetTiles
                    + additionalHeightTiles;
                return corner.CornerTile2f
                    .ExtendHeight(new HeightTilesF(height.ToFix32()))
                    .ToVector3();
            }

            private void HideChunks()
            {
                foreach (ChunkView chunk in m_chunks.Values)
                    chunk.GameObject.SetActive(false);
            }

            private static Texture2D CreateStripeTexture()
            {
                const int size = 32;
                var texture = new Texture2D(
                    size, size, TextureFormat.RGBA32,
                    mipChain: false, linear: true)
                {
                    name = "ATD Mining Hazard Stripes",
                    hideFlags = HideFlags.DontSave,
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                    anisoLevel = 1,
                };
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool yellow = ((x + y) % size) < size / 2;
                    pixels[y * size + x] = yellow
                        ? new Color(1f, 0.72f, 0.04f, 0.52f)
                        : new Color(0.015f, 0.012f, 0.006f, 0.46f);
                }
                texture.SetPixels(pixels);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                return texture;
            }

            public void Dispose()
            {
                if (m_disposed)
                    return;
                m_disposed = true;
                CancelRebuild();
                m_gameLoopEvents.RenderUpdate.RemoveNonSaveable(this, OnRenderUpdate);
                foreach (ChunkView chunk in m_chunks.Values)
                {
                    if (chunk.Mesh != null)
                        UnityEngine.Object.Destroy(chunk.Mesh);
                    UnityEngine.Object.Destroy(chunk.GameObject);
                }
                m_chunks.Clear();
                if (m_fencePreviewMesh != null)
                    UnityEngine.Object.Destroy(m_fencePreviewMesh);
                UnityEngine.Object.Destroy(m_fencePreviewMaterial);
                UnityEngine.Object.Destroy(m_fencePreviewRoot);
                Texture? texture = m_material.mainTexture;
                UnityEngine.Object.Destroy(m_material);
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
                UnityEngine.Object.Destroy(m_root);
            }
        }
    }
}
