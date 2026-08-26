using System;
using System.Collections.Generic;
using System.Reflection;
using Mafi;

namespace AutoTerrainDesignations.Access
{
    /// <summary>
    /// Small executable architecture guard for the Ticket 1 boundary. This
    /// intentionally checks object shape rather than implementation details:
    /// requests and snapshots must not retain delegates or live game objects,
    /// while request-local evaluator/cache state belongs to the workspace.
    /// </summary>
    internal static class AccessSearchArchitectureFixtures
    {
        internal static bool ValidateAll(out string failure)
        {
            if (ContainsForbiddenField(typeof(AccessPathRequest),
                    out string requestField))
            {
                failure = "request retains forbidden execution reference: " + requestField;
                return false;
            }
            if (ContainsForbiddenField(typeof(AccessSearchSnapshot),
                    out string snapshotField))
            {
                failure = "snapshot retains forbidden execution reference: " + snapshotField;
                return false;
            }

            var snapshot = new AccessSearchSnapshot(
                Tile2i.Zero, new Tile2i(8, 8), new Tile2i(8, 8),
                -2, 2, false, false, false, 1f, 1f,
                new Dictionary<Tile2i, int>(),
                new Dictionary<Tile2i, int>(),
                new Dictionary<Tile2i, AccessHeightProfile>(),
                Array.Empty<Tile2i>(), Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(), Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(), Array.Empty<AccessDurabilityCorner>());
            var workspace = new AccessSearchWorkspace(snapshot);
            if (!ReferenceEquals(workspace.Snapshot, snapshot)
                || workspace.Evaluator == null
                || !(workspace.Evaluator is SnapshotAccessSearchEvaluator)
                || snapshot.Policy == null)
            {
                failure =
                    "workspace must reconstruct its evaluator from one immutable snapshot and policy";
                return false;
            }

            AccessHeightProfile flat = new AccessHeightProfile(0, 0, 0, 0);
            IReadOnlyList<AccessGroundHandoff> v1Handoffs =
                workspace.Evaluator.GetWorkableHandoffs(
                    new Tile2i(4, 4), flat, new Tile2i(0, 4), flat);
            IReadOnlyList<AccessGroundHandoff> v2Handoffs =
                workspace.Evaluator.GetV2WorkableHandoffs(
                    new Tile2i(4, 4), flat, new Tile2i(0, 4), flat);
            if (!workspace.Evaluator.HasWorkableHandoffEvaluator
                || !workspace.Evaluator.HasWorkableHandoffSpanEvaluator
                || !workspace.Evaluator.HasV2WorkableHandoffEvaluator
                || v1Handoffs == null
                || v2Handoffs == null)
            {
                failure =
                    "snapshot-owned evaluator did not execute from value-owned facts";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool ContainsForbiddenField(
            Type type, out string fieldName)
        {
            foreach (FieldInfo field in type.GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (typeof(Delegate).IsAssignableFrom(field.FieldType)
                    || field.FieldType.Name == nameof(TerrainManagerMarker)
                    || field.FieldType.Name.Contains("TerrainManager"))
                {
                    fieldName = field.Name;
                    return true;
                }
            }
            fieldName = string.Empty;
            return false;
        }

        // Marker avoids putting a live game type in the fixture's forbidden
        // list while keeping the intent explicit in the guard.
        private sealed class TerrainManagerMarker { }
    }
}
