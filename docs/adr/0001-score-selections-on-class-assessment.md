# Score selections live on ClassAssessment

ScoreSelection state is owned by `ClassAssessment` (the dataset-level
container) rather than by a ViewModel or a separate service.
ClassAssessment owns dataset-level state (DefaultCurve, SavedCutoffs,
MuppetNameMap), so selections logically belong with the dataset.
Putting them there lets `StudentAssessment.RecalculateAggregate` read
selections directly without ViewModel coordination, and makes
persistence into `SavedState` a one-to-one mapping.

(Live grading state — current cutoffs, current counts, enabled-grades
set — moved off `ClassAssessment` and onto the paired `GradingSession`
in issue #11 / slice 5 of #6 per ADR-0008. ScoreSelections remained on
`ClassAssessment` because they are dataset-level, not grading-state.)
