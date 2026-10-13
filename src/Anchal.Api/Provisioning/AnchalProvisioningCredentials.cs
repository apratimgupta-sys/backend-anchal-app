namespace Anchal.Api.Provisioning;

public static class AnchalProvisioningCredentials
{
    private static readonly HashSet<string> ApprovedAccounts =
        ["anchal1", "anchal2", "anchal3", "anchal4", "anchal5", "anchal6", "commissioner"];

    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("Provisioning credentials file was not found.");
        var credentials = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (line, lineNumber) in File.ReadLines(path).Select((line, index) => (line, index + 1)))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0 || line != line.Trim())
                throw new InvalidOperationException($"Invalid provisioning credentials file line {lineNumber}.");
            var name = line[..separator];
            var password = line[(separator + 1)..];
            if (!ApprovedAccounts.Contains(name) || password.Length is < 12 or > 128 || !credentials.TryAdd(name, password))
                throw new InvalidOperationException("Provide one 12-128 character password for each of the seven approved monitoring accounts.");
        }
        if (credentials.Count != ApprovedAccounts.Count)
            throw new InvalidOperationException("Provide one 12-128 character password for each of the seven approved monitoring accounts.");
        return credentials;
    }
}
