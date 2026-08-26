using System;

namespace AutoTerrainDesignations.Access.V2
{
    /// <summary>
    /// Dense four-file terminal-center mask. Four terminal ranks occupy at
    /// most sixteen longitudinal cells, so the complete mask fits in one
    /// ulong and cardinal propagation needs no managed graph allocations.
    /// </summary>
    internal readonly struct AccessV2TerminalMask
    {
        public const int Files = 4;
        public const int MaxRanks = 4;
        public const int RowsPerRank = 4;
        public const int MaxRows = MaxRanks * RowsPerRank;
        public const int MaxCells = Files * MaxRows;

        private const ulong RowMask = 0xFUL;
        public ulong Bits { get; }
        public int Ranks { get; }

        public AccessV2TerminalMask(ulong bits, int ranks)
        {
            Ranks = Math.Max(1, Math.Min(MaxRanks, ranks));
            Bits = bits & GridMask(Ranks);
        }

        public bool IsEmpty => Bits == 0UL;

        public static AccessV2TerminalMask Empty(int ranks)
            => new AccessV2TerminalMask(0UL, ranks);

        public static AccessV2TerminalMask Single(int row, int file)
        {
            if (row < 0 || row >= MaxRows || file < 0 || file >= Files)
                return Empty(MaxRanks);
            return new AccessV2TerminalMask(
                1UL << (row * Files + file), MaxRanks);
        }

        public static AccessV2TerminalMask Row(int row, int ranks)
        {
            int rows = RankRowCount(ranks);
            if (row < 0 || row >= rows)
                return Empty(ranks);
            return new AccessV2TerminalMask(
                RowMask << (row * Files), ranks);
        }

        public bool Contains(int row, int file)
            => row >= 0 && row < RankRowCount(Ranks)
                && file >= 0 && file < Files
                && (Bits & (1UL << (row * Files + file))) != 0UL;

        public static int RankRowCount(int ranks)
            => Math.Max(1, Math.Min(MaxRanks, ranks)) * RowsPerRank;

        public static ulong GridMask(int ranks)
        {
            int cells = RankRowCount(ranks) * Files;
            return cells == 64 ? ulong.MaxValue : (1UL << cells) - 1UL;
        }

        public override string ToString()
            => $"ranks={Ranks} bits=0x{Bits:X16}";
    }

    internal readonly struct AccessV2TerminalProof
    {
        public bool Success { get; }
        public int Distance { get; }
        public ulong GoalBits { get; }
        public ulong VisitedBits { get; }

        public AccessV2TerminalProof(
            bool success,
            int distance,
            ulong goalBits,
            ulong visitedBits)
        {
            Success = success;
            Distance = distance;
            GoalBits = goalBits;
            VisitedBits = visitedBits;
        }
    }

    internal static class AccessV2TerminalProofHelper
    {
        public static AccessV2TerminalProof FindMinimumCardinalProof(
            AccessV2TerminalMask pathable,
            AccessV2TerminalMask starts,
            AccessV2TerminalMask goals)
        {
            int ranks = Math.Min(pathable.Ranks,
                Math.Min(starts.Ranks, goals.Ranks));
            ulong grid = AccessV2TerminalMask.GridMask(ranks);
            ulong frontier = starts.Bits & pathable.Bits & grid;
            ulong remainingGoals = goals.Bits & pathable.Bits & grid;
            ulong visited = frontier;
            if (frontier == 0UL || remainingGoals == 0UL)
                return new AccessV2TerminalProof(false, -1, 0UL, visited);

            if ((frontier & remainingGoals) != 0UL)
                return new AccessV2TerminalProof(
                    true, 0, frontier & remainingGoals, visited);

            int distance = 0;
            while (frontier != 0UL && distance < AccessV2TerminalMask.MaxCells)
            {
                ulong next = ExpandCardinal(frontier, ranks)
                    & pathable.Bits & grid & ~visited;
                distance++;
                ulong reached = next & remainingGoals;
                if (reached != 0UL)
                    return new AccessV2TerminalProof(
                        true, distance, reached, visited | next);
                visited |= next;
                frontier = next;
            }

            return new AccessV2TerminalProof(false, -1, 0UL, visited);
        }

        public static ulong ExpandCardinal(ulong frontier, int ranks)
        {
            ulong grid = AccessV2TerminalMask.GridMask(ranks);
            ulong horizontal = ((frontier << 1)
                    & AccessV2TerminalMaskFileMasks.File123)
                | ((frontier >> 1)
                    & AccessV2TerminalMaskFileMasks.File012);
            ulong vertical = (frontier << AccessV2TerminalMask.Files)
                | (frontier >> AccessV2TerminalMask.Files);
            return (horizontal | vertical) & grid;
        }

        private static class AccessV2TerminalMaskFileMasks
        {
            public const ulong File012 =
                0x7777777777777777UL;
            public const ulong File123 =
                0xEEEEEEEEEEEEEEEEUL;
        }
    }
}
