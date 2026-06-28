using System.Globalization;
using System.Text.Json;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Plugins.Weather;

public sealed class WeatherOptions
{
    /// <summary>Used when the user asks "jaka pogoda?" without naming a place.</summary>
    public string DefaultLocation { get; set; } = "Warszawa";
}

public sealed record CurrentWeather(double TempC, double FeelsC, int Humidity, double WindKmh, string Description);
public sealed record DayForecast(string Date, double MinC, double MaxC, int PrecipProb, string Description);
public sealed record WeatherReport(string Place, CurrentWeather Current, IReadOnlyList<DayForecast> Days);

/// <summary>Fetches current conditions + a few days of forecast from Open-Meteo (free, no API key).</summary>
public sealed class WeatherProvider(IHttpClientFactory httpFactory)
{
    public async Task<WeatherReport?> GetAsync(string place, CancellationToken ct)
    {
        var http = httpFactory.CreateClient();
        // 1) Geocode the place name → lat/lon.
        var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(place)}&count=1&language=pl&format=json";
        using var geoDoc = JsonDocument.Parse(await http.GetStringAsync(geoUrl, ct));
        if (!geoDoc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return null;
        var g = results[0];
        var lat = g.GetProperty("latitude").GetDouble().ToString(CultureInfo.InvariantCulture);
        var lon = g.GetProperty("longitude").GetDouble().ToString(CultureInfo.InvariantCulture);
        var name = g.GetProperty("name").GetString() ?? place;
        var country = g.TryGetProperty("country", out var c) ? c.GetString() : null;

        // 2) Current + 3-day daily forecast. (InvariantCulture coords — a pl-PL comma → Open-Meteo 400.)
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&timezone=auto"
                + "&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m"
                + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&forecast_days=3";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        var cur = doc.RootElement.GetProperty("current");
        var d = doc.RootElement.GetProperty("daily");
        var times = d.GetProperty("time");
        var days = new List<DayForecast>();
        for (var i = 0; i < times.GetArrayLength(); i++)
            days.Add(new DayForecast(
                times[i].GetString() ?? "",
                d.GetProperty("temperature_2m_min")[i].GetDouble(),
                d.GetProperty("temperature_2m_max")[i].GetDouble(),
                d.GetProperty("precipitation_probability_max")[i].ValueKind == JsonValueKind.Number
                    ? d.GetProperty("precipitation_probability_max")[i].GetInt32() : 0,
                Describe(d.GetProperty("weather_code")[i].GetInt32())));

        return new WeatherReport(
            country is null ? name : $"{name}, {country}",
            new CurrentWeather(
                cur.GetProperty("temperature_2m").GetDouble(),
                cur.GetProperty("apparent_temperature").GetDouble(),
                cur.GetProperty("relative_humidity_2m").GetInt32(),
                cur.GetProperty("wind_speed_10m").GetDouble(),
                Describe(cur.GetProperty("weather_code").GetInt32())),
            days);
    }

    // WMO weather codes → short Polish description.
    private static string Describe(int code) => code switch
    {
        0 => "bezchmurnie",
        1 or 2 => "częściowe zachmurzenie",
        3 => "pochmurno",
        45 or 48 => "mgła",
        >= 51 and <= 57 => "mżawka",
        >= 61 and <= 67 => "deszcz",
        >= 71 and <= 77 => "śnieg",
        >= 80 and <= 82 => "przelotny deszcz",
        85 or 86 => "przelotny śnieg",
        >= 95 => "burza",
        _ => "—"
    };
}

/// <summary>weather.current — current conditions or a day's forecast. Deterministic fetch; the data is the truth.</summary>
public sealed class WeatherTool(WeatherProvider provider, IOptions<WeatherOptions> options) : ITool
{
    public string ToolId => "weather.current";
    public bool HasSideEffects => false;
    public ScopeRequirement RequiredScope => new(ContextScope.User, MemberRole.Member);

    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(
            ("location", new JsonSchemaBuilder().Type(SchemaValueType.String)
                .Description("City/place name; omit to use the default")),
            ("when", new JsonSchemaBuilder().Type(SchemaValueType.String)
                .Description("'today' (now) or 'tomorrow' (forecast). Default 'today'.")))
        .Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var place = input.Arguments.TryGetProperty("location", out var l) && !string.IsNullOrWhiteSpace(l.GetString())
            ? l.GetString()!.Trim() : options.Value.DefaultLocation;
        var when = input.Arguments.TryGetProperty("when", out var w) ? (w.GetString() ?? "today").ToLowerInvariant() : "today";

        try
        {
            var r = await provider.GetAsync(place, ct);
            if (r is null)
                return new ToolResult(ToolResultStatus.Failed, null, "place not found",
                    $"❓ Nie znalazłem miejscowości „{place}”.");

            var msg = when.StartsWith("tomorrow") || when.StartsWith("jutro")
                ? DescribeDay(r.Place, "jutro", r.Days.ElementAtOrDefault(1))
                : $"🌤️ {r.Place} teraz: {r.Current.TempC:0}°C (odczuwalna {r.Current.FeelsC:0}°C), " +
                  $"{r.Current.Description}, wiatr {r.Current.WindKmh:0} km/h, wilgotność {r.Current.Humidity}%.";

            return new ToolResult(ToolResultStatus.Success, JsonSerializer.SerializeToElement(r), null, msg);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ToolResult(ToolResultStatus.Failed, null, $"weather failed: {ex.Message}",
                "🌧️ Nie mogę teraz sprawdzić pogody — spróbuj później.");
        }
    }

    // Reads like an answer to "czy będzie padać?" — leads with a rain verdict, then the details.
    private static string DescribeDay(string place, string label, DayForecast? day)
    {
        if (day is null) return $"Nie mam prognozy na {label} dla {place}.";
        var rain = day.PrecipProb >= 60 ? $"☔ {place} {label}: raczej będzie padać"
                 : day.PrecipProb >= 30 ? $"🌦️ {place} {label}: możliwe opady"
                 : $"🌤️ {place} {label}: raczej bez opadów";
        return $"{rain} (szansa {day.PrecipProb}%), {day.MinC:0}–{day.MaxC:0}°C, {day.Description}.";
    }
}

/// <summary>weather.current — "jaka jest pogoda w Warszawie?", "czy będzie jutro padać w Krakowie?".</summary>
public sealed class WeatherHandler : IIntentHandler
{
    public string IntentId => "weather.current";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => ["weather.current"];
    public string PromptTemplateId => "weather_current";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
    public bool PhraseResult => true; // answer naturally from the fetched data, not a fixed template
}

/// <summary>The whole plugin — namespace "weather", no DB. Registered from the composition root.</summary>
public sealed class WeatherPluginRegistration : IPluginRegistration
{
    public string Namespace => "weather";
    public string? DbSchema => null;

    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.Configure<WeatherOptions>(config);
        services.AddHttpClient();
        services.AddSingleton<WeatherProvider>();
        services.AddScoped<ITool, WeatherTool>();
        services.AddSingleton<IIntentHandler, WeatherHandler>();
    }
}
