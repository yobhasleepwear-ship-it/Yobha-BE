using System.Security.Claims;

namespace ShoppingPlatform.Helpers  // 👈 choose your actual project namespace
{
    public static class ClaimsPrincipalExtensions
    {
        public static string GetUserIdOrAnonymous(this ClaimsPrincipal user)
        {
            if (user?.Identity?.IsAuthenticated != true)
                return "anonymous";

            // Common claim keys
            var claimCandidates = new[]
            {
                "sub",
                ClaimTypes.NameIdentifier,
                "nameid",
                "user_id",
                "uid",
                "id",
                "userid",
                "email",
                "oid"
            };

            foreach (var type in claimCandidates)
            {
                var claim = user.FindFirst(type);
                if (!string.IsNullOrWhiteSpace(claim?.Value))
                    return claim.Value;
            }

            return user.Identity?.Name ?? "anonymous";
        }
    }
}
