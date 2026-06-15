// Data/ApplicationDbContext.cs
using ImsAgency.Web.Models.Identity;
using ImsAgency.Web.Models.IMS;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ImsAgency.Web.Data
{
    // IdentityDbContext<ApplicationUser> gives us AspNetUsers, AspNetRoles,
    // AspNetUserRoles, etc. for FREE — we don't need to define those tables.
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // ================================================================
        // MASTER DATA TABLES
        // ================================================================
        public DbSet<Insurer> Insurers => Set<Insurer>();
        public DbSet<PolicyClass> PolicyClasses => Set<PolicyClass>();
        public DbSet<NcdRate> NcdRates => Set<NcdRate>();
        public DbSet<VehicleMake> VehicleMakes => Set<VehicleMake>();

        // ================================================================
        // CLIENT TABLES
        // ================================================================
        public DbSet<Client> Clients => Set<Client>();
        public DbSet<ClientPhone> ClientPhones => Set<ClientPhone>();
        public DbSet<ClientAddress> ClientAddresses => Set<ClientAddress>();

        // ================================================================
        // VEHICLE TABLE
        // ================================================================
        public DbSet<Vehicle> Vehicles => Set<Vehicle>();

        // ================================================================
        // POLICY TABLES
        // ================================================================
        public DbSet<Policy> Policies => Set<Policy>();
        public DbSet<PremiumLedger> PremiumLedgers => Set<PremiumLedger>();
        public DbSet<PolicyGroupEmployee> PolicyGroupEmployees => Set<PolicyGroupEmployee>();

        // ================================================================
        // PAYMENTS & RENEWALS
        // ================================================================
        public DbSet<CustomerPaymentLedger> CustomerPaymentLedgers => Set<CustomerPaymentLedger>();
        public DbSet<RenewalNotice> RenewalNotices => Set<RenewalNotice>();

        // ================================================================
        // CLAIMS
        // ================================================================
        public DbSet<Claim> Claims => Set<Claim>();
        public DbSet<ClaimDocument> ClaimDocuments => Set<ClaimDocument>();

        // ================================================================
        // STAFF / COMPLIANCE / AUDIT
        // ================================================================
        public DbSet<AgentProfile> AgentProfiles => Set<AgentProfile>();
        public DbSet<LhdnEInvoiceRecord> LhdnEInvoiceRecords => Set<LhdnEInvoiceRecord>();
        public DbSet<AuditSessionLog> AuditSessionLogs => Set<AuditSessionLog>();

        // ================================================================
        // MODEL CONFIGURATION
        // ================================================================
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder); // IMPORTANT: keeps Identity's own table config

            // ================================================================
            // SECTION 1 — EXPLICIT TABLE NAMES (PascalCase, plural)
            // ================================================================
            modelBuilder.Entity<Insurer>().ToTable("Insurers");
            modelBuilder.Entity<PolicyClass>().ToTable("PolicyClasses");
            modelBuilder.Entity<NcdRate>().ToTable("NcdRates");
            modelBuilder.Entity<VehicleMake>().ToTable("VehicleMakes");

            modelBuilder.Entity<Client>().ToTable("Clients");
            modelBuilder.Entity<ClientPhone>().ToTable("ClientPhones");
            modelBuilder.Entity<ClientAddress>().ToTable("ClientAddresses");

            modelBuilder.Entity<Vehicle>().ToTable("Vehicles");

            modelBuilder.Entity<Policy>().ToTable("Policies");
            modelBuilder.Entity<PremiumLedger>().ToTable("PremiumLedgers");
            modelBuilder.Entity<PolicyGroupEmployee>().ToTable("PolicyGroupEmployees");

            modelBuilder.Entity<CustomerPaymentLedger>().ToTable("CustomerPaymentLedgers");
            modelBuilder.Entity<RenewalNotice>().ToTable("RenewalNotices");

            modelBuilder.Entity<Claim>().ToTable("Claims");
            modelBuilder.Entity<ClaimDocument>().ToTable("ClaimDocuments");

            modelBuilder.Entity<AgentProfile>().ToTable("AgentProfiles");
            modelBuilder.Entity<LhdnEInvoiceRecord>().ToTable("LhdnEInvoiceRecords");
            modelBuilder.Entity<AuditSessionLog>().ToTable("AuditSessionLogs");


            // ================================================================
            // SECTION 2 — ALTERNATE KEYS
            // (required for string-based FK relationships further below)
            // ================================================================
            modelBuilder.Entity<PolicyClass>()
                .HasAlternateKey(pc => pc.ClassCode);

            modelBuilder.Entity<AgentProfile>()
                .HasAlternateKey(a => a.AgentCode);


            // ================================================================
            // SECTION 3 — UNIQUE INDEXES (business keys)
            // ================================================================
            modelBuilder.Entity<Insurer>()
                .HasIndex(i => i.InsurerCode)
                .IsUnique();

            modelBuilder.Entity<Vehicle>()
                .HasIndex(v => v.RegistrationNumber)
                .IsUnique();

            modelBuilder.Entity<Policy>()
                .HasIndex(p => p.CoverNoteNumber)
                .IsUnique();

            modelBuilder.Entity<Client>()
                .HasIndex(c => c.ClientCode)
                .IsUnique();

            modelBuilder.Entity<CustomerPaymentLedger>()
                .HasIndex(c => c.ReceiptNumber)
                .IsUnique();

            modelBuilder.Entity<Claim>()
                .HasIndex(c => c.ClaimReferenceNumber)
                .IsUnique();

            modelBuilder.Entity<NcdRate>()
                .HasIndex(n => new { n.ClaimFreeYears, n.EffectiveYear })
                .IsUnique();


            // ================================================================
            // SECTION 4 — PERFORMANCE INDEXES
            // ================================================================
            // Renewal Pipeline (RenewalController) constantly filters by
            // PolicyStatus = "Active" AND ExpiryDate BETWEEN ... AND ...
            modelBuilder.Entity<Policy>()
                .HasIndex(p => new { p.PolicyStatus, p.ExpiryDate });


            // ================================================================
            // SECTION 5 — ONE-TO-ONE RELATIONSHIPS
            // ================================================================

            // ---- Policy <-> PremiumLedger ----
            // PolicyId is BOTH the primary key AND foreign key of PremiumLedger
            modelBuilder.Entity<PremiumLedger>()
                .HasKey(pl => pl.PolicyId);

            modelBuilder.Entity<PremiumLedger>()
                .HasOne(pl => pl.Policy)
                .WithOne(p => p.PremiumLedger!)
                .HasForeignKey<PremiumLedger>(pl => pl.PolicyId);

            // ---- Policy <-> LhdnEInvoiceRecord ----
            modelBuilder.Entity<LhdnEInvoiceRecord>()
                .HasKey(l => l.PolicyId);

            modelBuilder.Entity<LhdnEInvoiceRecord>()
                .HasOne(l => l.Policy)
                .WithOne(p => p.LhdnEInvoiceRecord!)
                .HasForeignKey<LhdnEInvoiceRecord>(l => l.PolicyId);


            // ================================================================
            // SECTION 6 — FOREIGN KEYS WITH NON-DEFAULT DELETE BEHAVIOR
            // (these prevent SQL Server's "multiple cascade paths" errors)
            // ================================================================

            // ---- Policy.PreviousPolicyId (self-referencing, Renewal Clone) ----
            modelBuilder.Entity<Policy>()
                .HasOne<Policy>()
                .WithMany()
                .HasForeignKey(p => p.PreviousPolicyId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---- CustomerPaymentLedger -> Policy ----
            // Client -> CustomerPaymentLedger already cascades; if Policy -> CustomerPaymentLedger
            // also cascaded, deleting a Client would create two paths to the same row.
            modelBuilder.Entity<CustomerPaymentLedger>()
                .HasOne(c => c.Policy)
                .WithMany()
                .HasForeignKey(c => c.PolicyId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---- Policy -> PolicyClass (via ClassCode alternate key) ----
            modelBuilder.Entity<Policy>()
                .HasOne(p => p.PolicyClass)
                .WithMany(pc => pc.Policies)
                .HasForeignKey(p => p.PolicyClassCode)
                .HasPrincipalKey(pc => pc.ClassCode)
                .OnDelete(DeleteBehavior.Restrict);

            // ---- Policy -> AgentProfile (via AgentCode alternate key, nullable FK) ----
            modelBuilder.Entity<Policy>()
                .HasOne(p => p.Agent)
                .WithMany(a => a.Policies)
                .HasForeignKey(p => p.AgentCode)
                .HasPrincipalKey(a => a.AgentCode)
                .OnDelete(DeleteBehavior.SetNull);


            // ================================================================
            // SECTION 7 — IDENTITY ROLE SEEDING
            // (Admin, SeniorAgent, Agent, Support)
            // ================================================================
            modelBuilder.Entity<IdentityRole>().HasData(
                new IdentityRole
                {
                    Id = "1d8f1f10-0001-4a1a-9c10-aaaaaaaaaaaa",
                    Name = "Admin",
                    NormalizedName = "ADMIN",
                    ConcurrencyStamp = "1d8f1f10-0001-4a1a-9c10-aaaaaaaaaaaa"
                },
                new IdentityRole
                {
                    Id = "1d8f1f10-0002-4a1a-9c10-bbbbbbbbbbbb",
                    Name = "SeniorAgent",
                    NormalizedName = "SENIORAGENT",
                    ConcurrencyStamp = "1d8f1f10-0002-4a1a-9c10-bbbbbbbbbbbb"
                },
                new IdentityRole
                {
                    Id = "1d8f1f10-0003-4a1a-9c10-cccccccccccc",
                    Name = "Agent",
                    NormalizedName = "AGENT",
                    ConcurrencyStamp = "1d8f1f10-0003-4a1a-9c10-cccccccccccc"
                },
                new IdentityRole
                {
                    Id = "1d8f1f10-0004-4a1a-9c10-dddddddddddd",
                    Name = "Support",
                    NormalizedName = "SUPPORT",
                    ConcurrencyStamp = "1d8f1f10-0004-4a1a-9c10-dddddddddddd"
                }
            );

            // ================================================================
            // SECTION 8 — MASTER DATA + SAMPLE SEED DATA
            // (Insurers, PolicyClasses, NcdRates, VehicleMakes, sample
            //  Clients/Vehicles/Policies/AgentProfiles)
            // Added in Step 1.4 — deferred for now as requested.
            // ================================================================
        }
    }
}