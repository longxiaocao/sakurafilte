using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SakuraFilter.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertHistoryP21 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // WHY: 用 IF NOT EXISTS 包装, 兼容 backend/migrations/018_create_alert_history.sql 已执行的环境
            //      首次部署任一路径都可以幂等跑通 (raw SQL 018 或 EF 迁移)
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS alert_history (
                    id            BIGSERIAL PRIMARY KEY,
                    type          VARCHAR(64)  NOT NULL,
                    severity      VARCHAR(16)  NOT NULL,
                    title         VARCHAR(256) NOT NULL,
                    content_json  JSONB        NOT NULL,
                    channel       VARCHAR(32)  NOT NULL,
                    status        VARCHAR(16)  NOT NULL,
                    response      TEXT,
                    error         TEXT,
                    recipients    JSONB,
                    correlation_id UUID,
                    sent_at       TIMESTAMPTZ  NOT NULL DEFAULT now()
                );

                CREATE TABLE IF NOT EXISTS alert_rules (
                    id          BIGSERIAL PRIMARY KEY,
                    type        VARCHAR(64)  NOT NULL,
                    enabled     BOOLEAN      NOT NULL DEFAULT true,
                    severity    VARCHAR(16)  NOT NULL,
                    channels    JSONB        NOT NULL,
                    conditions  JSONB,
                    recipients  JSONB,
                    description TEXT,
                    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
                    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
                );

                CREATE TABLE IF NOT EXISTS security_events (
                    id          BIGSERIAL PRIMARY KEY,
                    event_type  VARCHAR(64)  NOT NULL,
                    user_id     BIGINT,
                    username    VARCHAR(64),
                    ip          VARCHAR(45),
                    user_agent  VARCHAR(255),
                    details     JSONB,
                    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ix_alert_rules_type ON alert_rules (type);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS alert_history CASCADE;
                DROP TABLE IF EXISTS alert_rules CASCADE;
                DROP TABLE IF EXISTS security_events CASCADE;
            ");
        }
    }
}
