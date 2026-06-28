using AgentPlatform.PluginSdk.Contracts;

namespace AgentPlatform.Plugins.Family.Handlers;

public sealed class MarkPaymentPaidHandler : IIntentHandler
{
    public string IntentId => "family.mark_payment_paid";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => ["today.payments"];
    public string[] AllowedTools => ["family.payments.mark_paid"];
    public string PromptTemplateId => "mark_payment_paid";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.Required;
}

public sealed class AddPaymentManualHandler : IIntentHandler
{
    public string IntentId => "family.add_payment";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => ["family.payments.create"];
    public string PromptTemplateId => "add_payment";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}

public sealed class AddTaskHandler : IIntentHandler
{
    public string IntentId => "family.add_task";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => ["family.tasks.create"];
    public string PromptTemplateId => "add_task";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}

public sealed class MarkTaskDoneHandler : IIntentHandler
{
    public string IntentId => "family.mark_task_done";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => ["today.tasks"];
    public string[] AllowedTools => ["family.tasks.mark_done"];
    public string PromptTemplateId => "mark_task_done";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}
