namespace ImsAgency.Web.Models.ViewModels
{
    // Stat-strip counts shown above the AllClients grid
    public class ClientListViewModel
    {
        public int TotalClients { get; set; }
        public int IndividualCount { get; set; }
        public int CompanyCount { get; set; }
        public int BlacklistedCount { get; set; }
    }
}