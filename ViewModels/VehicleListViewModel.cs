namespace ImsAgency.Web.Models.ViewModels
{
    public class VehicleListViewModel
    {
        public int TotalVehicles { get; set; }
        public int ActiveVehicles { get; set; }
        public int ElectricVehicleCount { get; set; }
        public int VehiclesWithActivePolicy { get; set; }
    }
}