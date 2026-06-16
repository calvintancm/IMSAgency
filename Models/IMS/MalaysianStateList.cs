namespace ImsAgency.Web.Models.IMS
{
    // Malaysian states — a STATIC list, not a database table.
    // Used to populate the State dropdown on ClientAddress forms,
    // and shown read-only here for reference under Master Data.
    //
    // Same approach as MVA's static lookup lists — no CRUD needed
    // since Malaysia's 16 states/federal territories don't change.
    public static class MalaysianStateList
    {
        public static readonly List<string> States = new()
        {
            "Johor",
            "Kedah",
            "Kelantan",
            "Melaka",
            "Negeri Sembilan",
            "Pahang",
            "Perak",
            "Perlis",
            "Pulau Pinang",
            "Sabah",
            "Sarawak",
            "Selangor",
            "Terengganu",
            "Wilayah Persekutuan Kuala Lumpur",
            "Wilayah Persekutuan Labuan",
            "Wilayah Persekutuan Putrajaya"
        };
    }
}