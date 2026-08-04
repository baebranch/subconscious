namespace Subconscious.Engine.Api.DTOs;

/// <summary>Persisted left-to-right arrangement of the desktop's three panels.</summary>
public sealed record PanelConfigurationDto
{
    public required string Configuration { get; init; }
}

/// <summary>Validated panel arrangement accepted by the desktop settings endpoint.</summary>
public sealed record UpdatePanelConfigurationRequest
{
    public required string Configuration { get; init; }
}
