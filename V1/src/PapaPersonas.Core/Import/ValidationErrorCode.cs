namespace PapaPersonas.Core.Import;

public enum ValidationErrorCode
{
    MissingRequiredHeader = 1,
    UnknownHeader = 2,
    DuplicateSourceHeader = 3,
    CanonicalHeaderCollision = 4,
    MissingCuil = 5,
    InvalidCuilCharacters = 6,
    InvalidCuilLength = 7,
    DuplicateCuilInBatch = 8,
    InvalidDateValue = 9,
    InvalidSmallIntValue = 10,
    ImportRunStatusUpdateFailed = 11,
    SourceShapeMismatch = 12
}
