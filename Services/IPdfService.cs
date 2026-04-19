namespace BlueSquares.Services;

public interface IPdfService
{
    Task<byte[]> GenerateInvoicePdf(Guid invoiceId);
    Task<byte[]> GenerateReceiptPdf(Guid invoiceId);
}
