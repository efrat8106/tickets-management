using Microsoft.AspNetCore.Mvc;

namespace Tickets.Tests.Infrastructure;

/// <summary>
/// Test-only endpoint (registered from the test assembly by <see cref="ApiFixture"/>, not part of the production API)
/// used to exercise the global exception handler with a real unhandled exception.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("__tests/throw")]
public sealed class ThrowingTestController : ControllerBase
{
    public const string SecretMessage = "secret-internal-detail";

    [HttpGet]
    public IActionResult Throw() => throw new InvalidOperationException(SecretMessage);
}
