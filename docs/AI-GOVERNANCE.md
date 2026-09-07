# AI Governance

How the machine-learning parts of Lodestone are constrained, what they are allowed to do, and what
they are deliberately prevented from doing.

This document describes decisions that are enforced in code. Where a rule is upheld by a specific
type or test, that file is named, so a claim here can be checked rather than taken on trust.

---

## 1. What the system predicts

One thing: the probability that a consenting student withdraws from a course presentation within
the next 28 days, given their platform behaviour over the previous 28 days.

It does not predict grades, mental-health status, crisis risk, or anything clinical. It is a
**support-routing signal**: its only sanctioned effect is to place a student in a queue that a human
counselor reads.

## 2. What the model is allowed to see

The published model reads **seventeen behavioural features** derived from VLE clickstream and
assessment timing. The complete list is in `RiskFeatureSchemas.Withdrawal28DayV3`.

Explicitly excluded from the feature set:

| Excluded | Why |
| --- | --- |
| Gender, age, deprivation index, disability, region, prior education | Protected attributes; never collected by the application at all |
| Grades and assessment scores | Outcome-adjacent; invites the model to launder academic judgement |
| Journal text | The student's private space, encrypted at rest |
| Forum, peer-chat and counselling text | Written in confidence to a person, not to a system |
| Crisis-case text | Clinical, and the highest-harm data in the product |
| Any post-anchor activity | Would leak the future into a prediction about the future |

`StudentProfile` has no demographic columns. This is a deliberate choice with a cost, discussed in
section 7.

## 3. Human-in-the-loop: what the system may never do on its own

These are product rules, enforced in code and stated in the source of each job that could otherwise
break them.

- **No automatic student contact.** Risk scoring never messages a student. Automatic risk-based
  nudges are disabled and require product approval to enable.
- **No automated moderation decisions.** `ForumModerationJob` surfaces a backlog to moderators and
  deliberately does not call `ReviewPostAsync`. Removing a distressed person's post without a human
  reading it is not a decision the system may make.
- **No automated crisis escalation to a student.** `CrisisResourceEscalationJob` raises overdue
  cases to staff, sends no student-identifying detail in the notification, and never contacts the
  student or emergency services.
- **No automated academic consequence.** Nothing here changes a grade, triggers discipline, or
  enters a student record.

Every one of these is a case where an intervention reaches a real person. A human decides all of
them.

## 4. Consent

Behavioural monitoring is **opt-in**. `RiskMonitoringConsent` records the decision against a
`PolicyVersion`, with `RiskMonitoringConsentHistory` retaining the trail.

Withdrawing consent disables monitoring and deletes the derived activity logs, feature snapshots,
risk scores and risk-queue records. A student-supplied LMS number stays an unverified claim until an
administrator approves it, so no one can attach monitoring to an identifier they do not own.

## 5. Explainability

A counselor acting on a score needs to be able to interrogate it. The system produces, on demand,
the measured influence of each feature on a single prediction.

**How it is computed.** `AblationRiskExplainer` replaces one feature with a reference value, holds
everything else still, and re-scores. The difference is that feature's contribution. These are the
model's own outputs — no surrogate model is fitted and nothing is approximated.

**What the comparison is.** A contribution is meaningless without a reference point, so
`RiskExplanationBaseline` carries both the reference values and a plain-language description of what
population they came from. The description travels with the explanation to the counselor.

**Language discipline.** `RiskFeatureVocabulary` holds one agreed phrase per feature, and every
phrase says exactly what was measured. `RecentActiveDayRate` is described as "share of days active
on the platform", never as *attendance* — a counselor told attendance fell will picture an empty
seat and act on it, and the number cannot support that. `RiskFeatureVocabularyTests` fails the build
if any phrase contains attendance, quiz, exam, grade, mark, lecture or class, and fails if a schema
gains a feature with no agreed phrasing.

**No causal claims.** The model learned association from observational data. "Clicking more will
keep this student enrolled" is not something it can support: both the behaviour and the outcome sit
downstream of whatever is happening in the student's life. Where the system describes what would
move a student across the threshold, it is phrased strictly as a property of the model's decision
boundary, and says so in the same sentence.

**Nothing is stored.** Explanations are computed on request and discarded. "Why we believe this
student may withdraw" is a stronger inference than the score, and persisting it would create a new
class of sensitive record needing its own consent basis, retention rule and access control. The cost
of recomputing is seventeen model calls, for a student the counselor is already authorized to see.

## 6. Fairness

`audit-fairness` measures how the published model performs across groups it was never trained on:
gender, age band, deprivation band, disability, prior education and region.

```bash
dotnet run --project tools/Lodestone.ModelTrainer -- audit-fairness \
  --queue-threshold 0.83 --expect-student-hash <testStudentHash from the training report>
```

Design decisions worth stating:

- **It measures disparate impact, not disparate treatment.** The model is shown no protected
  attribute. A gap means the behavioural signal carries different predictive value for different
  groups — still a harm that reaches a student, but not evidence the model is reading something it
  should not.
- **It audits every operating point**, not just the threshold stamped into the artifact. Fairness is
  a property of an operating point, and the deployed queue threshold decides whose name a counselor
  actually sees. The current model is measurably more even-handed at the artifact threshold than at
  the deployed one — a single-threshold audit would have missed that entirely.
- **It verifies the partition.** The audit recomputes the student hash and checks it against the
  training report, so it cannot quietly score a lookalike partition and report reassuring numbers.
- **It suppresses small groups.** Below a size floor, rates are unstable enough to mislead and a
  full breakdown starts to identify people. Sizes are disclosed; metrics are not.
- **Rows are student-weeks, not students.** One student contributes many correlated rows, so group
  sizes overstate independent evidence. Student counts are reported alongside.

**Reporting discipline.** Test gives figures comparable to the headline metrics, and auditing a
frozen artifact does not spend the partition the way selection would. That holds only while the
audit stays a report: the moment a fairness result changes the model, the test partition has
informed selection and is no longer honest for the model that results. Explore on validation; report
on test once.

## 7. The limitation this project accepts on purpose

**A deployment that records no protected attributes cannot measure its own bias.**

Not collecting demographics is good for privacy and removes any possibility of the model reading
them. It also means subgroup performance is unmeasurable in production — the audit can only run
offline, against the research dataset, before the model is trusted with anyone.

This is fairness-through-unawareness and it is a real trade-off, not an oversight. It is recorded
here because a system that cannot audit itself in production should say so plainly rather than let
the absence of bad news be mistaken for good news.

## 8. Model publication

No model reaches the runtime by being trained. It must pass fixed gates on a validation partition
before the locked test partition is evaluated at all, and both must pass before any artifact is
published. `ModelQualityGates` documents why each threshold sits where it does, in terms of the
measured precision/recall frontier and the 2.5% base rate.

Loading is fail-closed: availability requires a mutually bound model, metadata and accepted
publication manifest. There is no fallback artifact path and no hot reload, so a scheduled batch
cannot mix model versions.

## 9. Operating point

The threshold that decides who enters the counselor queue is a **capacity decision, not a property
of the model**, and is configured separately as `MachineLearning:QueueThreshold`.

The fairness audit measures the deployed operating point directly. Anyone changing this value should
re-run the audit and read the recall at the new threshold before deploying it.

## 10. What is deliberately not built

- **`PeerChatHub` is not mapped.** Live messaging needs server-owned room membership and a
  moderation model first. Without them a support conversation could reach someone who should not see
  it, which in a wellbeing product is the worst available bug.
- **No self-harm classifier.** There is no labelled data for it, so its false-negative rate could
  not be measured — and the false negative is the catastrophic error. A category named "self-harm
  concern" would also imply a clinical judgement the system is not competent to make, and its
  absence would be read as reassurance.
- **No generative student-facing chat.** The crisis path is the worst possible place for a
  confident wrong answer.
- **No third-party inference on student text.** Journal notes are encrypted at rest precisely so
  they are not casually readable; sending them to an external endpoint would undo that decision.

## 11. Where the rules live

| Rule | Enforced in |
| --- | --- |
| Feature set excludes protected attributes | `RiskFeatureSchemas`, `OuladDataLoader` |
| Explanations use honest language | `RiskFeatureVocabulary`, `RiskFeatureVocabularyTests` |
| Explanations make no causal claim | `AblationRiskExplainer.DescribeBoundary` |
| Explanations are never persisted | `IRiskModelExplainer` (on-demand only) |
| Moderation surfaces, never decides | `ForumModerationJob` |
| Escalation reaches staff, never students | `CrisisResourceEscalationJob` |
| Consent gates monitoring | `RiskMonitoringConsentService` |
| Publication gates | `ModelQualityGates`, `TrainingPipeline` |
| Subgroup performance | `FairnessAuditor`, `FairnessMetrics` |
| Peer chat stays unmapped | `Program.cs` endpoint section |
