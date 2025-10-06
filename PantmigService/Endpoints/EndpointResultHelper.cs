using PantmigService.Services;

namespace PantmigService.Endpoints;

public static class EndpointResultHelper
{
    public static IResult ToProblemResult(this ValidationProblem problem, HttpContext ctx)
    {
        return Results.Problem(title: problem.Title, detail: problem.Detail, statusCode: problem.StatusCode, instance: ctx.TraceIdentifier);
    }
}
