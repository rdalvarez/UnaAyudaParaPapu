# process2-import-apply Specification

## Requirements

### Requirement: Import Date Entry and Validation

The system MUST parse at most one bounded `dd_MM_yyyy` token and prefill day/month/year fields when valid. Missing, ambiguous, incomplete, impossible, or future dates MUST block Analyze/Apply until corrected. Older dates SHOULD warn only.

#### Scenario: Prefill and manual override

- GIVEN one valid bounded token exists in filename
- WHEN the file is loaded
- THEN day/month/year fields are prefilled and editable

#### Scenario: Blocking vs warning

- GIVEN entered date is invalid/incomplete/future OR valid-but-old
- WHEN the operator runs Analyze or Apply
- THEN invalid/incomplete/future blocks the action
- AND valid-but-old warns and allows Analyze

### Requirement: Analyze Ownership and Apply Invalidation

Apply eligibility MUST bind to analyzed file + import date. If either changes after Analyze, ownership MUST be invalidated and Analyze required again.

#### Scenario: File/date edit after Analyze

- GIVEN Analyze succeeded for file A and date D
- WHEN file or date is edited
- THEN Apply is disabled for that context
- AND re-Analyze is required

### Requirement: Dual Date Writes on Apply

Apply MUST persist import date as `import_runs.fecha_importacion` and `personas.fecha_importacion`, and apply timestamp as `personas.fecha_actualizacion`.

#### Scenario: Apply persistence contract

- GIVEN Analyze ownership is valid
- WHEN Apply completes
- THEN affected rows contain import date and apply timestamp
- AND the run record contains the same import date
