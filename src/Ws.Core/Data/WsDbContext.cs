using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ws.Core.Domain;

namespace Ws.Core.Data;

public sealed class WsDbContext(DbContextOptions<WsDbContext> options) : DbContext(options)
{
    public DbSet<Salon> Salons => Set<Salon>();
    public DbSet<SalonHours> SalonHours => Set<SalonHours>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<PortfolioImage> PortfolioImages => Set<PortfolioImage>();
    public DbSet<WorkingHours> WorkingHours => Set<WorkingHours>();
    public DbSet<TimeOff> TimeOff => Set<TimeOff>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<ArtistService> ArtistServices => Set<ArtistService>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All times are stored as UTC; make sure they come back with Kind=Utc.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<decimal>().HavePrecision(10, 2);
        // Enums as text keep the DB readable and safe to reorder.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Salon>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
            e.Property(x => x.Currency).HasMaxLength(3);
            Localized(e, x => x.Address);
            Localized(e, x => x.About);
            Localized(e, x => x.Policies);
            e.HasMany(x => x.OpeningHours).WithOne().HasForeignKey(x => x.SalonId);
            e.HasMany(x => x.Rooms).WithOne().HasForeignKey(x => x.SalonId);
        });

        b.Entity<Artist>(e =>
        {
            e.HasIndex(x => x.SalonId);
            e.Property(x => x.Name).HasMaxLength(100);
            Localized(e, x => x.Bio);
            e.HasOne<Salon>().WithMany().HasForeignKey(x => x.SalonId);
            e.HasMany(x => x.Portfolio).WithOne().HasForeignKey(x => x.ArtistId);
            e.HasMany(x => x.WorkingHours).WithOne().HasForeignKey(x => x.ArtistId);
            e.HasMany(x => x.TimeOff).WithOne().HasForeignKey(x => x.ArtistId);
        });

        b.Entity<PortfolioImage>(e => Localized(e, x => x.Caption));
        b.Entity<TimeOff>().HasIndex(x => new { x.ArtistId, x.StartUtc });

        b.Entity<Service>(e =>
        {
            e.HasIndex(x => x.SalonId);
            Localized(e, x => x.Name);
            Localized(e, x => x.Description);
            e.HasOne<Salon>().WithMany().HasForeignKey(x => x.SalonId);
        });

        b.Entity<ArtistService>(e =>
        {
            e.HasKey(x => new { x.ArtistId, x.ServiceId });
            e.HasOne(x => x.Artist).WithMany(x => x.Services).HasForeignKey(x => x.ArtistId);
            e.HasOne(x => x.Service).WithMany(x => x.Artists).HasForeignKey(x => x.ServiceId);
        });

        b.Entity<Client>(e =>
        {
            e.HasIndex(x => new { x.SalonId, x.Phone });
            e.HasIndex(x => new { x.SalonId, x.TelegramUserId });
            e.HasOne<Salon>().WithMany().HasForeignKey(x => x.SalonId);
        });

        b.Entity<Booking>(e =>
        {
            e.HasIndex(x => new { x.SalonId, x.StartUtc });
            e.HasIndex(x => new { x.ArtistId, x.StartUtc });
            e.HasOne<Salon>().WithMany().HasForeignKey(x => x.SalonId);
            e.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId);
            e.HasOne(x => x.Artist).WithMany().HasForeignKey(x => x.ArtistId);
            e.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId);
            e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId);
            e.OwnsOne(x => x.Tattoo, t => t.ToJson());
        });

        b.Entity<Conversation>(e =>
        {
            e.HasIndex(x => new { x.SalonId, x.Channel, x.ExternalId });
            e.HasOne<Salon>().WithMany().HasForeignKey(x => x.SalonId);
            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId);
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ConversationId);
        });
    }

    private static void Localized<T>(EntityTypeBuilder<T> e, System.Linq.Expressions.Expression<Func<T, LocalizedText?>> property)
        where T : class =>
        e.OwnsOne(property, o => o.ToJson());

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
