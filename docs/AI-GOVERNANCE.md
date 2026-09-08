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

**Where it is shown.** Each open queue entry has a "Why this score" link (`/Counselor/Explain`).
`RiskExplanationService` rebuilds the model input from the scored snapshot, builds the baseline from
the median of the most recent snapshot per consenting student (at least five, or it declines), and
returns null — not an error — when the case is closed or the student has withdrawn consent. The
page shows the comparison group, each feature's measured movement in score points, the boundary
note, and the caveat that none of it is causal.

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

### 9a. Enablement decision (8 September 2026)

**Runtime scoring is enabled per environment, never by default.** The reasoning:

- The tracked default stays `MachineLearning:Enabled=false` because the model artifacts are not
  committed (`App_Data/ml/*` is git-ignored). Enabling by default would make every fresh clone,
  CI run and container report `/health/ready` unhealthy for want of a file it cannot have. The
  fail-closed behaviour is correct; the default must not fight it.
- An environment that holds all three verified artifacts turns scoring on explicitly — user-secrets
  or `MachineLearning__Enabled=true` locally, `LODESTONE_ML_ENABLED=true` in Docker. The project
  demo machine is enabled this way and `/health/ml` reports the v3 artifact.
- Scoring is enabled **knowing** the deployed threshold of `0.83` has a measured recall of 4.5%
  (`docs/ml-report/README.md` §6). That is accepted because the queue is a capacity-bounded
  triage aid, not a safety net, and the product's other channels — self-referral booking, peer
  support, crisis resources, forum triage — do not depend on it. A short, honest queue that a
  counselor can actually work through was judged better than a long one they cannot, and both
  choices are worse than pretending the number does not exist.
- Every score is explainable on demand (§5) and the fairness audit at this exact threshold is on
  record (§6). Nobody enabling this in another environment should do so without re-reading both.

Revisiting the threshold is expected once real counselor capacity is known. Lowering it raises
recall and widens the subgroup gaps the audit measured; both effects must be re-measured together.

## 10. What is deliberately not built

- **No self-harm classifier.** There is no labelled data for it, so its false-negative rate could
  not be measured — and the false negative is the catastrophic error. A category named "self-harm
  concern" would also imply a clinical judgement the system is not competent to make, and its
  absence would be read as reassurance. `ForumTriageRanker` closes the same gap without the claim:
  see section 11.
- **No generative student-facing chat.** The crisis path is the worst possible place for a
  confident wrong answer. The crisis page has a *retrieval-only* search (section 13) precisely so
  that the useful half of that idea ships without the dangerous half.
- **No language model in the product, local or hosted.** Report drafting (section 14) is a
  template filled from structured fields. Should a local model ever be introduced, it must run on
  the same machine as the application and this document must say which text it reads.
- **No third-party inference on student text.** Journal notes are encrypted at rest precisely so
  they are not casually readable; sending them to an external endpoint would undo that decision.

### 10a. Peer chat, and why it is now mapped

`PeerChatHub` was left unmapped until two conditions held: server-owned room membership and a
moderation model. Both now do.

- **Membership is a property of the `SupportRequest` row.** `PeerChatService.ResolveRoomAsync`
  admits exactly two people — the student who raised the request and the approved, active volunteer
  who accepted it — and the client can only name a request id, never a room. A non-participant and
  a non-existent request both receive `null`, so probing ids reveals nothing. Every send re-checks
  membership; a stale connection cannot keep speaking into a conversation it has left.
- **Sending is open only while the request is accepted.** Pending has nobody to talk to; completed
  and escalated are history.
- **The moderation model is human.** Every message is persisted as a `SupportInteraction` before it
  is broadcast, so the conversation a volunteer can escalate to a counselor is the same one that
  happened live. No classifier reads it, in keeping with the first bullet above.

`PeerChatServiceTests` pins the membership and status rules.

## 11. Forum moderation triage

`ForumTriageRanker` orders posts by how soon a moderator should read them. It exists because
reactive flagging cannot reach the post nobody engaged with — written, unanswered, unreported,
ageing away.

Signals are structural only: unreviewed community reports, no replies after 24 hours, a first-time
or infrequent author, a post far longer than that author's own median, and time unattended. Each is
a fact about a post's history that a moderator could have observed unaided.

Three properties are enforced rather than intended:

- **No category is ever assigned.** A test fails the build if any reason contains distress, crisis,
  self-harm, risk, concern, mental or suicide.
- **Reported posts outrank inferred ones structurally**, by tier rather than by weight, so tuning
  cannot let a derived signal outvote a human who explicitly asked for review.
- **Nothing is decided.** No status change, no hiding, no message to the author.

The upgrade path, should labelled and validated data ever exist, is a trained classifier reporting
its measured recall alongside its output. Until then the honest system is the one that orders a
queue without claiming to understand what is in it.

**Where it runs.** `ForumService.GetModerationQueueAsync` feeds the ranker every post with an
unreviewed report plus every visible post from the last fourteen days that no moderator has read.
Reported posts always enter the queue; unreported ones enter only above a surfacing threshold set at
the weight of the "no replies after 24 hours" signal, so an answered first post does not cost a
moderator's time. Reviewing a post — with either outcome — stamps `LastModeratorReviewAtUtc`, so a
post surfaced without a report stops resurfacing once a human has read it. `ForumModerationJob`
counts the same queue when it tells moderators there is a backlog.

## 12. Volunteer matching

`VolunteerMatcher` ranks volunteers for a support request on skill overlap, availability overlap and
current workload. It is local, deterministic and inspectable — word overlap and counts, no model and
no external service.

- **It recommends; it never assigns.** Matching a distressed student to a stranger is a judgement
  about two people, and the system lacks the context to make it.
- **Every match carries its reasons**, so an administrator can disagree with the order. A ranking
  nobody can audit is an instruction with a number attached.
- **The student's words are never quoted back.** The message is read to find matching skills and
  never reproduced in a match reason; a test enforces this.
- **Capacity is weighted small on purpose.** Spreading work protects volunteers from absorbing every
  request, but must never outrank being able to help.

**Where it runs.** `VolunteerSupportService.GetRequestRoutingAsync` finds pending requests whose
student has no active, approved volunteer — requests nobody can currently see — and ranks the
available volunteers for each. The administrator's routing page shows the top three with their
reasons; choosing one opens the ordinary assignment form with the student pre-selected. The DTO that
reaches the page has no `Message` field, so the student's words cannot be rendered there by accident.

## 13. Crisis resource retrieval

The crisis page has a search box. `CrisisResourceRetriever` is the retrieval half of a
retrieval-augmented design with the generation half deliberately absent.

- **Every result is an existing resource, verbatim.** Nothing is composed or paraphrased. The only
  text the retriever adds is the list of a resource's own words that matched.
- **Ranking is BM25 over title and description**, in-process and deterministic. A small
  query-expansion lexicon maps everyday words to resource vocabulary ("drinking" to "substance",
  "therapist" to "counselor"). Its values describe resources, not people.
- **It is not a classifier, and it cannot hide the way to help.** The page always renders the
  emergency resources regardless of the search. A poor match means the vocabulary fell short, and
  the page says so rather than implying nothing applies.
- **What a person types is never stored or logged.** The search is a POST so the text does not enter
  a URL, history entry or access log; it is used to rank, echoed into the person's own search box,
  and discarded.

`CrisisResourceRetrieverTests` checks that results are the stored resources, that no query word is
ever reported back as a reason, and that `CrisisResourceMatch` has no property that could carry a
category.

## 14. Session-note drafting

`SessionReportDrafter` offers a counselor an opening for their session note. It is a template, not a
language model.

- **Every line is a fact the booking system already holds** — when the session was, whether it
  happened, how many times this counselor has seen this student — **or a labelled blank.** The
  `SessionFacts` record has no string property, so there is no free text to echo.
- **The student's booking note is pointed at, not copied in.** A clinical record should be the
  counselor's account, and a pre-filled quotation is easy to leave in by accident.
- **The draft is never saved on its own.** It is offered only into an empty notes box, and
  `BookingService.RecordCounselorOutcomeAsync` rejects notes that still contain an unfilled `[ ]`,
  so an untouched template cannot become a record.

This is the deliberately modest answer to "AI report drafting": the useful part is structure and
recall of facts, and that part needs no model.

## 15. Where the rules live

| Rule | Enforced in |
| --- | --- |
| Feature set excludes protected attributes | `RiskFeatureSchemas`, `OuladDataLoader` |
| Explanations use honest language | `RiskFeatureVocabulary`, `RiskFeatureVocabularyTests` |
| Explanations make no causal claim | `AblationRiskExplainer.DescribeBoundary` |
| Explanations are never persisted | `IRiskModelExplainer` (on-demand only), `RiskExplanationService` |
| Explanations require live consent | `CounselorQueueRepository.GetExplanationContextAsync` |
| Moderation surfaces, never decides | `ForumModerationJob` |
| Escalation reaches staff, never students | `CrisisResourceEscalationJob` |
| Consent gates monitoring | `RiskMonitoringConsentService` |
| Publication gates | `ModelQualityGates`, `TrainingPipeline` |
| Subgroup performance | `FairnessAuditor`, `FairnessMetrics` |
| Peer chat membership is server-owned | `PeerChatService`, `PeerChatHub`, `PeerChatServiceTests` |
| Triage assigns no clinical category | `ForumTriageRanker`, `ForumTriageRankerTests` |
| Reported posts outrank inferred signals | `ForumTriageRanker.Rank` tier ordering |
| Surfaced posts stop resurfacing once read | `ForumService.ReviewPostAsync`, `ForumServiceModerationQueueTests` |
| Matching recommends, never assigns | `VolunteerMatcher`, `VolunteerMatcherTests` |
| Routing page never renders the student's message | `SupportRequestRoutingItemDto`, `VolunteerSupportServiceTests` |
| Crisis search returns resources verbatim, no category | `CrisisResourceRetriever`, `CrisisResourceRetrieverTests` |
| Emergency resources never filtered by search | `CrisisResourceController.BuildAsync` |
| Session drafts carry no free text and are never auto-saved | `SessionReportDrafter`, `BookingService.RecordCounselorOutcomeAsync`, `SessionReportDrafterTests` |
