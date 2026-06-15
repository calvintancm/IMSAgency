using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImsAgency.Web.Models.IMS
{
    // A client can have multiple phone numbers (mobile, home, office, fax).
    // The IsWhatsApp flag matters a lot here — it's what the
    // WhatsApp Renewal Reminder Simulator (Feature 6) checks before
    // showing the "Send WhatsApp Reminder" button.
    public class ClientPhone
    {
        [Key]
        public int PhoneId { get; set; }

        [ForeignKey(nameof(Client))]
        public int ClientId { get; set; }

        [Required]
        [StringLength(30)]
        public string PhoneNumber { get; set; } = string.Empty;

        // "M" = Mobile, "H" = Home, "O" = Office, "F" = Fax
        [Required]
        [StringLength(5)]
        public string PhoneType { get; set; } = "M";

        // Only ONE phone per client should have this set to true —
        // enforced in the controller, not the database, same as MVA's
        // "one primary address" rule
        public bool IsPrimary { get; set; } = false;

        // True if this number can receive WhatsApp messages
        // (used by the renewal reminder feature)
        public bool IsWhatsApp { get; set; } = false;

        // ---- Navigation ----
        public Client? Client { get; set; }
    }
}