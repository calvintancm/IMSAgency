using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImsAgency.Web.Models.IMS
{
    // 1:1 with Policy — PolicyId is BOTH the primary key AND foreign key
    // (configured in ApplicationDbContext.OnModelCreating).
    //
    // All amounts here are STORED values, calculated client-side by the
    // real-time JS premium calculator (Feature 2) and then re-validated
    // server-side on save using the same formula — never trust the
    // browser's numbers alone for a financial record.
    //
    // ===== MALAYSIAN PREMIUM FORMULA (reference) =====
    //   GrossPremium      = Tariff Rate x Sum Insured / 100
    //   NcdDiscountAmount = GrossPremium x NcdPercentage / 100
    //   NetPremium        = GrossPremium - NcdDiscountAmount
    //   TotalAddonAmount  = Windscreen + SpecialPerils + NamedDriver + TotalLoss
    //   ServiceTaxAmount  = (NetPremium + TotalAddonAmount) x 8%
    //   StampDutyAmount   = RM10.00 (FIXED by Malaysian law)
    //   NetPremiumPayable = NetPremium + TotalAddonAmount + ServiceTaxAmount + StampDutyAmount
    // ==================================================
    public class PremiumLedger
    {
        // PK and FK -> Policies.PolicyId (no separate identity column)
        [Key]
        [ForeignKey(nameof(Policy))]
        public int PolicyId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SumInsuredAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal GrossPremium { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal NcdDiscountAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal NetPremium { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal AddonWindscreen { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal AddonSpecialPerils { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal AddonNamedDriver { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal AddonTotalLoss { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAddonAmount { get; set; } = 0;

        // Malaysian statutory stamp duty — always RM10.00, but kept as a
        // column (not a hardcoded constant) in case the law changes
        [Column(TypeName = "decimal(18,2)")]
        public decimal StampDutyAmount { get; set; } = 10.00m;

        // 8% Service Tax (SST) on (NetPremium + TotalAddonAmount)
        [Column(TypeName = "decimal(18,2)")]
        public decimal ServiceTaxAmount { get; set; }

        // Optional agency override of the agent's commission for this policy
        [Column(TypeName = "decimal(18,2)")]
        public decimal? AgentCommission { get; set; }

        // FINAL amount the client must pay
        [Column(TypeName = "decimal(18,2)")]
        public decimal NetPremiumPayable { get; set; }

        // ---- Navigation ----
        public Policy? Policy { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal AddonEvCharger { get; set; } = 0;


        // ---- Add this near NetPremiumPayable ----

        // Concurrency token — same purpose as Policy.RowVersion.
        [Timestamp]
        public byte[]? RowVersion { get; set; }
        public void RecalculateTotals()
        {
            // Sum of all 5 addons (Windscreen, SpecialPerils, NamedDriver,
            // TotalLoss, EvCharger)
            TotalAddonAmount = AddonWindscreen
                              + AddonSpecialPerils
                              + AddonNamedDriver
                              + AddonTotalLoss
                              + AddonEvCharger;

            // 8% Service Tax on (NetPremium + TotalAddonAmount)
            ServiceTaxAmount = Math.Round((NetPremium + TotalAddonAmount) * 0.08m, 2);

            // StampDutyAmount stays as set (default 10.00m, fixed by law)

            // Final amount the client pays
            NetPremiumPayable = NetPremium + TotalAddonAmount + ServiceTaxAmount + StampDutyAmount;
        }
    }
}