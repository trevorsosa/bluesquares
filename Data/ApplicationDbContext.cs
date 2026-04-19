using Microsoft.EntityFrameworkCore;
using BlueSquares.Models;

namespace BlueSquares.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Merchant> Merchants { get; set; }
    public DbSet<Client> Clients { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<InvoiceLineItem> InvoiceLineItems { get; set; }
    public DbSet<InvoiceQuery> InvoiceQueries { get; set; }
    public DbSet<EmailSubscriber> EmailSubscribers { get; set; }
    public DbSet<ReminderSchedule> ReminderSchedules { get; set; }
    public DbSet<RecurringInvoiceSchedule> RecurringInvoiceSchedules { get; set; }
    public DbSet<RecurringInvoiceLineItem> RecurringInvoiceLineItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Merchant configuration
        modelBuilder.Entity<Merchant>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FirebaseUid).IsUnique();
            entity.HasIndex(e => e.Email);
            entity.Property(e => e.Currency).HasMaxLength(3);
            entity.Property(e => e.Country).HasMaxLength(2);
            entity.Property(e => e.QuickBooksEnvironment).HasMaxLength(20);
        });

        // Client configuration
        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MerchantId, e.WhatsAppNumber });
            entity.HasOne(e => e.Merchant)
                .WithMany(m => m.Clients)
                .HasForeignKey(e => e.MerchantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Invoice configuration
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.InvoiceNumber);
            entity.HasIndex(e => new { e.MerchantId, e.Status });
            entity.HasIndex(e => e.DueDate);
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
            entity.Property(e => e.Currency).HasMaxLength(3);
            
            entity.HasOne(e => e.Merchant)
                .WithMany(m => m.Invoices)
                .HasForeignKey(e => e.MerchantId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne(e => e.Client)
                .WithMany(c => c.Invoices)
                .HasForeignKey(e => e.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.RecurringInvoiceSchedule)
                .WithMany()
                .HasForeignKey(e => e.RecurringInvoiceScheduleId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // InvoiceLineItem configuration
        modelBuilder.Entity<InvoiceLineItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Quantity).HasPrecision(18, 2);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            
            entity.HasOne(e => e.Invoice)
                .WithMany(i => i.LineItems)
                .HasForeignKey(e => e.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // InvoiceQuery configuration
        modelBuilder.Entity<InvoiceQuery>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.InvoiceId, e.IsResolved });
            
            entity.HasOne(e => e.Invoice)
                .WithMany(i => i.Queries)
                .HasForeignKey(e => e.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // EmailSubscriber configuration
        modelBuilder.Entity<EmailSubscriber>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.CountryCode);
        });

        // ReminderSchedule configuration
        modelBuilder.Entity<ReminderSchedule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Merchant)
                .WithMany()
                .HasForeignKey(e => e.MerchantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecurringInvoiceSchedule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MerchantId, e.IsActive, e.NextRunDate });
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Frequency).HasMaxLength(20);
            entity.Property(e => e.Currency).HasMaxLength(3);

            entity.HasOne(e => e.Merchant)
                .WithMany(m => m.RecurringInvoiceSchedules)
                .HasForeignKey(e => e.MerchantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Client)
                .WithMany()
                .HasForeignKey(e => e.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.LastGeneratedInvoice)
                .WithMany()
                .HasForeignKey(e => e.LastGeneratedInvoiceId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RecurringInvoiceLineItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Quantity).HasPrecision(18, 2);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);

            entity.HasOne(e => e.RecurringInvoiceSchedule)
                .WithMany(s => s.LineItems)
                .HasForeignKey(e => e.RecurringInvoiceScheduleId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
