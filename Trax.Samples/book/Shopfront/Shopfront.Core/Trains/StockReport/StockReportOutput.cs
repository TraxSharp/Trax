using Shopfront.Trains.StockReport.Models;

namespace Shopfront.Trains.StockReport;

public record StockReportOutput(DateTime TakenAt, int Units, List<StockLine> Lines);
