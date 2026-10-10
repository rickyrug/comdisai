using Microsoft.AspNetCore.Mvc;

namespace ComdisAI.Authorization;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireActionAttribute : TypeFilterAttribute
{
    public RequireActionAttribute(string actionCode) : base(typeof(ActionAuthorizationFilter))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionCode);
        Arguments = [actionCode];
        IsReusable = false;
    }
}
