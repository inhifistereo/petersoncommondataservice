// filepath: /home/dpeterson/petersoncommondataservice/code/PetersonCommonDataService/Pages/Authorize.cshtml.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;

namespace PetersonCommonDataService.Pages
{
    [Authorize]
    public class AuthorizeModel : PageModel
    {
        private readonly ITokenAcquisition _tokenAcquisition;

        public AuthorizeModel(ITokenAcquisition tokenAcquisition)
        {
            _tokenAcquisition = tokenAcquisition;
        }

        public IActionResult OnGetSignIn()
        {
            var redirectUrl = Url.Page("/Authorize");
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
        }

    }
}