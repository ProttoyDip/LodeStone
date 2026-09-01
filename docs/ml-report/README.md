# ML Report

## Runtime Decision

ML is a first-class Lodestone application feature. Runtime scoring is available through `IRiskModelPredictor` only when a quality-gated model, metadata file, and publication manifest are present and valid.

Current result is State B: runtime integration is complete, but no acceptable real-data model exists.

## Dataset

The v2 experiment used the official Open University Learning Analytics Dataset from UCI.

- Source URL: `https://archive.ics.uci.edu/static/public/349/open%2Buniversity%2Blearning%2Banalytics%2Bdataset.zip`
- Source SHA-256: `f2ed1902616c1fe8d2824d872c0b7d2d72be435bf0124d077044fe4be2c6d3e4`
- Dataset directory hash: `6049a6bc0295a92eb556a28a0fc6ab82b8a31aab716df723cb68218d62f2256e`

## Feature Policy

V2 uses only anchor-time behavioral information: recent/prior activity and click trends, inactivity streaks, assessment timing rates, course progress, and leakage-safe cohort-relative activity. It excludes demographics, grades, scores, final outcomes, journal text, counseling/session text, crisis-case text, peer-chat/forum text, and future activity.

## Split And Tuning

- Seed: `20260831`
- Train: 17,393 students, 505,179 rows
- Validation: 3,726 students, 108,709 rows
- Locked test: 3,729 students, 108,728 rows
- Student grouping prevents a student's observations from appearing in multiple partitions.
- FastTree and LightGBM candidates were tuned through grouped cross-validation inside training only.

## Fixed Gates

Publication requires all of:

- ROC AUC >= `0.70`
- Recall >= `0.70`
- Precision >= `0.30`

The best grouped-CV candidates reached ROC AUC around `0.748`, but precision remained around `0.05`. No validation candidate satisfied all gates, so the locked test partition was not evaluated.

## Publication Outcome

Report:

`src/Lodestone.ML/Reports/experiments/risk-model.v2.report.failed-withdrawal-28d-v2-20260831T161658356Z.json`

No runtime artifact was published:

- no `risk-model.zip`
- no `risk-model.metadata.json`
- no `risk-model.publication.json`
- `eligibleForRuntimeIntegration=false`
- `modelSha256` is empty

The correct runtime state is fail-closed and unavailable until a future candidate passes the fixed validation and locked-test gates.
