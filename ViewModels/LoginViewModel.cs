// Models/Auth/LoginViewModel.cs
using System.ComponentModel.DataAnnotations;

namespace ImsAgency.Web.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Username is required")]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }

        // Where to send the user after a successful login
        // (e.g. if they tried to open a page without being logged in)
        public string? ReturnUrl { get; set; }
    }
}