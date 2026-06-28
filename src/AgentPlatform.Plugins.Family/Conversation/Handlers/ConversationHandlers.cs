using AgentPlatform.PluginSdk.Contracts;

namespace AgentPlatform.Plugins.Family.Conversation.Handlers;

public sealed class ResetConversationHandler : IIntentHandler
{
    public string IntentId => "conversation.reset";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => ["conversation.reset"];
    public string PromptTemplateId => "conversation_reset";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}

public sealed class CompactConversationHandler : IIntentHandler
{
    public string IntentId => "system.compact_conversation";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => ["conversation.history"];
    public string[] AllowedTools => ["conversation.save_summary"];
    public string PromptTemplateId => "compact_conversation_v1";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}

public sealed class RememberFactHandler : IIntentHandler
{
    public string IntentId => "user.remember_fact";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => ["user.remember_fact"];
    public string PromptTemplateId => "remember_fact";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}
