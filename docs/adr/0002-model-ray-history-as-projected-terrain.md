---
status: accepted
---

# Model ray history as projected terrain

Accessway feasibility and landscaping cost will query a unified projected-
terrain model instead of replaying chronological ray constraints. Existing
designation projections form the immutable base and each candidate route adds
a persistent path-local overlay. Per-tile cut and fill effects collapse to
their physically effective extrema, while post-termination safety spans remain
distinct from projected work. This preserves the modeled T3 rules, enables
height-aware ray merging and work credit, and replaces increasingly expensive
history scans with spatial lookup; T1/T2 generalization is deliberately
deferred until the T3 model is validated.
