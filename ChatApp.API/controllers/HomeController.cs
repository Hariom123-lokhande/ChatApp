using Microsoft.AspNetCore.Mvc;

namespace ChatApp.API.Controllers
{
    
    public class HomeController : Controller
    {
        // GET: / or /Home/Chat
        public IActionResult Chat()
        {
            return View();
        }
    }
}
