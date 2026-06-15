using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImsAgency.Web.Models.IMS
{
    // A log of every renewal reminder sent (or simulated) for a policy.
    // Powers the "Renewal Notices Log" page and the WhatsApp Reminder
    // Simulator (Feature 6) — when an agent clicks "Send WhatsApp Reminder",
    // a row is written here with Channel = "WhatsApp".
    public class RenewalNotice
    {
        [Key]
        public int NoticeId { get; set; }

        [ForeignKey(nameof(Policy))]
        public int PolicyId { get; set; }

        // "90Day", "60Day", "30Day", "7Day", "Expired"
        [Required]
        [StringLength(20)]
        public string NoticeType { get; set; } = string.Empty;

        [Required]
        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        // "WhatsApp", "SMS", "Email", "Letter"
        [Required]
        [StringLength(20)]
        public string Channel { get; set; } = string.Empty;

        // The phone number or email the notice was sent to (snapshot at
        // send-time, in case the client's contact info changes later)
        [Required]
        [StringLength(150)]
        public string PhoneOrEmail { get; set; } = string.Empty;

        // The actual rendered message text (e.g. the WhatsApp message
        // preview shown by Feature 6)
        public string? MessageContent { get; set; }

        // For the simulator, this stays false (no real delivery confirmation
        // exists yet). For real Email/SMS channels via an actual provider
        // later, this can be updated by a webhook.
        public bool IsDelivered { get; set; } = false;

        public DateTime? DeliveredAt { get; set; }

        // Free-text follow-up note from the agent
        // (e.g. "Client called back, will renew next week")
        public string? AgentNote { get; set; }

        // ---- Navigation ----
        public Policy? Policy { get; set; }
    }
}