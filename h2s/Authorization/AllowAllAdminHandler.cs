using Microsoft.AspNetCore.Authorization;

namespace h2s.Authorization;

/// <summary>
///   Satisfies <see cref="AdminRequirement" /> for every request, so the admin pages stay open to anyone who can reach
///   the dashboard.
/// </summary>
/// <remarks>
///   This is the authorization seam. Authentication will come from a homelab-wide solution rather than from h2s itself;
///   when it does, replace this handler (and add the authentication scheme) without touching the pages.
/// </remarks>
public sealed class AllowAllAdminHandler : AuthorizationHandler<AdminRequirement>
{
  /// <inheritdoc />
  protected override Task HandleRequirementAsync (AuthorizationHandlerContext context, AdminRequirement requirement)
  {
    context.Succeed (requirement);

    return Task.CompletedTask;
  }
}
