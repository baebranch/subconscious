using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Subconscious.Engine.Api.DTOs;

namespace Subconscious.Engine.Configuration;

/// <summary>Encrypted persistence and CRUD for Python-compatible <c>data.enc</c> model entries.</summary>
public interface IModelConfigurationStore
{
    Task<IReadOnlyList<ModelConfigurationDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<ModelConfigurationDto> CreateAsync(UpsertModelConfigurationRequest request, CancellationToken cancellationToken = default);
    Task<ModelConfigurationDto?> UpdateAsync(string id, UpsertModelConfigurationRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>Errors that prevent safely reading or writing encrypted model configuration data.</summary>
public sealed class ModelConfigurationStoreException : InvalidOperationException
{
    public ModelConfigurationStoreException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Reads and writes <c>data.enc</c> using the Fernet key that Python stores as the
/// <c>subconscious</c>/<c>encryption_key</c> Windows Credential Manager entry. Every write
/// preserves unrelated top-level data and unknown model fields so Python and .NET clients remain
/// interoperable.
/// </summary>
public sealed class EncryptedModelConfigurationStore : IModelConfigurationStore
{
    private readonly string _dataFilePath;
    private readonly IFernetKeyProvider _keyProvider;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public EncryptedModelConfigurationStore(EngineConfig config, IFernetKeyProvider keyProvider)
        : this(config.DataDirectory, keyProvider) { }

    internal EncryptedModelConfigurationStore(string dataDirectory, IFernetKeyProvider keyProvider)
    {
        _dataFilePath = Path.Combine(dataDirectory, "data.enc");
        _keyProvider = keyProvider;
    }

    public async Task<IReadOnlyList<ModelConfigurationDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var models = (await ReadSecretsAsync(cancellationToken))["models"] as JsonObject;
            return models is null
                ? []
                : models.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Where(entry => entry.Value is JsonObject)
                    .Select(entry => Map(entry.Key, entry.Value!.AsObject()))
                    .ToList();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<ModelConfigurationDto> CreateAsync(UpsertModelConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var secrets = await ReadSecretsAsync(cancellationToken);
            var models = GetOrCreateModels(secrets);
            var id = Guid.NewGuid().ToString();
            var model = new JsonObject();
            Apply(request, model, isNew: true);
            models[id] = model;
            await WriteSecretsAsync(secrets, cancellationToken);
            return Map(id, model);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<ModelConfigurationDto?> UpdateAsync(string id, UpsertModelConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var secrets = await ReadSecretsAsync(cancellationToken);
            var models = GetOrCreateModels(secrets);
            if (models[id] is not JsonObject model)
            {
                return null;
            }

            Apply(request, model, isNew: false);
            await WriteSecretsAsync(secrets, cancellationToken);
            return Map(id, model);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var secrets = await ReadSecretsAsync(cancellationToken);
            var models = GetOrCreateModels(secrets);
            if (!models.Remove(id))
            {
                return false;
            }

            await WriteSecretsAsync(secrets, cancellationToken);
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<JsonObject> ReadSecretsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_dataFilePath))
        {
            return new JsonObject();
        }

        try
        {
            var encrypted = await File.ReadAllBytesAsync(_dataFilePath, cancellationToken);
            var plaintext = new FernetProtector(_keyProvider.GetOrCreateKey()).Decrypt(encrypted);
            return JsonNode.Parse(plaintext) as JsonObject
                ?? throw new ModelConfigurationStoreException("data.enc must contain a JSON object.");
        }
        catch (ModelConfigurationStoreException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ModelConfigurationStoreException("data.enc contains invalid decrypted JSON.", exception);
        }
        catch (IOException exception)
        {
            throw new ModelConfigurationStoreException("data.enc could not be read.", exception);
        }
    }

    private async Task WriteSecretsAsync(JsonObject secrets, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(_dataFilePath)
                ?? throw new ModelConfigurationStoreException("The model configuration data directory is invalid.");
            Directory.CreateDirectory(directory);
            var encrypted = new FernetProtector(_keyProvider.GetOrCreateKey())
                .Encrypt(secrets.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            var temporaryPath = _dataFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, encrypted, cancellationToken);
            File.Move(temporaryPath, _dataFilePath, overwrite: true);
        }
        catch (ModelConfigurationStoreException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new ModelConfigurationStoreException("data.enc could not be written.", exception);
        }
    }

    private static JsonObject GetOrCreateModels(JsonObject secrets)
    {
        if (secrets["models"] is JsonObject models)
        {
            return models;
        }

        var created = new JsonObject();
        secrets["models"] = created;
        return created;
    }

    private static ModelConfigurationDto Map(string id, JsonObject model) => new()
    {
        Id = id,
        Provider = GetString(model, "provider") ?? string.Empty,
        Model = GetString(model, "model") ?? string.Empty,
        Alias = GetString(model, "alias"),
        BaseUrl = GetString(model, "base_url"),
        ContextWindow = GetInt(model, "context_window"),
        HasApiKey = !string.IsNullOrWhiteSpace(GetString(model, "api_key")),
    };

    private static string? GetString(JsonObject source, string key) =>
        source[key] is JsonValue value && value.TryGetValue<string>(out var result) ? result : null;

    private static int? GetInt(JsonObject source, string key)
    {
        if (source[key] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            return integer;
        }

        return value.TryGetValue<string>(out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)
            ? integer
            : null;
    }

    private static void Validate(UpsertModelConfigurationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            throw new ArgumentException("Provider is required.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new ArgumentException("Model is required.", nameof(request));
        }
        if (request.ContextWindow is <= 0)
        {
            throw new ArgumentException("Context window must be a positive number of tokens.", nameof(request));
        }
    }

    private static void Apply(UpsertModelConfigurationRequest request, JsonObject model, bool isNew)
    {
        model["provider"] = request.Provider.Trim();
        model["model"] = request.Model.Trim();
        model["alias"] = request.Alias?.Trim() ?? string.Empty;
        model["base_url"] = request.BaseUrl?.Trim() ?? string.Empty;
        if (request.ContextWindow is { } contextWindow)
        {
            // The Python Flet form serializes this field as text; retain that wire shape.
            model["context_window"] = contextWindow.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            model.Remove("context_window");
        }

        if (request.ApiKey is not null)
        {
            model["api_key"] = request.ApiKey;
        }
        else if (request.ClearApiKey)
        {
            model.Remove("api_key");
        }
        else if (isNew)
        {
            model["api_key"] = string.Empty;
        }
    }
}
