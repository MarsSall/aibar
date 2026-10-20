using System.Text.Json;

namespace AIBar.Domain;

public enum StatusProvider { Codex, Claude }

/// <summary>Public service health, independent of quota availability and credentials.</summary>
public sealed record ProviderStatus
{
    private ProviderStatus(string indicator, string description) { Indicator = indicator; Description = description; }
    public string Indicator { get; }
    public string Description { get; }
    public bool HasIncident => Indicator != "none";

    public static ProviderStatus? Parse(ReadOnlyMemory<byte> utf8)
    {
        if (utf8.Length > 65536) return null;
        try
        {
            using var document = JsonDocument.Parse(utf8, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var statuses = document.RootElement.EnumerateObject().Where(p => p.NameEquals("status")).ToArray();
            if (statuses.Length != 1 || statuses[0].Value.ValueKind != JsonValueKind.Object) return null;
            string? indicator = null, description = null;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in statuses[0].Value.EnumerateObject())
            {
                if (!names.Add(property.Name) || property.Value.ValueKind != JsonValueKind.String) return null;
                switch (property.Name)
                {
                    case "indicator": indicator = property.Value.GetString(); break;
                    case "description": description = property.Value.GetString(); break;
                    default: return null;
                }
            }
            if (indicator is not ("none" or "minor" or "major" or "critical") ||
                string.IsNullOrWhiteSpace(description) || description.Any(char.IsControl)) return null;
            return new(indicator, description);
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}
