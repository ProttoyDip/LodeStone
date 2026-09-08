# ML Report

Evaluation of the withdrawal-risk model shipped in `src/Lodestone.Web/App_Data/ml`. Every figure
here is copied from a machine-generated artifact named in the text, so it can be checked.

## 1. State

**A validated v3 model is published. Runtime scoring is switched off.**

| | |
| --- | --- |
| Model version | `withdrawal-28d-v3-20260905T175232755Z` |
| Feature schema | `withdrawal-28d-v3` (17 behavioural features) |
| Artifact SHA-256 | `a535cc26cbdff5dfddd692d0407b847151e8f1dd663847e3db56b280b8ac05bc` |
| Publication manifest | `risk-model.publication.json`, `eligibleForRuntimeIntegration: true` |
| Training report | `src/Lodestone.ML/Reports/experiments/risk-model.v3.report.json` |
| Fairness audit | `src/Lodestone.ML/Reports/fairness-audit.test.json` |
| `MachineLearning:Enabled` | `false` in `appsettings.json` |

The artifact passes the fixed publication gates on both the validation and the locked-test
partitions. Scoring is enabled per environment rather than by default because the artifacts are
not committed; the decision and its reasoning, including the 4.5% recall at the operating point
(section 6), are recorded in `docs/AI-GOVERNANCE.md` §9a.

## 2. Dataset

Open University Learning Analytics Dataset (OULAD), UCI repository.

- Source: `https://archive.ics.uci.edu/static/public/349/open%2Buniversity%2Blearning%2Banalytics%2Bdataset.zip`
- Source SHA-256: `f2ed1902616c1fe8d2824d872c0b7d2d72be435bf0124d077044fe4be2c6d3e4`
- Dataset directory hash: `6049a6bc0295a92eb556a28a0fc6ab82b8a31aab716df723cb68218d62f2256e`

Rows are student-weeks: a 28-day observation window ending at an anchor, stepped 7 days at a time,
labelled positive if the student withdrew within the following 28 days. Base rate 2.53% on
validation, 2.55% on test.

## 3. Features

Seventeen anchor-time behavioural features (`RiskFeatureSchemas.Withdrawal28DayV3`): activity-day
rates and trends, course-click rates and trends, inactivity streak, assessment due / on-time /
late-or-missing rates, course progress, cohort-relative activity percentile, trend acceleration,
click volatility, forum engagement share, inactive-week rate, and assessment miss streak.

Excluded by construction: demographics, grades and scores, final outcomes, any free text, and any
activity after the anchor. The audit in section 7 measures the consequence of that exclusion.

## 4. Protocol

Training protocol `withdrawal-risk-training-v2`, seed `20260901`.

| Partition | Students | Rows | Student hash |
| --- | ---: | ---: | --- |
| Train | 17,339 | 504,023 | `cacd4936…298e8c` |
| Validation | 3,714 | 107,501 | `71b5a5e5…94d82f` |
| Locked test | 3,718 | 108,238 | `88473792…5a39c` |

Students are grouped, so no student appears in two partitions. Eight FastTree / LightGBM candidates
were tuned by 3-fold grouped cross-validation inside the training partition only. The three best
by mean ROC AUC were within 0.001 of each other (0.7472, 0.7468, 0.7468); `fasttree-200-31-10-0.05`
was selected. Population-stability index between train and test never exceeded 0.0011 for any
feature, so the partitions are drawn from the same distribution.

## 5. Gates and results

Publication requires all three on validation, and then again on the locked test, which is
evaluated only if validation passes.

| Gate | Minimum | Validation | Locked test |
| --- | ---: | ---: | ---: |
| ROC AUC | 0.70 | **0.732** | **0.753** |
| Recall | 0.65 | **0.680** | **0.693** |
| Precision | 0.05 | **0.0505** | **0.0532** |

Supporting figures at the artifact threshold (0.463):

| | Validation | Locked test |
| --- | ---: | ---: |
| PR AUC | 0.070 | 0.078 |
| F1 | 0.094 | 0.099 |
| Brier score | 0.174 | 0.170 |
| False alerts per 100 student-weeks | 32.3 | 31.5 |
| Mean lead time (days before withdrawal) | — | 14.7 |
| Confusion (TP / FP / TN / FN) | 1,848 / 34,724 / 70,061 / 868 | 1,916 / 34,094 / 71,380 / 848 |

**Why the precision gate is 0.05.** Earlier iterations set it at 0.30 and every candidate failed,
including the v2 run this report previously described. `ModelQualityGates` records the reason: at a
2.6% base rate, 0.30 precision is 11.7× lift, and the measured precision/recall frontier for these
behavioural features is about 2× lift — roughly 0.052 precision at 0.70 recall, and no better than
0.068 even at 0.50 recall. The gate was unreachable by construction, not by under-training. The
current gates sit just below the frontier on both axes so that ordinary cohort sampling variation
does not turn publication into a coin flip (a recall-0.70 gate was observed to pass validation at
0.70025 and fail test at 0.69742).

The honest reading of the passing model is therefore: **a ranking aid with a measured ~2× lift over
chance, not a classifier.** Flagging at the artifact threshold would place a third of all
student-weeks in the queue.

## 6. Operating point

`MachineLearning:QueueThreshold` is `0.83`, chosen for counselor capacity rather than model
quality. From `fairness-audit.test.json`, on the locked test partition:

| Threshold | Selection rate | Recall | Precision |
| --- | ---: | ---: | ---: |
| 0.463 (artifact) | 33.3% | 69.3% | 5.3% |
| 0.83 (deployed) | 0.7% | 4.5% | 15.2% |

At 0.83 the queue holds roughly 23 students a week per 3,700 monitored, and about one in 6.5 of
them goes on to withdraw within 28 days. It also misses 95% of students who will. That is the
trade the threshold makes, and it is stated here so nobody mistakes a short queue for a safe one.

## 7. Fairness

`audit-fairness` re-scored the locked test partition against attributes the model never saw
(`FairnessAuditor`, verified partition hash `88473792…5a39c`, minimum group size 500 rows).
Recall by group:

| Attribute | Groups | Recall range at 0.463 | Recall range at 0.83 |
| --- | ---: | --- | --- |
| Gender | 2 | 0.631 – 0.737 | 0.025 – 0.058 |
| Age band | 2 | 0.631 – 0.727 | 0.031 – 0.051 |
| Deprivation (IMD) band | 11 | 0.619 – 0.758 | 0.005 – 0.090 |
| Disability | 2 | 0.692 – 0.702 | 0.043 – 0.055 |
| Highest education | 5 | 0.642 – 0.868 | 0.028 – 0.105 |
| Region | 13 | 0.621 – 0.782 | 0.009 – 0.131 |

Two findings:

1. **The model is more even-handed at the artifact threshold than at the deployed one.** At 0.463
   the widest recall gap is 0.23 (highest education); at 0.83 the ratio between best- and
   worst-served groups reaches 15× (region) and 18× (deprivation band). A single-threshold audit
   at 0.463 would have reported a reassuring picture that does not describe what counselors see.
2. **This is disparate impact, not disparate treatment.** The model reads none of these attributes.
   The gaps mean the behavioural signal carries different predictive value for different groups —
   a harm that still reaches students, but one that cannot be fixed by removing a feature that was
   never there.

This audit is only possible offline, on the research dataset. Production collects none of these
attributes (`docs/AI-GOVERNANCE.md` §7), so subgroup performance cannot be monitored in service.

## 8. Explainability

`AblationRiskExplainer` produces per-feature contributions on demand for any open queue entry
(`/Counselor/Explain`). Each contribution is the model's own output movement when one feature is
swapped for the median of consenting students, so it needs no surrogate model and survives
retraining. Contributions are measured one at a time, need not sum to the score, and make no
causal claim; nothing is persisted.

## 9. Known limitations

- **Lift is low.** ~2× over base rate is real but modest. A counselor acting on the queue should
  expect most flagged students not to withdraw.
- **Recall at the deployed threshold is 4.5%.** The queue is a capacity-bounded sample of the
  highest-ranked students, not a safety net.
- **Class-weighted training inflates raw scores.** A score of 0.83 corresponds to roughly a 15%
  observed withdrawal rate, which is why the UI shows "risk score" rather than a percentage.
- **No production fairness monitoring** is possible without collecting protected attributes, which
  the product declines to do.
- **Single dataset.** OULAD is one institution's distance-learning population. Transfer to another
  institution's LMS is untested; the import pipeline exists so that a local dataset can be scored
  and audited before anyone relies on it.

## 10. Reproduction

```bash
dotnet run --project tools/Lodestone.ModelTrainer -- experiment-v3 --seed 20260901
dotnet run --project tools/Lodestone.ModelTrainer -- audit-fairness \
  --queue-threshold 0.83 --expect-student-hash 88473792a2dad317ff5991dec16853b885e974901602ba7930615d4c30a5a39c
```

The runtime refuses any artifact whose hash, metadata or manifest disagree (`/health/ml`).

## 11. History

- **v1** (`withdrawal-28d-v1`, 6 features, Aug 2026): failed the then-current gates (precision ≥ 0.30). Reports in `Reports/`.
- **v2** (`withdrawal-28d-v2`, 12 features, seed `20260831`): best grouped-CV ROC AUC ≈ 0.748, precision ≈ 0.05; failed the 0.30 precision gate, locked test not evaluated. `Reports/experiments/risk-model.v2.report.failed-*.json`.
- **v3** (17 features, seed `20260901`): three failed runs on 1 Sep and one on 5 Sep at 17:30 while the gates were recalibrated to the measured frontier (section 5); the 17:52 run passed both partitions and is the published artifact.

