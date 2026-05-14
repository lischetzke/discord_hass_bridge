using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordHass.HomeAssistant;

internal sealed class HaHelperManager
{
    private readonly HaWebSocketClient _client;
    private List<HaInputBooleanItem>? _cachedList;
    private readonly object _cacheLock = new();

    public HaHelperManager(HaWebSocketClient client)
    {
        _client = client;
    }

    public void Reset()
    {
        lock (_cacheLock) { _cachedList = null; }
    }

    /// <summary>
    /// Ensures a helper exists in HA with the desired entity_id slug, friendly name, and icon.
    /// If a helper with <paramref name="lastKnownSlug"/> exists but the target slug differs,
    /// the existing helper is renamed via the entity registry; falls back to create-new on failure.
    /// Returns the entity_id slug that actually ended up in HA (caller should persist this).
    /// </summary>
    public async Task<string> EnsureAndSyncAsync(
        string entityIdSlug,
        string friendlyName,
        string icon,
        string? lastKnownSlug,
        CancellationToken ct)
    {
        IReadOnlyList<HaInputBooleanItem> list = await GetListAsync(ct).ConfigureAwait(false);

        bool slugChanged = !string.IsNullOrEmpty(lastKnownSlug)
            && !string.Equals(lastKnownSlug, entityIdSlug, StringComparison.OrdinalIgnoreCase);
        HaInputBooleanItem? oldEntry = slugChanged ? FindBySlug(list, lastKnownSlug!) : null;
        HaInputBooleanItem? newEntry = FindBySlug(list, entityIdSlug);

        if (slugChanged && oldEntry is not null && newEntry is null)
        {
            bool renamed = await TryRenameAsync(lastKnownSlug!, entityIdSlug, ct).ConfigureAwait(false);
            if (renamed)
            {
                await UpdateAsync(entityIdSlug, friendlyName, icon, ct).ConfigureAwait(false);
                InvalidateCache();
                return entityIdSlug;
            }
            // Rename failed — refresh list and fall through to create or update
            InvalidateCache();
            list = await GetListAsync(ct).ConfigureAwait(false);
            newEntry = FindBySlug(list, entityIdSlug);
        }

        if (newEntry is not null)
        {
            if (!string.Equals(newEntry.Name, friendlyName, StringComparison.Ordinal)
                || !string.Equals(newEntry.Icon ?? "", icon ?? "", StringComparison.Ordinal))
            {
                await UpdateAsync(entityIdSlug, friendlyName, icon, ct).ConfigureAwait(false);
                InvalidateCache();
            }
            return entityIdSlug;
        }

        string created = await CreateAsync(friendlyName, icon, ct).ConfigureAwait(false);
        InvalidateCache();
        return created;
    }

    public async Task SetStateAsync(string entityIdSlug, bool on, CancellationToken ct)
    {
        string service = on ? "turn_on" : "turn_off";
        object payload = new
        {
            type = "call_service",
            domain = "input_boolean",
            service,
            target = new { entity_id = $"input_boolean.{entityIdSlug}" },
        };
        await _client.SendCommandAsync(payload, ct).ConfigureAwait(false);
    }

    private async Task<bool> TryRenameAsync(string oldSlug, string newSlug, CancellationToken ct)
    {
        try
        {
            await _client.SendCommandAsync(new
            {
                type = "config/entity_registry/update",
                entity_id = $"input_boolean.{oldSlug}",
                new_entity_id = $"input_boolean.{newSlug}",
            }, ct).ConfigureAwait(false);
            return true;
        }
        catch (HaCommandException)
        {
            return false;
        }
    }

    private Task UpdateAsync(string slug, string name, string? icon, CancellationToken ct)
    {
        object payload = icon is null
            ? new { type = "input_boolean/update", input_boolean_id = slug, name }
            : new { type = "input_boolean/update", input_boolean_id = slug, name, icon };
        return _client.SendCommandAsync(payload, ct);
    }

    private async Task<string> CreateAsync(string friendlyName, string? icon, CancellationToken ct)
    {
        object payload = icon is null
            ? new { type = "input_boolean/create", name = friendlyName }
            : new { type = "input_boolean/create", name = friendlyName, icon };
        JsonElement result = await _client.SendCommandAsync(payload, ct).ConfigureAwait(false);
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("id", out JsonElement idEl))
        {
            string? assigned = idEl.GetString();
            if (!string.IsNullOrEmpty(assigned))
            {
                return assigned!;
            }
        }
        throw new HaCommandException("input_boolean/create response did not include an id");
    }

    private async Task<IReadOnlyList<HaInputBooleanItem>> GetListAsync(CancellationToken ct)
    {
        lock (_cacheLock)
        {
            if (_cachedList is not null)
            {
                return _cachedList;
            }
        }

        JsonElement result = await _client.SendCommandAsync(new { type = "input_boolean/list" }, ct).ConfigureAwait(false);
        List<HaInputBooleanItem> list = new();
        if (result.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement el in result.EnumerateArray())
            {
                HaInputBooleanItem? item = el.Deserialize<HaInputBooleanItem>();
                if (item is not null) list.Add(item);
            }
        }

        lock (_cacheLock)
        {
            _cachedList = list;
        }
        return list;
    }

    private void InvalidateCache()
    {
        lock (_cacheLock) { _cachedList = null; }
    }

    private static HaInputBooleanItem? FindBySlug(IReadOnlyList<HaInputBooleanItem> list, string slug)
    {
        foreach (HaInputBooleanItem item in list)
        {
            if (string.Equals(item.Id, slug, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }
        return null;
    }
}
