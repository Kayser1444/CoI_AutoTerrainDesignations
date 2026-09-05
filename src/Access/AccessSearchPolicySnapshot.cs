using System;

namespace AutoTerrainDesignations.Access
{
    /// <summary>
    /// Immutable semantic and execution limits captured for one access search.
    /// Search code must receive this value through its request snapshot rather
    /// than reading mutable mod settings while a search is running.
    /// </summary>
    internal sealed class AccessSearchPolicySnapshot
    {
        // Retained as an internal serialized compatibility field for schema-1
        // replay cases. Current live behavior is always routed; this value is
        // no longer a user setting and is intentionally ignored by callers.
        internal bool TurningRampsEnabled { get; }
        public bool SuppressLegacyAccessRamps { get; }
        public bool UseAStar { get; }
        public bool UseUsefulHeightEnvelope { get; }
        public bool AvoidOcean { get; }
        public bool AvoidBuildings { get; }
        public bool AllowRampsOutsideTowerAreas { get; }
        public bool ReduceOversizedAreas { get; }
        public bool HarvestDisruptedTrees { get; }
        public bool AllowDigToRemoveDebris { get; }
        public QuickRemoveDebrisPolicy QuickRemoveDebrisPolicy { get; }
        public float LandscapingCostDistanceScale { get; }
        public float PropCleanupLandscapingCost { get; }
        public float LandslideRunPerHeight { get; }
        public float GeneratedVFixedCost { get; }
        public float DirectWorkWeight { get; }
        public float SideRayWeight { get; }
        public float RaySlopeConservatism { get; }
        public int RayEndBuffer { get; }
        public int CandidateRayMaxDistance { get; }
        public float RayMaxCost { get; }
        public float RayUnresolvedPenalty { get; }
        public int MaxVisitedNodes { get; }
        public int SearchTimeoutSeconds { get; }
        public int SearchFrameBudgetMs { get; }
        public int SnapshotMemoryCeilingMiB { get; }
        public int ManagerAutomatedFrameBudgetMs { get; }
        public int ManagerInteractiveFrameBudgetMs { get; }
        public int ManagerPausedMaxFrameBudgetMs { get; }
        public int V1HeightEnvelopeLowerAllowance32 { get; }
        public int V2HeightEnvelopeLowerAllowance32 { get; }
        public int V1HeightEnvelopeUpperAllowance32 { get; }
        public int V2HeightEnvelopeUpperAllowance32 { get; }

        /// <summary>
        /// Stable request identity for all values that affect access planning.
        /// Diagnostic presentation options are intentionally excluded.
        /// </summary>
        public int SemanticFingerprint { get; }

        private AccessSearchPolicySnapshot(
            bool turningRampsEnabled,
            bool suppressLegacyAccessRamps,
            bool useAStar,
            bool useUsefulHeightEnvelope,
            bool avoidOcean,
            bool avoidBuildings,
            bool allowRampsOutsideTowerAreas,
            bool reduceOversizedAreas,
            bool harvestDisruptedTrees,
            bool allowDigToRemoveDebris,
            QuickRemoveDebrisPolicy quickRemoveDebrisPolicy,
            float landscapingCostDistanceScale,
            float propCleanupLandscapingCost,
            float landslideRunPerHeight,
            float generatedVFixedCost,
            float directWorkWeight,
            float sideRayWeight,
            float raySlopeConservatism,
            int rayEndBuffer,
            int candidateRayMaxDistance,
            float rayMaxCost,
            float rayUnresolvedPenalty,
            int maxVisitedNodes,
            int searchTimeoutSeconds,
            int searchFrameBudgetMs,
            int snapshotMemoryCeilingMiB,
            int managerAutomatedFrameBudgetMs,
            int managerInteractiveFrameBudgetMs,
            int managerPausedMaxFrameBudgetMs,
            int v1HeightEnvelopeLowerAllowance32,
            int v2HeightEnvelopeLowerAllowance32,
            int v1HeightEnvelopeUpperAllowance32,
            int v2HeightEnvelopeUpperAllowance32)
        {
            TurningRampsEnabled = turningRampsEnabled;
            SuppressLegacyAccessRamps = suppressLegacyAccessRamps;
            UseAStar = useAStar;
            UseUsefulHeightEnvelope = useUsefulHeightEnvelope;
            AvoidOcean = avoidOcean;
            AvoidBuildings = avoidBuildings;
            AllowRampsOutsideTowerAreas = allowRampsOutsideTowerAreas;
            ReduceOversizedAreas = reduceOversizedAreas;
            HarvestDisruptedTrees = harvestDisruptedTrees;
            AllowDigToRemoveDebris = allowDigToRemoveDebris;
            QuickRemoveDebrisPolicy = quickRemoveDebrisPolicy;
            LandscapingCostDistanceScale = landscapingCostDistanceScale;
            PropCleanupLandscapingCost = propCleanupLandscapingCost;
            LandslideRunPerHeight = landslideRunPerHeight;
            GeneratedVFixedCost = generatedVFixedCost;
            DirectWorkWeight = directWorkWeight;
            SideRayWeight = sideRayWeight;
            RaySlopeConservatism = raySlopeConservatism;
            RayEndBuffer = rayEndBuffer;
            CandidateRayMaxDistance = candidateRayMaxDistance;
            RayMaxCost = rayMaxCost;
            RayUnresolvedPenalty = rayUnresolvedPenalty;
            MaxVisitedNodes = maxVisitedNodes;
            SearchTimeoutSeconds = searchTimeoutSeconds;
            SearchFrameBudgetMs = searchFrameBudgetMs;
            SnapshotMemoryCeilingMiB = snapshotMemoryCeilingMiB;
            ManagerAutomatedFrameBudgetMs = managerAutomatedFrameBudgetMs;
            ManagerInteractiveFrameBudgetMs = managerInteractiveFrameBudgetMs;
            ManagerPausedMaxFrameBudgetMs = managerPausedMaxFrameBudgetMs;
            V1HeightEnvelopeLowerAllowance32 = v1HeightEnvelopeLowerAllowance32;
            V2HeightEnvelopeLowerAllowance32 = v2HeightEnvelopeLowerAllowance32;
            V1HeightEnvelopeUpperAllowance32 = v1HeightEnvelopeUpperAllowance32;
            V2HeightEnvelopeUpperAllowance32 = v2HeightEnvelopeUpperAllowance32;
            SemanticFingerprint = ComputeFingerprint(this);
        }

        internal static AccessSearchPolicySnapshot Capture()
            => new AccessSearchPolicySnapshot(
                true,
                AutoTerrainDesignationsMod.SuppressLegacyAccessRamps,
                AutoTerrainDesignationsMod.ExperimentalAccessUseAStar,
                AutoTerrainDesignationsMod.ExperimentalAccessUsefulHeightEnvelope,
                AutoTerrainDesignationsMod.AccessAvoidOcean,
                AutoTerrainDesignationsMod.AccessAvoidBuildings,
                AutoTerrainDesignationsMod.AllowRampsOutsideTowerAreas,
                AutoDepthDesignation.ReduceOversizedAreas,
                AutoTerrainDesignationsMod.AccessHarvestDisruptedTrees,
                AutoTerrainDesignationsMod.AccessAllowDigToRemoveDebris,
                AutoTerrainDesignationsMod.AccessQuickRemoveDebrisPolicy,
                AutoTerrainDesignationsMod.AccessLandscapingCostDistanceScale,
                AutoTerrainDesignationsMod.AccessPropCleanupLandscapingCost,
                AutoTerrainDesignationsMod.AccessLandslideRunPerHeight,
                AutoTerrainDesignationsMod.AccessGeneratedVFixedCost,
                AutoTerrainDesignationsMod.AccessDirectWorkWeight,
                AutoTerrainDesignationsMod.AccessSideRayWeight,
                AutoTerrainDesignationsMod.AccessRaySlopeConservatism,
                AutoTerrainDesignationsMod.AccessRayEndBuffer,
                AutoTerrainDesignationsMod.AccessCandidateRayMaxDistance,
                AutoTerrainDesignationsMod.AccessRayMaxCost,
                AutoTerrainDesignationsMod.AccessRayUnresolvedPenalty,
                AutoTerrainDesignationsMod.AccessMaxVisitedNodes,
                AutoTerrainDesignationsMod.AccessSearchTimeoutSeconds,
                AutoTerrainDesignationsMod.AccessSearchFrameBudgetMs,
                AutoTerrainDesignationsMod.AccessSnapshotMemoryCeilingMiB,
                AutoTerrainDesignationsMod.AccessManagerAutomatedFrameBudgetMs,
                AutoTerrainDesignationsMod.AccessManagerInteractiveFrameBudgetMs,
                AutoTerrainDesignationsMod.AccessManagerPausedMaxFrameBudgetMs,
                AutoTerrainDesignationsMod.ExperimentalAccessV1HeightEnvelopeLowerAllowance32,
                AutoTerrainDesignationsMod.ExperimentalAccessV2HeightEnvelopeLowerAllowance32,
                AutoTerrainDesignationsMod.ExperimentalAccessV1HeightEnvelopeUpperAllowance32,
                AutoTerrainDesignationsMod.ExperimentalAccessV2HeightEnvelopeUpperAllowance32);

        internal AccessSearchPolicySnapshot WithSnapshotOverrides(
            bool useAStar,
            float landscapingCostDistanceScale,
            float landslideRunPerHeight,
            bool avoidOcean,
            bool avoidBuildings)
            => new AccessSearchPolicySnapshot(
                TurningRampsEnabled,
                SuppressLegacyAccessRamps,
                useAStar,
                UseUsefulHeightEnvelope,
                avoidOcean,
                avoidBuildings,
                AllowRampsOutsideTowerAreas,
                ReduceOversizedAreas,
                HarvestDisruptedTrees,
                AllowDigToRemoveDebris,
                QuickRemoveDebrisPolicy,
                landscapingCostDistanceScale,
                PropCleanupLandscapingCost,
                landslideRunPerHeight,
                GeneratedVFixedCost,
                DirectWorkWeight,
                SideRayWeight,
                RaySlopeConservatism,
                RayEndBuffer,
                CandidateRayMaxDistance,
                RayMaxCost,
                RayUnresolvedPenalty,
                MaxVisitedNodes,
                SearchTimeoutSeconds,
                SearchFrameBudgetMs,
                SnapshotMemoryCeilingMiB,
                ManagerAutomatedFrameBudgetMs,
                ManagerInteractiveFrameBudgetMs,
                ManagerPausedMaxFrameBudgetMs,
                V1HeightEnvelopeLowerAllowance32,
                V2HeightEnvelopeLowerAllowance32,
                V1HeightEnvelopeUpperAllowance32,
                V2HeightEnvelopeUpperAllowance32);

        private static int ComputeFingerprint(AccessSearchPolicySnapshot policy)
        {
            unchecked
            {
                int hash = 17;
                void Add(int value) => hash = hash * 31 + value;
                Add(policy.TurningRampsEnabled ? 1 : 0);
                Add(policy.SuppressLegacyAccessRamps ? 1 : 0);
                Add(policy.UseAStar ? 1 : 0);
                Add(policy.UseUsefulHeightEnvelope ? 1 : 0);
                Add(policy.AvoidOcean ? 1 : 0);
                Add(policy.AvoidBuildings ? 1 : 0);
                Add(policy.AllowRampsOutsideTowerAreas ? 1 : 0);
                Add(policy.ReduceOversizedAreas ? 1 : 0);
                Add(policy.HarvestDisruptedTrees ? 1 : 0);
                Add(policy.AllowDigToRemoveDebris ? 1 : 0);
                Add((int)policy.QuickRemoveDebrisPolicy);
                Add(policy.LandscapingCostDistanceScale.GetHashCode());
                Add(policy.PropCleanupLandscapingCost.GetHashCode());
                Add(policy.LandslideRunPerHeight.GetHashCode());
                Add(policy.GeneratedVFixedCost.GetHashCode());
                Add(policy.DirectWorkWeight.GetHashCode());
                Add(policy.SideRayWeight.GetHashCode());
                Add(policy.RaySlopeConservatism.GetHashCode());
                Add(policy.RayEndBuffer);
                Add(policy.CandidateRayMaxDistance);
                Add(policy.RayMaxCost.GetHashCode());
                Add(policy.RayUnresolvedPenalty.GetHashCode());
                Add(policy.MaxVisitedNodes);
                Add(policy.SearchTimeoutSeconds);
                Add(policy.SearchFrameBudgetMs);
                Add(policy.V1HeightEnvelopeLowerAllowance32);
                Add(policy.V2HeightEnvelopeLowerAllowance32);
                Add(policy.V1HeightEnvelopeUpperAllowance32);
                Add(policy.V2HeightEnvelopeUpperAllowance32);
                return hash;
            }
        }
    }
}
