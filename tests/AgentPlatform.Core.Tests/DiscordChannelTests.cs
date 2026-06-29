using AgentPlatform.Plugins.Discord;
using Xunit;

namespace AgentPlatform.Core.Tests;

/// <summary>Pure DM routing + parsing for the Discord channel — no live Gateway needed.</summary>
public sealed class DiscordChannelTests
{
    [Fact]
    public void ParseAsync_DM_mapuje_na_InputMessage_z_ChannelId_discord()
    {
        var msg = DiscordParse.Parse("""{"authorId":"123456","text":"jakie mam dziś zadania?"}""");
        Assert.Equal("discord", msg.ChannelId);
        Assert.Equal("123456", msg.UserId);
        Assert.Equal("jakie mam dziś zadania?", msg.Text);
    }

    [Fact]
    public void StartCommand_z_kodem_linkuje_tozsamosc()
    {
        var links = new DiscordLinkStore();
        var code = links.Mint("user-1");
        var decision = DiscordDmRouter.Route("author-9", $"/start {code}", links);
        var link = Assert.IsType<LinkAccount>(decision);
        Assert.Equal("user-1", link.UserId);
        Assert.Equal("author-9", link.AuthorId);
    }

    [Fact]
    public void StartCommand_z_blednym_kodem_odpowiada_nie_linkuje()
    {
        var decision = DiscordDmRouter.Route("author-9", "/start NOPE99", new DiscordLinkStore());
        Assert.IsType<ReplyDm>(decision);
    }

    [Fact]
    public void Zalacznik_bez_tekstu_jest_ignorowany()
    {
        Assert.IsType<IgnoreDm>(DiscordDmRouter.Route("author-9", "   ", new DiscordLinkStore()));
    }

    [Fact]
    public void Zwykly_tekst_jest_publikowany()
    {
        var d = DiscordDmRouter.Route("author-9", "co mam w kalendarzu?", new DiscordLinkStore());
        var pub = Assert.IsType<PublishMessage>(d);
        Assert.Equal("author-9", pub.AuthorId);
        Assert.Equal("co mam w kalendarzu?", pub.Text);
    }
}
