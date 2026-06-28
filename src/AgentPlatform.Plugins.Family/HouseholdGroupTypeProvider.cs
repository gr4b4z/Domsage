using AgentPlatform.PluginSdk.Contracts;

namespace AgentPlatform.Plugins.Family;

public sealed class HouseholdGroupTypeProvider : IGroupTypeProvider
{
    public string GroupType => "household";
    public string[] KnownRoles => ["admin", "member", "guest", "child"];

    public MemberRole MapToCore(string role) => role switch
    {
        "admin" => MemberRole.Admin,
        "member" => MemberRole.Member,
        "child" => MemberRole.Member,
        _ => MemberRole.Guest
    };
}

/// <summary>Marker for assembly scanning.</summary>
public sealed class FamilyPluginsMarker;
