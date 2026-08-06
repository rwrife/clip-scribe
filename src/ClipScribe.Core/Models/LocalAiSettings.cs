namespace ClipScribe.Core.Models;

public sealed record LocalAiSettings(
    bool Enabled,
    string Endpoint,
    string Model)
{
    public static LocalAiSettings Default { get; } = new(
        Enabled: false,
        Endpoint: "http://localhost:11434",
        Model: string.Empty);

    public static LocalAiSettings Normalize(LocalAiSettings? value)
    {
        if (value is null)
        {
            return Default;
        }

        var endpoint = string.IsNullOrWhiteSpace(value.Endpoint)
            ? Default.Endpoint
            : value.Endpoint.Trim();

        var model = string.IsNullOrWhiteSpace(value.Model)
            ? string.Empty
            : value.Model.Trim();

        return new LocalAiSettings(
            Enabled: value.Enabled,
            Endpoint: endpoint,
            Model: model);
    }

    public bool IsEnabledAndConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(Model);
}
