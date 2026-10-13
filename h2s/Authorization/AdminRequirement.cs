using Microsoft.AspNetCore.Authorization;

namespace h2s.Authorization;

/// <summary>
///   Requires that the current user may administer the dashboard. The <see cref="PolicyName" /> policy, applied to the
///   <c>/Admin</c> pages, consists of this requirement alone.
/// </summary>
public sealed class AdminRequirement : IAuthorizationRequirement
{
  /// <summary>
  ///   The name of the authorization policy that guards the admin pages.
  /// </summary>
  public const string PolicyName = "Admin";
}
