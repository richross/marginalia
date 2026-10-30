namespace Marginalia.Web.Security;

internal static class LocalReturnUrl
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !value.StartsWith("/", StringComparison.Ordinal)
            || value.StartsWith("//", StringComparison.Ordinal)
            || value.Contains('\\')
            || value.Any(char.IsControl))
        {
            return "/";
        }

        return value;
    }
}
