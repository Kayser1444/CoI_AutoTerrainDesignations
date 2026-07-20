using System;
using System.Collections.Generic;
using System.Diagnostics;
using Mafi;
using Mafi.Core;
using AutoTerrainDesignations.Access.V2;

namespace AutoTerrainDesignations.Access.V2
{
    internal static class AccessV2ReverseBfsHeuristic
    {
        private struct ReverseEdge
        {
            public int GridDx;
            public int GridDy;
            public int InvariantU;
        }

        public static void Compute(AccessSearchSnapshot snapshot)
        {
            Stopwatch sw = Stopwatch.StartNew();

            int minX = snapshot.BoundsMin.X / 4;
            int maxX = snapshot.BoundsMax.X / 4;
            int minY = snapshot.BoundsMin.Y / 4;
            int maxY = snapshot.BoundsMax.Y / 4;
            
            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            int depth = snapshot.MaxHeight2 - snapshot.MinHeight2 + 1;

            if (width <= 0 || height <= 0 || depth <= 0)
            {
                Log.Info("[ReverseBFS Prototype] Invalid bounds.");
                return;
            }

            int modeCount = 5;
            int dirCount = 4;
            int turnCount = 2;
            int invariantCount = depth * modeCount * dirCount * turnCount;
            int stateCount = width * height * invariantCount;

            Log.Info($"[ReverseBFS Prototype] Bounds: {width}x{height}x{depth}. Total states: {stateCount}");

            // Indexing for invariant states
            int GetInvariantIndex(int h2Idx, int modeIdx, int dirIdx, int turnIdx)
            {
                if (h2Idx < 0 || h2Idx >= depth) return -1;
                return ((h2Idx * modeCount + modeIdx) * dirCount + dirIdx) * turnCount + turnIdx;
            }

            var validModes = new[] { AccessSearchMode.Flat, AccessSearchMode.XPositive, AccessSearchMode.XNegative, AccessSearchMode.YPositive, AccessSearchMode.YNegative };
            var dirs = new[] { new Tile2i(4, 0), new Tile2i(-4, 0), new Tile2i(0, 4), new Tile2i(0, -4) };

            int ModeToIndex(AccessSearchMode m) => m switch
            {
                AccessSearchMode.Flat => 0,
                AccessSearchMode.XPositive => 1,
                AccessSearchMode.XNegative => 2,
                AccessSearchMode.YPositive => 3,
                AccessSearchMode.YNegative => 4,
                _ => -1
            };

            int DirToIndex(Tile2i d)
            {
                if (d.X == 4) return 0;
                if (d.X == -4) return 1;
                if (d.Y == 4) return 2;
                if (d.Y == -4) return 3;
                return -1;
            }

            // Phase 1: Build Invariant Templates
            var tempEdges = new List<ReverseEdge>[invariantCount];
            for (int i = 0; i < invariantCount; i++) tempEdges[i] = new List<ReverseEdge>(8);

            Tile2i baseAnchor = Tile2i.Zero;

            for (int h2Idx = 0; h2Idx < depth; h2Idx++)
            {
                int realH2 = h2Idx + snapshot.MinHeight2;
                foreach (AccessSearchMode mode in validModes)
                {
                    if (!AccessHeightProfile.TryForMode(mode, realH2, out AccessHeightProfile profile)) continue;
                    
                    foreach (Tile2i dir in dirs)
                    {
                        AccessV2TravelAxis axis = (dir.X != 0) ? AccessV2TravelAxis.X : AccessV2TravelAxis.Y;
                        
                        bool modeMatchesAxis = axis == AccessV2TravelAxis.X
                            ? mode == AccessSearchMode.XPositive || mode == AccessSearchMode.XNegative
                            : mode == AccessSearchMode.YPositive || mode == AccessSearchMode.YNegative;
                        
                        if (mode != AccessSearchMode.Flat && !modeMatchesAxis) continue;
                        if (!AccessV2BandProfile.TryCreateEnabled(axis, profile, profile, out AccessV2BandProfile band, out _)) continue;

                        for (int turnIdx = 0; turnIdx < 2; turnIdx++)
                        {
                            bool isTurnPending = turnIdx == 1;
                            int modeIdx = ModeToIndex(mode);
                            int dirIdx = DirToIndex(dir);
                            int invariantU = GetInvariantIndex(h2Idx, modeIdx, dirIdx, turnIdx);

                            var stateU = new AccessV2BandState(baseAnchor, band, dir, isTurnPending);

                            void RegisterForward(AccessV2BandState stateV)
                            {
                                int dxGrid = (stateV.Anchor.X - baseAnchor.X) / 4;
                                int dyGrid = (stateV.Anchor.Y - baseAnchor.Y) / 4;
                                int vH2Idx = stateV.Band.Lane0.Center2 - snapshot.MinHeight2;
                                AccessV2BandProfile.TryGetProfileMode(stateV.Band.Lane0, out AccessSearchMode vMode);
                                int invariantV = GetInvariantIndex(vH2Idx, ModeToIndex(vMode), DirToIndex(stateV.EntryDirection), stateV.IsTurnPending ? 1 : 0);
                                
                                if (invariantV >= 0)
                                {
                                    tempEdges[invariantV].Add(new ReverseEdge { GridDx = -dxGrid, GridDy = -dyGrid, InvariantU = invariantU });
                                }
                            }

                            // Straights
                            foreach (var t in AccessV2Geometry.EnumerateStraight(stateU)) RegisterForward(t.Next);

                            // Strafes
                            if (AccessV2Geometry.TryStrafe(stateU, 1, profile, out var strafePos, out _)) RegisterForward(strafePos.Next);
                            if (AccessV2Geometry.TryStrafe(stateU, -1, profile, out var strafeNeg, out _)) RegisterForward(strafeNeg.Next);

                            // Turns
                            if (mode == AccessSearchMode.Flat && !isTurnPending)
                            {
                                AccessV2TravelAxis otherAxis = AccessV2Geometry.OtherAxis(axis);
                                Tile2i curLaneDir = AccessV2Geometry.GetCanonicalLaneDirection(axis);
                                Tile2i otherLaneDir = AccessV2Geometry.GetCanonicalLaneDirection(otherAxis);
                                
                                if (AccessV2BandProfile.TryCreateEnabled(otherAxis, profile, profile, out AccessV2BandProfile nextBand, out _))
                                {
                                    Tile2i nextAnchorPos = CanonicalAnchor(otherAxis, AccessV2Geometry.Add(baseAnchor, otherLaneDir), AccessV2Geometry.Add(baseAnchor, otherLaneDir));
                                    RegisterForward(new AccessV2BandState(nextAnchorPos, nextBand, otherLaneDir, true));

                                    Tile2i nextAnchorNeg = CanonicalAnchor(otherAxis, AccessV2Geometry.Subtract(baseAnchor, otherLaneDir), AccessV2Geometry.Subtract(baseAnchor, otherLaneDir));
                                    RegisterForward(new AccessV2BandState(nextAnchorNeg, nextBand, new Tile2i(-otherLaneDir.X, -otherLaneDir.Y), true));
                                }
                            }
                        }
                    }
                }
            }

            // Flatten templates into CSR arrays for extreme cache locality
            int totalEdges = 0;
            int[] edgeOffsets = new int[invariantCount + 1];
            for (int i = 0; i < invariantCount; i++)
            {
                edgeOffsets[i] = totalEdges;
                totalEdges += tempEdges[i].Count;
            }
            edgeOffsets[invariantCount] = totalEdges;
            
            ReverseEdge[] flatEdges = new ReverseEdge[totalEdges];
            for (int i = 0; i < invariantCount; i++)
            {
                var list = tempEdges[i];
                int start = edgeOffsets[i];
                for (int j = 0; j < list.Count; j++)
                {
                    flatEdges[start + j] = list[j];
                }
            }

            long templateGenTime = sw.ElapsedMilliseconds;

            // Phase 2: BFS Initialization
            byte[] distances = new byte[stateCount];
            for (int i = 0; i < stateCount; i++) distances[i] = 255;

            int[] queue = new int[stateCount];
            int head = 0;
            int tail = 0;
            int seededCount = 0;

            foreach (Tile2i goal in snapshot.GoalGroundNodes)
            {
                int x = (goal.X / 4) - minX;
                int y = (goal.Y / 4) - minY;
                if (x < 0 || x >= width || y < 0 || y >= height) continue;

                if (!snapshot.TryGetGroundHeight2(goal, out int h2)) continue;

                int h2Idx = h2 - snapshot.MinHeight2;
                if (h2Idx >= 0 && h2Idx < depth)
                {
                    for (int modeIdx = 0; modeIdx < modeCount; modeIdx++)
                    {
                        for (int dirIdx = 0; dirIdx < dirCount; dirIdx++)
                        {
                            for (int turnIdx = 0; turnIdx < turnCount; turnIdx++)
                            {
                                int invU = GetInvariantIndex(h2Idx, modeIdx, dirIdx, turnIdx);
                                if (invU >= 0)
                                {
                                    int fullIdx = (x * height + y) * invariantCount + invU;
                                    if (distances[fullIdx] == 255)
                                    {
                                        distances[fullIdx] = 0;
                                        queue[tail++] = fullIdx;
                                        seededCount++;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            long seedTime = sw.ElapsedMilliseconds;

            // Phase 3: Fast Reverse BFS (Data Oriented)
            int maxDist = 0;
            while (head < tail)
            {
                int fullV = queue[head++];
                byte d = distances[fullV];
                if (d > maxDist) maxDist = d;
                if (d >= 254) continue; // Prevent byte overflow

                int invV = fullV % invariantCount;
                int posIdx = fullV / invariantCount;
                int y = posIdx % height;
                int x = posIdx / height;

                int start = edgeOffsets[invV];
                int end = edgeOffsets[invV + 1];
                for (int i = start; i < end; i++)
                {
                    ReverseEdge edge = flatEdges[i];
                    int prevX = x + edge.GridDx;
                    int prevY = y + edge.GridDy;

                    if (prevX >= 0 && prevX < width && prevY >= 0 && prevY < height)
                    {
                        int prevIdx = (prevX * height + prevY) * invariantCount + edge.InvariantU;
                        if (distances[prevIdx] == 255)
                        {
                            distances[prevIdx] = (byte)(d + 1);
                            queue[tail++] = prevIdx;
                        }
                    }
                }
            }

            long bfsTime = sw.ElapsedMilliseconds;
            Log.Info($"[ReverseBFS Prototype] Built templates in {templateGenTime}ms, seeded {seededCount} in {seedTime - templateGenTime}ms, BFS maxDist {maxDist} in {bfsTime - seedTime}ms. Total: {bfsTime}ms");
        }

        private static Tile2i CanonicalAnchor(AccessV2TravelAxis axis, Tile2i first, Tile2i second)
        {
            if (axis == AccessV2TravelAxis.X) return first.Y <= second.Y ? first : second;
            return first.X <= second.X ? first : second;
        }
    }
}
