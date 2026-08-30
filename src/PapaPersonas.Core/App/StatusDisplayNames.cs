using PapaPersonas.Core.Import.Paso2;
using PapaPersonas.Core.Query.Paso3;
using PapaPersonas.Core.Stock.Paso4;

namespace PapaPersonas.Core.App;

public static class StatusDisplayNames
{
    public static string For(SergioStagePreviewStatus status) => status switch
    {
        SergioStagePreviewStatus.ReadyForConfirmation => "Lista para confirmar",
        SergioStagePreviewStatus.ValidationFailed => "Validación fallida",
        SergioStagePreviewStatus.Failed => "Fallida",
        SergioStagePreviewStatus.ConfirmationRequired => "Requiere confirmación",
        _ => "Desconocido"
    };

    public static string For(SergioApplyStatus status) => status switch
    {
        SergioApplyStatus.Completed => "Completada",
        SergioApplyStatus.ValidationFailed => "Validación fallida",
        SergioApplyStatus.Failed => "Fallida",
        _ => "Desconocido"
    };

    public static string For(Paso3ExportStatus status) => status switch
    {
        Paso3ExportStatus.Completed => "Completada",
        Paso3ExportStatus.Canceled => "Cancelada",
        Paso3ExportStatus.Failed => "Fallida",
        _ => "Desconocida"
    };

    public static string For(Paso4GenerateStockStatus status) => status switch
    {
        Paso4GenerateStockStatus.Completed => "Completado",
        Paso4GenerateStockStatus.ValidationFailed => "Validación fallida",
        Paso4GenerateStockStatus.Failed => "Fallido",
        _ => "Desconocido"
    };

    public static string For(Paso4RecoveryStatus status) => status switch
    {
        Paso4RecoveryStatus.Completed => "Completada",
        Paso4RecoveryStatus.ValidationFailed => "Validación fallida",
        Paso4RecoveryStatus.Blocked => "Bloqueada",
        Paso4RecoveryStatus.Failed => "Fallida",
        _ => "Desconocida"
    };
}
