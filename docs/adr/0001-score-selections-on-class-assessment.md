# Score selections live on ClassAssessment

ScoreSelection state is owned by `ClassAssessment` (the dataset-level
container) rather than by a ViewModel or a separate service.
ClassAssessment already owns dataset-level state (current GradeCutoffs,
SavedCutoffs, MuppetNameMap), so selections logically belong with the
dataset. Putting them there lets `StudentAssessment.RecalculateAggregate`
read selections directly without ViewModel coordination, and makes
persistence into `SavedState` a one-to-one mapping.
