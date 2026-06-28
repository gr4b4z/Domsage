using AgentPlatform.Core.Contracts;
using AgentPlatform.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Notifications;

public sealed class GroupDirectory(AppDbContext db) : IGroupDirectory
{
    public async Task<IReadOnlyList<GroupMember>> GetMembersAsync(string groupId, CancellationToken ct)
    {
        if (!Guid.TryParse(groupId, out var gid)) return [];
        var rows = await (
            from gm in db.GroupMembers.AsNoTracking()
            join u in db.Users.AsNoTracking() on gm.UserId equals u.Id
            where gm.GroupId == gid
            select new { u.Id, u.DisplayName, u.PreferredChannel })
            .ToListAsync(ct);
        return rows.Select(r => new GroupMember(
            r.Id.ToString(), r.DisplayName, r.PreferredChannel)).ToList();
    }

    public async Task<IReadOnlyList<GroupMember>> ResolveByNamesAsync(
        string groupId, IEnumerable<string> names, CancellationToken ct)
    {
        var members = await GetMembersAsync(groupId, ct);
        var result = new List<GroupMember>();
        foreach (var raw in names)
        {
            var n = Fold(raw);
            if (n.Length == 0) continue;
            // Diacritic-insensitive, declension-tolerant: exact, prefix, or shared 3+ char stem
            // ("Agatą"->"agata" ~ "Agatha"->"agatha"; "Olą"->"ola" == "Ola").
            var match = members.FirstOrDefault(m => Fold(m.DisplayName) == n)
                     ?? members.FirstOrDefault(m => Stem(Fold(m.DisplayName), n));
            if (match is not null && result.All(r => r.UserId != match.UserId))
                result.Add(match);
        }
        return result;
    }

    private static bool Stem(string a, string b)
    {
        if (a.StartsWith(b) || b.StartsWith(a)) return true;
        var k = Math.Min(Math.Min(a.Length, b.Length), 4);
        return k >= 3 && a[..k] == b[..k];
    }

    private static string Fold(string s)
    {
        var d = s.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in d)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark && char.IsLetter(c))
                sb.Append(c);
        return sb.ToString();
    }
}
