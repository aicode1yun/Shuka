namespace Shuka.Core;

/// <summary>
/// Thrown when a Cloudflare challenge page is still showing after the
/// maximum wait time. This typically means the CF clearance cookie has
/// expired and the user needs to solve the challenge manually.
/// </summary>
public class CloudflareExpiredException : Exception
{
    public string SiteUrl { get; }

    public CloudflareExpiredException(string siteUrl)
        : base($"Cloudflare challenge did not clear for {siteUrl}. " +
               "The clearance cookie may have expired.")
    {
        SiteUrl = siteUrl;
    }
}
