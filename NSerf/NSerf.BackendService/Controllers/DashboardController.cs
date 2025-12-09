using Microsoft.AspNetCore.Mvc;
using NSerf.Agent;

namespace NSerf.BackendService.Controllers;

[Route("serf")]
public class DashboardController : Controller
{
    private readonly SerfAgent _agent;

    public DashboardController(SerfAgent agent)
    {
        _agent = agent;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
