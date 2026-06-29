using AgentPlatform.Infrastructure.Notifications;
using AgentPlatform.Infrastructure.Postgres.Entities;
using Xunit;

namespace AgentPlatform.Core.Tests;

/// <summary>Pure push-channel selection — runs on every notification, must preserve telegram&gt;signal + email fallback.</summary>
public sealed class NotificationRoutingTests
{
    private static readonly string[] Default = ["telegram", "signal", "discord"];
    private static ChannelIdentity Id(string ch, string ext) => new() { ChannelId = ch, ExternalId = ext };

    [Fact]
    public void Telegram_i_Signal_wybiera_Telegram()
    {
        var pick = ChannelRouting.SelectPushChannel([Id("signal", "s1"), Id("telegram", "t1")], Default);
        Assert.Equal(("telegram", "t1"), pick);
    }

    [Fact]
    public void Tylko_Signal_wybiera_Signal()
    {
        var pick = ChannelRouting.SelectPushChannel([Id("signal", "s1"), Id("email", "a@x.pl")], Default);
        Assert.Equal(("signal", "s1"), pick);
    }

    [Fact]
    public void Brak_messaging_zwraca_null()
    {
        // Only email/http → no push target (→ email fallback path in NotificationService).
        var pick = ChannelRouting.SelectPushChannel([Id("email", "a@x.pl"), Id("http", "web")], Default);
        Assert.Null(pick);
    }

    [Fact]
    public void Nowy_kanal_w_priority_jest_wybierany()
    {
        var pick = ChannelRouting.SelectPushChannel([Id("discord", "d1")], Default);
        Assert.Equal(("discord", "d1"), pick);
    }

    [Fact]
    public void Nieznany_kanal_spoza_priority_nadal_osiagalny()
    {
        // Zero-touch: a channel not in the priority list still gets reached (leftover fallback).
        var pick = ChannelRouting.SelectPushChannel([Id("matrix", "m1")], Default);
        Assert.Equal(("matrix", "m1"), pick);
    }
}
