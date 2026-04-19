namespace BlueSquares.Services;

public interface IWhatsAppService
{
    Task<bool> SendInvoiceMessage(Guid invoiceId);
    Task<bool> SendReminderMessage(Guid invoiceId);
    Task<bool> SendReceiptMessage(Guid invoiceId);
    Task<bool> SendStatementMessage(Guid clientId, string statementUrl);
    Task<bool> SendMerchantReply(Guid queryId, string message);
    Task ProcessIncomingMessage(string from, string message, string messageId);
}
