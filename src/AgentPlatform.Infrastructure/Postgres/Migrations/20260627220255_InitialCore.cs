using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentPlatform.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_type = table.Column<string>(type: "text", nullable: true),
                    intent = table.Column<string>(type: "text", nullable: false),
                    planner_mode = table.Column<string>(type: "text", nullable: false),
                    tool_id = table.Column<string>(type: "text", nullable: true),
                    target_id = table.Column<string>(type: "text", nullable: true),
                    result = table.Column<string>(type: "text", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    prompt_version = table.Column<string>(type: "text", nullable: false),
                    model_tier = table.Column<string>(type: "text", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric", nullable: false),
                    diagnostic_steps = table.Column<int>(type: "integer", nullable: false),
                    context_fetched = table.Column<string[]>(type: "text[]", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false),
                    eval_signal = table.Column<string>(type: "text", nullable: true),
                    eval_correction = table.Column<string>(type: "text", nullable: true),
                    eval_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "budget_states",
                columns: table => new
                {
                    scope_key = table.Column<string>(type: "text", nullable: false),
                    spent_usd = table.Column<decimal>(type: "numeric", nullable: false),
                    tripped = table.Column<bool>(type: "boolean", nullable: false),
                    tripped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reset_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_states", x => x.scope_key);
                });

            migrationBuilder.CreateTable(
                name: "conversation_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    intent = table.Column<string>(type: "text", nullable: true),
                    action_summary = table.Column<string>(type: "text", nullable: true),
                    tokens = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id = table.Column<string>(type: "text", nullable: false),
                    incognito = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    close_reason = table.Column<string>(type: "text", nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true),
                    summary_covers_up_to = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_active_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dead_letter_queue",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tool_id = table.Column<string>(type: "text", nullable: false),
                    input = table.Column<string>(type: "jsonb", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: false),
                    error_type = table.Column<string>(type: "text", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    resolved = table.Column<bool>(type: "boolean", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dead_letter_queue", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "group_members",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_members", x => new { x.group_id, x.user_id });
                });

            migrationBuilder.CreateTable(
                name: "groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "memory_facts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memory_facts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pending_confirmations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id = table.Column<string>(type: "text", nullable: false),
                    action_plan = table.Column<string>(type: "jsonb", nullable: false),
                    message_id = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    eval_signal = table.Column<string>(type: "text", nullable: true),
                    eval_correction = table.Column<string>(type: "text", nullable: true),
                    eval_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_confirmations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pending_intents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    intent_id = table.Column<string>(type: "text", nullable: false),
                    gathered_slots = table.Column<string>(type: "jsonb", nullable: false),
                    missing_slots = table.Column<string[]>(type: "text[]", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_intents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_versions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    template_id = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    model_id = table.Column<string>(type: "text", nullable: false),
                    model_tier = table.Column<string>(type: "text", nullable: false),
                    temperature = table.Column<double>(type: "double precision", nullable: false),
                    top_p = table.Column<double>(type: "double precision", nullable: true),
                    max_tokens = table.Column<int>(type: "integer", nullable: true),
                    reasoning_level = table.Column<string>(type: "text", nullable: true),
                    provider_id = table.Column<string>(type: "text", nullable: false),
                    cache_key = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    promoted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prompt_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scheduler_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    r_rule = table.Column<string>(type: "text", nullable: true),
                    timezone = table.Column<string>(type: "text", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduler_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usage_meter_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_id = table.Column<string>(type: "text", nullable: false),
                    model_tier = table.Column<string>(type: "text", nullable: false),
                    prompt_version = table.Column<string>(type: "text", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    cached_tokens = table.Column<int>(type: "integer", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric", nullable: false),
                    intent = table.Column<string>(type: "text", nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_meter_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_id = table.Column<long>(type: "bigint", nullable: true),
                    signal_number = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    timezone = table.Column<string>(type: "text", nullable: false),
                    preferred_channel = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_group_id_occurred_at",
                table: "audit_log",
                columns: new[] { "group_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_user_id_occurred_at",
                table: "audit_log",
                columns: new[] { "user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_messages_conversation_id_created_at",
                table: "conversation_messages",
                columns: new[] { "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_user_id_status",
                table: "conversations",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_memory_facts_group_id_user_id_key",
                table: "memory_facts",
                columns: new[] { "group_id", "user_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pending_confirmations_user_id_expires_at",
                table: "pending_confirmations",
                columns: new[] { "user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_scheduler_jobs_next_run_at",
                table: "scheduler_jobs",
                column: "next_run_at");

            migrationBuilder.CreateIndex(
                name: "ix_usage_meter_events_group_id_occurred_at",
                table: "usage_meter_events",
                columns: new[] { "group_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_tokens_token_hash",
                table: "user_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_tokens_user_id",
                table: "user_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_signal_number",
                table: "users",
                column: "signal_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_telegram_id",
                table: "users",
                column: "telegram_id",
                unique: true);

            // pgvector extension installed now; used by ISemanticMemory in MVP4+.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            // Audit log is append-only and RLS-guarded (insert always allowed).
            migrationBuilder.Sql("ALTER TABLE audit_log ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY audit_insert_only ON audit_log FOR INSERT WITH CHECK (true);");
            migrationBuilder.Sql(
                "CREATE POLICY audit_select_group ON audit_log FOR SELECT USING (true);");

            // Conversations + memory facts are group/user scoped — enable RLS.
            migrationBuilder.Sql("ALTER TABLE conversations ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY conv_isolation ON conversations USING (" +
                "group_id IS NULL OR group_id = current_setting('app.current_group_id', true)::uuid);");
            migrationBuilder.Sql("ALTER TABLE memory_facts ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY memory_isolation ON memory_facts USING (" +
                "group_id IS NULL OR group_id = current_setting('app.current_group_id', true)::uuid);");

            // Full-text index over conversation messages (used by MVP4 search).
            migrationBuilder.Sql(
                "CREATE INDEX idx_conv_messages_fts ON conversation_messages " +
                "USING GIN (to_tsvector('simple', content)) WHERE content <> '[incognito]';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "budget_states");

            migrationBuilder.DropTable(
                name: "conversation_messages");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "dead_letter_queue");

            migrationBuilder.DropTable(
                name: "group_members");

            migrationBuilder.DropTable(
                name: "groups");

            migrationBuilder.DropTable(
                name: "idempotency_keys");

            migrationBuilder.DropTable(
                name: "memory_facts");

            migrationBuilder.DropTable(
                name: "pending_confirmations");

            migrationBuilder.DropTable(
                name: "pending_intents");

            migrationBuilder.DropTable(
                name: "prompt_versions");

            migrationBuilder.DropTable(
                name: "scheduler_jobs");

            migrationBuilder.DropTable(
                name: "usage_meter_events");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
