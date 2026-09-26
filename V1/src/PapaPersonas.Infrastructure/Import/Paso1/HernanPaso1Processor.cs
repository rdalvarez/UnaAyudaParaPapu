using PapaPersonas.Core.Import.Paso1;

namespace PapaPersonas.Infrastructure.Import.Paso1;

public sealed class HernanPaso1Processor
{
    private readonly HernanPreparationService _service;

    public HernanPaso1Processor()
    {
        _service = new HernanPreparationService(new ExcelDataReaderRowSource(), new CsvFileWriter());
    }

    public HernanPreparationResult Process(HernanPreparationRequest request)
    {
        return _service.Process(request);
    }
}
