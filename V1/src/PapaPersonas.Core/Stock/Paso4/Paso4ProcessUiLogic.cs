using System.Globalization;

namespace PapaPersonas.Core.Stock.Paso4;

public static class Paso4ProcessUiLogic
{
    public static string FormatDateIso(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static string ToDisplayValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "(Vacío)"
            : value.Trim();
    }

    /// <summary>Convierte cantidades ingresadas en solicitudes normalizadas y rechaza valores inválidos.</summary>
    public static IReadOnlyList<Paso4ExtractionGroupQuantityRequest> BuildGroupRequests(
        IReadOnlyList<Paso4QuantityInput> rows,
        out string? validationMessage)
    {
        validationMessage = null;
        var result = new List<Paso4ExtractionGroupQuantityRequest>();

        foreach (var row in rows)
        {
            var text = row.QuantityText?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) || quantity < 0)
            {
                validationMessage = "Las cantidades deben ser números enteros no negativos.";
                return [];
            }

            if (quantity == 0)
            {
                continue;
            }

            result.Add(new Paso4ExtractionGroupQuantityRequest(row.NormalizedCodigoObraSocial, row.NormalizedObraSocial, quantity));
        }

        return result;
    }

    public static Paso4RequestTotals ComputeRequestTotals(IReadOnlyList<Paso4QuantityInput> rows)
    {
        var groups = 0;
        var total = 0;

        foreach (var row in rows)
        {
            if (!int.TryParse(row.QuantityText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity))
            {
                continue;
            }

            if (quantity <= 0)
            {
                continue;
            }

            groups++;
            total += quantity;
        }

        return new Paso4RequestTotals(groups, total);
    }

    public static string BuildExtractionConfirmationMessage(Paso4RequestTotals totals)
    {
        var peopleVerb = totals.TotalPeopleRequested == 1 ? "Se extraerá" : "Se extraerán";
        var peopleNoun = totals.TotalPeopleRequested == 1 ? "persona" : "personas";
        var groupNoun = totals.SelectedGroups == 1 ? "grupo" : "grupos";

        return $"{peopleVerb} {totals.TotalPeopleRequested} {peopleNoun} de {totals.SelectedGroups} {groupNoun}. ¿Desea continuar?";
    }

    public static string FormatRowCount(int rowCount)
    {
        return rowCount == 1 ? "1 fila" : $"{rowCount} filas";
    }

    public static IReadOnlyList<Paso4QuantityInput> ClearQuantities(IReadOnlyList<Paso4QuantityInput> rows)
    {
        return rows
            .Select(row => row with { QuantityText = string.Empty })
            .ToArray();
    }

    /// <summary>Determina qué acciones de stock puede habilitar la interfaz según actividad, pendientes y disponibilidad.</summary>
    public static Paso4ControlState EvaluateControlState(bool isBusy, bool hasPending, bool hasStock)
    {
        var canMutate = !isBusy && !hasPending;
        return new Paso4ControlState(
            CanGenerateOrRegenerate: canMutate,
            CanExtract: canMutate && hasStock,
            CanRetryOrCancelPending: !isBusy && hasPending,
            CanReadOnlyExport: !isBusy && hasStock);
    }

    public static string BuildSuggestedFileName(string prefix, DateOnly sourceDate, DateTime nowLocal, bool includeTimestamp = false)
    {
        var datePart = sourceDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        if (!includeTimestamp)
        {
            return $"{prefix}_{datePart}.csv";
        }

        var timePart = nowLocal.ToString("HHmmss", CultureInfo.InvariantCulture);
        return $"{prefix}_{datePart}_{timePart}.csv";
    }

    public static string BuildPreflightValidationMessage(Paso4ExtractionPreflightResult preflight)
    {
        return preflight.Status switch
        {
            Paso4ExtractionPreflightStatus.NoStock => "No hay stock generado para extraer.",
            Paso4ExtractionPreflightStatus.Pending => "Existe una extracción pendiente. Resolvela antes de iniciar una nueva extracción.",
            Paso4ExtractionPreflightStatus.InvalidRequest => "La solicitud de extracción es inválida. Verificá cantidades y grupos sin duplicados.",
            Paso4ExtractionPreflightStatus.GroupNotFound => $"El grupo solicitado ({ToDisplayValue(preflight.NormalizedCodigoObraSocial)} / {ToDisplayValue(preflight.NormalizedObraSocial)}) no existe en el stock actual.",
            Paso4ExtractionPreflightStatus.InsufficientStock => $"Stock insuficiente para el grupo ({ToDisplayValue(preflight.NormalizedCodigoObraSocial)} / {ToDisplayValue(preflight.NormalizedObraSocial)}): solicitadas {preflight.RequestedQuantity}, disponibles {preflight.AvailableQuantity}.",
            Paso4ExtractionPreflightStatus.Failed => "No se pudo validar la extracción en este momento. Reintentá.",
            _ => "No se pudo validar la extracción."
        };
    }

    public static string BuildGenerateStockErrorMessage(Paso4GenerateStockStatus status)
    {
        return status switch
        {
            Paso4GenerateStockStatus.ValidationFailed => "No se pudo generar/regenerar el stock por una validación operativa.",
            _ => "No se pudo generar/regenerar el stock en este momento."
        };
    }

    public static string BuildExportSummaryErrorMessage() => "No se pudo exportar el resumen de stock.";

    public static string BuildExportFullErrorMessage() => "No se pudo exportar el stock completo.";

    public static string BuildBeginExtractionErrorMessage(Paso4BeginExtractionStatus status)
    {
        return status switch
        {
            Paso4BeginExtractionStatus.ValidationFailed => "No se pudo completar la extracción por una validación operativa. Actualizá y reintentá.",
            _ => "No se pudo completar la extracción en este momento."
        };
    }

    public static string BuildRetryPendingErrorMessage(Paso4RecoveryStatus status)
    {
        return status switch
        {
            Paso4RecoveryStatus.ValidationFailed => "No se pudo reintentar la extracción pendiente por una validación operativa.",
            Paso4RecoveryStatus.Blocked => "No se pudo reintentar la extracción pendiente porque el estado está bloqueado.",
            _ => "No se pudo reintentar la extracción pendiente."
        };
    }

    public static string BuildCancelPendingErrorMessage(Paso4RecoveryStatus status)
    {
        return status switch
        {
            Paso4RecoveryStatus.ValidationFailed => "No se pudo cancelar la extracción pendiente por una validación operativa.",
            Paso4RecoveryStatus.Blocked => "No se pudo cancelar la extracción pendiente porque hay archivos o estado bloqueado.",
            _ => "No se pudo cancelar la extracción pendiente."
        };
    }

    public static string BuildReExportErrorMessage(Paso4RecoveryStatus status)
    {
        return status switch
        {
            Paso4RecoveryStatus.ValidationFailed => "No se pudo reexportar la extracción por una validación operativa.",
            _ => "No se pudo reexportar la extracción en este momento."
        };
    }
}

public sealed record Paso4QuantityInput(
    string NormalizedCodigoObraSocial,
    string NormalizedObraSocial,
    string? QuantityText);

public sealed record Paso4RequestTotals(int SelectedGroups, int TotalPeopleRequested);

public sealed record Paso4ControlState(
    bool CanGenerateOrRegenerate,
    bool CanExtract,
    bool CanRetryOrCancelPending,
    bool CanReadOnlyExport);
