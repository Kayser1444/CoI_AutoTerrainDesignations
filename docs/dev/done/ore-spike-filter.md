# Ore spike filter

Status: initial public implementation qualified for v0.8.0. The detector remains
replayable and parameterized for later corpus tuning.

## World setting

Provide a separate **Filter ore spikes** toggle under a dedicated world-settings
heading, **Vanilla fixes** (presentation capitalization may follow
the settings UI). It is not another ore-quality level or a per-tower setting.
The maintainer explicitly requested this grouping on 2026-08-31.
The toggle is world-persistent and defaults to enabled. The new-game default is
also written to `ATDsettings.json` when world settings are saved as defaults.

## Planning boundary

Raw capture → spike-filtered ore interpretation → existing ore-quality rules
→ mine geometry, bottom flattening and corner smoothing.

The initial policy uses the median bedrock top of all eight physical neighbors.
It trims selected ore below four terrain layers under that reference only when
all eight neighbors contain bedrock. The detector and its parameters live behind
a separate preprocessing seam so later corpus tuning does not change the public
setting or pipeline boundary.

Preserve raw captured layers and recorded baselines. Produce a derived mining
view that disregards or adjusts anomalous ore intervals for planning. The
filter must be independently switchable at every ore-quality level and
replayable without recapturing the world. Actual terrain remains authoritative
for safety, access, and physical excavation calculations: disregarding ore
does not remove the rock or change terrain in the game.

## Evidence and next experiments

The three samples cover iron, quartz and copper. All show isolated deep
ore-bottom outliers, downward displacement of the lower ore interval and
bedrock, and small adjacent groups. The third sample is reported by the
maintainer to be a fresh map on January 1 with simulation never advanced.
It nevertheless already contains disrupted rock and copper. Material names
alone do not indicate player mining or later simulation, and disrupted
material must not itself be treated as a defect.

Sample 4 is different: it records a mostly excavated deposit for a high-quality
mop-up run and has no confirmed spike. Treat it as a provisional negative
operational control. A correction in that case is not a benefit unless the
in-game viewer establishes that the changed source really is a vanilla spike.

Sample 6 is a known-spike deposit after Medium quality excavated most of the
ore without a spike filter, followed by an Off-quality recapture. It tests
whether a detector can still recognize the shortened remnants when surrounding
ore support has been removed. The intended pre-excavation paired capture was
aborted before publication, so the longitudinal comparison is incomplete.
The in-game viewer confirms all six plan-affecting bedrock-r4 corrections in
the post-Medium capture as spikes. Bedrock-r6 catches only two of them; r8 and
r10 change no final geometry.

See [sample 1](../../test/ore-spike-01-characterization.md),
[sample 2](../../test/ore-spike-02-characterization.md), and
[fresh sample 3](../../test/ore-spike-03-characterization.md). See also
[mop-up sample 4](../../test/ore-spike-04-characterization.md) and
[post-Medium sample 6](../../test/ore-spike-06-characterization.md). Detection
thresholds in these surveys are descriptive, not chosen filter defaults.
Bedrock-aware replay across nine captured mining cases retained the r4
bedrock-neighborhood detector as the best initial release boundary. It directly
matches the vanilla defect, retains the deliberately captured smaller spikes
that r5 misses, and avoids the broader assumptions of ore-profile detectors.

## Success criterion

Evaluate a candidate by comparing the final mine excavation plan with the
spike filter disabled and enabled for the same raw captured world and mining
request. Run the complete ordinary pipeline in both branches, including ore
quality and bottom-flattening states selected for that experiment,
connectivity, corner smoothing, and comparable safety stages. Do not score the
filter from the number of detected spikes or from its intermediate ore
interpretation. A one-column correction can change the target profile of an
entire designation and its neighbors, so only the final plans represent the
player-facing effect.

For every captured terrain column, derive the excavation interval in each
branch from the current surface and the final interpolated target surface. Use
the raw captured material layers to classify the difference; never use the
filtered ore interpretation as the scoring truth.

The two primary measures are:

- **Avoided waste-rock excavation**: mineable rock volume in the excavation
  interval present with the filter disabled but absent with it enabled. More
  is better.
- **Foregone target product**: selected target-product volume in that same
  avoided interval. Less is better. This is ore omitted by the generated mine
  plan, even though the material still exists and could be designated later.
  Report each selected product separately as well as their total.

Express material volume as captured layer thickness multiplied by one terrain
tile's area. Captain of Industry mines `Bedrock_Terrain` indefinitely below the
stored layers toward the designation target. It produces the normal Rock
product with a 200% material multiplier versus ordinary rock's 80%, so count
bedrock below its captured top and convert each material with its own live
yield before combining products. Report avoided dirt, other waste, and
foregone non-target useful products as secondary material totals.

The v0.8.0 qualification measured up to 99% less bedrock excavation locally
and 36% across entire designation plans. The strongest whole-plan capture
prevented an estimated 4.79 million Rock while changing estimated target ore
by about 0.002%.

Do not choose a candidate from one blended score. Plot and compare the Pareto
frontier of avoided waste rock against foregone target product. Also report:

- avoided waste rock per unit of foregone target product, with zero-loss cases
  labelled explicitly rather than represented as infinity;
- waste-rock reduction relative to the unfiltered plan;
- target-product retention relative to the unfiltered plan;
- excavation added by the filtered plan but absent from the unfiltered plan;
- affected designation count, maximum floor raise, and area of influence; and
- exact geometry outside the correction neighborhood, which should remain
  unchanged.

Select and compare the spike detector initially with **Ore quality Off** and
**Bottom flattening Off**. This isolates the correction itself while retaining
designation aggregation and corner smoothing. After selecting candidates,
qualify their composition with every ore-quality level and with bottom
flattening enabled. Ore omitted by the bottom-density criterion in that later
phase belongs to ordinary ore-quality behavior, not the detector's direct
product cost.

Show results per replay case and as aggregate totals. Whole-deposit percentages
can hide a severe local loss, while aggregate totals can let one large deposit
dominate, so retain both local and per-case results. Use the characterized spike
captures as positive cases and add legitimate steep edges, thin seams, and
separated seams as negative controls before selecting a default algorithm or
threshold.

The detailed counterfactual calculation and proposed laboratory report are in
[the evaluation protocol](../../test/ore-spike-filter-evaluation.md).

The first parameter sweep is recorded in
[prototype experiment 01](../../test/ore-spike-filter-prototype-01.md).
