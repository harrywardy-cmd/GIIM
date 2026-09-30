namespace Giim.Api.Security;

/// <summary>
/// The four GIIM roles from the brief. Each is an app role on the GIIM app registration in Entra, assigned to people
/// or groups; the values must match these names exactly.
/// </summary>
internal static class Roles
{
    public const string Administrator = "Administrator";
    public const string Technician = "Technician";
    public const string Manager = "Manager";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [Administrator, Technician, Manager, Viewer];
}

/// <summary>Authorisation policies. Higher roles include the lower ones' access.</summary>
internal static class Policies
{
    /// <summary>Any GIIM role: can read.</summary>
    public const string Read = "Giim.Read";

    /// <summary>Technician or Administrator: can change assets, stock, repairs and so on.</summary>
    public const string Change = "Giim.Change";

    /// <summary>
    /// Manager, Technician or Administrator: raise, decide, answer, cancel and comment on device requests. Only
    /// endpoints marked with <see cref="AuthSetup.AllowManagers{TBuilder}"/> use it; the request itself then checks who
    /// may decide (the named approver or an administrator).
    /// </summary>
    public const string Request = "Giim.Request";

    /// <summary>Administrator: locations, categories, imports and directory sync.</summary>
    public const string Administer = "Giim.Administer";
}
