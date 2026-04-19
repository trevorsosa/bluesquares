using BlueSquares.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BlueSquares.Services;

public class PdfService : IPdfService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<PdfService> _logger;

    public PdfService(ApplicationDbContext context, ILogger<PdfService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<byte[]> GenerateInvoicePdf(Guid invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Merchant)
            .Include(i => i.Client)
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null)
            throw new ArgumentException($"Invoice {invoiceId} not found");

        var m = invoice.Merchant;
        var c = invoice.Client;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                page.Content().Column(col =>
                {
                    // ── Header ──────────────────────────────────────────────
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(inner =>
                        {
                            inner.Item().Text(m.BusinessName)
                                .Bold().FontSize(20).FontColor(Color.FromHex("#0066CC"));
                            if (!string.IsNullOrEmpty(m.Address))
                                inner.Item().Text(m.Address).FontColor(Colors.Grey.Darken2);
                            if (!string.IsNullOrEmpty(m.ContactNumber))
                                inner.Item().Text(m.ContactNumber).FontColor(Colors.Grey.Darken2);
                        });

                        row.ConstantItem(180).Column(inner =>
                        {
                            inner.Item().AlignRight().Text("INVOICE")
                                .Bold().FontSize(24).FontColor(Color.FromHex("#0066CC"));
                            inner.Item().AlignRight().Text($"#{invoice.InvoiceNumber}").Bold();
                            inner.Item().AlignRight()
                                .Text($"Date: {invoice.InvoiceDate:dd MMM yyyy}")
                                .FontColor(Colors.Grey.Darken2);
                            inner.Item().AlignRight()
                                .Text($"Due: {invoice.DueDate:dd MMM yyyy}")
                                .FontColor(Colors.Grey.Darken2);
                            if (invoice.Status == "Paid")
                                inner.Item().AlignRight().PaddingTop(4)
                                    .Text("✓ PAID")
                                    .Bold().FontColor(Color.FromHex("#28a745"));
                        });
                    });

                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Color.FromHex("#0066CC"));

                    // ── Bill To ──────────────────────────────────────────────
                    col.Item().PaddingTop(16).Column(inner =>
                    {
                        inner.Item().Text("Bill To:").Bold().FontColor(Colors.Grey.Darken1);
                        inner.Item().Text(c.Name).Bold();
                        if (!string.IsNullOrEmpty(c.CompanyName))
                            inner.Item().Text(c.CompanyName);
                        if (!string.IsNullOrEmpty(c.Email))
                            inner.Item().Text(c.Email).FontColor(Colors.Grey.Darken2);
                    });

                    // ── Line Items ────────────────────────────────────────────
                    col.Item().PaddingTop(20).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(5);
                            cols.RelativeColumn(1.5f);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(2);
                        });

                        // Header row
                        static IContainer HeaderCell(IContainer c) =>
                            c.Background(Color.FromHex("#0066CC")).Padding(6);

                        table.Header(h =>
                        {
                            h.Cell().Element(HeaderCell)
                                .Text("Description").Bold().FontColor(Colors.White);
                            h.Cell().Element(HeaderCell)
                                .Text("Qty").Bold().FontColor(Colors.White);
                            h.Cell().Element(HeaderCell).AlignRight()
                                .Text("Unit Price").Bold().FontColor(Colors.White);
                            h.Cell().Element(HeaderCell).AlignRight()
                                .Text("Total").Bold().FontColor(Colors.White);
                        });

                        var rowIndex = 0;
                        foreach (var item in invoice.LineItems)
                        {
                            var bg = rowIndex++ % 2 == 0 ? Colors.White : Color.FromHex("#f5f8ff");

                            static IContainer DataCell(IContainer c, string bg) =>
                                c.Background(bg).BorderBottom(1).BorderColor(Color.FromHex("#e0e0e0")).Padding(6);

                            table.Cell().Element(c => DataCell(c, bg))
                                .Text(item.Description);
                            table.Cell().Element(c => DataCell(c, bg))
                                .Text(item.Quantity.ToString("G29"));
                            table.Cell().Element(c => DataCell(c, bg)).AlignRight()
                                .Text(FormatCurrency(item.UnitPrice, invoice.Currency));
                            table.Cell().Element(c => DataCell(c, bg)).AlignRight()
                                .Text(FormatCurrency(item.Total, invoice.Currency));
                        }
                    });

                    // ── Total ─────────────────────────────────────────────────
                    col.Item().AlignRight().PaddingTop(10).PaddingRight(0).Column(inner =>
                    {
                        inner.Item().BorderTop(2).BorderColor(Color.FromHex("#0066CC"))
                            .PaddingTop(6).Row(row =>
                            {
                                row.RelativeItem().Text("Total Due:").Bold().FontSize(13);
                                row.ConstantItem(120).AlignRight()
                                    .Text(FormatCurrency(invoice.TotalAmount, invoice.Currency))
                                    .Bold().FontSize(13).FontColor(Color.FromHex("#0066CC"));
                            });

                        if (invoice.Status == "Paid" && invoice.PaidDate.HasValue)
                        {
                            inner.Item().PaddingTop(4).Row(row =>
                            {
                                row.RelativeItem().Text("Paid on:").FontColor(Colors.Grey.Darken2);
                                row.ConstantItem(120).AlignRight()
                                    .Text($"{invoice.PaidDate.Value:dd MMM yyyy}")
                                    .FontColor(Colors.Grey.Darken2);
                            });
                        }
                    });

                    // ── Payment Instructions ──────────────────────────────────
                    if (!string.IsNullOrEmpty(m.BankName) && invoice.Status != "Paid")
                    {
                        col.Item().PaddingTop(24).Column(inner =>
                        {
                            inner.Item().Text("Payment Instructions").Bold()
                                .FontColor(Color.FromHex("#0066CC"));
                            inner.Item().PaddingTop(4).Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(110);
                                    c.RelativeColumn();
                                });

                                void Row(string label, string? value)
                                {
                                    if (string.IsNullOrEmpty(value)) return;
                                    t.Cell().Padding(3).Text(label).Bold()
                                        .FontColor(Colors.Grey.Darken1);
                                    t.Cell().Padding(3).Text(value);
                                }

                                Row("Bank:", m.BankName);
                                Row("Account:", m.AccountNumber);
                                Row("Branch Code:", m.BranchCode);
                                Row("Account Name:", m.AccountHolderName);
                                Row("Reference:", invoice.PaymentRefCode);
                            });
                        });
                    }

                    // ── Notes ────────────────────────────────────────────────
                    if (!string.IsNullOrEmpty(invoice.Notes))
                    {
                        col.Item().PaddingTop(16).Column(inner =>
                        {
                            inner.Item().Text("Notes").Bold().FontColor(Colors.Grey.Darken1);
                            inner.Item().PaddingTop(2).Text(invoice.Notes)
                                .FontColor(Colors.Grey.Darken2);
                        });
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Powered by BlueSquares · squares.blue")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            });
        });

        return doc.GeneratePdf();
    }

    public async Task<byte[]> GenerateReceiptPdf(Guid invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Merchant)
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null)
            throw new ArgumentException($"Invoice {invoiceId} not found");

        if (invoice.Status != "Paid")
            throw new InvalidOperationException("Invoice is not paid — cannot generate receipt.");

        var m = invoice.Merchant;
        var c = invoice.Client;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                page.Content().Column(col =>
                {
                    // ── Header ──────────────────────────────────────────────
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(inner =>
                        {
                            inner.Item().Text(m.BusinessName)
                                .Bold().FontSize(20).FontColor(Color.FromHex("#0066CC"));
                            if (!string.IsNullOrEmpty(m.Address))
                                inner.Item().Text(m.Address).FontColor(Colors.Grey.Darken2);
                            if (!string.IsNullOrEmpty(m.ContactNumber))
                                inner.Item().Text(m.ContactNumber).FontColor(Colors.Grey.Darken2);
                        });

                        row.ConstantItem(180).Column(inner =>
                        {
                            inner.Item().AlignRight().Text("RECEIPT")
                                .Bold().FontSize(24).FontColor(Color.FromHex("#28a745"));
                            inner.Item().AlignRight().Text($"Invoice #{invoice.InvoiceNumber}").Bold();
                            inner.Item().AlignRight()
                                .Text($"Paid: {invoice.PaidDate:dd MMM yyyy}")
                                .FontColor(Colors.Grey.Darken2);
                        });
                    });

                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Color.FromHex("#28a745"));

                    // ── Receipt Details ───────────────────────────────────────
                    col.Item().PaddingTop(20)
                        .Background(Color.FromHex("#f0fff4"))
                        .Border(1).BorderColor(Color.FromHex("#28a745"))
                        .Padding(16)
                        .Column(inner =>
                        {
                            inner.Item().AlignCenter().Text("✓ Payment Confirmed")
                                .Bold().FontSize(16).FontColor(Color.FromHex("#28a745"));
                            inner.Item().PaddingTop(12).Table(t =>
                            {
                                t.ColumnsDefinition(cols =>
                                {
                                    cols.ConstantColumn(140);
                                    cols.RelativeColumn();
                                });

                                void Row(string label, string? value)
                                {
                                    if (string.IsNullOrEmpty(value)) return;
                                    t.Cell().Padding(4).Text(label).Bold()
                                        .FontColor(Colors.Grey.Darken1);
                                    t.Cell().Padding(4).Text(value);
                                }

                                Row("Received from:", c.Name);
                                Row("Amount:", FormatCurrency(invoice.TotalAmount, invoice.Currency));
                                Row("Date paid:", invoice.PaidDate?.ToString("dd MMMM yyyy"));
                                Row("Payment method:", invoice.PaymentMethod);
                                Row("Reference:", invoice.PaymentReference ?? invoice.PaymentTransactionId);
                                Row("Invoice #:", invoice.InvoiceNumber);
                            });
                        });

                    // ── Thank you ─────────────────────────────────────────────
                    col.Item().PaddingTop(24).AlignCenter()
                        .Text("Thank you for your payment!")
                        .Bold().FontSize(13).FontColor(Colors.Grey.Darken2);

                    if (!string.IsNullOrEmpty(m.DefaultInvoiceFooter))
                    {
                        col.Item().PaddingTop(8).AlignCenter()
                            .Text(m.DefaultInvoiceFooter)
                            .FontColor(Colors.Grey.Darken1);
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Powered by BlueSquares · squares.blue")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            });
        });

        return doc.GeneratePdf();
    }

    private static string FormatCurrency(decimal amount, string currency) =>
        currency switch
        {
            "ZAR" => $"R {amount:N2}",
            "GBP" => $"£{amount:N2}",
            "EUR" => $"EUR {amount:N2}",
            _ => $"{currency} {amount:N2}"
        };
}
