namespace ImsAgency.Web.ViewModels
{
    public class UserPreferences
    {
        public string Theme { get; set; }        
        public string Language { get; set; }     
        public bool EmailNotifications { get; set; }
        public int DefaultPageSize { get; set; } 
    }
}
